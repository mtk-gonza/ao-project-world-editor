using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Average colour of each GRH's sprite region, computed once per image region and
/// cached. Using the real average instead of the legacy palette colour removes the
/// aliasing (stripes) when the result is drawn at 1-2 px per tile.
/// </summary>
internal sealed class GraphicAverageColorService : IDisposable
{
    /// <summary>Cap of decoded PNGs kept open; the computed colours stay cached.</summary>
    private const int TextureCacheLimit = 64;

    private static readonly object SharedSync = new();
    private static readonly Dictionary<string, GraphicAverageColorService> Shared =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();
    private readonly GraphicSourceResolver? _resolver;
    private readonly Dictionary<string, Bitmap?> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, int> _cache = [];

    /// <summary>
    /// Shared instance per resource root. Building the world minimaps used to
    /// create one service per map, so the same PNGs were decoded 418 times and
    /// starved the renderer.
    /// </summary>
    public static GraphicAverageColorService For(string resourceRoot)
    {
        lock (SharedSync)
        {
            if (Shared.TryGetValue(resourceRoot, out var existing)) return existing;
            var created = new GraphicAverageColorService(resourceRoot);
            Shared[resourceRoot] = created;
            return created;
        }
    }

    public GraphicAverageColorService(string resourceRoot)
    {
        try { _resolver = new GraphicSourceResolver(resourceRoot); }
        catch (DirectoryNotFoundException) { _resolver = null; }
    }

    /// <summary>ARGB colour, or 0 when the sprite has no pixels.</summary>
    public int Get(GraphicData data)
    {
        if (_resolver is null || data.FileNumber <= 0 || data.PixelWidth <= 0 || data.PixelHeight <= 0)
            return 0;
        var key = ((long)data.FileNumber << 40) | ((long)data.SourceX << 30) |
                  ((long)data.SourceY << 20) | ((long)data.PixelWidth << 10) | (long)(ushort)data.PixelHeight;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var value = Compute(data);
            _cache[key] = value;
            return value;
        }
    }

    private int Compute(GraphicData data)
    {
        try
        {
            var pathKey = data.FileNumber.ToString();
            if (!_textures.TryGetValue(pathKey, out var texture))
            {
                if (_textures.Count >= TextureCacheLimit) ReleaseTextures();
                var source = _resolver!.Resolve(data);
                var loaded = new Bitmap(source.Path);
                _textures[pathKey] = loaded;
                texture = loaded;
            }
            if (texture is null) return 0;
            var stepX = Math.Max(1, data.PixelWidth / 8);
            var stepY = Math.Max(1, data.PixelHeight / 8);
            long totalR = 0, totalG = 0, totalB = 0;
            var samples = 0;
            for (int py = data.SourceY; py < data.SourceY + data.PixelHeight; py += stepY)
            for (int px = data.SourceX; px < data.SourceX + data.PixelWidth; px += stepX)
            {
                var pixel = texture.GetPixel(
                    Math.Clamp(px, 0, texture.Width - 1),
                    Math.Clamp(py, 0, texture.Height - 1));
                if (pixel.A == 0 || (pixel.R == 0 && pixel.G == 0 && pixel.B == 0 && data.PixelWidth <= 32))
                    continue;
                totalR += pixel.R; totalG += pixel.G; totalB += pixel.B;
                samples++;
            }
            if (samples == 0) return 0;
            return unchecked((int)0xFF000000) |
                   ((int)(totalR / samples) << 16) | ((int)(totalG / samples) << 8) | (int)(totalB / samples);
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        lock (_sync) ReleaseTextures();
    }

    private void ReleaseTextures()
    {
        foreach (var texture in _textures.Values) texture?.Dispose();
        _textures.Clear();
    }
}
