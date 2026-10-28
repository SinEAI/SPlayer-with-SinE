using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Vortice.DXGI;
using Vortice.Direct3D11;

namespace SPlayer.Ui;

[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    [PreserveSig] int SetSwapChain(IntPtr swapChain);
}

/// <summary>
/// Presents libmpv into a SwapChainPanel. The render thread never calls back into libmpv's control API.
/// A hidden 64×64 target keeps the core from stalling while the panel has no size.
/// </summary>
internal sealed class VideoPresenter : IDisposable
{
    private D3D11Video _video;
    private MpvRenderContext _render;
    private readonly MpvPlayer _player;
    private int _recoveries;
    private bool _failed;
    private bool _recoveryPending;
    private int _generation;
    internal event Action<string>? Failed;
    internal event Action? Recovered;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private readonly SynchronizationContext _ui;
    private SwapChainPanel? _panel;
    private IDXGISwapChain1? _swapChain;
    private int _swapWidth;
    private int _swapHeight;
    private volatile int _width;
    private volatile int _height;
    private volatile float _scaleX = 1;
    private volatile float _scaleY = 1;
    private volatile bool _running = true;
    private int _presented;
    private bool _swapChainSent;
    private Format _format = Format.R10G10B10A2_UNorm;
    private bool _formatDropped;

    internal VideoPresenter(MpvPlayer player)
    {
        _player = player;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _video = D3D11Video.Create();
        // auto-safe often stays on software when the app supplies the D3D11 device.
        // d3d11va matches that device and still falls back to software if the codec cannot be decoded.
        var requested = player.GetString("hwdec");
        if (!_video.Hardware || requested == "no") player.SetString("hwdec", "no");
        else player.SetString("hwdec", "d3d11va");
        _render = player.AttachRender(_video.Device.NativePointer);
        _thread = new Thread(Loop) { IsBackground = true, Name = "SPlayer.render" };
        _thread.Start();
        _render.SetUpdateCallback(() => _signal.Set());
    }

    internal bool Hardware => _video.Hardware;
    internal int Presented => _presented;

    internal void Attach(SwapChainPanel panel)
    {
        _panel = panel;
        panel.SizeChanged += (_, _) => PublishSize();
        panel.CompositionScaleChanged += (_, _) => PublishSize();
        PublishSize();
    }

    public void Dispose()
    {
        _running = false;
        _signal.Set();
        _thread.Join();
        _render.Dispose();
        _swapChain?.Dispose();
        _video.Dispose();
        _signal.Dispose();
    }

    private void PublishSize()
    {
        var panel = _panel;
        if (panel is null) return;
        var scaleX = panel.CompositionScaleX < 0.1 ? 1 : panel.CompositionScaleX;
        var scaleY = panel.CompositionScaleY < 0.1 ? 1 : panel.CompositionScaleY;
        _scaleX = (float)scaleX;
        _scaleY = (float)scaleY;
        _width = Math.Max(0, (int)Math.Round(panel.ActualWidth * scaleX));
        _height = Math.Max(0, (int)Math.Round(panel.ActualHeight * scaleY));
        _signal.Set();
    }

    private void Loop()
    {
        ID3D11Texture2D? dummy = null;
        try
        {
            while (_running)
            {
                _signal.WaitOne(200);
                if (!_running) break;
                if (_failed) continue;
                try { Draw(ref dummy); }
                catch (Exception ex)
                {
                    Log.Write("渲染失败：" + ex);
                    if (ex.HResult is unchecked((int)0x887A0005) or unchecked((int)0x887A0007) && _recoveries++ < 2)
                    {
                        try
                        {
                            dummy?.Dispose(); dummy = null;
                            _generation++;
                            _render.Dispose();
                            _swapChain?.Dispose(); _swapChain = null;
                            _video.Dispose();
                            _video = D3D11Video.Create();
                            _render = _player.AttachRender(_video.Device.NativePointer);
                            _render.SetUpdateCallback(() => _signal.Set());
                            _swapChainSent = false; _swapWidth = _swapHeight = 0;
                            _recoveryPending = true;
                            continue;
                        }
                        catch (Exception recovery) { Log.Write("画面恢复失败：" + recovery); }
                    }
                    _failed = true;
                    _ui.Post(_ => { if (_running) Failed?.Invoke("画面输出中断。请关闭此窗口后重新打开视频。"); }, null);
                }
            }
        }
        finally { dummy?.Dispose(); }
    }

    private void Draw(ref ID3D11Texture2D? dummy)
    {
        var flags = _render.Update();
        var frame = (flags & MpvNative.UpdateFrame) != 0;
        var width = _width;
        var height = _height;
        if (width < 2 || height < 2)
        {
            if (!frame) return;
            dummy ??= _video.CreateTarget(64, 64);
            _render.Render(dummy.NativePointer, false);
            return;
        }
        var resized = EnsureSwapChain(width, height);
        if (_swapChain is null) return;
        if (!frame && !resized) return;
        using var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        var code = _render.Render(buffer.NativePointer, true);
        if (code < 0)
        {
            Log.Write("mpv_render_context_render：" + MpvNative.ErrorString(code));
            if (!_formatDropped && _format != Format.B8G8R8A8_UNorm)
            {
                _formatDropped = true;
                _format = Format.B8G8R8A8_UNorm;
                _generation++;
                _swapChain?.Dispose();
                _swapChain = null;
                _swapChainSent = false;
                _swapWidth = 0;
                _swapHeight = 0;
                Log.Write("10 位画面无法交给合成器，已改用 8 位。");
            }
            return;
        }
        var present = _swapChain.Present(1, PresentFlags.None);
        if (present.Failure)
        {
            Marshal.ThrowExceptionForHR(present.Code);
            return;
        }
        _render.ReportSwap();
        if (_recoveryPending)
        {
            _recoveryPending = false;
            _ui.Post(_ => { if (_running && !_failed) Recovered?.Invoke(); }, null);
        }
        Interlocked.Increment(ref _presented);
    }

    private bool EnsureSwapChain(int width, int height)
    {
        if (_swapChain is null)
        {
            using var factory = _video.Factory();
            try
            {
                _swapChain = CreateChain(factory, width, height, _format);
            }
            catch (Exception ex)
            {
                if (_format == Format.B8G8R8A8_UNorm) throw;
                Log.Write("10 位交换链创建失败，改用 8 位：" + ex.Message);
                _formatDropped = true;
                _format = Format.B8G8R8A8_UNorm;
                _swapChain = CreateChain(factory, width, height, _format);
            }
            _swapWidth = width;
            _swapHeight = height;
            ApplyDpi(_swapChain);
            ApplyColorSpace(_swapChain);
            var pointer = _swapChain.NativePointer;
            if (!_swapChainSent)
            {
                _swapChainSent = true;
                var generation = _generation;
                Marshal.AddRef(pointer);
                _ui.Post(_ =>
                {
                    try { if (_running && generation == _generation) Bind(pointer); }
                    finally { Marshal.Release(pointer); }
                }, null);
            }
            return true;
        }
        if (_swapWidth == width && _swapHeight == height) return false;
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
        _swapWidth = width;
        _swapHeight = height;
        ApplyDpi(_swapChain);
        return true;
    }

    private IDXGISwapChain1 CreateChain(IDXGIFactory2 factory, int width, int height, Format format) =>
        factory.CreateSwapChainForComposition(_video.Device, new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Ignore,
        });

    /// <summary>
    /// The buffer is in physical pixels. Without the inverse scale, a 150% display
    /// shows the frame 1.5 times too large and clips the bottom-right.
    /// </summary>
    private void ApplyDpi(IDXGISwapChain1 swapChain)
    {
        var sx = _scaleX < 0.1f ? 1f : _scaleX;
        var sy = _scaleY < 0.1f ? 1f : _scaleY;
        try
        {
            using var scaled = swapChain.QueryInterface<IDXGISwapChain2>();
            scaled.MatrixTransform = new Matrix3x2(1f / sx, 0, 0, 1f / sy, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Write("交换链缩放失败：" + ex.Message);
        }
    }

    /// <summary>SDR BT.709 gamma 2.2, including the 10-bit target. HDR stays tone-mapped into this.</summary>
    private static void ApplyColorSpace(IDXGISwapChain1 swapChain)
    {
        try
        {
            using var chain = swapChain.QueryInterface<IDXGISwapChain3>();
            chain.SetColorSpace1(ColorSpaceType.RgbFullG22NoneP709);
        }
        catch (Exception ex)
        {
            Log.Write("色彩空间：" + ex.Message);
        }
    }

    private void Bind(IntPtr swapChain)
    {
        if (_panel is null) return;
        try
        {
            var panel = WinRT.CastExtensions.As<ISwapChainPanelNative>(_panel);
            var set = panel.SetSwapChain(swapChain);
            if (set != 0) Log.Write("SetSwapChain：" + set);
            return;
        }
        catch (Exception ex)
        {
            Log.Write("SwapChainPanel.As 失败，改用 QueryInterface：" + ex.Message);
        }
        var unknown = Marshal.GetIUnknownForObject(_panel);
        var iid = typeof(ISwapChainPanelNative).GUID;
        var hr = Marshal.QueryInterface(unknown, ref iid, out var native);
        Marshal.Release(unknown);
        if (hr != 0)
        {
            Log.Write("SwapChainPanel 不接受 DXGI 交换链：" + hr);
            return;
        }
        var fallback = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(native);
        Marshal.Release(native);
        var code = fallback.SetSwapChain(swapChain);
        if (code != 0) Log.Write("SetSwapChain：" + code);
    }
}
