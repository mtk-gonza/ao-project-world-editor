using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed class GlobalWorldDocument
{
    private readonly HashSet<short> _pinnedMaps = [];
    public GlobalWorldDocument(
        GlobalWorldAssembly assembly,
        IReadOnlyDictionary<short, WorldMap> maps,
        IReadOnlyDictionary<short, string> paths,
        short rootMap)
    {
        Assembly = assembly;
        Maps = maps;
        Paths = paths;
        RootMap = rootMap;
        _pinnedMaps.Add(rootMap);
    }

    public GlobalWorldDocument(
        GlobalWorldAssembly assembly,
        IReadOnlyDictionary<short, string> paths,
        short rootMap,
        Func<string, WorldMap> loader)
    {
        Assembly = assembly;
        Paths = paths;
        RootMap = rootMap;
        _pinnedMaps.Add(rootMap);
        Maps = new LazyWorldMapCatalog(paths, loader);
    }

    public GlobalWorldAssembly Assembly { get; private set; }
    public IReadOnlyDictionary<short, WorldMap> Maps { get; }
    public IReadOnlyDictionary<short, string> Paths { get; }
    public short RootMap { get; private set; }
    public IReadOnlyList<MapsWorldDataService.WorldLayout> WorldLayouts { get; set; } = [];
    public IReadOnlyList<int> IgnoredWorldIds { get; set; } = [];
    public IReadOnlyList<string> LoadErrors { get; set; } = [];
    public bool KeepAllLoaded { get; set; }
    public IReadOnlyDictionary<string, SpecialWorldCatalog> SpecialWorlds { get; set; } =
        new Dictionary<string, SpecialWorldCatalog>();

    public void SwitchLevel(GlobalWorldAssembly assembly, short rootMap)
    {
        Assembly = assembly;
        RootMap = rootMap;
        _pinnedMaps.Clear();
        _pinnedMaps.Add(rootMap);
    }

    public int LoadedMapCount => Maps is LazyWorldMapCatalog lazy ? lazy.LoadedCount : Maps.Count;

    public void TrimLoadedMaps(IEnumerable<short> keepIds)
    {
        if (KeepAllLoaded) return;
        if (Maps is LazyWorldMapCatalog lazy) lazy.Trim(keepIds.Concat(_pinnedMaps));
    }

    public void MarkMapUnavailable(short mapId)
    {
        if (Maps is LazyWorldMapCatalog lazy) lazy.MarkUnavailable(mapId);
    }

    public void PinMap(short mapId) => _pinnedMaps.Add(mapId);

    /// <summary>
    /// Keeps only the root and the map being edited. Pinning every map that was
    /// ever clicked would defeat the lazy cache when panning across the world.
    /// Changed maps are kept by <see cref="LazyWorldMapCatalog.Trim"/> anyway.
    /// </summary>
    public void SetActiveMap(short mapId)
    {
        _pinnedMaps.Clear();
        _pinnedMaps.Add(RootMap);
        if (mapId > 0 && mapId != RootMap) _pinnedMaps.Add(mapId);
    }

    /// <summary>
    /// Maps currently materialized in memory. Used by save/undo paths so they
    /// never force a full-world load just to inspect <c>Changed</c>.
    /// </summary>
    public IEnumerable<(short MapId, WorldMap Map)> LoadedMaps =>
        Maps is LazyWorldMapCatalog lazy
            ? lazy.LoadedEntries
            : Maps.Select(item => (item.Key, item.Value));

    /// <summary>Distinct maps touched by a global rectangle.</summary>
    public IReadOnlyCollection<short> CollectMapIds(GlobalTileSelection selection)
    {
        var normalized = selection.Normalize();
        return CollectMapIds(normalized.StartX, normalized.StartY, normalized.EndX, normalized.EndY);
    }

    public IReadOnlyCollection<short> CollectMapIds(int startX, int startY, int endX, int endY)
    {
        var chunks = Assembly.Placements.Select(item => item.Chunk).ToHashSet();
        var ids = new HashSet<short>();
        foreach (var placement in Assembly.Placements)
        {
            if (!Maps.ContainsKey(placement.MapId)) continue;
            var (left, top, right, bottom) = GlobalBounds(placement, chunks);
            if (endX < left || startX > right || endY < top || startY > bottom) continue;
            ids.Add(placement.MapId);
        }
        return ids;
    }

    public bool TryResolve(int globalX, int globalY, out GlobalTileLocation location)
    {
        var chunks = Assembly.Placements.Select(item => item.Chunk).ToHashSet();
        foreach (var placement in Assembly.Placements)
        {
            if (!Maps.ContainsKey(placement.MapId)) continue;
            var (left, top, right, bottom) = GlobalBounds(placement, chunks);
            if (globalX < left || globalX > right || globalY < top || globalY > bottom) continue;
            location = new GlobalTileLocation(placement.MapId,
                globalX - placement.Chunk.X * GlobalChunkGeometry.WidthTiles + 1,
                globalY - placement.Chunk.Y * GlobalChunkGeometry.HeightTiles + 1);
            return true;
        }
        location = default;
        return false;
    }

    private static (int Left, int Top, int Right, int Bottom) GlobalBounds(
        GlobalMapPlacement placement, HashSet<GlobalChunkCoordinate> chunks)
    {
        var hasWest = chunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
        var hasNorth = chunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
        var minX = hasWest ? GlobalChunkGeometry.CoreMinX + 1 : GlobalChunkGeometry.CoreMinX;
        var minY = hasNorth ? GlobalChunkGeometry.CoreMinY + 1 : GlobalChunkGeometry.CoreMinY;
        // Only the rendered core is resolvable. Without this, clicks in the
        // blank padding west/north of the first map edited invisible tiles.
        var left = placement.Chunk.X * GlobalChunkGeometry.WidthTiles + minX - 1;
        var top = placement.Chunk.Y * GlobalChunkGeometry.HeightTiles + minY - 1;
        var right = placement.Chunk.X * GlobalChunkGeometry.WidthTiles + GlobalChunkGeometry.CoreMaxX - 1;
        var bottom = placement.Chunk.Y * GlobalChunkGeometry.HeightTiles + GlobalChunkGeometry.CoreMaxY - 1;
        return (left, top, right, bottom);
    }

    public bool TryGetGlobalTile(short mapId, int localX, int localY, out (int X, int Y) global)
    {
        var placement = Assembly.Placements.FirstOrDefault(item => item.MapId == mapId);
        if (placement.MapId != mapId || !Maps.ContainsKey(mapId) ||
            localX is < 1 or > WorldMap.Width || localY is < 1 or > WorldMap.Height)
        {
            global = default;
            return false;
        }
        var chunks = Assembly.Placements.Select(item => item.Chunk).ToHashSet();
        var (left, top, right, bottom) = GlobalBounds(placement, chunks);
        var globalX = placement.Chunk.X * GlobalChunkGeometry.WidthTiles + localX - 1;
        var globalY = placement.Chunk.Y * GlobalChunkGeometry.HeightTiles + localY - 1;
        if (globalX < left || globalX > right || globalY < top || globalY > bottom)
        {
            global = default;
            return false;
        }
        // Guarantee the round trip: hidden border tiles belong to the neighbour
        // and must not be reported as this map.
        if (!TryResolve(globalX, globalY, out var resolved) ||
            resolved.MapId != mapId || resolved.LocalX != localX || resolved.LocalY != localY)
        {
            global = default;
            return false;
        }
        global = (globalX, globalY);
        return true;
    }
}

internal sealed class LazyWorldMapCatalog : IReadOnlyDictionary<short, WorldMap>
{
    private readonly object _sync = new();
    private readonly IReadOnlyDictionary<short, string> _paths;
    private readonly Func<string, WorldMap> _loader;
    private readonly Dictionary<short, WorldMap> _cache = [];
    private readonly HashSet<short> _unavailable = [];

    public LazyWorldMapCatalog(IReadOnlyDictionary<short, string> paths, Func<string, WorldMap> loader)
    {
        _paths = paths; _loader = loader;
    }

    public WorldMap this[short key] => Get(key);
    public IEnumerable<short> Keys => _paths.Keys;
    public IEnumerable<WorldMap> Values => _paths.Keys.Select(Get);
    public int Count => _paths.Count;
    public int LoadedCount { get { lock (_sync) return _cache.Count; } }
    public IEnumerable<(short MapId, WorldMap Map)> LoadedEntries
    {
        get { lock (_sync) return _cache.Select(pair => (pair.Key, pair.Value)).ToArray(); }
    }
    public bool ContainsKey(short key) => _paths.ContainsKey(key) && !_unavailable.Contains(key);
    public bool TryGetValue(short key, out WorldMap value)
    {
        if (!_paths.ContainsKey(key) || _unavailable.Contains(key)) { value = null!; return false; }
        value = Get(key); return true;
    }
    public IEnumerator<KeyValuePair<short, WorldMap>> GetEnumerator() =>
        _paths.Keys.Select(key => new KeyValuePair<short, WorldMap>(key, Get(key))).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public void Trim(IEnumerable<short> keepIds)
    {
        lock (_sync)
        {
            var keep = keepIds.ToHashSet();
            foreach (var mapId in _cache.Keys.ToArray())
                if (!keep.Contains(mapId) && !_cache[mapId].Info.Changed)
                    _cache.Remove(mapId);
        }
    }

    public void MarkUnavailable(short key)
    {
        lock (_sync)
        {
            _unavailable.Add(key);
            _cache.Remove(key);
        }
    }

    private WorldMap Get(short key)
    {
        lock (_sync)
        {
            if (_unavailable.Contains(key)) throw new InvalidDataException($"El mapa {key} no está disponible.");
            if (_cache.TryGetValue(key, out var map)) return map;
            if (!_paths.TryGetValue(key, out var path)) throw new KeyNotFoundException($"No existe el mapa {key}.");
            map = _loader(path);
            _cache[key] = map;
            return map;
        }
    }
}

internal readonly record struct GlobalTileLocation(short MapId, int LocalX, int LocalY);
