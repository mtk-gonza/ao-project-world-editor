using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Discovers a continuous global map from legacy map exits. Chunks remain
/// individual 100x100 CSM files; only their global placement is assembled.
/// </summary>
internal sealed class GlobalWorldAssembler
{
    private const int WestSourceBoundary = 13;
    private const int EastSourceBoundary = 88;
    private const int WestTargetBoundary = 14;
    private const int EastTargetBoundary = 87;
    private const int NorthSourceBoundary = 10;
    private const int SouthSourceBoundary = 91;
    private const int NorthTargetBoundary = 11;
    private const int SouthTargetBoundary = 90;

    public GlobalWorldAssembly Assemble(
        IReadOnlyDictionary<short, WorldSector> maps,
        short rootMap,
        string levelId = "surface")
    {
        ArgumentNullException.ThrowIfNull(maps);
        if (!maps.ContainsKey(rootMap))
            throw new KeyNotFoundException($"No existe el mapa raiz {rootMap}.");
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("El nivel global es obligatorio.", nameof(levelId));

        var links = DiscoverLinks(maps);
        var linksByMap = links.GroupBy(link => link.FromMap)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var positions = new Dictionary<short, GlobalChunkCoordinate> { [rootMap] = new(0, 0) };
        var conflicts = new List<GlobalWorldConflict>();
        var queue = new Queue<short>();
        queue.Enqueue(rootMap);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!linksByMap.TryGetValue(current, out var outgoing)) continue;
            foreach (var link in outgoing.GroupBy(item => item.Side).Select(SelectBestLink))
            {
                if (!maps.ContainsKey(link.ToMap)) continue;
                var expected = Move(positions[current], link.Side);
                if (!positions.TryGetValue(link.ToMap, out var actual))
                {
                    positions[link.ToMap] = expected;
                    queue.Enqueue(link.ToMap);
                    continue;
                }
                if (actual != expected)
                    conflicts.Add(new GlobalWorldConflict(
                        current, link.ToMap, link.Side, expected, actual,
                        "Las salidas del mapa forman una posicion global incompatible."));
            }
        }

        var width = positions.Count == 0 ? 0
            : positions.Values.Max(chunk => chunk.X) - positions.Values.Min(chunk => chunk.X) + 1;
        var height = positions.Count == 0 ? 0
            : positions.Values.Max(chunk => chunk.Y) - positions.Values.Min(chunk => chunk.Y) + 1;

        // A horizontal seam makes the legacy world a cylinder: the west exit of
        // column 0 points to column width-1 and closes the loop. Normalize X
        // modulo the inferred width and drop the wrap conflicts instead of
        // reporting half the world as misplaced.
        var wrapConflicts = conflicts.Where(conflict =>
            conflict.Expected.Y == conflict.Actual.Y &&
            Math.Abs(conflict.Expected.X - conflict.Actual.X) == width).ToArray();
        if (width > 0 && wrapConflicts.Length > 0)
        {
            foreach (var mapId in positions.Keys.ToArray())
                positions[mapId] = new GlobalChunkCoordinate(Mod(positions[mapId].X, width), positions[mapId].Y);
            conflicts.RemoveAll(conflict => wrapConflicts.Contains(conflict));
        }

        var placements = positions
            .Select(item => new GlobalSectorPlacement(item.Key, item.Value, levelId))
            .OrderBy(item => item.Chunk.Y)
            .ThenBy(item => item.Chunk.X)
            .ToArray();
        return new GlobalWorldAssembly
        {
            Placements = placements,
            Links = links,
            Conflicts = conflicts,
            Width = width,
            Height = height,
            WrapX = wrapConflicts.Length > 0
        };
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static IReadOnlyList<GlobalMapLink> DiscoverLinks(IReadOnlyDictionary<short, WorldSector> maps)
    {
        var candidates = new List<GlobalMapLink>();
        foreach (var (mapId, map) in maps)
        for (var y = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
             y <= Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height); y++)
        for (var x = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
             x <= Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width); x++)
        {
            var exit = map.Blocks[x, y].TileExit;
            if (exit.SectorId <= 0 || exit.SectorId == mapId || !maps.ContainsKey(exit.SectorId)) continue;
            if (x <= WestSourceBoundary && exit.X >= EastTargetBoundary)
                candidates.Add(new(mapId, exit.SectorId, GlobalWorldSide.West, 1, (short)x, (short)y, exit.X, exit.Y));
            else if (x >= EastSourceBoundary && exit.X <= WestTargetBoundary)
                candidates.Add(new(mapId, exit.SectorId, GlobalWorldSide.East, 1, (short)x, (short)y, exit.X, exit.Y));
            else if (y <= NorthSourceBoundary && exit.Y >= SouthTargetBoundary)
                candidates.Add(new(mapId, exit.SectorId, GlobalWorldSide.North, 1, (short)x, (short)y, exit.X, exit.Y));
            else if (y >= SouthSourceBoundary && exit.Y <= NorthTargetBoundary)
                candidates.Add(new(mapId, exit.SectorId, GlobalWorldSide.South, 1, (short)x, (short)y, exit.X, exit.Y));
        }

        return candidates
            .GroupBy(link => (link.FromMap, link.ToMap, link.Side, link.TargetX, link.TargetY))
            .Select(group =>
            {
                var first = group.First();
                return first with { Evidence = group.Count() };
            })
            .ToArray();
    }

    private static GlobalMapLink SelectBestLink(IEnumerable<GlobalMapLink> links) =>
        links.OrderByDescending(link => link.Evidence).ThenBy(link => link.ToMap).First();

    private static GlobalChunkCoordinate Move(GlobalChunkCoordinate chunk, GlobalWorldSide side) => side switch
    {
        GlobalWorldSide.North => new(chunk.X, chunk.Y - 1),
        GlobalWorldSide.South => new(chunk.X, chunk.Y + 1),
        GlobalWorldSide.East => new(chunk.X + 1, chunk.Y),
        GlobalWorldSide.West => new(chunk.X - 1, chunk.Y),
        _ => chunk
    };
}
