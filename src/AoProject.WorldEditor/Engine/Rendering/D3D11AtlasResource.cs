using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AoProject.WorldEditor.Engine.Rendering;

/// <summary>GPU representation of the CPU atlas as a D3D11 texture array.</summary>
internal sealed class D3D11AtlasResource : IDisposable
{
    private readonly ID3D11ShaderResourceView _view;
    private bool _disposed;

    private D3D11AtlasResource(ID3D11ShaderResourceView view, int pageCount)
    {
        _view = view;
        PageCount = pageCount;
    }

    public int PageCount { get; }
    public ID3D11ShaderResourceView View => _view;

    public static D3D11AtlasResource Create(ID3D11Device device, IReadOnlyList<AtlasPage> pages)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            throw new ArgumentException("El atlas no contiene paginas.", nameof(pages));

        var description = new Texture2DDescription
        {
            Width = GraphicAtlas.PageSize,
            Height = GraphicAtlas.PageSize,
            MipLevels = 1,
            ArraySize = (uint)pages.Count,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource
        };
        var subresources = new SubresourceData[pages.Count];
        var handles = new GCHandle[pages.Count];
        try
        {
            for (var index = 0; index < pages.Count; index++)
            {
                handles[index] = GCHandle.Alloc(pages[index].Pixels, GCHandleType.Pinned);
                subresources[index] = new SubresourceData(
                    handles[index].AddrOfPinnedObject(),
                    (uint)(GraphicAtlas.PageSize * 4));
            }
            using var texture = device.CreateTexture2D(description, subresources);
            return new D3D11AtlasResource(device.CreateShaderResourceView(texture), pages.Count);
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _view.Dispose();
    }
}
