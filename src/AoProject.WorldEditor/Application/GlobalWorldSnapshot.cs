namespace AoProject.WorldEditor.Application;

/// <summary>
/// Undo snapshot for global-world operations. Only the maps touched by the
/// selection are captured; snapshotting the whole world cost several GB and
/// tens of seconds per operation.
/// </summary>
internal sealed class GlobalWorldSnapshot
{
    private readonly IReadOnlyDictionary<short, WorldSectorSnapshot> _maps;

    private GlobalWorldSnapshot(IReadOnlyDictionary<short, WorldSectorSnapshot> maps) => _maps = maps;

    public int MapCount => _maps.Count;

    public static GlobalWorldSnapshot Capture(GlobalWorldDocument world, IEnumerable<short> mapIds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapIds);
        var snapshots = new Dictionary<short, WorldSectorSnapshot>();
        foreach (var mapId in mapIds.Distinct())
            if (world.Maps.TryGetValue(mapId, out var map))
                snapshots[mapId] = WorldSectorSnapshot.Capture(map);
        return new GlobalWorldSnapshot(snapshots);
    }

    public void Restore(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var (mapId, snapshot) in _maps)
            if (world.Maps.TryGetValue(mapId, out var map)) snapshot.Restore(map);
    }
}
