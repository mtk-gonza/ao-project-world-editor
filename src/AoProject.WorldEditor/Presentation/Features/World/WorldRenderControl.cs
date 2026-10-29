using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;
using AoProject.WorldEditor.Application;

namespace AoProject.WorldEditor.Presentation.Features.World;

/// <summary>WinForms host for the backend-independent world renderer.</summary>
internal sealed class WorldRenderControl : Control
{
    public event Action<int, int>? TileClicked;
    public event Action<int, int>? TileEraseRequested;
    public event Action? UndoRequested;
    public event Action<int, int>? TileRightClicked;
    public event Action<int, int>? TileDoubleClicked;
    public event Action<TileSelection>? SelectionChanged;
    public event Action? ViewChanged;
    public event Action<short>? GlobalMapActivated;
    public bool NavigationMode { get; set; }
    public int SelectedGraphicIndex { get; set; } = 1;
    public int SelectedLayer { get; set; }
    private readonly System.Windows.Forms.Timer _renderTimer;
    private IWorldRenderer? _renderer;
    private Exception? _initializationError;
    private WorldMap? _map;
    private GlobalWorldDocument? _world;
    private WorldEditorResourceCatalog? _worldResources;
    private (int MinX, int MinY, int MaxX, int MaxY)? _lastGlobalChunkBounds;
    private HashSet<GlobalChunkCoordinate> _globalChunks = [];
    private WorldMap? _selectedWorldMap;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<short, MapRenderGeometry> _globalGeometryCache = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<short, IReadOnlyList<RenderSprite>> _globalSpriteCache = [];
    private int _globalWarmupGeneration;
    private (int MinX, int MinY, int MaxX, int MaxY)? _globalWarmingBounds;
    private MapRenderGeometry? _geometry;
    private GraphicAtlas? _atlas;
    private readonly Dictionary<int, AtlasRegion> _regions = [];
    private readonly Dictionary<int, AnimationInfo> _animations = [];
    private int[,,]? _lights;
    private WorldBorderStitcher? _stitcher;
    private IReadOnlyDictionary<GlobalChunkCoordinate, int>? _stitchColumns;
    private IReadOnlyDictionary<GlobalChunkCoordinate, int>? _stitchRows;

    /// <summary>Columns/rows collapsed per chunk (0 = the junction keeps its border).</summary>
    public IReadOnlyDictionary<GlobalChunkCoordinate, int>? StitchColumns => _stitchColumns;
    public IReadOnlyDictionary<GlobalChunkCoordinate, int>? StitchRows => _stitchRows;    // VB6 renders round lights during map load. Keep the load-time path enabled
    // by default; the View toggle remains available for diagnostic comparison.
    private bool _lightingEnabled = true;
    public bool NightMode { get; private set; }
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _animationEpoch;
    private readonly Dictionary<WorldMap, ParticleGroupService> _particleGroups = new();
    private readonly WorldCamera _camera = new();
    private readonly TextRenderService _text = new();
    private FontAtlas? _hudFont;
    private IReadOnlyList<RenderSprite> _sprites = [];
    public WorldRenderOptions RenderOptions { get; } = new();
    private Point _lastMouse;
    private bool _panning;
    private (int X, int Y)? _selectedTile;
    private (int X, int Y)? _selectedGlobalTile;
    private GlobalTileSelection? _globalSelection;
    private TileSelection? _selection;
    private (int X, int Y)? _selectionAnchor;
    private bool _selecting;
    private const float KeyboardPanPixels = 64f;
    public (int X, int Y)? SelectedTile => _selectedTile;
    public (int X, int Y)? SelectedGlobalTile => _selectedGlobalTile;
    public GlobalTileSelection? SelectedGlobalSelection => _globalSelection;
    public TileSelection? SelectedSelection => _selection;

    public void CenterOnTile(int x, int y)
    {
        _camera.CenterOnTile(x, y, ClientSize.Width, ClientSize.Height);
        Invalidate();
    }

    public void CenterOnGlobalTile(int x, int y)
    {
        _camera.CenterOnGlobalTile(x, y, ClientSize.Width, ClientSize.Height);
        if (_world is not null && _worldResources is not null)
            WarmGlobalCachesAsync(_world, _worldResources, GlobalChunkBounds());
        ViewChanged?.Invoke();
        Invalidate();
    }

    public (float X, float Y, float Width, float Height) GlobalViewport =>
        (_camera.OffsetX / MapRenderGeometry.TilePixels, _camera.OffsetY / MapRenderGeometry.TilePixels,
            ClientSize.Width / _camera.Zoom / MapRenderGeometry.TilePixels,
            ClientSize.Height / _camera.Zoom / MapRenderGeometry.TilePixels);

    public void SetMap(WorldMap map)
    {
        _particleGroups.Clear();
        _world = null;
        _selectedGlobalTile = null;
        _globalSelection = null;
        _selectedWorldMap = null;
        _globalChunks.Clear();
        _map = map;
        _animationEpoch = _clock.Elapsed.TotalSeconds;
        _geometry = MapRenderGeometry.Build(map, RenderOptions);
        (_renderer as D3D11WorldRenderer)?.ResetCaptureCounter();
        Invalidate();
    }

    public void SetGlobalWorld(GlobalWorldDocument world, WorldEditorResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(world);
        _particleGroups.Clear();
        _world = world;
        _worldResources = resources;
        _globalGeometryCache.Clear();
        _globalSpriteCache.Clear();
        _map = world.Maps[world.RootMap];
        _animationEpoch = _clock.Elapsed.TotalSeconds;
        _atlas?.Dispose();
        _atlas = new GraphicAtlas(resources.Root);
        _lastGlobalChunkBounds = null;
        _selectedWorldMap = null;
        _globalChunks.Clear();
        RefreshGlobalViewport();
        BindRenderer();
        WarmGlobalCachesAsync(world, resources, GlobalChunkBounds());
        Invalidate();
    }

    private void WarmGlobalCachesAsync(GlobalWorldDocument world, WorldEditorResourceCatalog resources,
        (int MinX, int MinY, int MaxX, int MaxY) bounds)
    {
        if (_globalWarmingBounds == bounds || GlobalCachesReady(bounds)) return;
        _globalWarmingBounds = bounds;
        var generation = ++_globalWarmupGeneration;
        var options = new WorldRenderOptions
        {
            ShowLayer1 = RenderOptions.ShowLayer1,
            ShowLayer2 = RenderOptions.ShowLayer2,
            ShowLayer3 = RenderOptions.ShowLayer3,
            ShowLayer4 = RenderOptions.ShowLayer4,
            ShowObjects = RenderOptions.ShowObjects,
            ShowNpcs = RenderOptions.ShowNpcs,
            ShowParticles = false,
            ShowBlocked = RenderOptions.ShowBlocked,
            ShowTriggers = RenderOptions.ShowTriggers,
            ShowTransfers = RenderOptions.ShowTransfers,
            ShowGrid = RenderOptions.ShowGrid,
            ShowRoofFade = RenderOptions.ShowRoofFade,
            AlphaRoofs = RenderOptions.AlphaRoofs
        };
        _ = Task.Run(() =>
        {
            var geometries = new Dictionary<short, MapRenderGeometry>();
            var sprites = new Dictionary<short, IReadOnlyList<RenderSprite>>();
            var errors = new List<string>();
            try
            {
                var visible = world.Assembly.Placements.Where(item =>
                        item.Chunk.X >= bounds.MinX && item.Chunk.X <= bounds.MaxX &&
                        item.Chunk.Y >= bounds.MinY && item.Chunk.Y <= bounds.MaxY &&
                        world.Maps.ContainsKey(item.MapId))
                    .DistinctBy(item => item.MapId);
                foreach (var placement in visible)
                {
                    try
                    {
                        if (!world.Maps.TryGetValue(placement.MapId, out var map)) continue;
                        geometries[placement.MapId] = MapRenderGeometry.Build(map, options);
                        sprites[placement.MapId] = BuildSprites(map, resources, options, 0d);
                    }
                    catch (Exception error)
                    {
                        errors.Add($"{placement.MapId}: {error.GetType().Name}: {error.Message}");
                    }
                }
            }
            catch (Exception error)
            {
                errors.Add($"{error.GetType().Name}: {error.Message}");
            }

            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(_world, world) || generation != _globalWarmupGeneration) return;
                    foreach (var item in geometries)
                        _globalGeometryCache.TryAdd(item.Key, item.Value);
                    foreach (var item in sprites)
                        _globalSpriteCache.TryAdd(item.Key, item.Value);
                    _globalWarmingBounds = null;
                    if (errors.Count > 0) LogWarmupErrors(errors);
                    if (geometries.Count == 0) return;
                    RefreshGlobalViewport();
                    BindRenderer();
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                // The handle was destroyed while the background build finished.
            }
        });
    }

    private static void LogWarmupErrors(IReadOnlyList<string> errors)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("warmup-error.log"),
                $"[{DateTimeOffset.Now:O}] {string.Join(Environment.NewLine, errors)}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError($"WorldEditor warmup log failed: {error}");
        }
    }

    private bool GlobalCachesReady((int MinX, int MinY, int MaxX, int MaxY) bounds)
    {
        if (_world is null || RenderOptions.ShowParticles) return false;
        return _world.Assembly.Placements
            .Where(item => item.Chunk.X >= bounds.MinX && item.Chunk.X <= bounds.MaxX &&
                item.Chunk.Y >= bounds.MinY && item.Chunk.Y <= bounds.MaxY &&
                _world.Maps.ContainsKey(item.MapId))
            .Select(item => item.MapId).Distinct()
            .All(mapId => _globalGeometryCache.ContainsKey(mapId) && _globalSpriteCache.ContainsKey(mapId));
    }

    private void RefreshGlobalViewport()
    {
        if (_world is null || _worldResources is null) return;
        var bounds = GlobalChunkBounds();
        var minX = bounds.MinX;
        var minY = bounds.MinY;
        var maxX = bounds.MaxX;
        var maxY = bounds.MaxY;
        var visible = _world.Assembly.Placements.Where(item => item.Chunk.X >= minX && item.Chunk.X <= maxX &&
            item.Chunk.Y >= minY && item.Chunk.Y <= maxY && _world.Maps.ContainsKey(item.MapId)).ToArray();
        // If a visible map has not been warmed yet, keep the previous geometry
        // instead of combining only the ready ones: the missing maps were drawn
        // as black holes until the next warm-up. The warm-up refreshes this when
        // it finishes.
        if (visible.Any(item => !_globalGeometryCache.ContainsKey(item.MapId))) return;
        // Neighbour detection must use every placement, not only the visible
        // ones: a map at the left edge of the viewport still has a west
        // neighbour that owns the shared border columns.
        _globalChunks = _world.Assembly.Placements.Select(item => item.Chunk).ToHashSet();
        // Do not evict maps while the background warm-up is reading them.
        if (_globalWarmingBounds is null)
            _world.TrimLoadedMaps(visible.Select(item => item.MapId).Append(_world.RootMap));
        var mapGeometry = visible.Select(item =>
        {
            return _globalGeometryCache.TryGetValue(item.MapId, out var geometry)
                ? (geometry, item.Chunk)
                : ((MapRenderGeometry?)null, item.Chunk);
        }).Where(item => item.Item1 is not null)
            .Select(item => (item.Item1!, item.Chunk)).ToArray();
        // Collapse the junctions whose border is duplicated in the CSM (same design
        // in both maps): drawing both copies leaves a 1-tile stripe and the roof
        // fade applies to only one, which is the flicker at map borders.
        // Only the four border lines are read: loading whole maps for every junction
        // cost 2 GB of cached maps.
        _stitcher ??= new WorldBorderStitcher(_world.Assembly.Placements, mapId =>
        {
            if (!_world.Paths.TryGetValue(mapId, out var path)) return null;
            try { return WorldBorderStitcher.Sample(new CsmMapFileService().Load(path)); }
            catch { return null; }
        });
        // Lines are computed in the background on first use (loading border maps on
        // the UI thread froze the editor). The world starts un-collapsed and snaps
        // into the collapsed grid when the prewarm finishes.
        if (!_stitcher.IsComplete)
            _stitcher.Prewarm(() => { try { BeginInvoke(new Action(() => { Invalidate(); })); } catch (InvalidOperationException) { } });
        var stitch = _stitcher.ForAll(mapGeometry.Select(item => item.Chunk));
        _stitchColumns = stitch.Columns;
        _stitchRows = stitch.Rows;
        _geometry = MapRenderGeometry.Combine(mapGeometry, stitch.Columns, stitch.Rows);
        _regions.Clear();
        foreach (var tile in _geometry.Tiles.DistinctBy(tile => tile.GraphicIndex))
            if (tile.GraphicIndex > 0 && tile.GraphicIndex < _worldResources.Graphics.Count &&
                _atlas!.TryResolve(_worldResources.Graphics[tile.GraphicIndex], out var region)) _regions[tile.GraphicIndex] = region;
        foreach (var marker in MapOverlayGeometry.MarkerGraphics())
            if (marker > 0 && marker < _worldResources.Graphics.Count && !_regions.ContainsKey(marker) &&
                _atlas!.TryResolve(_worldResources.Graphics[marker], out var markerRegion)) _regions[marker] = markerRegion;
        var sprites = new List<RenderSprite>();
        foreach (var placement in visible)
        {
            var dx = (placement.Chunk.X * GlobalChunkGeometry.WidthTiles -
                (_stitchColumns is not null && _stitchColumns.TryGetValue(placement.Chunk, out var columnShift)
                    ? columnShift : 0)) * MapRenderGeometry.TilePixels;
            var dy = (placement.Chunk.Y * GlobalChunkGeometry.HeightTiles -
                (_stitchRows is not null && _stitchRows.TryGetValue(placement.Chunk, out var rowShift)
                    ? rowShift : 0)) * MapRenderGeometry.TilePixels;
            if (!_globalSpriteCache.TryGetValue(placement.MapId, out var localSprites)) continue;
            if (RenderOptions.ShowParticles)
            {
                if (!_world.Maps.TryGetValue(placement.MapId, out var map)) continue;
                localSprites = BuildSprites(map, _worldResources, RenderOptions, AnimationSeconds());
            }
            var hasWest = _globalChunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
            var hasNorth = _globalChunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
            foreach (var sprite in localSprites)
            {
                // Sprites are not clipped by MapRenderGeometry (that only
                // clips tile quads). Drop the ones anchored in the hidden
                // border so they do not bleed over the neighbouring map.
                if (!GlobalChunkGeometry.IsCorePixel(sprite.PixelX, sprite.PixelY,
                        MapRenderGeometry.TilePixels, hasWest, hasNorth)) continue;
                sprites.Add(sprite with
                {
                    PixelX = sprite.PixelX + dx, PixelY = sprite.PixelY + dy
                });
            }
        }
        _sprites = sprites;
        _animations.Clear();
        foreach (var index in _geometry.Tiles.Select(tile => tile.GraphicIndex).Concat(_sprites.Select(sprite => sprite.GraphicIndex)))
            RegisterGraphic(index, _worldResources);
        _lights = null;
        _lastGlobalChunkBounds = bounds;
    }

    private (int MinX, int MinY, int MaxX, int MaxY) GlobalChunkBounds()
    {
        var minX = (int)MathF.Floor(_camera.OffsetX / (GlobalChunkGeometry.WidthTiles * MapRenderGeometry.TilePixels)) - 1;
        var minY = (int)MathF.Floor(_camera.OffsetY / (GlobalChunkGeometry.HeightTiles * MapRenderGeometry.TilePixels)) - 1;
        var maxX = (int)MathF.Ceiling((_camera.OffsetX + ClientSize.Width / Math.Max(_camera.Zoom, .01f)) /
            (GlobalChunkGeometry.WidthTiles * MapRenderGeometry.TilePixels)) + 1;
        var maxY = (int)MathF.Ceiling((_camera.OffsetY + ClientSize.Height / Math.Max(_camera.Zoom, .01f)) /
            (GlobalChunkGeometry.HeightTiles * MapRenderGeometry.TilePixels)) + 1;
        return (minX, minY, maxX, maxY);
    }

    private void BindRenderer()
    {
        if (_renderer is not D3D11WorldRenderer d3d || _atlas is null) return;
        d3d.SetAtlas(_atlas);
        d3d.SetSprites(_sprites);
        d3d.SetAnimations(_animations);
        d3d.SetLightMap(_lights);
    }

    /// <summary>Tile at the center of the viewport (1-based map grid).</summary>
    public (int X, int Y) ViewportCenterTile => _camera.CenterTile(ClientSize.Width, ClientSize.Height);

    /// <summary>World tile at the center of the viewport (0-based geometry index).</summary>
    public (int X, int Y) ViewportCenterGlobalTile => _camera.CenterGlobalTile(ClientSize.Width, ClientSize.Height);

    public void SetMap(WorldMap map, WorldEditorResourceCatalog resources)
    {
        SetMap(map);
        _atlas?.Dispose();
        _atlas = new GraphicAtlas(resources.Root);
        _regions.Clear();
        foreach (var tile in _geometry!.Tiles.DistinctBy(tile => tile.GraphicIndex))
        {
            if (tile.GraphicIndex > 0 && tile.GraphicIndex < resources.Graphics.Count &&
                _atlas.TryResolve(resources.Graphics[tile.GraphicIndex], out var region))
                _regions[tile.GraphicIndex] = region;
        }

        _sprites = BuildSprites(map, resources, RenderOptions, AnimationSeconds());
        foreach (var graphic in _sprites.Select(sprite => sprite.GraphicIndex).Distinct())
        {
            if (graphic > 0 && graphic < resources.Graphics.Count && !_regions.ContainsKey(graphic) &&
                _atlas.TryResolve(resources.Graphics[graphic], out var region))
                _regions[graphic] = region;
        }

        _animations.Clear();
        foreach (var index in _geometry.Tiles.Select(tile => tile.GraphicIndex))
            RegisterGraphic(index, resources);
        foreach (var index in _sprites.Select(sprite => sprite.GraphicIndex))
            RegisterGraphic(index, resources);

        _lights = _lightingEnabled
            ? new MapLightingService().Compute(map, AmbientColor(map))
            : null;

        foreach (var marker in MapOverlayGeometry.MarkerGraphics())
            if (marker > 0 && marker < resources.Graphics.Count && !_regions.ContainsKey(marker) &&
                _atlas.TryResolve(resources.Graphics[marker], out var markerRegion))
                _regions[marker] = markerRegion;

        BindRenderer();
        MapBound?.Invoke(map, resources);
        Invalidate();
    }

    /// <summary>Raised when a single map is bound to the control (atlas ready).</summary>
    public event Action<WorldMap, WorldEditorResourceCatalog>? MapBound;

    private void RegisterGraphic(int index, WorldEditorResourceCatalog resources)
    {
        if (index <= 0 || index >= resources.Graphics.Count) return;
        var data = resources.Graphics[index];
        // Runtime-generated body/equipment GRHs can contain a single frame
        // and still have no direct file. They are animation records pointing
        // to the generated frame graphic.
        if (data.FileNumber <= 0 && data.Frames.Length > 0)
        {
            _animations[index] = new AnimationInfo(data.Frames, data.Speed);
            foreach (var frame in data.Frames)
                RegisterGraphic(frame, resources);
            return;
        }

        if (!_regions.ContainsKey(index) && _atlas!.TryResolve(data, out var region))
            _regions[index] = region;
    }

    public bool LightingAvailable => _world is null;

    public void SetLighting(bool enabled, bool night = false)
    {
        _lightingEnabled = enabled;
        NightMode = enabled && night;
        if (_map is null) return;
        if (_world is not null)
        {
            // The global view has no world-sized light map yet. Applying the
            // root map's 100x100 light map to the whole world would be wrong
            // (and out of bounds), so keep lighting off in this mode.
            _lights = null;
            if (_renderer is D3D11WorldRenderer worldRenderer) worldRenderer.SetLightMap(null);
            Invalidate();
            return;
        }
        _lights = enabled
            ? new MapLightingService().Compute(_map, AmbientColor(_map))
            : null;
        if (_renderer is D3D11WorldRenderer d3d) d3d.SetLightMap(_lights);
        Invalidate();
    }

    public void SetNightMode(bool night) => SetLighting(night || _lightingEnabled, night);

    public void RefreshLightingAfterEdit()
    {
        if (!_lightingEnabled || _map is null) return;
        if (_world is not null)
        {
            _lights = null;
            if (_renderer is D3D11WorldRenderer worldRenderer) worldRenderer.SetLightMap(null);
            Invalidate();
            return;
        }
        _lights = new MapLightingService().Compute(_map, AmbientColor(_map), includeFlatLights: true);
        if (_renderer is D3D11WorldRenderer d3d) d3d.SetLightMap(_lights);
        Invalidate();
    }

    private int AmbientColor(WorldMap map) => NightMode
        ? MapLightingService.NightLight
        : MapLightingService.AmbientFromColor(map.Environment.BaseLight);

    private double AnimationSeconds() => Math.Max(0d, _clock.Elapsed.TotalSeconds - _animationEpoch);

    private IReadOnlyList<RenderSprite> BuildSprites(WorldMap map, WorldEditorResourceCatalog resources,
        WorldRenderOptions options, double elapsedSeconds)
    {
        var sprites = new List<RenderSprite>();
        if (options.ShowObjects)
            AppendObjectSprites(map, resources, sprites);

        if (options.ShowNpcs)
        {
                var characters = new CharacterListService();
                characters.RefreshAll(map, resources.Npcs);
                sprites.AddRange(new CharacterRenderService().Render(
                    characters.Characters, resources.Bodies, resources.Heads,
                    resources.Helmets, resources.Weapons, resources.Shields));
        }

        if (options.ShowParticles)
        {
            if (!_particleGroups.TryGetValue(map, out var groups))
                _particleGroups[map] = groups = new ParticleGroupService();
            groups.SyncFromMap(map);
            sprites.AddRange(new ParticleRenderService().Render(groups.Groups, resources.Particles, (float)elapsedSeconds));
        }
        return sprites;
    }

    /// <summary>
    /// Objects are stored in OBJInfo + ObjGrh and anchored to the bottom of the
    /// tile, so taller graphics extend upwards (VB6 RenderObject behaviour).
    /// </summary>
    private static void AppendObjectSprites(WorldMap map, WorldEditorResourceCatalog resources, List<RenderSprite> sprites)
    {
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            var graphic = block.ObjectGraphic.GraphicIndex;
            if (graphic <= 0 && block.Object.Index > 0 && block.Object.Index < resources.Objects.Count)
                graphic = resources.Objects[block.Object.Index].GraphicIndex;
            if (graphic <= 0 || graphic >= resources.Graphics.Count) continue;
            // The CSM often stores the same graphic as ObjGrh *and* on one of the
            // tile layers, and the tile pass already draws those: adding the sprite
            // draws it again with a different anchor (the "double" ghost: towers,
            // awnings, houses seen twice with an offset).
            if (block.Graphics[0].GraphicIndex == graphic || block.Graphics[1].GraphicIndex == graphic ||
                block.Graphics[2].GraphicIndex == graphic || block.Graphics[3].GraphicIndex == graphic) continue;
            // Keep the root tile as the sort row. Draw-time centering applies
            // the same bottom anchoring as VB6 Draw_Grh(center=1).
            sprites.Add(new RenderSprite(graphic, (x - 1) * MapRenderGeometry.TilePixels,
                (y - 1) * MapRenderGeometry.TilePixels, 0xFFFFFF, 255, SpriteKind.Object));
        }
    }

    private bool TryGetEditorTile(int screenX, int screenY, out (int X, int Y) tile)
    {
        if (_world is null)
        {
            tile = _camera.ScreenToTile(screenX, screenY) ?? default;
            if (_map is null) return false;
            return tile.X >= _map.CsmMapSize.XMin && tile.X <= _map.CsmMapSize.XMax &&
                tile.Y >= _map.CsmMapSize.YMin && tile.Y <= _map.CsmMapSize.YMax &&
                tile.X is >= 1 and <= WorldMap.Width && tile.Y is >= 1 and <= WorldMap.Height;
        }
        var global = _camera.ScreenToGlobalTile(screenX, screenY);
        if (!_world.TryResolve(global.X, global.Y, out var location))
        {
            tile = default;
            return false;
        }
        if (!_world.Maps.TryGetValue(location.MapId, out var resolvedMap))
        {
            tile = default;
            return false;
        }
        _selectedGlobalTile = global;
        _selectedWorldMap = resolvedMap;
        GlobalMapActivated?.Invoke(location.MapId);
        tile = (location.LocalX, location.LocalY);
        return true;
    }

    public void RefreshWorldView()
    {
        if (_world is not null && _worldResources is not null)
            SetGlobalWorld(_world, _worldResources);
        else if (_map is not null && _worldResources is not null)
            SetMap(_map, _worldResources);
    }

    /// <summary>
    /// Rebuilds the render cache for one map after an interactive edit (paint,
    /// erase, object/NPC/trigger). Unlike <see cref="RefreshWorldView"/> it
    /// keeps the texture atlas and, in world mode, only rebuilds the edited
    /// map instead of every visible chunk.
    /// </summary>
    public void RefreshAfterEdit(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_worldResources is null) return;

        if (_world is null)
        {
            if (!ReferenceEquals(_map, map)) return;
            _geometry = MapRenderGeometry.Build(map, RenderOptions);
            _sprites = BuildSprites(map, _worldResources, RenderOptions, AnimationSeconds());
            foreach (var tile in _geometry.Tiles) RegisterGraphic(tile.GraphicIndex, _worldResources);
            foreach (var sprite in _sprites) RegisterGraphic(sprite.GraphicIndex, _worldResources);
            // A paint/erase does not change the light data; keep the current
            // light map so flat lights are not activated by accident.
            BindRenderer();
            MapBound?.Invoke(map, _worldResources);
            Invalidate();
            return;
        }

        short? mapId = null;
        foreach (var placement in _world.Assembly.Placements)
        {
            if (!_world.Maps.TryGetValue(placement.MapId, out var candidate)) continue;
            if (!ReferenceEquals(candidate, map)) continue;
            mapId = placement.MapId;
            break;
        }
        if (mapId is null) return;

        _globalGeometryCache[mapId.Value] = MapRenderGeometry.Build(map, RenderOptions);
        _globalSpriteCache[mapId.Value] = BuildSprites(map, _worldResources, RenderOptions, 0d);
        RefreshGlobalViewport();
        BindRenderer();
        Invalidate();
    }

    public WorldRenderControl()
    {
        // The HWND is owned by the D3D11 swap chain. Letting WinForms paint
        // the background (or a GDI overlay) on the same HWND causes a visible
        // flash whenever Windows delivers WM_PAINT during mouse interaction.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque, true);
        BackColor = Color.FromArgb(24, 30, 38);
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (_, _) => RenderFrame();
        MouseWheel += (_, e) => { _camera.SetZoom(_camera.Zoom * (e.Delta > 0 ? 1.1f : 1f / 1.1f), e.X, e.Y); Invalidate(); };
        MouseDown += (_, e) =>
        {
            Focus();
            _lastMouse = e.Location;
            _panning = e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && NavigationMode);
            if (e.Button == MouseButtons.Left && NavigationMode) { _camera.Pan(0, 0); Invalidate(); return; }
            if (e.Button == MouseButtons.Left && TryGetEditorTile(e.X, e.Y, out var clickedTile))
            {
                _selectedTile = clickedTile;
                if (ModifierKeys.HasFlag(Keys.Shift))
                {
                    _selecting = true;
                    _selectionAnchor = clickedTile;
                    _selection = new TileSelection(clickedTile.X, clickedTile.Y, clickedTile.X, clickedTile.Y);
                    if (_world is not null && _selectedGlobalTile is { } global)
                        _globalSelection = new GlobalTileSelection(global.X, global.Y, global.X, global.Y);
                    SelectionChanged?.Invoke(_selection.Value);
                }
                else TileClicked?.Invoke(clickedTile.X, clickedTile.Y);
            }
            if (e.Button == MouseButtons.Right && TryGetEditorTile(e.X, e.Y, out var rightTile))
            {
                _selectedTile = rightTile;
                TileRightClicked?.Invoke(rightTile.X, rightTile.Y);
                Invalidate();
            }
        };
        MouseMove += (_, e) =>
        {
            if (_panning) { _camera.Pan(-(e.X - _lastMouse.X) / _camera.Zoom, -(e.Y - _lastMouse.Y) / _camera.Zoom); _lastMouse = e.Location; ViewChanged?.Invoke(); Invalidate(); return; }
            if (_selecting && TryGetEditorTile(e.X, e.Y, out var tile))
            {
                _selection = new TileSelection(_selectionAnchor!.Value.X, _selectionAnchor.Value.Y, tile.X, tile.Y).Normalize();
                if (_world is not null)
                {
                    var global = _camera.ScreenToGlobalTile(e.X, e.Y);
                    if (_globalSelection is { } anchor)
                        _globalSelection = new GlobalTileSelection(anchor.StartX, anchor.StartY, global.X, global.Y).Normalize();
                }
                SelectionChanged?.Invoke(_selection.Value); Invalidate();
            }
        };
        MouseUp += (_, _) => { _panning = false; _selecting = false; };
        MouseDoubleClick += (_, e) => { if (TryGetEditorTile(e.X, e.Y, out var tile)) TileDoubleClicked?.Invoke(tile.X, tile.Y); };
        KeyDown += (_, e) =>
        {
            if (PanWithKey(e.KeyCode))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Delete && _selectedTile is { } tile)
                TileEraseRequested?.Invoke(tile.X, tile.Y);
            if (e.Control && e.KeyCode == Keys.Z)
                UndoRequested?.Invoke();
        };
        TabStop = true;
    }

    private bool PanWithKey(Keys key)
    {
        var (panX, panY) = key switch
        {
            Keys.Left or Keys.A => (-KeyboardPanPixels, 0f),
            Keys.Right or Keys.D => (KeyboardPanPixels, 0f),
            Keys.Up or Keys.W => (0f, -KeyboardPanPixels),
            Keys.Down or Keys.S => (0f, KeyboardPanPixels),
            _ => (0f, 0f)
        };
        if (panX == 0f && panY == 0f) return false;
        _camera.Pan(panX / _camera.Zoom, panY / _camera.Zoom);
        ViewChanged?.Invoke();
        Invalidate();
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // WinForms routes cursor keys through command-key processing instead
        // of KeyDown. Capture them here so panning works without a middle
        // mouse button.
        return PanWithKey(keyData & Keys.KeyCode) || base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            _renderer = new D3D11WorldRenderer(Handle, ClientSize.Width, ClientSize.Height);
            if (_renderer is D3D11WorldRenderer created)
            {
                created.SetCamera(_camera);
                var capture = Environment.GetEnvironmentVariable("AO_CAPTURE");
                if (!string.IsNullOrWhiteSpace(capture)) created.CapturePath = capture;
                if (Environment.GetEnvironmentVariable("AO_NIGHT") == "1") SetLighting(true, true);
            }
            _text.Create("hud", "Consolas", 13);
            _hudFont = _text.Atlas("hud");
            if (_atlas is not null && _renderer is D3D11WorldRenderer d3d)
            {
                d3d.SetAtlas(_atlas);
                d3d.SetSprites(_sprites);
                d3d.SetAnimations(_animations);
                d3d.SetLightMap(_lights);
            }
            _renderTimer.Start();
        }
        catch (Exception error)
        {
            _initializationError = error;
            try
            {
                File.WriteAllText(EditorPaths.LogFile("d3d11-error.log"), error.ToString());
            }
            catch { }
            Invalidate();
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _renderTimer.Stop();
        _renderer?.Dispose();
        _renderer = null;
        _atlas?.Dispose();
        _atlas = null;
        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _lastGlobalChunkBounds = null;
        _renderer?.Resize(ClientSize.Width, ClientSize.Height);
    }

    private void RenderFrame()
    {        if (_renderer is null)
        {
            Invalidate();
            return;
        }

        var frameWatch = System.Diagnostics.Stopwatch.StartNew();
        var overlayWatch = new System.Diagnostics.Stopwatch();
        try
        {
            var globalBounds = _world is null ? default : GlobalChunkBounds();
            if (_world is not null && _lastGlobalChunkBounds != globalBounds &&
                !GlobalCachesReady(globalBounds))
            {
                if (_worldResources is not null)
                    WarmGlobalCachesAsync(_world, _worldResources, globalBounds);
            }
            else if (_world is not null && _lastGlobalChunkBounds != globalBounds)
            {
                RefreshGlobalViewport();
                BindRenderer();
            }
            _renderer.Resize(ClientSize.Width, ClientSize.Height);
            if (_renderer is D3D11WorldRenderer cameraRenderer)
                cameraRenderer.SetCamera(_camera);
            if (_renderer is D3D11WorldRenderer d3d && _geometry is not null)
            {
                d3d.SetRoofFocus(FindRoofFocusTrigger());
                d3d.SetRoofFadeEnabled(RenderOptions.ShowRoofFade);
                d3d.SetAlphaRoofsEnabled(RenderOptions.AlphaRoofs);
                d3d.SetTime(AnimationSeconds());
                d3d.SetAnimations(_animations);
                if (_hudFont is not null)
                {
                    var label = "AO WorldEditor";
                    if (_map is not null && !string.IsNullOrWhiteSpace(_map.Info.Name)) label += $"  |  {_map.Info.Name}";
                    if (_selectedTile is { } selectedTile) label += $"  |  {selectedTile.X},{selectedTile.Y}";
                    d3d.SetHud(_text.Render("hud", label, 8, 6), _hudFont);
                }
            if (_atlas is not null && _regions.Count > 0)
                {
                    overlayWatch.Start();
                    var overlays = BuildOverlays();
                    overlayWatch.Stop();
                    d3d.RenderTextured(BackColor, _geometry, _regions, overlays);
                }
                else
                    d3d.Render(BackColor, _geometry);
            }
            else
                _renderer.Render(BackColor);
            TrackPerformance(frameWatch.Elapsed.TotalMilliseconds, overlayWatch.Elapsed.TotalMilliseconds);
            LogMissingRegions();
        }
        catch (Exception error)
        {
            _initializationError = error;
            LogRenderError(error);
            _renderTimer.Stop();
            _renderer.Dispose();
            _renderer = null;
            Invalidate();
        }
    }

    private static readonly bool RenderDebugEnabled = Environment.GetEnvironmentVariable("AO_RENDER_DEBUG") == "1";
    private static readonly System.Diagnostics.Stopwatch RenderDebugClock = System.Diagnostics.Stopwatch.StartNew();
    private long _renderDebugLastMs = -2000;

    /// <summary>
    /// AO_RENDER_DEBUG=1: every 2 s logs how many tiles the geometry has, how many
    /// have no atlas region (invisible tiles = black holes) and how many vertices
    /// were emitted. Diagnoses "the world looks black" without guessing.
    /// </summary>
    private void LogMissingRegions()
    {
        if (!RenderDebugEnabled || _geometry is null) return;
        var now = RenderDebugClock.ElapsedMilliseconds;
        if (now - _renderDebugLastMs < 2000) return;
        _renderDebugLastMs = now;
        try
        {
            var tiles = _geometry.Tiles;
            var missing = 0;
            var missingGraphics = new HashSet<int>();
            foreach (var tile in tiles)
            {
                if (tile.GraphicIndex <= 0 || _regions.ContainsKey(tile.GraphicIndex)) continue;
                missing++;
                missingGraphics.Add(tile.GraphicIndex);
            }
            var sprites = _sprites.Count;
            var vertices = (_renderer as D3D11WorldRenderer)?.LastVertexCount ?? 0;
            File.AppendAllText(EditorPaths.LogFile("render-debug.log"),
                $"[{DateTimeOffset.Now:O}] tiles={tiles.Count} sinRegion={missing} " +
                $"graficosFaltantes={missingGraphics.Count} sprites={sprites} vertices={vertices} " +
                $"camara=({(int)(_camera.OffsetX / MapRenderGeometry.TilePixels)}," +
                $"{(int)(_camera.OffsetY / MapRenderGeometry.TilePixels)}) zoom={_camera.Zoom:0.##}" +
                Environment.NewLine);
        }
        catch { }
    }

    private int _perfFrames;    private double _perfFrameSum;
    private double _perfFrameMax;
    private double _perfOverlaySum;
    private double _perfSpriteSum;
    private double _perfDrawSum;
    private long _perfVertices;

    private static readonly bool PerfEnabled = Environment.GetEnvironmentVariable("AO_PERF") == "1";

    /// <summary>
    /// Lightweight sampling for AO_PERF=1: averages every 120 frames into
    /// logs/perf.log. Disabled by default and allocation-free when off.
    /// </summary>
    private void TrackPerformance(double frameMs, double overlayMs)
    {
        if (!PerfEnabled) return;
        _perfFrames++;
        _perfFrameSum += frameMs;
        _perfFrameMax = Math.Max(_perfFrameMax, frameMs);
        _perfOverlaySum += overlayMs;
        if (_renderer is D3D11WorldRenderer d3d)
        {
            _perfSpriteSum += d3d.LastSpriteRowsMs;
            _perfDrawSum += d3d.LastDrawMs;
            _perfVertices += d3d.LastVertexCount;
        }
        if (_perfFrames < 120) return;
        try
        {
            var frames = _perfFrames;
            File.AppendAllText(EditorPaths.LogFile("perf.log"),
                $"[{DateTimeOffset.Now:O}] frames={frames} avgFrame={_perfFrameSum / frames:F2}ms " +
                $"maxFrame={_perfFrameMax:F2}ms avgOverlays={_perfOverlaySum / frames:F2}ms " +
                $"avgSpriteRows={_perfSpriteSum / frames:F2}ms avgDraw={_perfDrawSum / frames:F2}ms " +
                $"avgVertices={_perfVertices / frames:F0} global={_world is not null} atlas={_atlas is not null}" +
                Environment.NewLine);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError($"perf logging failed: {error}");
        }
        _perfFrames = 0;
        _perfFrameSum = 0;
        _perfFrameMax = 0;
        _perfOverlaySum = 0;
        _perfSpriteSum = 0;
        _perfDrawSum = 0;
        _perfVertices = 0;
    }

    private static void LogRenderError(Exception error)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("render-error.log"),
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError)
        {
            System.Diagnostics.Trace.TraceError($"WorldEditor render error logging failed: {logError}");
        }
    }

    /// <summary>Visible local tiles (1-based) plus a one-tile margin.</summary>
    private (int MinX, int MaxX, int MinY, int MaxY) VisibleLocalTileBounds()
    {
        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);
        var pixels = MapRenderGeometry.TilePixels * Math.Max(0.01f, _camera.Zoom);
        var minX = (int)MathF.Floor(_camera.OffsetX / pixels) + 1;
        var maxX = (int)MathF.Floor((width + _camera.OffsetX) / pixels) + 1;
        var minY = (int)MathF.Floor(_camera.OffsetY / pixels) + 1;
        var maxY = (int)MathF.Floor((height + _camera.OffsetY) / pixels) + 1;
        return (minX - 1, maxX + 1, minY - 1, maxY + 1);
    }

    /// <summary>Visible world tiles (0-based geometry index) plus a margin.</summary>
    private (int MinX, int MaxX, int MinY, int MaxY) VisibleGlobalTileBounds()
    {
        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);
        var pixels = MapRenderGeometry.TilePixels * Math.Max(0.01f, _camera.Zoom);
        var minX = (int)MathF.Floor(_camera.OffsetX / pixels) - 1;
        var maxX = (int)MathF.Floor((width + _camera.OffsetX) / pixels) + 1;
        var minY = (int)MathF.Floor(_camera.OffsetY / pixels) - 1;
        var maxY = (int)MathF.Floor((height + _camera.OffsetY) / pixels) + 1;
        return (minX, maxX, minY, maxY);
    }

    private IReadOnlyList<MapOverlay> BuildOverlays()
    {
        if (_world is null || _map is null)
        {
            var clip = VisibleLocalTileBounds();
            var localOverlays = MapOverlayGeometry.Build(_map!, RenderOptions,
                _selection is null ? _selectedTile : null, clip).ToList();
            if (_selection is { } localSelection)
            {
                var normalized = localSelection.Normalize();
                var minX = Math.Max(normalized.StartX, (int)_map!.CsmMapSize.XMin);
                var maxX = Math.Min(normalized.EndX, (int)_map.CsmMapSize.XMax);
                var minY = Math.Max(normalized.StartY, (int)_map.CsmMapSize.YMin);
                var maxY = Math.Min(normalized.EndY, (int)_map.CsmMapSize.YMax);
                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                    localOverlays.Add(new(OverlayKind.Selection, x, y, 1, 1));
            }
            return localOverlays;
        }
        var overlays = new List<MapOverlay>();
        if (MapOverlayGeometry.AffectsTiles(RenderOptions))
        {
            var bounds = _lastGlobalChunkBounds ?? GlobalChunkBounds();
            var visible = VisibleGlobalTileBounds();
            var chunks = _globalChunks.Count > 0
                ? _globalChunks
                : _world.Assembly.Placements.Select(item => item.Chunk).ToHashSet();
            foreach (var placement in _world.Assembly.Placements)
            {
                if (placement.Chunk.X < bounds.MinX || placement.Chunk.X > bounds.MaxX ||
                    placement.Chunk.Y < bounds.MinY || placement.Chunk.Y > bounds.MaxY) continue;
                if (!_world.Maps.TryGetValue(placement.MapId, out var map)) continue;
                var hasWest = chunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
                var hasNorth = chunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
                var dx = placement.Chunk.X * GlobalChunkGeometry.WidthTiles;
                var dy = placement.Chunk.Y * GlobalChunkGeometry.HeightTiles;
                // Translate the visible global rect into this map's local tiles.
                var clip = GlobalChunkGeometry.ToLocalTileBounds(visible, placement.Chunk);
                overlays.AddRange(MapOverlayGeometry.Build(map, RenderOptions, null, clip)
                    .Where(item => GlobalChunkGeometry.IsCoreTile(item.X, item.Y, hasWest, hasNorth))
                    .Select(item => item with { X = item.X + dx, Y = item.Y + dy }));
            }
        }
        if (_selectedGlobalTile is { } selected) overlays.Add(new(OverlayKind.Selection, selected.X, selected.Y, 1, 1));
        if (_globalSelection is { } globalSelection)
        {
            var normalized = globalSelection.Normalize();
            for (var y = normalized.StartY; y <= normalized.EndY; y++)
            for (var x = normalized.StartX; x <= normalized.EndX; x++)
                overlays.Add(new(OverlayKind.Selection, x, y, 1, 1));
        }
        return overlays;
    }

    private int FindRoofFocusTrigger()
    {
        var map = _selectedWorldMap ?? _map;
        if (map is null) return 0;
        if (_selectedTile is { } selected && TryFindNearRoof(map, selected.X, selected.Y, out var selectedRoof))
            return selectedRoof;

        // VB6 has no automatic NPC/player roof focus. Roof fading is driven
        // only by the explicitly selected tile in the editor.
        return 0;
    }

    private static bool TryFindNearRoof(WorldMap map, int centerX, int centerY, out int roofTrigger)
    {
        roofTrigger = 0;
        var minX = Math.Max(map.CsmMapSize.XMin, (short)Math.Max(1, centerX - 1));
        var maxX = Math.Min(map.CsmMapSize.XMax, (short)Math.Min(WorldMap.Width, centerX + 1));
        var minY = Math.Max(map.CsmMapSize.YMin, (short)Math.Max(1, centerY - 1));
        var maxY = Math.Min(map.CsmMapSize.YMax, (short)Math.Min(WorldMap.Height, centerY + 1));
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var trigger = map.Blocks[x, y].Trigger;
            if (MapRenderGeometry.IsRoofTrigger(trigger))
            {
                roofTrigger = trigger;
                return true;
            }
        }
        return false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Normal frames are presented exclusively by D3D11. Do not call the
        // base implementation: it paints BackColor over the swap chain.
        if (_renderer is null && _initializationError is not null)
        {
            e.Graphics.Clear(BackColor);
            using var brush = new SolidBrush(Color.OrangeRed);
            e.Graphics.DrawString(
                "No se pudo inicializar D3D11: " + _initializationError.Message,
                Font,
                brush,
                new PointF(16, 16));
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Suppress WinForms background erasure for the D3D11-owned surface.
    }
}
