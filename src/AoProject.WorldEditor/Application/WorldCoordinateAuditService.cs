using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Audits the official world layout and the local -> global coordinate mapping:
/// core coverage, overlaps, gaps, wrap seam, CsmMapSize intersection, canonical
/// exits between adjacent cells and the TryGetGlobalTile/TryResolve round trip.
/// </summary>
internal sealed class WorldCoordinateAuditService
{
    public IReadOnlyList<WorldAuditSummary> Audit(
        IReadOnlyList<MapsWorldDataService.WorldLayout> layouts,
        IReadOnlyCollection<short> availableMaps,
        IReadOnlyDictionary<short, MapCoordinateInfo> mapInfo,
        IReadOnlyDictionary<short, string>? mapPaths = null)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(availableMaps);
        ArgumentNullException.ThrowIfNull(mapInfo);
        var available = availableMaps.ToHashSet();
        var results = new List<WorldAuditSummary>();
        foreach (var layout in layouts)
        {
            var issues = new List<WorldAuditIssue>();
            var root = MapsWorldDataService.ChooseRoot(layout, available);
            if (root <= 0)
            {
                results.Add(new WorldAuditSummary(layout.WorldId, layout.Width, layout.Height, 0, 1, 0, 0,
                    [new WorldAuditIssue(layout.WorldId, 0, "WORLD-ROOT", AuditSeverity.Error,
                        "No hay un mapa raiz disponible para este WORLDMAP.")]));
                continue;
            }
            var assembly = MapsWorldDataService.BuildAssembly(layouts, available, root);
            if (assembly is null)
            {
                results.Add(new WorldAuditSummary(layout.WorldId, layout.Width, layout.Height, 0, 1, 0, 0,
                    [new WorldAuditIssue(layout.WorldId, root, "WORLD-LAYOUT", AuditSeverity.Error,
                        "No se pudo construir el layout oficial.")]));
                continue;
            }

            var cells = new Dictionary<GlobalChunkCoordinate, short>();
            foreach (var placement in assembly.Placements)
            {
                if (cells.ContainsKey(placement.Chunk))
                {
                    issues.Add(new WorldAuditIssue(layout.WorldId, placement.MapId, "WORLD-CHUNK", AuditSeverity.Error,
                        $"Chunk duplicado {placement.Chunk.X},{placement.Chunk.Y}.",
                        placement.Chunk.X, placement.Chunk.Y));
                    continue;
                }
                cells[placement.Chunk] = placement.MapId;
            }
            var chunks = cells.Keys.ToHashSet();
            var width = assembly.Width > 0 ? assembly.Width : (cells.Count == 0 ? 0 : cells.Keys.Max(chunk => chunk.X) + 1);
            var height = assembly.Height > 0 ? assembly.Height : (cells.Count == 0 ? 0 : cells.Keys.Max(chunk => chunk.Y) + 1);
            var mapCounts = cells.Values.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
            var filler = mapCounts.Where(pair => pair.Value > 1).Select(pair => pair.Key).ToHashSet();
            var dummy = new WorldMap();
            var paths = mapPaths ?? new Dictionary<short, string>();
            var document = new GlobalWorldDocument(assembly,
                available.ToDictionary(id => id, _ => dummy), paths, root);
            var visible = new List<VisibleCell>();

            foreach (var (chunk, mapId) in cells)
            {
                if (chunk.X < 0 || chunk.X >= width || chunk.Y < 0 || chunk.Y >= height)
                    issues.Add(new WorldAuditIssue(layout.WorldId, mapId, "WORLD-CHUNK", AuditSeverity.Error,
                        $"Chunk fuera de la grilla {width}x{height}.", chunk.X, chunk.Y));

                var hasWest = chunks.Contains(new GlobalChunkCoordinate(chunk.X - 1, chunk.Y));
                var hasNorth = chunks.Contains(new GlobalChunkCoordinate(chunk.X, chunk.Y - 1));
                var minX = hasWest ? GlobalChunkGeometry.CoreMinX + 1 : GlobalChunkGeometry.CoreMinX;
                var minY = hasNorth ? GlobalChunkGeometry.CoreMinY + 1 : GlobalChunkGeometry.CoreMinY;
                var shift = document.StitchShift(chunk);
                var left = chunk.X * GlobalChunkGeometry.WidthTiles - shift.X + minX - 1;
                var top = chunk.Y * GlobalChunkGeometry.HeightTiles - shift.Y + minY - 1;
                var right = chunk.X * GlobalChunkGeometry.WidthTiles - shift.X + GlobalChunkGeometry.CoreMaxX - 1;
                var bottom = chunk.Y * GlobalChunkGeometry.HeightTiles - shift.Y + GlobalChunkGeometry.CoreMaxY - 1;
                visible.Add(new VisibleCell(mapId, chunk, minX, minY, GlobalChunkGeometry.CoreMaxX,
                    GlobalChunkGeometry.CoreMaxY, left, top, right, bottom));

                if (!mapInfo.TryGetValue(mapId, out var info))
                {
                    issues.Add(new WorldAuditIssue(layout.WorldId, mapId, "WORLD-MAP", AuditSeverity.Warning,
                        "No hay informacion del CSM para este mapa.", chunk.X, chunk.Y, left, top));
                    continue;
                }

                var intersectX1 = Math.Max(minX, (int)info.XMin);
                var intersectX2 = Math.Min(GlobalChunkGeometry.CoreMaxX, (int)info.XMax);
                var intersectY1 = Math.Max(minY, (int)info.YMin);
                var intersectY2 = Math.Min(GlobalChunkGeometry.CoreMaxY, (int)info.YMax);
                if (intersectX1 > intersectX2 || intersectY1 > intersectY2)
                    issues.Add(new WorldAuditIssue(layout.WorldId, mapId, "WORLD-CSM-EMPTY", AuditSeverity.Error,
                        $"El core {minX}..{GlobalChunkGeometry.CoreMaxX},{minY}..{GlobalChunkGeometry.CoreMaxY} " +
                        $"no intersecta CsmMapSize {info.XMin}..{info.XMax},{info.YMin}..{info.YMax}.",
                        chunk.X, chunk.Y, left, top));
                else if (intersectX1 > minX || intersectX2 < GlobalChunkGeometry.CoreMaxX ||
                         intersectY1 > minY || intersectY2 < GlobalChunkGeometry.CoreMaxY)
                    issues.Add(new WorldAuditIssue(layout.WorldId, mapId, "WORLD-CSM-PARTIAL", AuditSeverity.Warning,
                        $"El core no esta cubierto completo por CsmMapSize " +
                        $"{info.XMin}..{info.XMax},{info.YMin}..{info.YMax}.",
                        chunk.X, chunk.Y, left, top));

                if (mapCounts[mapId] == 1)
                    CheckRoundTrip(document, layout.WorldId, mapId, chunk, minX, minY,
                        GlobalChunkGeometry.CoreMaxX, GlobalChunkGeometry.CoreMaxY, issues);
            }

            foreach (var (chunk, mapId) in cells)
            {
                if (chunk.X < width - 1)
                {
                    if (cells.TryGetValue(new GlobalChunkCoordinate(chunk.X + 1, chunk.Y), out var east) && east != mapId)
                        CheckPair(layout.WorldId, chunk, mapId, east, GlobalWorldSide.East, GlobalWorldSide.West,
                            filler, mapInfo, issues);
                }
                else if (assembly.WrapX && cells.TryGetValue(new GlobalChunkCoordinate(0, chunk.Y), out var seam) && seam != mapId)
                {
                    CheckPair(layout.WorldId, chunk, mapId, seam, GlobalWorldSide.East, GlobalWorldSide.West,
                        filler, mapInfo, issues);
                }
                if (chunk.Y < height - 1 &&
                    cells.TryGetValue(new GlobalChunkCoordinate(chunk.X, chunk.Y + 1), out var south) && south != mapId)
                {
                    CheckPair(layout.WorldId, chunk, mapId, south, GlobalWorldSide.South, GlobalWorldSide.North,
                        filler, mapInfo, issues);
                }
            }

            CheckCoverage(layout.WorldId, visible, issues);
            foreach (var pair in mapCounts.Where(pair => pair.Value > 1))
                issues.Add(new WorldAuditIssue(layout.WorldId, pair.Key, "WORLD-FILLER", AuditSeverity.Info,
                    $"Mapa repetido en {pair.Value} celdas (una sola instancia compartida)."));

            results.Add(new WorldAuditSummary(layout.WorldId, width, height, cells.Count,
                issues.Count(issue => issue.Severity == AuditSeverity.Error),
                issues.Count(issue => issue.Severity == AuditSeverity.Warning),
                issues.Count(issue => issue.Severity == AuditSeverity.Info),
                issues));
        }
        return results;
    }

    private static void CheckRoundTrip(GlobalWorldDocument document, int worldId, short mapId,
        GlobalChunkCoordinate chunk, int minX, int minY, int maxX, int maxY, List<WorldAuditIssue> issues)
    {
        var midX = (minX + maxX) / 2;
        var midY = (minY + maxY) / 2;
        var points = new (int X, int Y)[]
        {
            (minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY),
            (midX, midY), (minX, midY), (maxX, midY), (midX, minY), (midX, maxY)
        };
        foreach (var (x, y) in points)
        {
            if (!document.TryGetGlobalTile(mapId, x, y, out var global))
            {
                issues.Add(new WorldAuditIssue(worldId, mapId, "WORLD-ROUNDTRIP", AuditSeverity.Error,
                    $"TryGetGlobalTile rechazo el tile visible {x},{y}.", chunk.X, chunk.Y));
                continue;
            }
            if (!document.TryResolve(global.X, global.Y, out var resolved) ||
                resolved.MapId != mapId || resolved.LocalX != x || resolved.LocalY != y)
            {
                issues.Add(new WorldAuditIssue(worldId, mapId, "WORLD-ROUNDTRIP", AuditSeverity.Error,
                    $"Round-trip roto en {x},{y} -> {global.X},{global.Y} -> " +
                    $"{resolved.MapId}({resolved.LocalX},{resolved.LocalY}).",
                    chunk.X, chunk.Y, global.X, global.Y));
            }
        }
    }

    private static void CheckPair(int worldId, GlobalChunkCoordinate chunk, short fromId, short toId,
        GlobalWorldSide forward, GlobalWorldSide backward, HashSet<short> filler,
        IReadOnlyDictionary<short, MapCoordinateInfo> mapInfo, List<WorldAuditIssue> issues)
    {
        if (filler.Contains(fromId) || filler.Contains(toId)) return;
        if (!mapInfo.TryGetValue(fromId, out var from) || !mapInfo.TryGetValue(toId, out var to)) return;
        if (!HasExit(from, forward, toId))
            issues.Add(new WorldAuditIssue(worldId, fromId, "WORLD-EXIT", AuditSeverity.Error,
                $"Falta salida {forward} de {fromId} hacia {toId} en el borde canonico.",
                chunk.X, chunk.Y));
        if (!HasExit(to, backward, fromId))
            issues.Add(new WorldAuditIssue(worldId, toId, "WORLD-EXIT", AuditSeverity.Error,
                $"Falta salida {backward} de {toId} hacia {fromId} en el borde canonico.",
                chunk.X, chunk.Y));
    }

    private static bool HasExit(MapCoordinateInfo info, GlobalWorldSide side, short target) =>
        info.BorderExits.Any(exit => exit.Side == side && exit.Target == target);

    private static void CheckCoverage(int worldId, IReadOnlyList<VisibleCell> visible, List<WorldAuditIssue> issues)
    {
        for (var i = 0; i < visible.Count; i++)
        for (var j = i + 1; j < visible.Count; j++)
        {
            var a = visible[i];
            var b = visible[j];
            if (a.Left <= b.Right && b.Left <= a.Right && a.Top <= b.Bottom && b.Top <= a.Bottom)
                issues.Add(new WorldAuditIssue(worldId, a.MapId, "WORLD-OVERLAP", AuditSeverity.Error,
                    $"Solapamiento visible con mapa {b.MapId} en chunks {a.Chunk.X},{a.Chunk.Y} / {b.Chunk.X},{b.Chunk.Y}.",
                    a.Chunk.X, a.Chunk.Y, Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top)));
        }

        foreach (var row in visible.GroupBy(cell => cell.Chunk.Y))
        {
            var ordered = row.OrderBy(cell => cell.Chunk.X).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                if (previous.Right + 1 != current.Left)
                    issues.Add(new WorldAuditIssue(worldId, current.MapId,
                        previous.Right + 1 < current.Left ? "WORLD-GAP" : "WORLD-OVERLAP",
                        AuditSeverity.Error,
                        $"Celdas {previous.Chunk.X},{previous.Chunk.Y} y {current.Chunk.X},{current.Chunk.Y} " +
                        $"no son contiguas en X (right={previous.Right}, left={current.Left}).",
                        current.Chunk.X, current.Chunk.Y, current.Left, current.Top));
            }
        }

        foreach (var column in visible.GroupBy(cell => cell.Chunk.X))
        {
            var ordered = column.OrderBy(cell => cell.Chunk.Y).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                if (previous.Bottom + 1 != current.Top)
                    issues.Add(new WorldAuditIssue(worldId, current.MapId,
                        previous.Bottom + 1 < current.Top ? "WORLD-GAP" : "WORLD-OVERLAP",
                        AuditSeverity.Error,
                        $"Celdas {previous.Chunk.X},{previous.Chunk.Y} y {current.Chunk.X},{current.Chunk.Y} " +
                        $"no son contiguas en Y (bottom={previous.Bottom}, top={current.Top}).",
                        current.Chunk.X, current.Chunk.Y, current.Left, current.Top));
            }
        }
    }

    private readonly record struct VisibleCell(
        short MapId,
        GlobalChunkCoordinate Chunk,
        int MinLocalX,
        int MinLocalY,
        int MaxLocalX,
        int MaxLocalY,
        int Left,
        int Top,
        int Right,
        int Bottom);
}
