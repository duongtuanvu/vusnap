using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Rectangle = System.Drawing.Rectangle;

namespace VuSnap.Services;

internal sealed class ScrollGpuCrop : IDisposable
{
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(ref Guid iid);
    }

    private readonly Rectangle _crop;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _staging;

    internal static ID3D11Device OpenDevice(IDirect3DDevice device)
    {
        var iid = typeof(ID3D11Device).GUID;
        return new ID3D11Device(device.As<IDirect3DDxgiInterfaceAccess>().GetInterface(ref iid));
    }

    public ScrollGpuCrop(ID3D11Device device, ID3D11DeviceContext context, Rectangle crop)
    {
        _crop = crop;
        _context = context;
        // WGC and this worker use the same immediate context.
        using var multithread = context.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
        _staging = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)crop.Width, (uint)crop.Height, mipLevels: 1,
            bindFlags: BindFlags.None, usage: ResourceUsage.Staging, cpuAccessFlags: CpuAccessFlags.Read));
    }

    public bool TryCopy(IDirect3DSurface surface, int sourceWidth, int sourceHeight, int[] destination)
    {
        var iid = typeof(ID3D11Texture2D).GUID;
        using var texture = new ID3D11Texture2D(surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(ref iid));
        var description = texture.Description;
        if (description.Width != sourceWidth || description.Height != sourceHeight) return false;

        var region = new Box(_crop.Left, _crop.Top, 0, _crop.Right, _crop.Bottom, 1);
        _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, texture, 0, region);
        var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int pitch = checked((int)mapped.RowPitch);
            if (pitch == checked(_crop.Width * sizeof(int)))
                Marshal.Copy(mapped.DataPointer, destination, 0, checked(_crop.Width * _crop.Height));
            else
                for (int y = 0; y < _crop.Height; y++)
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, checked(y * pitch)), destination, y * _crop.Width, _crop.Width);
            return true;
        }
        finally { _context.Unmap(_staging, 0); }
    }

    public void Dispose() => _staging.Dispose();
}
