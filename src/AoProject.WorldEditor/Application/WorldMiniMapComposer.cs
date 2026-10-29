using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Builds one minimap per WORLDMAP: the 19x22 grid of 100x100 map minimaps
/// drawn at their official cell (row-major, like the client's world view).
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

    public WorldMiniMapComposer(
        IReadOnlyList<MapsWorldDataService.WorldLayout> layouts,
        MapMiniMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        _layouts = layouts;
        _options = options;
        _canvases = new Bitmap[layouts.Count];
        _graphics = new Graphics[layouts.Count];
        for (var world = 0; world < layouts.Count; world++)
        {
            var layout = layouts[world];
            _canvases[world] = new Bitmap(layout.Width * WorldMap.Width,
                layout.Height * WorldMap.Height, PixelFormat.Format32bppArgb);
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
        using var mini = _service.Create(map, graphics, _options, objects);
        foreach (var (world, column, row) in cells)
            _graphics[world].DrawImageUnscaled(mini, column * WorldMap.Width, row * WorldMap.Height);
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
            var path = Path.Combine(directory, $"worldmap{layout.WorldId}.png");
            _canvases[world].Save(path, ImageFormat.Png);
            files.Add(path);

            var width = Math.Max(1, (int)Math.Round(layout.Width * WorldMap.Width * previewScale));
            var height = Math.Max(1, (int)Math.Round(layout.Height * WorldMap.Height * previewScale));
            using var preview = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var canvas = Graphics.FromImage(preview);
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.PixelOffsetMode = PixelOffsetMode.Half;
            canvas.SmoothingMode = SmoothingMode.None;
            canvas.DrawImage(_canvases[world], new Rectangle(0, 0, width, height));
            var previewPath = Path.Combine(directory, $"worldmap{layout.WorldId}-preview.png");
            preview.Save(previewPath, ImageFormat.Png);
            files.Add(previewPath);
        }
        return files;
    }

    public void Dispose()
    {
        foreach (var canvas in _graphics) canvas.Dispose();
        foreach (var image in _canvases) image.Dispose();
    }
}
