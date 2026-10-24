# Shared helpers of the end-to-end update tests (build\e2e-update.ps1, build\e2e-handoff.ps1); dot-sourced, no output.
# Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -ErrorAction SilentlyContinue -Name Win -Namespace E2e -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr dc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint Size, Time; }
'@

$script:failures = 0
function Check($ok, $what) { if ($ok) { Write-Host "PASS  $what" } else { Write-Host "FAIL  $what"; $script:failures++ } }

function Wait-Until([scriptblock]$condition, [double]$seconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) { $r = & $condition; if ($r) { return $r }; Start-Sleep -Milliseconds 200 }
    return (& $condition)
}

# A release document in the format of GitHub's "latest release" API, written beside the assets it lists.
function Write-ReleaseDocument($dir, $tag, $notes, $base, $assetNames) {
    $assets = @()
    foreach ($name in $assetNames) {
        $assets += [ordered]@{ name = $name; size = (Get-Item -LiteralPath (Join-Path $dir $name)).Length; browser_download_url = $base + $name }
    }
    $document = [ordered]@{ tag_name = $tag; body = $notes; published_at = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); assets = $assets }
    [IO.File]::WriteAllText((Join-Path $dir 'latest.json'), ($document | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
}

# A tiny static HTTP server on 127.0.0.1 (a background job) that serves the files of $dir until $stopFile exists.
function Start-ReleaseServer($dir, $port, $stopFile) {
    Start-Job -ArgumentList $dir, $port, $stopFile -ScriptBlock {
        param($dir, $port, $stopFile)
        $listener = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback, $port); $listener.Start()
        try {
            while (-not (Test-Path -LiteralPath $stopFile)) {
                if (-not $listener.Pending()) { Start-Sleep -Milliseconds 30; continue }
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream(); $reader = New-Object IO.StreamReader ($stream, [Text.Encoding]::ASCII, $false, 4096, $true)
                    $request = $reader.ReadLine(); while ($true) { $h = $reader.ReadLine(); if ($h -eq $null -or $h -eq '') { break } }
                    $parts = $request.Split(' '); $name = [Uri]::UnescapeDataString($parts[1].Split('?')[0].TrimStart('/'))
                    $file = Join-Path $dir $name
                    if ($name -and (Test-Path -LiteralPath $file -PathType Leaf)) { $body = [IO.File]::ReadAllBytes($file); $status = '200 OK' }
                    else { $body = [Text.Encoding]::ASCII.GetBytes('not found'); $status = '404 Not Found' }
                    $type = 'application/octet-stream'; if ($name -like '*.json') { $type = 'application/json' } elseif ($name -eq 'SHA256SUMS') { $type = 'text/plain' }
                    $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: $type`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($head, 0, $head.Length); if ($parts[0] -ne 'HEAD') { $stream.Write($body, 0, $body.Length) }; $stream.Flush()
                } catch { } finally { $client.Close() }
            }
        } finally { $listener.Stop() }
    }
}

# ---- UI Automation: the in-window sheet, pill and toasts are descendants of the window of the process ----
function Windows-Of($processId) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$processId)
    return [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $cond)
}
# The first element of the process whose automation name is $name (optionally of one control type, optionally enabled).
function Find-Element($processId, $name, $controlType = $null, [switch]$Enabled) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty, $name)
    foreach ($w in Windows-Of $processId) {
        try {
            foreach ($e in $w.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)) {
                if ($controlType -and $e.Current.ControlType.Id -ne $controlType.Id) { continue }
                if ($Enabled -and -not $e.Current.IsEnabled) { continue }
                return $e
            }
        } catch { }
    }
    return $null
}
# The first element named $name inside a top-level window whose name starts with $windowName (any process: the page
# of the 1.4.1 window belongs to a child of the process that was started).
function Find-InWindow($windowName, $name) {
    $nameCondition = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty, $name)
    foreach ($w in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)) {
        try {
            if (-not $w.Current.Name.StartsWith($windowName)) { continue }
            # A name can appear twice (the 1.4.1 window has a "Restart to update" in its toolbar and one in the sheet): the lowest one is the sheet's.
            $found = $null
            foreach ($e in $w.FindAll([Windows.Automation.TreeScope]::Descendants, $nameCondition)) { if ($found -eq $null -or $e.Current.BoundingRectangle.Top -gt $found.Current.BoundingRectangle.Top) { $found = $e } }
            if ($found) { return $found }
        } catch { }
    }
    return $null
}

# Saves the whole primary screen as a PNG (for windows PrintWindow cannot render, such as the 1.4.1 page).
function Save-ScreenCapture($path) {
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $bitmap = New-Object Drawing.Bitmap ($bounds.Width, $bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size); $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    return $true
}

function Find-ById($processId, $id) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    foreach ($w in Windows-Of $processId) {
        try { $e = $w.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond); if ($e) { return $e } } catch { }
    }
    return $null
}
function Invoke-Element($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

# Seconds since the last keyboard or mouse input of the user.
function Idle-Seconds() {
    $info = New-Object E2e.Win+LASTINPUTINFO; $info.Size = [uint32][Runtime.InteropServices.Marshal]::SizeOf($info)
    [void][E2e.Win]::GetLastInputInfo([ref]$info)
    return ([Environment]::TickCount - [int]$info.Time) / 1000.0
}

# Presses an element: the Invoke pattern when it has one; otherwise (the web page of the 1.4.1 window has none) a real
# mouse click at its centre, only while the machine has been idle for 3 s, so a user who is typing is never disturbed.
function Press-Element($element) {
    try { Invoke-Element $element; return } catch { }
    $idle = Wait-Until { (Idle-Seconds) -ge 3 } 60
    if (-not $idle) { throw 'the desktop is not free: no mouse click was made' }
    # The test window must be the one in front: bring the top-level window of the element forward first.
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker; $top = $element
    while ($true) { $parent = $walker.GetParent($top); if ($parent -eq $null -or $parent -eq [Windows.Automation.AutomationElement]::RootElement) { break }; $top = $parent }
    [void][E2e.Win]::SetForegroundWindow([IntPtr]$top.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 400
    $r = $element.Current.BoundingRectangle
    [void][E2e.Win]::SetCursorPos([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
    Start-Sleep -Milliseconds 150
    [E2e.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); [E2e.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

# Saves the main window of the process as a PNG (PrintWindow renders the window itself, whatever is above it).
function Save-WindowCapture($processId, $path) {
    $handle = [IntPtr]::Zero
    foreach ($w in Windows-Of $processId) { $h = [IntPtr]$w.Current.NativeWindowHandle; if ($h -ne [IntPtr]::Zero) { $handle = $h; break } }
    if ($handle -eq [IntPtr]::Zero) { return $false }
    $rect = New-Object E2e.Win+RECT
    [void][E2e.Win]::GetWindowRect($handle, [ref]$rect)
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { return $false }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $bitmap = New-Object Drawing.Bitmap ($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try { [void][E2e.Win]::PrintWindow($handle, $dc, 2) } finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    return $true
}

# Processes whose executable lives under $folder.
function Processes-Under($folder) {
    @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) })
}

# Builds Retake with another version number through build\build.ps1 -Version (a copy of the sources; the working tree
# and its Version.cs stay untouched). Returns the path of the built exe in <scratch>\build-<version>.
function Build-Retake($root, $scratch, $version) {
    $out = Join-Path $scratch "build-$version"
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $lines = @(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build\build.ps1') -Version $version -OutDir $out) }
    finally { $ErrorActionPreference = $saved }
    if ($LASTEXITCODE -ne 0) { $lines | Out-Host; throw "build of $version failed" }
    return $lines[-1]
}

# The version of a built exe (its product version, x.y.z).
function Exe-Version($exe) { return (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion }

function Current-Version($root) {
    $m = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'src\Retake\Version.cs') -Raw), 'Version\s*=\s*"(\d+)\.(\d+)\.(\d+)"')
    return @{ N = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"; N1 = "$($m.Groups[1].Value).$($m.Groups[2].Value).$([int]$m.Groups[3].Value + 1)" }
}
