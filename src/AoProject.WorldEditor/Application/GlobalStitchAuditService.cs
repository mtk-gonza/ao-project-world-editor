using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Engine.Rendering;

namespace AoProject.WorldEditor.Application;

internal sealed record GlobalStitchAuditEntry(
    short MapId,
    int ChunkX,
    int ChunkY,
    int ShiftX,
    int ShiftY);

internal sealed record StitchedCompileAuditEntry(
    short MapId,
    int ShiftX,
    int ShiftY,
    int WrittenTiles,
    int DuplicateTiles,
    int ConflictingTiles,
    int MinX,
    int MaxX,
    int MinY,
    int MaxY);

internal sealed record StitchedConflictAuditEntry(
    short ExistingMapId,
    short IncomingMapId,
    int ExistingLocalX,
    int ExistingLocalY,
    int IncomingLocalX,
    int IncomingLocalY,
    int GlobalX,
    int GlobalY,
    string Field,
    string Resolution = "KeepExistingPlacement");

internal sealed record ExtendedExitAuditEntry(
    short SourceMapId,
    int SourceX,
    int SourceY,
    short TargetMapId,
    int TargetX,
    int TargetY,
    string Classification);

internal sealed record MapLevelAuditEntry(
    short MapId,
    int MetadataLevel,
    int ChunkX,
    int ChunkY,
    string Name);

/// <summary>Reports detected border collapses without changing coordinates.</summary>
internal sealed class GlobalStitchAuditService
{
    public IReadOnlyList<GlobalStitchAuditEntry> Audit(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var stitcher = new WorldBorderStitcher(world.Assembly.Placements, mapId =>
            world.Maps.TryGetValue(mapId, out var map) ? WorldBorderStitcher.Sample(map) : null);
        var shifts = stitcher.ForAll(world.Assembly.Placements.Select(item => item.Chunk));
        return world.Assembly.Placements
            .Select(item =>
            {
                var x = shifts.Columns.GetValueOrDefault(item.Chunk);
                var y = shifts.Rows.GetValueOrDefault(item.Chunk);
                return new GlobalStitchAuditEntry(item.MapId, item.Chunk.X, item.Chunk.Y, x, y);
            })
            .Where(item => item.ShiftX != 0 || item.ShiftY != 0)
            .OrderBy(item => item.ChunkY).ThenBy(item => item.ChunkX)
            .ThenBy(item => item.MapId)
            .ToArray();
    }

    public void SaveTsv(string path, IReadOnlyList<GlobalStitchAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder()
            .AppendLine("MapId\tChunkX\tChunkY\tShiftX\tShiftY");
        foreach (var entry in entries)
            builder.Append(entry.MapId).Append('\t').Append(entry.ChunkX).Append('\t')
                .Append(entry.ChunkY).Append('\t').Append(entry.ShiftX).Append('\t')
                .Append(entry.ShiftY).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Simulates compacted tile placement using the detected shifts. It never
    /// mutates a map or produces a runtime floor; its purpose is to quantify
    /// overlaps before changing the physical .gmap arrays.
    /// </summary>
    public IReadOnlyList<StitchedCompileAuditEntry> AuditCompactedMaterialization(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var stitcher = new WorldBorderStitcher(world.Assembly.Placements, mapId =>
            world.Maps.TryGetValue(mapId, out var map) ? WorldBorderStitcher.Sample(map) : null);
        var shifts = stitcher.ForAll(world.Assembly.Placements.Select(item => item.Chunk));
        var minChunkX = world.Assembly.Placements.Min(item => item.Chunk.X);
        var minChunkY = world.Assembly.Placements.Min(item => item.Chunk.Y);
        var owners = new Dictionary<(int X, int Y), (short MapId, MapBlock Block)>();
        var stats = world.Assembly.Placements.ToDictionary(item => item.MapId,
            item => new MutableCompileAudit(item.MapId,
                shifts.Columns.GetValueOrDefault(item.Chunk),
                shifts.Rows.GetValueOrDefault(item.Chunk)));

        foreach (var placement in world.Assembly.Placements)
        {
            if (!world.Maps.TryGetValue(placement.MapId, out var map)) continue;
            var hasWest = world.Assembly.Placements.Any(item =>
                item.Chunk.X == placement.Chunk.X - 1 && item.Chunk.Y == placement.Chunk.Y);
            var hasNorth = world.Assembly.Placements.Any(item =>
                item.Chunk.X == placement.Chunk.X && item.Chunk.Y == placement.Chunk.Y - 1);
            var shiftX = shifts.Columns.GetValueOrDefault(placement.Chunk);
            var shiftY = shifts.Rows.GetValueOrDefault(placement.Chunk);
            var stat = stats[placement.MapId];

            for (var localY = 1; localY <= WorldMap.Height; localY++)
            for (var localX = 1; localX <= WorldMap.Width; localX++)
            {
                if (!GlobalChunkGeometry.IsCoreTile(localX, localY, hasWest, hasNorth)) continue;
                var coordinate = (
                    (placement.Chunk.X - minChunkX) * GlobalChunkGeometry.WidthTiles + localX - 1 - shiftX,
                    (placement.Chunk.Y - minChunkY) * GlobalChunkGeometry.HeightTiles + localY - 1 - shiftY);
                var block = map.Blocks[localX, localY];
                if (owners.TryGetValue(coordinate, out var owner))
                {
                    stat.DuplicateTiles++;
                    if (!Equivalent(owner.Block, block)) stat.ConflictingTiles++;
                }
                else
                {
                    owners[coordinate] = (placement.MapId, block);
                    stat.WrittenTiles++;
                }
                stat.MinX = Math.Min(stat.MinX, coordinate.Item1);
                stat.MaxX = Math.Max(stat.MaxX, coordinate.Item1);
                stat.MinY = Math.Min(stat.MinY, coordinate.Item2);
                stat.MaxY = Math.Max(stat.MaxY, coordinate.Item2);
            }
        }

        return stats.Values.Select(item => item.ToRecord()).OrderBy(item => item.MapId).ToArray();
    }

    public void SaveCompactedMaterializationTsv(string path, IReadOnlyList<StitchedCompileAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder().AppendLine(
            "MapId\tShiftX\tShiftY\tWrittenTiles\tDuplicateTiles\tConflictingTiles\tMinX\tMaxX\tMinY\tMaxY");
        foreach (var entry in entries)
            builder.Append(entry.MapId).Append('\t').Append(entry.ShiftX).Append('\t')
                .Append(entry.ShiftY).Append('\t').Append(entry.WrittenTiles).Append('\t')
                .Append(entry.DuplicateTiles).Append('\t').Append(entry.ConflictingTiles).Append('\t')
                .Append(entry.MinX).Append('\t').Append(entry.MaxX).Append('\t')
                .Append(entry.MinY).Append('\t').Append(entry.MaxY).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    public IReadOnlyList<StitchedConflictAuditEntry> AuditCompactedConflicts(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var stitcher = new WorldBorderStitcher(world.Assembly.Placements, mapId =>
            world.Maps.TryGetValue(mapId, out var map) ? WorldBorderStitcher.Sample(map) : null);
        var shifts = stitcher.ForAll(world.Assembly.Placements.Select(item => item.Chunk));
        var minChunkX = world.Assembly.Placements.Min(item => item.Chunk.X);
        var minChunkY = world.Assembly.Placements.Min(item => item.Chunk.Y);
        var owners = new Dictionary<(int X, int Y), (short MapId, int LocalX, int LocalY, MapBlock Block)>();
        var conflicts = new List<StitchedConflictAuditEntry>();

        foreach (var placement in world.Assembly.Placements.OrderBy(item => item.Chunk.Y).ThenBy(item => item.Chunk.X))
        {
            if (!world.Maps.TryGetValue(placement.MapId, out var map)) continue;
            var hasWest = world.Assembly.Placements.Any(item =>
                item.Chunk.X == placement.Chunk.X - 1 && item.Chunk.Y == placement.Chunk.Y);
            var hasNorth = world.Assembly.Placements.Any(item =>
                item.Chunk.X == placement.Chunk.X && item.Chunk.Y == placement.Chunk.Y - 1);
            var shiftX = shifts.Columns.GetValueOrDefault(placement.Chunk);
            var shiftY = shifts.Rows.GetValueOrDefault(placement.Chunk);
            for (var localY = 1; localY <= WorldMap.Height; localY++)
            for (var localX = 1; localX <= WorldMap.Width; localX++)
            {
                if (!GlobalChunkGeometry.IsCoreTile(localX, localY, hasWest, hasNorth)) continue;
                var coordinate = (
                    (placement.Chunk.X - minChunkX) * GlobalChunkGeometry.WidthTiles + localX - 1 - shiftX,
                    (placement.Chunk.Y - minChunkY) * GlobalChunkGeometry.HeightTiles + localY - 1 - shiftY);
                var block = map.Blocks[localX, localY];
                if (owners.TryGetValue(coordinate, out var owner) && !Equivalent(owner.Block, block))
                    conflicts.Add(new StitchedConflictAuditEntry(owner.MapId, placement.MapId,
                        owner.LocalX, owner.LocalY, localX, localY,
                        coordinate.Item1, coordinate.Item2, DifferenceField(owner.Block, block)));
                else
                    owners[coordinate] = (placement.MapId, localX, localY, block);
            }
        }
        return conflicts;
    }

    public void SaveConflictTsv(string path, IReadOnlyList<StitchedConflictAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder().AppendLine(
            "ExistingMapId\tIncomingMapId\tExistingLocalX\tExistingLocalY\tIncomingLocalX\tIncomingLocalY\tGlobalX\tGlobalY\tField\tResolution");
        foreach (var entry in entries)
            builder.Append(entry.ExistingMapId).Append('\t').Append(entry.IncomingMapId).Append('\t')
                .Append(entry.ExistingLocalX).Append('\t').Append(entry.ExistingLocalY).Append('\t')
                .Append(entry.IncomingLocalX).Append('\t').Append(entry.IncomingLocalY).Append('\t')
                .Append(entry.GlobalX).Append('\t').Append(entry.GlobalY).Append('\t')
                .Append(entry.Field).Append('\t').Append(entry.Resolution).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    public IReadOnlyList<ExtendedExitAuditEntry> AuditExtendedExits(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var result = new List<ExtendedExitAuditEntry>();
        foreach (var placement in world.Assembly.Placements)
        {
            if (!world.Maps.TryGetValue(placement.MapId, out var map)) continue;
            for (var y = 1; y <= WorldMap.Height; y++)
            for (var x = 1; x <= WorldMap.Width; x++)
            {
                var exit = map.Blocks[x, y].TileExit;
                if (exit.Map == 0) continue;
                var classification = exit.Map < 0
                    ? "MAPA-ESPECIAL-NEGATIVO"
                    : !world.Maps.ContainsKey(exit.Map)
                        ? "MAPA-FUERA-DE-ASSEMBLY"
                        : exit.X is < 1 or > 100 || exit.Y is < 1 or > 100
                            ? (exit.X is < 1 or > 100 ? "X-EXTENDIDA" : "Y-EXTENDIDA")
                            : string.Empty;
                if (classification.Length == 0) continue;
                result.Add(new ExtendedExitAuditEntry(placement.MapId, x, y,
                    exit.Map, exit.X, exit.Y,
                    classification));
            }
        }
        return result;
    }

    public void SaveExtendedExitTsv(string path, IReadOnlyList<ExtendedExitAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder().AppendLine(
            "SourceMapId\tSourceX\tSourceY\tTargetMapId\tTargetX\tTargetY\tClassification");
        foreach (var entry in entries)
            builder.Append(entry.SourceMapId).Append('\t').Append(entry.SourceX).Append('\t')
                .Append(entry.SourceY).Append('\t').Append(entry.TargetMapId).Append('\t')
                .Append(entry.TargetX).Append('\t').Append(entry.TargetY).Append('\t')
                .Append(entry.Classification).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    public IReadOnlyList<MapLevelAuditEntry> AuditMapLevels(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Assembly.Placements
            .Where(item => world.Maps.ContainsKey(item.MapId))
            .Select(item =>
            {
                var map = world.Maps[item.MapId];
                return new MapLevelAuditEntry(item.MapId, map.Info.Level,
                    item.Chunk.X, item.Chunk.Y, map.Info.Name);
            })
            .OrderBy(item => item.MetadataLevel)
            .ThenBy(item => item.ChunkY)
            .ThenBy(item => item.ChunkX)
            .ToArray();
    }

    public void SaveMapLevelsTsv(string path, IReadOnlyList<MapLevelAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder().AppendLine("MapId\tMetadataLevel\tChunkX\tChunkY\tName");
        foreach (var entry in entries)
            builder.Append(entry.MapId).Append('\t').Append(entry.MetadataLevel).Append('\t')
                .Append(entry.ChunkX).Append('\t').Append(entry.ChunkY).Append('\t')
                .Append(entry.Name).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static bool Equivalent(MapBlock first, MapBlock second)
    {
        if (first.Blocked != second.Blocked || first.Trigger != second.Trigger ||
            first.CharacterIndex != second.CharacterIndex || first.NpcIndex != second.NpcIndex ||
            first.ParticleGroup != second.ParticleGroup || first.ParticleIndex != second.ParticleIndex ||
            first.Marked != second.Marked || first.Object.Index != second.Object.Index ||
            first.Object.Amount != second.Object.Amount || first.ObjectGraphic.GraphicIndex != second.ObjectGraphic.GraphicIndex ||
            first.Light.Range != second.Light.Range || first.Light.Color != second.Light.Color ||
            first.Red != second.Red || first.Green != second.Green || first.Blue != second.Blue)
            return false;
        for (var i = 0; i < first.Graphics.Length; i++)
            if (first.Graphics[i].GraphicIndex != second.Graphics[i].GraphicIndex) return false;
        return true;
    }

    private static string DifferenceField(MapBlock first, MapBlock second)
    {
        if (first.Blocked != second.Blocked) return "Blocked";
        if (first.Trigger != second.Trigger) return "Trigger";
        if (first.CharacterIndex != second.CharacterIndex) return "Character";
        if (first.NpcIndex != second.NpcIndex) return "Npc";
        if (first.ParticleGroup != second.ParticleGroup || first.ParticleIndex != second.ParticleIndex) return "Particle";
        if (first.Marked != second.Marked) return "Marked";
        if (first.Object.Index != second.Object.Index || first.Object.Amount != second.Object.Amount ||
            first.ObjectGraphic.GraphicIndex != second.ObjectGraphic.GraphicIndex) return "Object";
        if (first.Light.Range != second.Light.Range || first.Light.Color != second.Light.Color) return "Light";
        if (first.Red != second.Red || first.Green != second.Green || first.Blue != second.Blue) return "Color";
        for (var i = 0; i < first.Graphics.Length; i++)
            if (first.Graphics[i].GraphicIndex != second.Graphics[i].GraphicIndex) return $"Graphic[{i}]";
        return "Unknown";
    }

    private sealed class MutableCompileAudit(short mapId, int shiftX, int shiftY)
    {
        public short MapId { get; } = mapId;
        public int ShiftX { get; } = shiftX;
        public int ShiftY { get; } = shiftY;
        public int WrittenTiles { get; set; }
        public int DuplicateTiles { get; set; }
        public int ConflictingTiles { get; set; }
        public int MinX { get; set; } = int.MaxValue;
        public int MaxX { get; set; } = int.MinValue;
        public int MinY { get; set; } = int.MaxValue;
        public int MaxY { get; set; } = int.MinValue;

        public StitchedCompileAuditEntry ToRecord() => new(MapId, ShiftX, ShiftY,
            WrittenTiles, DuplicateTiles, ConflictingTiles,
            MinX == int.MaxValue ? 0 : MinX, MaxX == int.MinValue ? 0 : MaxX,
            MinY == int.MaxValue ? 0 : MinY, MaxY == int.MinValue ? 0 : MaxY);
    }
}
