# Writes the app icon from the Retake mark: build\icon.ico (the exe icon, ApplicationIcon of src\Retake\Retake.csproj)
# and docs\assets\icon.svg (the icon of the README banner). The mark has one source, the class AppGlyph in
# src\Retake\Design\Shell.cs: its three paths (PlatePath, MarkPath, DotPath, a 20 x 20 grid) and their theme tokens;
# the colours are the default theme's values in src\Retake\Design\themes.json. Run it after a change of the mark and
# commit both files; the build reads build\icon.ico as it is.
# Windows PowerShell 5.1 (WPF draws the PNG frames; the console is STA). The script text is ASCII on purpose.
#   -Sizes <n,...>   icon frame sizes (default 16, 20, 24, 32, 40, 48, 64, 128, 256)
[CmdletBinding()]
param([int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256))

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationCore, WindowsBase

# ---- the mark: paths and tokens from Shell.cs ----
$shell = Get-Content -LiteralPath (Join-Path $root 'src\Retake\Design\Shell.cs') -Raw
function Constant($name) {
    $v = [regex]::Match($shell, '\b' + $name + '\s*=\s*"([^"]+)"').Groups[1].Value
    if (!$v) { throw "$name not found in src\Retake\Design\Shell.cs" }
    return $v
}
$parts = @(
    @{ Path = (Constant 'PlatePath'); Token = (Constant 'PlateToken') },
    @{ Path = (Constant 'MarkPath'); Token = (Constant 'MarkToken') },
    @{ Path = (Constant 'DotPath'); Token = (Constant 'DotToken') }
)

# ---- the colours: overrides over derived over tokens of the default theme ----
$themes = Get-Content -LiteralPath (Join-Path $root 'src\Retake\Design\themes.json') -Raw | ConvertFrom-Json
$theme = $themes.themes | Where-Object { $_.name -eq $themes.default } | Select-Object -First 1
if (!$theme) { throw "default theme '$($themes.default)' not found in themes.json" }
function Token($name) {
    foreach ($layer in @($theme.overrides, $theme.derived, $theme.tokens)) {
        $p = $layer.PSObject.Properties["--$name"]
        if ($p) { return [string]$p.Value }
    }
    throw "token --$name not found in theme $($theme.name)"
}
# A paint: a solid #rrggbb, or a linear-gradient(<deg>deg, #rrggbb <n>%, ...) of the kit's form.
function Paint($value) {
    $value = $value.Trim()
    if ($value -match '^#[0-9a-fA-F]{6}$') { return @{ Solid = $value.ToLowerInvariant() } }
    $m = [regex]::Match($value, '^linear-gradient\(\s*([0-9.]+)deg\s*,(.*)\)$')
    if (!$m.Success) { throw "unsupported paint: $value" }
    $stops = @()
    foreach ($s in [regex]::Matches($m.Groups[2].Value, '(#[0-9a-fA-F]{6})\s+([0-9.]+)%')) {
        $stops += @{ Color = $s.Groups[1].Value.ToLowerInvariant(); Offset = [double]$s.Groups[2].Value / 100 }
    }
    if ($stops.Count -lt 2) { throw "gradient without two stops: $value" }
    return @{ Angle = [double]$m.Groups[1].Value; Stops = $stops }
}
foreach ($p in $parts) { $p.Paint = Paint (Token $p.Token) }

# CSS angle (0deg = to top, clockwise) as end points on the unit square, the way the design layer maps it.
function Ends($angle) {
    $r = $angle * [Math]::PI / 180
    $dx = [Math]::Sin($r); $dy = -[Math]::Cos($r)
    $half = ([Math]::Abs($dx) + [Math]::Abs($dy)) / 2
    return @((0.5 - $dx * $half), (0.5 - $dy * $half), (0.5 + $dx * $half), (0.5 + $dy * $half))
}
function Brush($paint) {
    if ($paint.Solid) { $b = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($paint.Solid)) }
    else {
        $e = Ends $paint.Angle
        $b = New-Object Windows.Media.LinearGradientBrush
        $b.StartPoint = New-Object Windows.Point ($e[0], $e[1]); $b.EndPoint = New-Object Windows.Point ($e[2], $e[3])
        foreach ($s in $paint.Stops) { $b.GradientStops.Add((New-Object Windows.Media.GradientStop ([Windows.Media.ColorConverter]::ConvertFromString($s.Color)), $s.Offset)) }
    }
    $b.Freeze()
    return $b
}

# ---- icon.ico: one 32-bit PNG frame per size ----
$frames = @()
foreach ($size in ($Sizes | Sort-Object -Unique)) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 20.0), ($size / 20.0)))
    foreach ($p in $parts) { $dc.DrawGeometry((Brush $p.Paint), $null, [Windows.Media.Geometry]::Parse($p.Path)) }
    $dc.Pop(); $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap ($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    $frames += @{ Size = $size; Bytes = $stream.ToArray() }
}
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $edge = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$edge); $w.Write([byte]$edge); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
$icoPath = Join-Path $root 'build\icon.ico'
[IO.File]::WriteAllBytes($icoPath, $ico.ToArray())

# ---- icon.svg: the same paths and paints on the 20 x 20 grid ----
$defs = ''; $body = ''; $i = 0
foreach ($p in $parts) {
    $i++
    if ($p.Paint.Solid) { $fill = $p.Paint.Solid }
    else {
        $e = Ends $p.Paint.Angle
        # The gradient spans the plate's bounding box (1.5 .. 18.5), as the design layer's relative brush does.
        $c = @($e | ForEach-Object { 1.5 + 17 * $_ })
        $stops = ($p.Paint.Stops | ForEach-Object { '<stop offset="{0}" stop-color="{1}"/>' -f $_.Offset.ToString([Globalization.CultureInfo]::InvariantCulture), $_.Color }) -join ''
        $defs += ('<linearGradient id="paint{0}" gradientUnits="userSpaceOnUse" x1="{1}" y1="{2}" x2="{3}" y2="{4}">{5}</linearGradient>' -f $i,
            $c[0].ToString('0.###', [Globalization.CultureInfo]::InvariantCulture), $c[1].ToString('0.###', [Globalization.CultureInfo]::InvariantCulture),
            $c[2].ToString('0.###', [Globalization.CultureInfo]::InvariantCulture), $c[3].ToString('0.###', [Globalization.CultureInfo]::InvariantCulture), $stops)
        $fill = "url(#paint$i)"
    }
    $body += ('<path d="{0}" fill="{1}"/>' -f $p.Path, $fill)
}
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="256" height="256"><defs>' + $defs + '</defs>' + $body + "</svg>`n"
$svgPath = Join-Path $root 'docs\assets\icon.svg'
[IO.File]::WriteAllText($svgPath, $svg, (New-Object Text.UTF8Encoding $false))

Write-Host "Wrote $icoPath ($($frames.Count) sizes: $(($frames | ForEach-Object { $_.Size }) -join ', '); $([math]::Round((Get-Item $icoPath).Length / 1KB)) KB)"
Write-Host "Wrote $svgPath"
