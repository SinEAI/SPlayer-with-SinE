using System.Runtime.InteropServices;

namespace SPlayer;

/// <summary>libmpv's D3D11 render context. Render, update and free happen on one thread, never inside the update callback.</summary>
public sealed class MpvRenderContext : IDisposable
{
    private IntPtr _ctx;
    private MpvNative.MpvRenderUpdate? _callback;
    private Action? _signal;
    private int _disposed;

    private MpvRenderContext(IntPtr ctx) => _ctx = ctx;

    public static MpvRenderContext Create(IntPtr mpv, IntPtr device)
    {
        var api = Utf8.Alloc("d3d11");
        var init = Marshal.AllocHGlobal(IntPtr.Size);
        var advanced = Marshal.AllocHGlobal(4);
        var parameters = Marshal.AllocHGlobal(Marshal.SizeOf<MpvRenderParam>() * 4);
        try
        {
            Marshal.WriteIntPtr(init, device);
            Marshal.WriteInt32(advanced, 1);
            Write(parameters, 0, MpvNative.RenderApiType, api);
            Write(parameters, 1, MpvNative.RenderD3DInit, init);
            Write(parameters, 2, MpvNative.RenderAdvanced, advanced);
            Write(parameters, 3, 0, IntPtr.Zero);
            var code = MpvNative.mpv_render_context_create(out var ctx, mpv, parameters);
            if (code < 0 || ctx == IntPtr.Zero)
                throw new InvalidOperationException("无法创建 D3D11 渲染上下文：" + MpvNative.ErrorString(code));
            return new MpvRenderContext(ctx);
        }
        finally
        {
            Marshal.FreeHGlobal(parameters);
            Marshal.FreeHGlobal(advanced);
            Marshal.FreeHGlobal(init);
            Marshal.FreeHGlobal(api);
        }
    }

    public void SetUpdateCallback(Action signal)
    {
        _signal = signal;
        _callback = _ =>
        {
            try { _signal?.Invoke(); }
            catch (Exception ex) { Log.Write(ex.ToString()); }
        };
        MpvNative.mpv_render_context_set_update_callback(_ctx, _callback, IntPtr.Zero);
    }

    public ulong Update() => _ctx == IntPtr.Zero ? 0 : MpvNative.mpv_render_context_update(_ctx);

    public int Render(IntPtr texture, bool blockForTargetTime)
    {
        if (_ctx == IntPtr.Zero) return -3;
        var fbo = Marshal.AllocHGlobal(IntPtr.Size);
        var block = Marshal.AllocHGlobal(4);
        var parameters = Marshal.AllocHGlobal(Marshal.SizeOf<MpvRenderParam>() * 3);
        try
        {
            Marshal.WriteIntPtr(fbo, texture);
            Marshal.WriteInt32(block, blockForTargetTime ? 1 : 0);
            Write(parameters, 0, MpvNative.RenderD3DFbo, fbo);
            Write(parameters, 1, MpvNative.RenderBlock, block);
            Write(parameters, 2, 0, IntPtr.Zero);
            return MpvNative.mpv_render_context_render(_ctx, parameters);
        }
        finally
        {
            Marshal.FreeHGlobal(parameters);
            Marshal.FreeHGlobal(block);
            Marshal.FreeHGlobal(fbo);
        }
    }

    public void ReportSwap()
    {
        if (_ctx != IntPtr.Zero) MpvNative.mpv_render_context_report_swap(_ctx);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ctx != IntPtr.Zero)
        {
            MpvNative.mpv_render_context_free(_ctx);
            _ctx = IntPtr.Zero;
        }
        _callback = null;
    }

    private static void Write(IntPtr parameters, int index, int type, IntPtr data)
    {
        var size = Marshal.SizeOf<MpvRenderParam>();
        var slot = parameters + index * size;
        Marshal.WriteInt32(slot, type);
        Marshal.WriteIntPtr(slot, 8, data);
    }
}
