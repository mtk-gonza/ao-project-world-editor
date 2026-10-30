using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Engine.Rendering;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;
using System.Drawing.Drawing2D;

namespace AoProject.WorldEditor.Presentation.Features.World;

internal sealed class WorldMiniMapControl : Control
{
    private GlobalWorldDocument? _world;
    private readonly Dictionary<short, Bitmap> _maps = [];
    private readonly System.Windows.Forms.Timer _buildTimer;
    private List<GlobalSectorPlacement> _pending = [];
    private IReadOnlyList<GraphicData>? _graphics;
    private string? _resources;
    private (int MinX, int MinY, int MaxX, int MaxY) _bounds;
    private Func<(float X, float Y, float Width, float Height)>? _viewport;
    private Action<int, int>? _center;
    private bool _building;
    private int _buildGeneration;

    public WorldMiniMapControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(20, 24, 30);
        DoubleBuffered = true;
        _buildTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _buildTimer.Tick += (_, _) => BuildBatch();
        MouseClick += (_, e) =>
        {
            if (_detail is not null && _detailCenter is not null)
            {
                var tileX = (int)MathF.Floor((e.X - 6 + _detailOffsetX) / (DetailScale * Math.Max(0.01f, _detailFit))) + 1;
                var tileY = (int)MathF.Floor((e.Y - 6 + _detailOffsetY) / (DetailScale * Math.Max(0.01f, _detailFit))) + 1;
                _detailCenter(Math.Clamp(tileX, 1, WorldSector.Width), Math.Clamp(tileY, 1, WorldSector.Height));
                return;
            }
            if (_unifiedOverview is not null && _unifiedCenter is not null)
            {
                var fit = Math.Min((float)(ClientSize.Width - 12) / _unifiedOverview.Width,
                    (float)(ClientSize.Height - 12) / _unifiedOverview.Height);
                fit = Math.Max(.01f, fit);
                var left = (ClientSize.Width - _unifiedOverview.Width * fit) / 2f;
                var top = (ClientSize.Height - _unifiedOverview.Height * fit) / 2f;
                var tileX = (int)MathF.Floor((e.X - left) / (4f * fit));
                var tileY = (int)MathF.Floor((e.Y - top) / (4f * fit));
                if (tileX >= 0 && tileY >= 0 &&
                    tileX < _unifiedOverview.Width / 4 && tileY < _unifiedOverview.Height / 4)
                    _unifiedCenter(tileX, tileY);
                return;
            }
            if (_world is null || _center is null || _maps.Count == 0) return;
            var scale = ScaleFactor;
            _center((int)MathF.Floor(_bounds.MinX + (e.X - 6) / scale),
                (int)MathF.Floor(_bounds.MinY + (e.Y - 6) / scale));
        };
    }

    private float ScaleFactor => Math.Min((float)(ClientSize.Width - 12) / Math.Max(1, _bounds.MaxX - _bounds.MinX),
        (float)(ClientSize.Height - 12) / Math.Max(1, _bounds.MaxY - _bounds.MinY));

    // 8 px per tile keeps the tile borders and water waves from aliasing when the
    // panel downscales again to ~208 px.
    private const int DetailScale = 8;
    private Bitmap? _detail;
    private Func<(int X, int Y)?>? _detailSelection;
    private Action<int, int>? _detailCenter;
    private int _detailGeneration;
    private float _detailFit;
    private float _detailOffsetX;
    private float _detailOffsetY;

    private Bitmap? _worldImage;
    private Bitmap? _radarImage;
    private Bitmap? _unifiedOverview;
    private Func<(float X, float Y, float Width, float Height)>? _unifiedViewport;
    private Action<int, int>? _unifiedCenter;
    private WorldBorderStitcher? _stitcher;

    /// <summary>
    /// Radar pixel of a map tile, using the same collapsed grid as the renderer.
    /// </summary>
    public (int X, int Y)? RadarPixel(GlobalChunkCoordinate chunk, int localX, int localY)
    {
        if (_world is null) return null;
        var (shiftX, shiftY) = StitchOf(chunk);
        return (chunk.X * WorldRadarService.RadarStrideX + (localX - WorldRadarService.MinTileX) - shiftX,
            chunk.Y * WorldRadarService.RadarStrideY + (localY - WorldRadarService.MinTileY) - shiftY);
    }

    /// <summary>AO_RADAR_DEBUG: collapse applied to a chunk (0,0 means none).</summary>
    public (int X, int Y) DebugStitch(GlobalChunkCoordinate chunk) => StitchOf(chunk);

    private (int X, int Y) StitchOf(GlobalChunkCoordinate chunk)
    {
        if (_stitcher is null) return (0, 0);
        if (!_stitcher.IsComplete)
            _stitcher.Prewarm(() => { try { BeginInvoke(new Action(() => { WorldImageChanged?.Invoke(); Invalidate(); })); } catch (InvalidOperationException) { } });
        return _stitcher.For(chunk);
    }

    /// <summary>
    /// World image composed from the per-map minimaps (100 px per map). The radar
    /// crops this image, like the client does with mapaN_200x200.bmp but at a
    /// resolution that keeps a 200 tile window readable.
    /// </summary>
    public Bitmap? WorldImage => _worldImage;

    public bool IsWorldImageComplete => _world is not null && _worldImage is not null &&
        _pending.Count == 0 && !_building;

    public async Task WaitForWorldImageAsync()
    {
        while (_world is not null && !IsWorldImageComplete)
            await Task.Delay(50);
        if (_world is not null && _worldImage is null)
            throw new InvalidOperationException("El mapa mundial no pudo componerse.");
    }

    /// <summary>Core-only composition (1 px/tile) used by the radar crop.</summary>
    public Bitmap? RadarImage => _radarImage;

    /// <summary>
    /// Raised after the composed world image changes (a map minimap finished
    /// building). The radar must recrop: otherwise it keeps the first crop, taken
    /// before the surrounding maps existed, and shows black until the camera moves.
    /// </summary>
    public event Action? WorldImageChanged;

    /// <summary>AO_RADAR_DEBUG only: the minimap built for a map.</summary>
    public Bitmap? DebugMapImage(short mapId) => _maps.TryGetValue(mapId, out var image) ? image : null;

    /// <summary>AO_RADAR_DEBUG only: maps already composed.</summary>
    public int BuiltCount => _maps.Count;

    /// <summary>AO_RADAR_DEBUG only: maps still waiting in the queue.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Smooth minimap of the loaded map (1 px/tile), used by the radar in map mode.</summary>
    public Bitmap? MapImage => _detail;

    /// <summary>
    /// Single-map mode: a real miniature of the tile art (2 px per tile),
    /// centered on the selected tile. The world overview keeps the flat colour
    /// map because at 220 px a world tile is sub-pixel.
    /// </summary>
    public void SetLocalMap(WorldSector? map, WorldEditorResourceCatalog? resources,
        Func<(int X, int Y)?> selected, Action<int, int> center)
    {
        _unifiedOverview?.Dispose(); _unifiedOverview = null;
        _unifiedViewport = null; _unifiedCenter = null;
        foreach (var image in _maps.Values) image.Dispose();
        _maps.Clear();
        _pending.Clear();
        _world = null;
        _buildGeneration++;
        _buildTimer.Stop();
        // The world composition is stale in single-map mode; the radar must fall
        // back to the map minimap instead of cropping the old world.
        _worldImage?.Dispose(); _worldImage = null;
        _radarImage?.Dispose(); _radarImage = null;
        _detail?.Dispose();
        _detail = null;
        _detailSelection = selected;
        _detailCenter = center;
        var generation = ++_detailGeneration;
        if (map is null || resources is null)
        {
            Invalidate();
            return;
        }
        _ = Task.Run(() =>
        {
            Bitmap? image = null;
            try
            {
                using var averages = new GraphicAverageColorService(resources.Root);
                image = new MapMiniMapService().CreateSmooth(map, resources.Graphics, averages,
                    new MapMiniMapOptions(ShowNpcs: true), resources.Objects);
            }
            catch (Exception error)
            {
                TryLogMinimapError(0, error);
            }
            if (IsDisposed) { image?.Dispose(); return; }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (generation != _detailGeneration) { image?.Dispose(); return; }
                    _detail?.Dispose();
                    _detail = image;
                    // The radar in map mode uses this minimap: it was requested
                    // before the build finished, so it showed "Radar sin datos".
                    WorldImageChanged?.Invoke();
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                image?.Dispose();
            }
        });
        Invalidate();
    }

    /// <summary>
    /// Adds one built minimap to the composed images. Rebuilding the whole world
    /// image for every map (418 times, 1425x1782 px on the UI thread) froze the
    /// renderer, so only the new cell is drawn.
    /// </summary>
    private void AddMapToWorldImage(GlobalSectorPlacement placement, Bitmap mapImage)
    {
        if (_world is null) return;
        var cellsX = Math.Max(1, _world.Assembly.Width);
        var cellsY = Math.Max(1, _world.Assembly.Height);
        // The global renderer joins playable map bands, not 100x100 file
        // rectangles. Keep this bitmap on the same 75x81 grid used by the
        // viewport, hit-testing and radar.
        var worldWidth = cellsX * GlobalChunkGeometry.WidthTiles;
        var worldHeight = cellsY * GlobalChunkGeometry.HeightTiles;
        var radarWidth = cellsX * WorldRadarService.RadarStrideX;
        var radarHeight = cellsY * WorldRadarService.RadarStrideY;
        if (_worldImage is null || _worldImage.Width != worldWidth || _worldImage.Height != worldHeight ||
            _radarImage is null || _radarImage.Width != radarWidth || _radarImage.Height != radarHeight)
        {
            _worldImage?.Dispose();
            _radarImage?.Dispose();
            // MAPA keeps the previous look: the whole 100x100 minimap per cell.
            _worldImage = new Bitmap(worldWidth, worldHeight);
            // RADAR needs the playable core only, so neighbouring maps are
            // contiguous. Same stride as the renderer (75x81): pixel-exact crop.
            _radarImage = new Bitmap(radarWidth, radarHeight);
            using var clear = Graphics.FromImage(_worldImage);
            clear.Clear(Color.Black);
            using var clearRadar = Graphics.FromImage(_radarImage);
            clearRadar.Clear(Color.Black);
        }
        using (var canvas = Graphics.FromImage(_worldImage))
        {
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.PixelOffsetMode = PixelOffsetMode.Half;
            var (shiftX, shiftY) = StitchOf(placement.Chunk);
            canvas.DrawImage(mapImage,
                new RectangleF(placement.Chunk.X * GlobalChunkGeometry.WidthTiles - shiftX,
                    placement.Chunk.Y * GlobalChunkGeometry.HeightTiles - shiftY,
                    WorldSector.Width, WorldSector.Height),
                new RectangleF(0, 0, mapImage.Width, mapImage.Height), GraphicsUnit.Pixel);
        }
        var coreWidth = Math.Min(WorldRadarService.RadarStrideX, mapImage.Width - (WorldRadarService.MinTileX - 1));
        var coreHeight = Math.Min(WorldRadarService.RadarStrideY, mapImage.Height - (WorldRadarService.MinTileY - 1));
        if (coreWidth <= 0 || coreHeight <= 0) return;
        using (var radarCanvas = Graphics.FromImage(_radarImage))
        {
            radarCanvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            radarCanvas.PixelOffsetMode = PixelOffsetMode.Half;
            var (shiftX, shiftY) = StitchOf(placement.Chunk);
            radarCanvas.DrawImage(mapImage,
                new Rectangle(placement.Chunk.X * WorldRadarService.RadarStrideX - shiftX,
                    placement.Chunk.Y * WorldRadarService.RadarStrideY - shiftY, coreWidth, coreHeight),
                WorldRadarService.MinTileX - 1, WorldRadarService.MinTileY - 1, coreWidth, coreHeight,
                GraphicsUnit.Pixel);
        }
        WorldImageChanged?.Invoke();
    }

    private void SetLocalLayout()    {
        if (_detail is null) return;
        _detailFit = Math.Min((float)(ClientSize.Width - 12) / _detail.Width,
            (float)(ClientSize.Height - 12) / _detail.Height);
        var drawWidth = _detail.Width * _detailFit;
        var drawHeight = _detail.Height * _detailFit;
        _detailOffsetX = 0;
        _detailOffsetY = 0;
        if (_detailSelection?.Invoke() is not { } tile) return;
        var centerX = (tile.X - 0.5f) * DetailScale * _detailFit;
        var centerY = (tile.Y - 0.5f) * DetailScale * _detailFit;
        _detailOffsetX = Math.Clamp(centerX - ClientSize.Width / 2f, 0f, Math.Max(0f, drawWidth - ClientSize.Width));
        _detailOffsetY = Math.Clamp(centerY - ClientSize.Height / 2f, 0f, Math.Max(0f, drawHeight - ClientSize.Height));
    }

    public void SetWorld(GlobalWorldDocument? world, IReadOnlyList<GraphicData>? graphics,
        Func<(float X, float Y, float Width, float Height)> viewport, Action<int, int> center,
        string? resourceRoot = null)
    {
        _unifiedOverview?.Dispose(); _unifiedOverview = null;
        _unifiedViewport = null; _unifiedCenter = null;
        foreach (var image in _maps.Values) image.Dispose();
        _maps.Clear(); _pending.Clear(); _world = world; _graphics = graphics; _resources = resourceRoot; _viewport = viewport; _center = center;
        // Drop the composed images: keeping them would let the radar crop a world
        // (or level) that is no longer the one on screen.
        _worldImage?.Dispose(); _worldImage = null;
        _radarImage?.Dispose(); _radarImage = null;
        _detail?.Dispose(); _detail = null; _detailGeneration++; _detailSelection = null; _detailCenter = null;
        _building = false;
        _buildGeneration++;
        _buildTimer.Stop();
        if (world is not null && graphics is not null)
        {
            // Collapse the junctions whose border the CSM duplicates, so the radar
            // image uses the same grid as the renderer (and the marker stays exact).
            // Lines are evaluated on demand: doing it here forced loading every map.
            _stitcher = new WorldBorderStitcher(world.Assembly.Placements, mapId =>
            {
                if (!world.Paths.TryGetValue(mapId, out var path)) return null;
                try { return WorldBorderStitcher.Sample(new SectorCsmFileService().Load(path)); }
                catch { return null; }
            });
            var placements = world.Assembly.Placements.Where(item => world.Maps.ContainsKey(item.SectorId)).ToArray();
            if (placements.Length > 0)
            {
                _bounds = (placements.Min(item => item.Chunk.X) * GlobalChunkGeometry.WidthTiles +
                        GlobalChunkGeometry.CoreMinX - 1,
                    placements.Min(item => item.Chunk.Y) * GlobalChunkGeometry.HeightTiles +
                        GlobalChunkGeometry.CoreMinY - 1,
                    placements.Max(item => item.Chunk.X) * GlobalChunkGeometry.WidthTiles +
                        GlobalChunkGeometry.CoreMaxX,
                    placements.Max(item => item.Chunk.Y) * GlobalChunkGeometry.HeightTiles +
                        GlobalChunkGeometry.CoreMaxY);
                _pending = placements.DistinctBy(item => item.SectorId).ToList();
                _buildTimer.Start();
            }
        }
        Invalidate();
    }

    /// <summary>
    /// Shows the generated floor overview in MAPA. The editable and
    /// game-authoritative tiles remain the UnifiedWorldMap rendered by the main
    /// canvas; this bitmap is only the small navigation overview.
    /// </summary>
    public void SetUnifiedOverview(Bitmap? image,
        Func<(float X, float Y, float Width, float Height)>? viewport = null,
        Action<int, int>? center = null)
    {
        _unifiedOverview?.Dispose();
        _unifiedOverview = image;
        _unifiedViewport = viewport;
        _unifiedCenter = center;
        _world = null;
        foreach (var mapImage in _maps.Values) mapImage.Dispose();
        _maps.Clear();
        _pending.Clear();
        _detail?.Dispose(); _detail = null;
        _worldImage?.Dispose(); _worldImage = null;
        _radarImage?.Dispose(); _radarImage = null;
        _buildTimer.Stop();
        Invalidate();
    }

    private void BuildBatch()
    {
        if (_world is null || _graphics is null || _pending.Count == 0)
        {
            if (_pending.Count == 0) _buildTimer.Stop();
            return;
        }
        if (_building) return;
        // The radar crops the world image around the viewport, so build the maps
        // closest to the view first. FIFO order filled the top-left corner of the
        // level and the radar showed black until the whole world was done.
        var placement = TakeNearestToView();
        var world = _world;
        var graphics = _graphics;
        var resources = _resources;
        var generation = _buildGeneration;
        var visibleIds = VisibleSectorIds(world);
        _building = true;
        _ = Task.Run(() =>
        {
            Bitmap? image = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!world.Maps.TryGetValue(placement.SectorId, out var map)) return;
                // Average of the real tile art: the legacy palette alternates per
                // tile and reads as stripes once the world image is scaled down.
                // The service is shared per resource root so the PNGs are decoded
                // once instead of once per map.
                if (resources is not null)
                {
                    image = new MapMiniMapService().CreateSmooth(map, graphics,
                        GraphicAverageColorService.For(resources),
                        new MapMiniMapOptions(ShowLayer2: false, ShowLayer3: false, ShowLayer4: false));
                }
                else
                {
                    image = new MapMiniMapService().Create(map, graphics,
                        new MapMiniMapOptions(ShowLayer2: false, ShowLayer3: false, ShowLayer4: false));
                }
                // Keep memory bounded without evicting maps that are still on
                // screen: the renderer draws the visible chunks plus one ring, so
                // trim with a wider margin than it needs.
                world.TrimLoadedMaps(visibleIds);
            }
            catch (Exception error)
            {
                TryLogMinimapError(placement.SectorId, error);
            }
            if (Environment.GetEnvironmentVariable("AO_RADAR_DEBUG") == "1")
                LogBuild($"{placement.SectorId} {watch.ElapsedMilliseconds} ms pendientes={_pending.Count}");
            if (IsDisposed) { image?.Dispose(); return; }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _building = false;
                    if (image is null || generation != _buildGeneration || !ReferenceEquals(_world, world))
                    { image?.Dispose(); return; }
                    _maps[placement.SectorId] = image;
                    AddMapToWorldImage(placement, image);
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                image?.Dispose();
            }
        });
    }

    /// <summary>Chunk under the viewport center (0,0 when there is no view yet).</summary>
    private GlobalChunkCoordinate ViewCenterChunk()
    {
        if (_viewport is null) return new GlobalChunkCoordinate(0, 0);
        var view = _viewport();
        return new GlobalChunkCoordinate(
            (int)MathF.Floor((view.X + view.Width / 2f) / GlobalChunkGeometry.WidthTiles),
            (int)MathF.Floor((view.Y + view.Height / 2f) / GlobalChunkGeometry.HeightTiles));
    }

    /// <summary>Removes and returns the pending map closest to the viewport center.</summary>
    private GlobalSectorPlacement TakeNearestToView()
    {
        var center = ViewCenterChunk();
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var index = 0; index < _pending.Count; index++)
        {
            var chunk = _pending[index].Chunk;
            var distance = Math.Abs(chunk.X - center.X) + Math.Abs(chunk.Y - center.Y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = index;
            if (distance == 0) break;
        }
        var placement = _pending[best];
        _pending.RemoveAt(best);
        return placement;
    }

    private IReadOnlyCollection<short> VisibleSectorIds(GlobalWorldDocument world)    {
        if (_viewport is null) return Array.Empty<short>();
        var view = _viewport();
        // Wider than the renderer's own ring (one chunk): the minimap must never
        // evict a map the renderer is drawing.
        var minX = (int)MathF.Floor(view.X / GlobalChunkGeometry.WidthTiles) - 3;
        var minY = (int)MathF.Floor(view.Y / GlobalChunkGeometry.HeightTiles) - 3;
        var maxX = (int)MathF.Ceiling((view.X + view.Width) / GlobalChunkGeometry.WidthTiles) + 3;
        var maxY = (int)MathF.Ceiling((view.Y + view.Height) / GlobalChunkGeometry.HeightTiles) + 3;
        return world.Assembly.Placements
            .Where(item => item.Chunk.X >= minX && item.Chunk.X <= maxX &&
                item.Chunk.Y >= minY && item.Chunk.Y <= maxY)
            .Select(item => item.SectorId)
            .Distinct()
            .ToArray();
    }

    /// <summary>AO_RADAR_DEBUG=1: per-map build timing.</summary>
    private static void LogBuild(string message)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("minimap-debug.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void TryLogMinimapError(short mapId, Exception error)    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("minimap-error.log"),
                $"[{DateTimeOffset.Now:O}] map {mapId}: {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError)
        {
            System.Diagnostics.Trace.TraceError($"WorldEditor minimap log failed: {logError}");
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_detail is not null)
        {
            SetLocalLayout();
            // Downscaling the 4 px/tile miniature with nearest-neighbour aliases the
            // tile art into stripes (water waves at 2 px/tile). Use a high quality
            // filter when shrinking and nearest only when zooming in.
            e.Graphics.InterpolationMode = _detailFit < 1f
                ? InterpolationMode.HighQualityBicubic
                : InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = _detailFit < 1f ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            var state = e.Graphics.Save();
            e.Graphics.SetClip(ClientRectangle);
            e.Graphics.DrawImage(_detail, new RectangleF(6 - _detailOffsetX, 6 - _detailOffsetY,
                _detail.Width * _detailFit, _detail.Height * _detailFit));
            e.Graphics.Restore(state);
            if (_detailSelection?.Invoke() is { } tile)
            {
                var size = DetailScale * _detailFit;
                using var pen = new Pen(Color.Yellow, 2);
                e.Graphics.DrawRectangle(pen,
                    6 - _detailOffsetX + (tile.X - 1) * size,
                    6 - _detailOffsetY + (tile.Y - 1) * size,
                    Math.Max(3f, size), Math.Max(3f, size));
            }
            return;
        }
        if (_unifiedOverview is not null)
        {
            var overviewFit = Math.Min((float)(ClientSize.Width - 12) / _unifiedOverview.Width,
                (float)(ClientSize.Height - 12) / _unifiedOverview.Height);
            overviewFit = Math.Max(.01f, overviewFit);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var drawWidth = _unifiedOverview.Width * overviewFit;
            var drawHeight = _unifiedOverview.Height * overviewFit;
            var left = (ClientSize.Width - drawWidth) / 2f;
            var top = (ClientSize.Height - drawHeight) / 2f;
            e.Graphics.DrawImage(_unifiedOverview, left, top, drawWidth, drawHeight);
            if (_unifiedViewport is { } overviewViewport)
            {
                var view = overviewViewport();
                const float overviewScale = 4f;
                using var pen = new Pen(Color.Yellow, 2);
                e.Graphics.DrawRectangle(pen, left + view.X * overviewScale * overviewFit,
                    top + view.Y * overviewScale * overviewFit,
                    view.Width * overviewScale * overviewFit, view.Height * overviewScale * overviewFit);
            }
            return;
        }
        if (_world is null || _maps.Count == 0) return;
        var scale = ScaleFactor;
        // The exported world.png is _worldImage. Draw that exact bitmap here too;
        // composing the panel again from individual maps used to omit the same
        // border-stitch shifts applied by AddMapToWorldImage, so the editor and
        // the generated PNG visibly disagreed.
        e.Graphics.InterpolationMode = scale < 1f
            ? InterpolationMode.HighQualityBicubic
            : InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = scale < 1f ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half;
        if (_worldImage is null) return;
        e.Graphics.DrawImage(_worldImage,
            6f - _bounds.MinX * scale,
            6f - _bounds.MinY * scale,
            _worldImage.Width * scale,
            _worldImage.Height * scale);
        if (_viewport is { } viewport)
        {
            var view = viewport();
            using var pen = new Pen(Color.Yellow, 2);
            e.Graphics.DrawRectangle(pen, 6 + (view.X - _bounds.MinX) * scale,
                6 + (view.Y - _bounds.MinY) * scale, view.Width * scale, view.Height * scale);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buildTimer.Stop();
            _buildTimer.Dispose();
            _unifiedOverview?.Dispose();
            _detail?.Dispose();
            foreach (var image in _maps.Values) image.Dispose();
        }
        base.Dispose(disposing);
    }
}
