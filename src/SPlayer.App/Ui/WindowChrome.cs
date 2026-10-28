using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics;

namespace SPlayer.Ui;

internal enum MediaCommand
{
    PlayPause,
    Stop,
    Next,
    Previous,
}

/// <summary>Fullscreen measurement, the hidden cursor, sleep, and media keys.</summary>
internal sealed class WindowChrome : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _proc;
    private bool _disposed;
    private int _covering;

    internal bool HideCursor { get; set; }
    internal event Action? Suspending;
    internal event Action? Resuming;
    internal event Action<MediaCommand>? Media;

    internal WindowChrome(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = Callback;
        if (!SetWindowSubclass(hwnd, _proc, 1, 0)) Log.Write("无法安装窗口子类。");
        CoverTopEdge(hwnd);
    }

    /// <summary>
    /// WinUI places the page one pixel below the top of the client area.
    /// The uncovered pixel is the light theme color, which shows as a white line on video.
    /// </summary>
    private void CoverTopEdge(IntPtr hwnd)
    {
        if (_covering > 0) return;
        if (!GetClientRect(hwnd, out var client)) return;
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width < 2 || height < 2) return;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return;
        _covering++;
        try
        {
            EnumChildWindows(hwnd, (child, _) =>
            {
                var name = new StringBuilder(64);
                GetClassName(child, name, name.Capacity);
                if (name.ToString() != "Microsoft.UI.Content.DesktopChildSiteBridge") return true;
                if (!GetWindowRect(child, out var rect)) return true;
                if (rect.Left == origin.X && rect.Top == origin.Y && rect.Right - rect.Left == width && rect.Bottom - rect.Top == height)
                    return true;
                SetWindowPos(child, IntPtr.Zero, 0, 0, width, height, 0x0004 | 0x0010);
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            _covering--;
        }
    }

    internal static RectInt32 WindowRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    internal static RectInt32 MonitorRect(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return default;
        var rect = info.Monitor;
        return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveWindowSubclass(_hwnd, _proc, 1);
    }

    private nint Callback(IntPtr hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        const uint WM_SETCURSOR = 0x0020;
        const uint WM_POWERBROADCAST = 0x0218;
        const uint WM_APPCOMMAND = 0x0319;
        const int PBT_APMSUSPEND = 0x0004;
        const int PBT_APMRESUMESUSPEND = 0x0007;
        const uint WM_MOVE = 0x0003;
        const uint WM_EXITSIZEMOVE = 0x0232;
        const uint WM_WINDOWPOSCHANGED = 0x0047;
        if (message is WM_MOVE or WM_EXITSIZEMOVE or WM_WINDOWPOSCHANGED)
        {
            var handled = DefSubclassProc(hwnd, message, wParam, lParam);
            CoverTopEdge(hwnd);
            return handled;
        }
        if (message == WM_SETCURSOR && HideCursor)
        {
            SetCursor(IntPtr.Zero);
            return 1;
        }
        if (message == WM_POWERBROADCAST)
        {
            if (wParam == PBT_APMSUSPEND) Suspending?.Invoke();
            else if (wParam == PBT_APMRESUMESUSPEND) Resuming?.Invoke();
        }
        else if (message == WM_APPCOMMAND)
        {
            var command = (int)((lParam >> 16) & 0xFFFF);
            MediaCommand? mapped = command switch
            {
                14 => MediaCommand.PlayPause,
                13 => MediaCommand.Stop,
                11 => MediaCommand.Next,
                12 => MediaCommand.Previous,
                _ => null,
            };
            if (mapped is { } value)
            {
                Media?.Invoke(value);
                return 1;
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private delegate nint SUBCLASSPROC(IntPtr hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(IntPtr hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);
}
