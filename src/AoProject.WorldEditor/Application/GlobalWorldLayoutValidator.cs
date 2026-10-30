using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed record GlobalWorldLayoutIssue(short SectorId, string Message);

/// <summary>
/// Validates the official mapsworlddata grid in place: every adjacent pair of
/// cells must have the canonical border exit in both directions. The legacy
/// world wraps horizontally (column 0 is next to the last column), so the seam
/// is checked too. Repeated map ids are filler and are not expected to connect.
/// </summary>
internal sealed class GlobalWorldLayoutValidator
{
    public IReadOnlyList<GlobalWorldLayoutIssue> Validate(
        GlobalWorldAssembly official,
        IReadOnlyDictionary<short, WorldSector> maps,
        short rootMap)
    {
        ArgumentNullException.ThrowIfNull(official);
        ArgumentNullException.ThrowIfNull(maps);

        var issues = new List<GlobalWorldLayoutIssue>();
        var cells = official.Placements
            .GroupBy(placement => placement.Chunk)
            .ToDictionary(group => group.Key, group => group.First().SectorId);
        if (cells.Count == 0)
        {
            issues.Add(new GlobalWorldLayoutIssue(rootMap, "El layout activo no tiene sectores."));
            return issues;
        }
        if (!cells.Values.Contains(rootMap))
            issues.Add(new GlobalWorldLayoutIssue(rootMap, "El mapa raiz no pertenece al layout activo."));

        var minX = cells.Keys.Min(chunk => chunk.X);
        var maxX = cells.Keys.Max(chunk => chunk.X);
        var width = official.Width > 0 ? official.Width : maxX - minX + 1;
        var filler = cells.Values.GroupBy(id => id).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();

        foreach (var (chunk, mapId) in cells)
        {
            if (!maps.ContainsKey(mapId)) continue;

            // East neighbour; the seam is only a neighbour on a cylindrical world.
            if (chunk.X < width - 1)
            {
                if (cells.TryGetValue(new GlobalChunkCoordinate(chunk.X + 1, chunk.Y), out var eastId) && eastId != mapId)
                    CheckPair(mapId, maps, eastId, filler, issues,
                        GlobalWorldSide.East, GlobalWorldSide.West);
            }
            else if (official.WrapX &&
                     cells.TryGetValue(new GlobalChunkCoordinate(minX, chunk.Y), out var seamId) && seamId != mapId)
            {
                CheckPair(mapId, maps, seamId, filler, issues,
                    GlobalWorldSide.East, GlobalWorldSide.West);
            }

            // South neighbour; the legacy world has no vertical wrap.
            if (cells.TryGetValue(new GlobalChunkCoordinate(chunk.X, chunk.Y + 1), out var southId) && southId != mapId)
                CheckPair(mapId, maps, southId, filler, issues,
                    GlobalWorldSide.South, GlobalWorldSide.North);
        }

        return issues;
    }

    private static void CheckPair(
        short fromId,
        IReadOnlyDictionary<short, WorldSector> maps,
        short toId,
        HashSet<short> filler,
        List<GlobalWorldLayoutIssue> issues,
        GlobalWorldSide forward,
        GlobalWorldSide backward)
    {
        if (filler.Contains(fromId) || filler.Contains(toId)) return;
        if (!maps.TryGetValue(fromId, out var from) || !maps.TryGetValue(toId, out var to)) return;
        if (!HasExit(from, forward, toId))
            issues.Add(new GlobalWorldLayoutIssue(fromId,
                $"Falta salida {forward} de {fromId} hacia {toId} en el borde canonico."));
        if (!HasExit(to, backward, fromId))
            issues.Add(new GlobalWorldLayoutIssue(toId,
                $"Falta salida {backward} de {toId} hacia {fromId} en el borde canonico."));
    }

    private static bool HasExit(WorldSector map, GlobalWorldSide side, short target)
    {
        for (var index = 1; index <= WorldSector.Width; index++)
        {
            var exit = BorderExit(map, side, index);
            if (exit.Map != target) continue;
            var canonical = side switch
            {
                GlobalWorldSide.West => exit.X == 87,
                GlobalWorldSide.East => exit.X == 14,
                GlobalWorldSide.North => exit.Y == 90,
                GlobalWorldSide.South => exit.Y == 11,
                _ => false
            };
            if (canonical) return true;
        }
        return false;
    }

    private static WorldPosition BorderExit(WorldSector map, GlobalWorldSide side, int index) => side switch
    {
        GlobalWorldSide.West => map.Blocks[13, index].TileExit,
        GlobalWorldSide.East => map.Blocks[88, index].TileExit,
        GlobalWorldSide.North => map.Blocks[index, 10].TileExit,
        _ => map.Blocks[index, 91].TileExit
    };
}
