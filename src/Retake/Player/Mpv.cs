using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Retake.Player
{
    /// <summary>
    /// The libmpv client API (client.h, API 2.x) by P/Invoke. The DLL is loaded by <see cref="LibmpvLoader"/>
    /// from its full path before the first call, so the name below resolves to the module already in memory.
    /// Strings cross as NUL-terminated UTF-8.
    /// </summary>
    static class MpvNative
    {
        const string Dll = "libmpv-2.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_create();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_initialize(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong mpv_client_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command(IntPtr ctx, IntPtr[] args);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command_ret(IntPtr ctx, IntPtr[] args, IntPtr result);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free_node_contents(IntPtr node);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_get_property(IntPtr ctx, byte[] name, int format, out double data);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_get_property(IntPtr ctx, byte[] name, int format, out long data);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free(IntPtr data);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_wakeup(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_observe_property(IntPtr ctx, ulong userdata, byte[] name, int format);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_error_string(int error);

        public const int FormatString = 1, FormatFlag = 3, FormatInt64 = 4, FormatDouble = 5;
        public const int EventNone = 0, EventShutdown = 1, EventStartFile = 6, EventEndFile = 7, EventFileLoaded = 8,
            EventPlaybackRestart = 21, EventPropertyChange = 22;
        public const int EndFileReasonError = 4;

        public static byte[] U8(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        public static string? FromU8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public static string Error(int code) => FromU8(mpv_error_string(code)) ?? ("error " + code);
    }

    public sealed class MpvException : Exception
    {
        public MpvException(string message) : base(message) { }
    }

    /// <summary>A frame as <c>screenshot-raw</c> returns it: native size, <c>bgr0</c> (4 bytes per pixel, the fourth unused).</summary>
    public sealed class RawFrame
    {
        public int Width, Height, Stride;
        public string Format = "bgr0";
        public byte[] Data = new byte[0];
    }

    /// <summary>
    /// A thin libmpv client: options, commands, properties and an event thread that reports events through
    /// <see cref="Event"/> (raised on that thread). Property changes are observed by name with a format.
    /// Shutdown stops the event thread first (<c>mpv_wakeup</c>, join) and destroys the handle afterwards.
    /// </summary>
    public sealed class MpvClient : IDisposable
    {
        readonly Thread events;
        volatile bool stopping;
        readonly Dictionary<ulong, string> observed = new Dictionary<ulong, string>();
        ulong nextObserve = 1;

        public IntPtr Handle { get; private set; }

        /// <summary>(event id, property name or null, value or null, end-file reason, error code).</summary>
        public event Action<MpvEvent>? Event;

        public MpvClient(IEnumerable<KeyValuePair<string, string>> options)
        {
            Handle = MpvNative.mpv_create();
            if (Handle == IntPtr.Zero) throw new MpvException("libmpv could not create a player");
            foreach (var kv in options) Check(MpvNative.mpv_set_option_string(Handle, MpvNative.U8(kv.Key), MpvNative.U8(kv.Value)), "option " + kv.Key);
            Check(MpvNative.mpv_initialize(Handle), "initialize");
            events = new Thread(Loop) { IsBackground = true, Name = "mpv-events" };
            events.Start();
        }

        public static string Version()
        {
            ulong v = MpvNative.mpv_client_api_version();
            return (v >> 16) + "." + (v & 0xFFFF);
        }

        static void Check(int r, string what)
        {
            if (r < 0) throw new MpvException(what + ": " + MpvNative.Error(r));
        }

        public void Observe(string name, int format)
        {
            ulong id;
            lock (observed) { id = nextObserve++; observed[id] = name; }
            Check(MpvNative.mpv_observe_property(Handle, id, MpvNative.U8(name), format), "observe " + name);
        }

        void Loop()
        {
            while (!stopping)
            {
                IntPtr h = Handle;
                if (h == IntPtr.Zero) break;
                IntPtr ev = MpvNative.mpv_wait_event(h, 1.0);
                if (stopping) break;
                int id = Marshal.ReadInt32(ev);
                if (id == MpvNative.EventNone) continue;
                if (id == MpvNative.EventShutdown) break;
                var e = new MpvEvent { Id = id, Error = Marshal.ReadInt32(ev, 4) };
                IntPtr data = Marshal.ReadIntPtr(ev, 16);
                if (id == MpvNative.EventPropertyChange && data != IntPtr.Zero)
                {
                    e.Name = MpvNative.FromU8(Marshal.ReadIntPtr(data));
                    int format = Marshal.ReadInt32(data, IntPtr.Size);
                    IntPtr p = Marshal.ReadIntPtr(data, IntPtr.Size * 2);
                    if (p != IntPtr.Zero)
                    {
                        if (format == MpvNative.FormatDouble) e.Number = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p));
                        else if (format == MpvNative.FormatFlag) e.Number = Marshal.ReadInt32(p);
                        else if (format == MpvNative.FormatInt64) e.Number = Marshal.ReadInt64(p);
                        else if (format == MpvNative.FormatString) e.Text = MpvNative.FromU8(Marshal.ReadIntPtr(p));
                    }
                }
                else if (id == MpvNative.EventEndFile && data != IntPtr.Zero)
                {
                    e.Reason = Marshal.ReadInt32(data);
                    e.Error = Marshal.ReadInt32(data, 4);
                }
                try { Event?.Invoke(e); }
                catch (Exception ex) { Retake.Core.Log.Error("player event handler failed", ex); }
            }
        }

        static IntPtr[] Args(string[] args, List<IntPtr> allocs)
        {
            var arr = new IntPtr[args.Length + 1];
            for (int i = 0; i < args.Length; i++)
            {
                byte[] b = MpvNative.U8(args[i]);
                IntPtr p = Marshal.AllocHGlobal(b.Length);
                Marshal.Copy(b, 0, p, b.Length);
                allocs.Add(p);
                arr[i] = p;
            }
            return arr;
        }

        /// <summary>Runs a command; returns the libmpv result code (negative on failure).</summary>
        public int Command(params string[] args)
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return -1;
            var allocs = new List<IntPtr>();
            try { return MpvNative.mpv_command(h, Args(args, allocs)); }
            finally { foreach (IntPtr p in allocs) Marshal.FreeHGlobal(p); }
        }

        public void Set(string name, string value)
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return;
            Check(MpvNative.mpv_set_property_string(h, MpvNative.U8(name), MpvNative.U8(value)), "set " + name);
        }

        public double GetDouble(string name)
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return double.NaN;
            return MpvNative.mpv_get_property(h, MpvNative.U8(name), MpvNative.FormatDouble, out double v) < 0 ? double.NaN : v;
        }

        public long GetLong(string name, long fallback = -1)
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return fallback;
            return MpvNative.mpv_get_property(h, MpvNative.U8(name), MpvNative.FormatInt64, out long v) < 0 ? fallback : v;
        }

        public string? GetString(string name)
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return null;
            IntPtr p = MpvNative.mpv_get_property_string(h, MpvNative.U8(name));
            if (p == IntPtr.Zero) return null;
            try { return MpvNative.FromU8(p); } finally { MpvNative.mpv_free(p); }
        }

        /// <summary><c>screenshot-raw</c>: the frame mpv shows, at native size for <c>video</c>. Blocks until mpv has rendered it; call it off the UI thread.</summary>
        public RawFrame ScreenshotRaw(string mode = "video")
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) throw new MpvException("No player");
            var allocs = new List<IntPtr>();
            IntPtr node = Marshal.AllocHGlobal(16);
            try
            {
                int r = MpvNative.mpv_command_ret(h, Args(new[] { "screenshot-raw", mode }, allocs), node);
                if (r < 0) throw new MpvException("screenshot-raw: " + MpvNative.Error(r));
                var f = new RawFrame();
                try
                {
                    IntPtr list = Marshal.ReadIntPtr(node);
                    int num = Marshal.ReadInt32(list);
                    IntPtr values = Marshal.ReadIntPtr(list, 8);
                    IntPtr keys = Marshal.ReadIntPtr(list, 16);
                    for (int i = 0; i < num; i++)
                    {
                        string? key = MpvNative.FromU8(Marshal.ReadIntPtr(keys, i * IntPtr.Size));
                        IntPtr v = values + i * 16;
                        switch (key)
                        {
                            case "w": f.Width = (int)Marshal.ReadInt64(v); break;
                            case "h": f.Height = (int)Marshal.ReadInt64(v); break;
                            case "stride": f.Stride = (int)Marshal.ReadInt64(v); break;
                            case "format": f.Format = MpvNative.FromU8(Marshal.ReadIntPtr(v)) ?? ""; break;
                            case "data":
                                IntPtr ba = Marshal.ReadIntPtr(v);
                                IntPtr ptr = Marshal.ReadIntPtr(ba);
                                long size = Marshal.ReadInt64(ba, 8);
                                f.Data = new byte[size];
                                Marshal.Copy(ptr, f.Data, 0, (int)size);
                                break;
                        }
                    }
                }
                finally { MpvNative.mpv_free_node_contents(node); }
                return f;
            }
            finally
            {
                Marshal.FreeHGlobal(node);
                foreach (IntPtr p in allocs) Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>Stops the event thread, then destroys the player. Safe to call twice.</summary>
        public void Dispose()
        {
            IntPtr h = Handle;
            if (h == IntPtr.Zero) return;
            stopping = true;
            MpvNative.mpv_wakeup(h);
            if (Thread.CurrentThread != events) events.Join(3000);
            Handle = IntPtr.Zero;
            MpvNative.mpv_terminate_destroy(h);
        }
    }

    /// <summary>One libmpv event as the event thread read it.</summary>
    public sealed class MpvEvent
    {
        public int Id, Error, Reason;
        public string? Name, Text;
        public double Number = double.NaN;
    }
}
