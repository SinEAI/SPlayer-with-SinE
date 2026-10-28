using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SPlayer;

/// <summary>A multithread-protected D3D11 device shared with libmpv. Hardware first, WARP if the GPU device cannot be created.</summary>
public sealed class D3D11Video : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public bool Hardware { get; }

    private D3D11Video(ID3D11Device device, ID3D11DeviceContext context, bool hardware)
    {
        Device = device;
        Context = context;
        Hardware = hardware;
        using var multithread = device.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
    }

    public static D3D11Video Create()
    {
        var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out var device, out _, out var context).Success)
            return new D3D11Video(device, context, true);
        device?.Dispose();
        context?.Dispose();
        var warp = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out _, out context);
        if (warp.Failure || device is null || context is null)
            throw new InvalidOperationException("无法创建 D3D11 设备：" + warp);
        return new D3D11Video(device, context, false);
    }

    public ID3D11Texture2D CreateTarget(uint width, uint height)
    {
        return Device.CreateTexture2D(new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        });
    }

    public byte[] ReadBgra(ID3D11Texture2D source)
    {
        var description = source.Description;
        using var staging = Device.CreateTexture2D(new Texture2DDescription
        {
            Width = description.Width,
            Height = description.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = description.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        Context.CopyResource(staging, source);
        Context.Flush();
        var mapped = Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var width = (int)description.Width;
            var height = (int)description.Height;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var row = IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch);
                System.Runtime.InteropServices.Marshal.Copy(row, pixels, y * width * 4, width * 4);
            }
            return pixels;
        }
        finally
        {
            Context.Unmap(staging, 0);
        }
    }

    public static double Variance(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4) return 0;
        double count = bgra.Length / 4;
        double red = 0, green = 0, blue = 0;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            blue += bgra[i];
            green += bgra[i + 1];
            red += bgra[i + 2];
        }
        red /= count;
        green /= count;
        blue /= count;
        double variance = 0;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var db = bgra[i] - blue;
            var dg = bgra[i + 1] - green;
            var dr = bgra[i + 2] - red;
            variance += dr * dr + dg * dg + db * db;
        }
        return variance / count;
    }

    public IDXGIFactory2 Factory()
    {
        using var dxgi = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        return adapter.GetParent<IDXGIFactory2>();
    }

    public void Dispose()
    {
        Context.Dispose();
        Device.Dispose();
    }
}
