using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Engine.Rendering;

internal readonly record struct AtlasRegion(
    int Page,
    float U0,
    float V0,
    float U1,
    float V1,
    int Width,
    int Height,
    float TileWidth,
    float TileHeight);

internal sealed class AtlasPage
{
    public AtlasPage(int size)
    {
        Pixels = new byte[size * size * 4];
    }

    public byte[] Pixels { get; }
    public List<(int Y, int Height, int CursorX)> Shelves { get; } = [];
    public int Bottom { get; set; }
}

/// <summary>CPU atlas with deterministic shelf packing and transparent padding.</summary>
internal sealed class GraphicAtlas : IDisposable
{
    public const int PageSize = 2048;
    public const int Padding = 1;
    private readonly GraphicSourceResolver _resolver;
    private readonly Dictionary<(string Path, int X, int Y, int Width, int Height, float TileWidth, float TileHeight), AtlasRegion> _regions = [];
    private readonly List<AtlasPage> _pages = [];
    private bool _disposed;

    public GraphicAtlas(string resourceRoot)
    {
        _resolver = new GraphicSourceResolver(resourceRoot);
    }

    public IReadOnlyList<AtlasPage> Pages => _pages;
    public int RegionCount => _regions.Count;

    public bool TryResolve(GraphicData graphic, out AtlasRegion region)
    {
        region = default;
        if (_disposed || graphic.FileNumber <= 0 || graphic.PixelWidth <= 0 || graphic.PixelHeight <= 0)
            return false;
        GraphicSource source;
        try { source = _resolver.Resolve(graphic); }
        catch (FileNotFoundException) { return false; }
        var tileWidth = graphic.TileWidth <= 0 ? 1f : graphic.TileWidth;
        var tileHeight = graphic.TileHeight <= 0 ? 1f : graphic.TileHeight;
        var key = (source.Path, source.SourceX, source.SourceY, source.Width, source.Height, tileWidth, tileHeight);
        if (_regions.TryGetValue(key, out region))
            return true;
        if (!TryPlace(source.Width, source.Height, out var page, out var x, out var y) ||
            !Copy(source, page, x, y))
            return false;
        region = new AtlasRegion(
            _pages.IndexOf(page),
            x / (float)PageSize,
            y / (float)PageSize,
            (x + source.Width) / (float)PageSize,
            (y + source.Height) / (float)PageSize,
            source.Width,
            source.Height,
            tileWidth,
            tileHeight);
        _regions[key] = region;
        return true;
    }

    private bool TryPlace(int width, int height, out AtlasPage page, out int x, out int y)
    {
        page = _pages.LastOrDefault() ?? NewPage();
        if (!TryPlaceOn(page, width + Padding, height + Padding, out x, out y))
        {
            page = NewPage();
            if (!TryPlaceOn(page, width + Padding, height + Padding, out x, out y))
                return false;
        }
        return true;
    }

    private AtlasPage NewPage()
    {
        var page = new AtlasPage(PageSize);
        _pages.Add(page);
        return page;
    }

    private static bool TryPlaceOn(AtlasPage page, int width, int height, out int x, out int y)
    {
        x = 0;
        y = 0;
        foreach (var shelf in page.Shelves.ToArray())
        {
            var index = page.Shelves.IndexOf(shelf);
            if (shelf.Height < height || shelf.CursorX + width > PageSize)
                continue;
            x = shelf.CursorX;
            y = shelf.Y;
            page.Shelves[index] = (shelf.Y, shelf.Height, shelf.CursorX + width);
            return true;
        }
        if (page.Bottom + height > PageSize)
            return false;
        y = page.Bottom;
        page.Shelves.Add((y, height, width));
        page.Bottom += height;
        return true;
    }

    private static bool Copy(GraphicSource source, AtlasPage page, int x, int y)
    {
        Bitmap bitmap;
        try { bitmap = new Bitmap(source.Path); }
        catch (Exception) { return false; }
        using var _ = bitmap;
        if (source.SourceX < 0 || source.SourceY < 0 || source.SourceX + source.Width > bitmap.Width ||
            source.SourceY + source.Height > bitmap.Height)
            return false;
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            for (var iy = 0; iy < source.Height; iy++)
            {
                Marshal.Copy(data.Scan0 + (source.SourceY + iy) * data.Stride, row, 0, row.Length);
                var sourceOffset = source.SourceX * 4;
                var targetOffset = ((y + iy) * PageSize + x) * 4;
                for (var ix = 0; ix < source.Width; ix++)
                {
                    var pixel = sourceOffset + ix * 4;
                    var target = targetOffset + ix * 4;
                    // The official WorldEditor creates textures with the
                    // Direct3D color key &HFF000000. Legacy graphics use
                    // opaque pure-black pixels for transparency; preserve the
                    // original RGB for every other pixel and only clear alpha
                    // for that exact key.
                    page.Pixels[target] = row[pixel];
                    page.Pixels[target + 1] = row[pixel + 1];
                    page.Pixels[target + 2] = row[pixel + 2];
                    page.Pixels[target + 3] = row[pixel + 3];
                    if (row[pixel] == 0 && row[pixel + 1] == 0 && row[pixel + 2] == 0)
                        page.Pixels[target + 3] = 0;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _regions.Clear();
        _pages.Clear();
    }
}
