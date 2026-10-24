using System;
using System.Runtime.InteropServices;

namespace Retake.Design
{
    /// <summary>Win32 and DWM calls of the window shell.</summary>
    static class Native
    {
        public const int WM_NCLBUTTONDBLCLK = 0x00A3, WM_SYSCOMMAND = 0x0112;
        public const int SC_SIZE = 0xF000, SC_MAXIMIZE = 0xF030;
        public const int GWL_STYLE = -16;
        public const int WS_MAXIMIZEBOX = 0x00010000, WS_THICKFRAME = 0x00040000;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
        public const int DWMWCP_ROUND = 2;
        const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();

        /// <summary>The system DPI (96 = 100 %); 96 where the call is missing.</summary>
        public static double SystemDpi()
        {
            try { uint d = GetDpiForSystem(); return d > 0 ? d : 96; }
            catch (EntryPointNotFoundException) { return 96; }
        }

        /// <summary>The work area of the monitor that holds the window, in physical pixels.</summary>
        public static RECT? WorkArea(IntPtr hwnd)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return null;
            return info.rcWork;
        }

        /// <summary>Sets a DWM attribute; silently ignored where the system does not know it (before Windows 11).</summary>
        public static void Dwm(IntPtr hwnd, int attribute, int value)
        {
            try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
