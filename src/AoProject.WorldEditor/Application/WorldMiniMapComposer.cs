using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Builds one minimap per WORLDMAP: map minimaps drawn on the same 75x81
/// playable-band grid used by the global editor view.
/// Empty cells stay black. A scaled preview is written next to the full image.
/// </summary>
internal sealed class WorldMiniMapComposer : IDisposable
{
    private readonly IReadOnlyList<MapsWorldDataService.WorldLayout> _layouts;
    private readonly Bitmap[] _canvases;
    private readonly Graphics[] _graphics;
    private readonly Dictionary<short, List<(int World, int Column, int Row)>> _cells = new();
    private readonly MapMiniMapOptions _options;
    private readonly MapMiniMapService _service = new();
    private readonly GraphicAverageColorService? _averages;
    private readonly IReadOnlyDictionary<short, (int X, int Y)> _shifts;

    public WorldMiniMapComposer(
        IReadOnlyList<MapsWorldDataService.WorldLayout> layouts,
        MapMiniMapOptions options, string? resourceRoot = null,
        IReadOnlyDictionary<short, (int X, int Y)>? shifts = null)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        _layouts = layouts;
        _options = options;
        _averages = string.IsNullOrWhiteSpace(resourceRoot)
            ? null
            : GraphicAverageColorService.For(resourceRoot);
        _shifts = shifts ?? new Dictionary<short, (int X, int Y)>();
        _canvases = new Bitmap[layouts.Count];
        _graphics = new Graphics[layouts.Count];
        for (var world = 0; world < layouts.Count; world++)
        {
            var layout = layouts[world];
            _canvases[world] = new Bitmap(layout.Width * GlobalChunkGeometry.WidthTiles,
                layout.Height * GlobalChunkGeometry.HeightTiles, PixelFormat.Format32bppArgb);
            _graphics[world] = Graphics.FromImage(_canvases[world]);
            _graphics[world].Clear(Color.Black);
            for (var index = 0; index < layout.MapIds.Count; index++)
            {
                var mapId = layout.MapIds[index];
                if (mapId <= 0) continue;
                if (!_cells.TryGetValue(mapId, out var cells))
                    _cells[mapId] = cells = [];
                cells.Add((world, index % layout.Width, index / layout.Width));
            }
        }
    }

    public int Worlds => _canvases.Length;

    public int LoadedMaps { get; private set; }

    /// <summary>Draws the minimap of one map in every cell that references it.</summary>
    public bool Add(short mapId, WorldMap map, IReadOnlyList<GraphicData> graphics,
        IReadOnlyList<ObjectData>? objects)
    {
        if (!_cells.TryGetValue(mapId, out var cells)) return false;
        using var mini = _averages is null
            ? _service.Create(map, graphics, _options, objects)
            : _service.CreateSmooth(map, graphics, _averages,
                _options with { ShowLayer2 = false, ShowLayer3 = false, ShowLayer4 = false }, objects);
        var shift = _shifts.TryGetValue(mapId, out var value) ? value : (X: 0, Y: 0);
        foreach (var (world, column, row) in cells)
            _graphics[world].DrawImageUnscaled(mini,
                column * GlobalChunkGeometry.WidthTiles - shift.X,
                row * GlobalChunkGeometry.HeightTiles - shift.Y);
        LoadedMaps++;
        return true;
    }

    /// <summary>Writes worldmap&lt;id&gt;.png plus a quarter-size preview.</summary>
    public IReadOnlyList<string> Save(string directory, double previewScale = 0.25)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        for (var world = 0; world < _canvases.Length; world++)
        {
            var layout = _layouts[world];
            var path = Path.Combine(directory, ImageName(layout.WorldId));
            File.WriteAllBytes(path, EncodePng(_canvases[world]));
            files.Add(path);

            var width = Math.Max(1, (int)Math.Round(layout.Width * GlobalChunkGeometry.WidthTiles * previewScale));
            var height = Math.Max(1, (int)Math.Round(layout.Height * GlobalChunkGeometry.HeightTiles * previewScale));
            using var preview = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var canvas = Graphics.FromImage(preview);
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.PixelOffsetMode = PixelOffsetMode.Half;
            canvas.SmoothingMode = SmoothingMode.None;
            canvas.DrawImage(_canvases[world], new Rectangle(0, 0, width, height));
            var previewPath = Path.Combine(directory, PreviewName(layout.WorldId));
            preview.Save(previewPath, ImageFormat.Png);
            files.Add(previewPath);
        }
        return files;
    }

    /// <summary>Returns the generated full-world PNGs without writing sidecar files.</summary>
    public IReadOnlyList<(string Name, byte[] Data)> EncodeWorldImages()
    {
        var result = new List<(string Name, byte[] Data)>(_canvases.Length);
        for (var world = 0; world < _canvases.Length; world++)
            result.Add(($"worldmap/" + ImageName(_layouts[world].WorldId), EncodePng(_canvases[world])));
        return result;
    }

    private static string ImageName(int worldId) => worldId == 1 ? "world.png" : $"world-{worldId}.png";

    private static string PreviewName(int worldId) => worldId == 1 ? "world-preview.png" : $"world-{worldId}-preview.png";

    private static byte[] EncodePng(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose()
    {
        foreach (var canvas in _graphics) canvas.Dispose();
        foreach (var image in _canvases) image.Dispose();
    }
}
