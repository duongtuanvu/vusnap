using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VuSnap.Services;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace VuSnap.Tests;

internal static class GpuCropTest
{
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr wrapper);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr surface, out IntPtr wrapper);

    public static void Run()
    {
        // WARP exercises D3D11 readback on a hosted runner without a physical monitor.
        using var device = D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var devicePtr));
        IDirect3DDevice wrappedDevice;
        try { wrappedDevice = MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr); }
        finally { Marshal.Release(devicePtr); }

        const int width = 113, height = 67;
        var pixels = Enumerable.Range(0, width * height).Select(i => unchecked((int)0xFF000000) | i).ToArray();
        using (wrappedDevice)
        using (var native = ScrollGpuCrop.OpenDevice(wrappedDevice))
        using (var context = native.ImmediateContext)
        using (var texture = device.CreateTexture2D(pixels, Format.B8G8R8A8_UNorm, width, height))
        using (var dxgiSurface = texture.QueryInterface<IDXGISurface>())
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var surfacePtr));
            IDirect3DSurface surface;
            try { surface = MarshalInterface<IDirect3DSurface>.FromAbi(surfacePtr); }
            finally { Marshal.Release(surfacePtr); }
            var crop = new Rectangle(54, 21, 59, 46);
            using (surface)
            using (var reader = new ScrollGpuCrop(native, context, crop))
            {
                var actual = new int[crop.Width * crop.Height];
                if (reader.TryCopy(surface, width + 1, height, actual)) throw new Exception("Resized surface accepted");
                if (!reader.TryCopy(surface, width, height, actual)) throw new Exception("Valid crop refused");
                for (int y = 0; y < crop.Height; y++)
                    for (int x = 0; x < crop.Width; x++)
                        if (actual[y * crop.Width + x] != pixels[(y + crop.Top) * width + x + crop.Left])
                            throw new Exception($"Wrong pixel at ({x}, {y})");
            }
        }
    }
}
