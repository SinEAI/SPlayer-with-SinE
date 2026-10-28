using System.Runtime.InteropServices;
using System.Text;

namespace SPlayer;

internal static class Utf8
{
    public static IntPtr Alloc(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var memory = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        Marshal.WriteByte(memory, bytes.Length, 0);
        return memory;
    }

    public static string From(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        var length = 0;
        while (Marshal.ReadByte(pointer, length) != 0) length++;
        if (length == 0) return "";
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct MpvNode
{
    [FieldOffset(0)] public IntPtr String;
    [FieldOffset(0)] public int Flag;
    [FieldOffset(0)] public long Int64;
    [FieldOffset(0)] public double Double;
    [FieldOffset(0)] public IntPtr List;
    [FieldOffset(8)] public int Format;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvNodeList
{
    public int Num;
    public IntPtr Values;
    public IntPtr Keys;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public int Id;
    public int Error;
    public ulong Reply;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public IntPtr Name;
    public int Format;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLog
{
    public IntPtr Prefix;
    public IntPtr Level;
    public IntPtr Text;
    public int LogLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    public int Reason;
    public int Error;
    public long PlaylistEntryId;
    public long PlaylistInsertId;
    public int PlaylistInsertCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvRenderParam
{
    public int Type;
    public IntPtr Data;
}

public static class MpvNative
{
    private const string Lib = "libmpv-2.dll";

    internal const int None = 0;
    internal const int String = 1;
    internal const int Flag = 3;
    internal const int Int64 = 4;
    internal const int Double = 5;
    internal const int Node = 6;
    internal const int NodeArray = 7;
    internal const int NodeMap = 8;

    internal const int EventNone = 0;
    internal const int EventShutdown = 1;
    internal const int EventLog = 2;
    internal const int EventEndFile = 7;
    internal const int EventFileLoaded = 8;
    internal const int EventSeek = 20;
    internal const int EventPlaybackRestart = 21;
    internal const int EventProperty = 22;

    internal const int EndEof = 0;
    internal const int EndStop = 2;
    internal const int EndQuit = 3;
    internal const int EndError = 4;

    internal const int RenderApiType = 1;
    internal const int RenderAdvanced = 10;
    internal const int RenderBlock = 12;
    internal const int RenderD3DInit = 21;
    internal const int RenderD3DFbo = 22;
    public const ulong UpdateFrame = 1;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mpv_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_option_string(IntPtr ctx, IntPtr name, IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_get_property(IntPtr ctx, IntPtr name, int format, IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_property(IntPtr ctx, IntPtr name, int format, IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_observe_property(IntPtr ctx, ulong reply, IntPtr name, int format);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_request_log_messages(IntPtr ctx, IntPtr level);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mpv_error_string(int error);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_free(IntPtr data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_free_node_contents(IntPtr node);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_render_context_create(out IntPtr result, IntPtr mpv, IntPtr parameters);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong mpv_render_context_update(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_render_context_render(IntPtr ctx, IntPtr parameters);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_render_context_set_update_callback(IntPtr ctx, MpvRenderUpdate callback, IntPtr user);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_render_context_report_swap(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_render_context_free(IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MpvRenderUpdate(IntPtr user);

    internal static int SetOption(IntPtr ctx, string name, string value)
    {
        var n = Utf8.Alloc(name);
        var v = Utf8.Alloc(value);
        try { return mpv_set_option_string(ctx, n, v); }
        finally { Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(v); }
    }

    internal static int Command(IntPtr ctx, string[] args)
    {
        var pointers = new IntPtr[args.Length + 1];
        var array = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
        try
        {
            for (var i = 0; i < args.Length; i++) pointers[i] = Utf8.Alloc(args[i]);
            for (var i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, pointers[i]);
            return mpv_command(ctx, array);
        }
        finally
        {
            Marshal.FreeHGlobal(array);
            foreach (var pointer in pointers)
            {
                if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer);
            }
        }
    }

    public static string ErrorString(int code) => Utf8.From(mpv_error_string(code));

    internal static int Get(IntPtr ctx, string name, int format, IntPtr data)
    {
        var n = Utf8.Alloc(name);
        try { return mpv_get_property(ctx, n, format, data); }
        finally { Marshal.FreeHGlobal(n); }
    }

    internal static int Set(IntPtr ctx, string name, int format, IntPtr data)
    {
        var n = Utf8.Alloc(name);
        try { return mpv_set_property(ctx, n, format, data); }
        finally { Marshal.FreeHGlobal(n); }
    }

    internal static int Observe(IntPtr ctx, ulong id, string name, int format)
    {
        var n = Utf8.Alloc(name);
        try { return mpv_observe_property(ctx, id, n, format); }
        finally { Marshal.FreeHGlobal(n); }
    }
}
