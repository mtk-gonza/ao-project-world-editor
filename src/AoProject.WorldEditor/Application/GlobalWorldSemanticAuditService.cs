using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed record GlobalSemanticAuditIssue(
    string Code,
    AuditSeverity Severity,
    short SectorId,
    int LocalX,
    int LocalY,
    int GlobalX,
    int GlobalY,
    string Message);

internal sealed record GlobalSemanticAuditResult(IReadOnlyList<GlobalSemanticAuditIssue> Issues)
{
    public int Errors => Issues.Count(issue => issue.Severity == AuditSeverity.Error);
    public int Warnings => Issues.Count(issue => issue.Severity == AuditSeverity.Warning);
    public int Infos => Issues.Count(issue => issue.Severity == AuditSeverity.Info);
}

/// <summary>
/// Audits the semantic contract of the assembled world, independently from the
/// binary container audit. Global coordinates are derived with the same 75x81
/// stride used by the resolved snapshot.
/// </summary>
internal sealed class GlobalWorldSemanticAuditService
{
    private const int MaxIssues = 10000;

    public GlobalSemanticAuditResult Audit(GlobalWorldDocument world, WorldEditorResourceCatalog? catalog = null,
        string? globalCsmPath = null, string? globalInfPath = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var issues = new List<GlobalSemanticAuditIssue>();
        var placements = world.Assembly.Placements.ToList();
        var byChunk = placements.GroupBy(item => item.Chunk).ToDictionary(group => group.Key, group => group.ToList());
        var minChunkX = placements.Count == 0 ? 0 : placements.Min(item => item.Chunk.X);
        var minChunkY = placements.Count == 0 ? 0 : placements.Min(item => item.Chunk.Y);
        var occupied = new Dictionary<(int X, int Y), (short SectorId, int LocalX, int LocalY)>();

        if (placements.Count == 0)
            Add(issues, "WORLD-EMPTY", AuditSeverity.Error, 0, 0, 0, 0, 0, "El ensamblado no contiene sectores.");
        if (placements.Select(item => item.SectorId).Distinct().Count() != placements.Count)
            Add(issues, "WORLD-DUPLICATE-MAP", AuditSeverity.Error, 0, 0, 0, 0, 0, "Hay mapId duplicados en los placements.");
        if (byChunk.Any(pair => pair.Value.Count > 1))
            Add(issues, "WORLD-DUPLICATE-CHUNK", AuditSeverity.Error, 0, 0, 0, 0, 0, "Hay más de un mapa en el mismo chunk global.");

        foreach (var placement in placements)
        {
            if (!world.Maps.TryGetValue(placement.SectorId, out var map))
            {
                Add(issues, "WORLD-MISSING-MAP", AuditSeverity.Error, placement.SectorId, 0, 0, 0, 0, "El placement no tiene mapa materializado.");
                continue;
            }
            if (!map.CsmSectorSize.IsValid)
                Add(issues, "WORLD-BOUNDS", AuditSeverity.Error, placement.SectorId, 0, 0, 0, 0, "CsmSectorSize inválido.");
            AuditMetadata(placement.SectorId, map, issues);

            var hasWest = byChunk.ContainsKey(new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
            var hasNorth = byChunk.ContainsKey(new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
            for (var localY = 1; localY <= WorldSector.Height; localY++)
            for (var localX = 1; localX <= WorldSector.Width; localX++)
            {
                if (!GlobalChunkGeometry.IsCoreTile(localX, localY, hasWest, hasNorth)) continue;
                var global = world.GlobalTileForPlacement(placement, localX, localY, minChunkX, minChunkY);
                var globalX = global.X;
                var globalY = global.Y;
                if (!occupied.TryAdd((globalX, globalY), (placement.SectorId, localX, localY)))
                    Add(issues, "WORLD-OVERLAP", AuditSeverity.Error, placement.SectorId, localX, localY, globalX, globalY, "Dos mapas ocupan la misma coordenada global.");
                AuditBlock(world, catalog, placement.SectorId, map, localX, localY, globalX, globalY, issues);
            }
        }

        foreach (var placement in placements)
            AuditExits(world, placement, minChunkX, minChunkY, occupied, issues);

        foreach (var layoutIssue in new GlobalWorldLayoutValidator().Validate(world.Assembly, world.Maps, world.RootMap))
            Add(issues, "WORLD-LAYOUT", AuditSeverity.Error, layoutIssue.SectorId, 0, 0, 0, 0, layoutIssue.Message);

        if (!string.IsNullOrWhiteSpace(globalCsmPath))
            AuditResolvedSnapshot(globalCsmPath, globalInfPath, world, placements, minChunkX, minChunkY, occupied, issues);

        return new GlobalSemanticAuditResult(issues);
    }

    private static void AuditResolvedSnapshot(string csmPath, string? infPath, GlobalWorldDocument world,
        IReadOnlyList<GlobalSectorPlacement> placements, int minChunkX, int minChunkY,
        IReadOnlyDictionary<(int X, int Y), (short SectorId, int LocalX, int LocalY)> occupied,
        List<GlobalSemanticAuditIssue> issues)
    {
        if (!GlobalCsmArchiveService.TryReadInfEntry(csmPath, "__global_resolved_world.bin", out var payload, infPath))
        {
            Add(issues, "SNAPSHOT-MISSING", AuditSeverity.Error, 0, 0, 0, 0, 0, "No se pudo leer __global_resolved_world.bin.");
            return;
        }
        GlobalResolvedWorldSnapshot.ResolvedSnapshot snapshot;
        try { snapshot = GlobalResolvedWorldSnapshot.Read(payload); }
        catch (Exception error)
        {
            Add(issues, "SNAPSHOT-READ", AuditSeverity.Error, 0, 0, 0, 0, 0, error.Message);
            return;
        }

        var expectedWidth = world.Assembly.Width * GlobalChunkGeometry.WidthTiles + WorldSector.Width;
        var expectedHeight = world.Assembly.Height * GlobalChunkGeometry.HeightTiles + WorldSector.Height;
        if (snapshot.Width != expectedWidth || snapshot.Height != expectedHeight)
            Add(issues, "SNAPSHOT-DIMENSIONS", AuditSeverity.Error, 0, 0, 0, 0, 0,
                $"Dimensiones {snapshot.Width}x{snapshot.Height} != esperado {expectedWidth}x{expectedHeight}.");

        var placementByMap = placements.ToDictionary(item => item.SectorId);
        var expected = new Dictionary<(int X, int Y), (short SectorId, int LocalX, int LocalY, SectorTile Block)>();
        foreach (var tile in occupied)
        {
            if (!world.Maps.TryGetValue(tile.Value.SectorId, out var map)) continue;
            expected[tile.Key] = (tile.Value.SectorId, tile.Value.LocalX, tile.Value.LocalY, map.Blocks[tile.Value.LocalX, tile.Value.LocalY]);
        }
        var actual = new Dictionary<(int X, int Y), GlobalResolvedWorldSnapshot.ResolvedTile>();
        foreach (var tile in snapshot.Tiles)
        {
            if (!actual.TryAdd((tile.X, tile.Y), tile))
                Add(issues, "SNAPSHOT-DUPLICATE", AuditSeverity.Error, 0, 0, 0, tile.X, tile.Y, "El snapshot contiene coordenadas duplicadas.");
            if (!expected.ContainsKey((tile.X, tile.Y)))
                Add(issues, "SNAPSHOT-UNEXPECTED-TILE", AuditSeverity.Error, 0, 0, 0, tile.X, tile.Y, "El snapshot contiene un tile fuera del ensamblado.");
        }
        if (snapshot.Tiles.Count != expected.Count)
            Add(issues, "SNAPSHOT-TILE-COUNT", AuditSeverity.Error, 0, 0, 0, 0, 0,
                $"Cantidad de tiles {snapshot.Tiles.Count} != esperada {expected.Count}.");
        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var tile))
            {
                Add(issues, "SNAPSHOT-MISSING-TILE", AuditSeverity.Error, pair.Value.SectorId, pair.Value.LocalX, pair.Value.LocalY,
                    pair.Key.X, pair.Key.Y, "Falta el tile en el snapshot resuelto.");
                continue;
            }
            CompareResolvedTile(world, pair.Key.X, pair.Key.Y, pair.Value, tile, placementByMap, minChunkX, minChunkY, issues);
        }
    }

    private static void CompareResolvedTile(GlobalWorldDocument world, int globalX, int globalY,
        (short SectorId, int LocalX, int LocalY, SectorTile Block) expected,
        GlobalResolvedWorldSnapshot.ResolvedTile actual,
        IReadOnlyDictionary<short, GlobalSectorPlacement> placements, int minChunkX, int minChunkY,
        List<GlobalSemanticAuditIssue> issues)
    {
        var block = expected.Block;
        void Mismatch(string field, object source, object output) => Add(issues, "SNAPSHOT-MISMATCH", AuditSeverity.Error,
            expected.SectorId, expected.LocalX, expected.LocalY, globalX, globalY, $"{field}: fuente={source}, snapshot={output}.");
        if (block.Blocked != actual.Blocked) Mismatch("Blocked", block.Blocked, actual.Blocked);
        if (block.Trigger != actual.Trigger) Mismatch("Trigger", block.Trigger, actual.Trigger);
        if (block.CharacterIndex != actual.CharacterIndex) Mismatch("CharacterIndex", block.CharacterIndex, actual.CharacterIndex);
        if (block.NpcIndex != actual.NpcIndex) Mismatch("NpcIndex", block.NpcIndex, actual.NpcIndex);
        if (block.ParticleGroup != actual.ParticleGroup) Mismatch("ParticleGroup", block.ParticleGroup, actual.ParticleGroup);
        if (block.ParticleIndex != actual.ParticleIndex) Mismatch("ParticleIndex", block.ParticleIndex, actual.ParticleIndex);
        if (block.Marked != actual.Marked) Mismatch("Marked", block.Marked, actual.Marked);
        if (block.Object.Index != actual.ObjectIndex) Mismatch("Object.Index", block.Object.Index, actual.ObjectIndex);
        if (block.Object.Amount != actual.ObjectAmount) Mismatch("Object.Amount", block.Object.Amount, actual.ObjectAmount);
        CompareGraphic("ObjectGraphic", block.ObjectGraphic, actual.ObjectGraphic, Mismatch);
        if (block.Light.Range != actual.LightRange) Mismatch("Light.Range", block.Light.Range, actual.LightRange);
        if (block.Light.Color != actual.LightColor) Mismatch("Light.Color", block.Light.Color, actual.LightColor);
        for (var value = 0; value < block.LightValues.Length; value++)
            if (block.LightValues[value] != actual.LightValues[value]) Mismatch($"LightValues[{value}]", block.LightValues[value], actual.LightValues[value]);
        for (var value = 0; value < block.Colors.Length; value++)
            if (block.Colors[value] != actual.Colors[value]) Mismatch($"Colors[{value}]", block.Colors[value], actual.Colors[value]);
        if (block.Red != actual.Red) Mismatch("Red", block.Red, actual.Red);
        if (block.Green != actual.Green) Mismatch("Green", block.Green, actual.Green);
        if (block.Blue != actual.Blue) Mismatch("Blue", block.Blue, actual.Blue);
        for (var layer = 0; layer < block.Graphics.Length; layer++)
        {
            if (block.Graphics[layer].GraphicIndex != actual.Graphics[layer]) Mismatch($"Graphics[{layer}]", block.Graphics[layer].GraphicIndex, actual.Graphics[layer]);
            CompareGraphic($"Graphics[{layer}]", block.Graphics[layer], actual.GraphicAnimations[layer], Mismatch, compareIndex: false);
        }
        var expectedExitX = -1; var expectedExitY = -1;
        if (block.TileExit.Map > 0 && placements.TryGetValue(block.TileExit.Map, out var target))
        {
            var targetGlobal = world.GlobalTileForPlacement(target, block.TileExit.X,
                block.TileExit.Y, minChunkX, minChunkY);
            expectedExitX = targetGlobal.X;
            expectedExitY = targetGlobal.Y;
        }
        if (expectedExitX != actual.ExitX || expectedExitY != actual.ExitY)
            Mismatch("ExitGlobal", $"{expectedExitX},{expectedExitY}", $"{actual.ExitX},{actual.ExitY}");
    }

    private static void CompareGraphic(string field, GraphicAnimation expected, GraphicAnimation actual,
        Action<string, object, object> mismatch, bool compareIndex = true)
    {
        if (compareIndex && expected.GraphicIndex != actual.GraphicIndex) mismatch($"{field}.GraphicIndex", expected.GraphicIndex, actual.GraphicIndex);
        if (expected.FrameCounter != actual.FrameCounter) mismatch($"{field}.FrameCounter", expected.FrameCounter, actual.FrameCounter);
        if (expected.Speed != actual.Speed) mismatch($"{field}.Speed", expected.Speed, actual.Speed);
        if (expected.Started != actual.Started) mismatch($"{field}.Started", expected.Started, actual.Started);
        if (expected.Loops != actual.Loops) mismatch($"{field}.Loops", expected.Loops, actual.Loops);
        if (expected.Angle != actual.Angle) mismatch($"{field}.Angle", expected.Angle, actual.Angle);
    }

    private static void AuditBlock(GlobalWorldDocument world, WorldEditorResourceCatalog? catalog, short mapId,
        WorldSector map, int x, int y, int globalX, int globalY, List<GlobalSemanticAuditIssue> issues)
    {
        var block = map.Blocks[x, y];
        if (!BlockedMask.IsValid(block.Blocked))
            Add(issues, "BLOCK-MASK", AuditSeverity.Error, mapId, x, y, globalX, globalY, $"Máscara inválida: {block.Blocked}.");
        if (block.Object.Index == 0 && block.Object.Amount != 0)
            Add(issues, "OBJECT-AMOUNT", AuditSeverity.Warning, mapId, x, y, globalX, globalY, "Cantidad de objeto sin objeto.");
        if (block.Object.Index < 0 || (block.Object.Index > 0 && catalog is not null && block.Object.Index >= catalog.Objects.Count))
            Add(issues, "OBJECT-INDEX", AuditSeverity.Error, mapId, x, y, globalX, globalY, $"Índice de objeto inválido: {block.Object.Index}.");
        if (catalog is not null && block.Object.Index > 0 && block.Object.Index < catalog.Objects.Count)
        {
            var expectedMask = catalog.Objects[block.Object.Index].DefaultBlockedMask;
            if (expectedMask != 0 && block.Blocked != expectedMask)
                Add(issues, "OBJECT-BLOCK-MISMATCH", AuditSeverity.Warning, mapId, x, y, globalX, globalY,
                    $"El objeto {block.Object.Index} requiere máscara {expectedMask}, pero la celda tiene {block.Blocked}.");
        }
        if (block.NpcIndex < 0 || (block.NpcIndex > 0 && catalog is not null && block.NpcIndex >= catalog.Npcs.Count))
            Add(issues, "NPC-INDEX", AuditSeverity.Error, mapId, x, y, globalX, globalY, $"Índice de NPC inválido: {block.NpcIndex}.");
        if (block.Trigger < 0 || (catalog is not null && catalog.Triggers.Count > 0 && !catalog.Triggers.Any(item => item.Index == block.Trigger)))
            Add(issues, "TRIGGER-INDEX", AuditSeverity.Warning, mapId, x, y, globalX, globalY, $"Trigger sin definición: {block.Trigger}.");
        if (block.Light.Range == 0 && block.Light.Color != 0)
            Add(issues, "LIGHT-STATE", AuditSeverity.Warning, mapId, x, y, globalX, globalY, "Color de luz sin rango activo.");
        if (block.ParticleIndex < 0 || (block.ParticleIndex > 0 && catalog is not null && block.ParticleIndex >= catalog.Particles.Count))
            Add(issues, "PARTICLE-INDEX", AuditSeverity.Warning, mapId, x, y, globalX, globalY, $"Partícula fuera de catálogo: {block.ParticleIndex}.");
        for (var layer = 0; layer < block.Graphics.Length; layer++)
        {
            var graphic = block.Graphics[layer].GraphicIndex;
            if (graphic < 0 || (catalog is not null && graphic >= catalog.Graphics.Count))
                Add(issues, "GRAPHIC-INDEX", AuditSeverity.Error, mapId, x, y, globalX, globalY, $"GRH inválido en capa {layer + 1}: {graphic}.");
        }
    }

    private static void AuditMetadata(short mapId, WorldSector map, List<GlobalSemanticAuditIssue> issues)
    {
        var info = map.Info;
        if (info.MusicNumberHigh < 0 || info.MusicNumberLow < 0)
            Add(issues, "META-MUSIC", AuditSeverity.Warning, mapId, 0, 0, 0, 0, "Los identificadores de música no pueden ser negativos.");
        if (info.Safety > 1)
            Add(issues, "META-SAFETY", AuditSeverity.Warning, mapId, 0, 0, 0, 0, $"Safety fuera de 0/1: {info.Safety}.");
        if (map.Environment.Rain > 1 || map.Environment.Snow > 1 || map.Environment.Fog > 1)
            Add(issues, "META-WEATHER", AuditSeverity.Warning, mapId, 0, 0, 0, 0,
                $"Clima fuera de 0/1: lluvia={map.Environment.Rain}, nieve={map.Environment.Snow}, niebla={map.Environment.Fog}.");
        if (!string.IsNullOrWhiteSpace(info.Restriction) &&
            !byte.TryParse(info.Restriction, out _) &&
            !info.Restriction.Equals("NEWBIE", StringComparison.OrdinalIgnoreCase))
            Add(issues, "META-RESTRICTION", AuditSeverity.Warning, mapId, 0, 0, 0, 0, $"Restricción no numérica: '{info.Restriction}'.");
        if (!string.IsNullOrWhiteSpace(info.Exit))
        {
            var fields = info.Exit.Split('-');
            if (fields.Length != 3 || !short.TryParse(fields[0], out _) || !short.TryParse(fields[1], out _) || !short.TryParse(fields[2], out _))
                Add(issues, "META-EXIT", AuditSeverity.Warning, mapId, 0, 0, 0, 0, $"Salida legacy mal formada: '{info.Exit}'.");
        }
        var minLevel = info.Level & 0xFF;
        var maxLevel = (info.Level >> 8) & 0xFF;
        if (maxLevel > 0 && minLevel > maxLevel)
            Add(issues, "META-LEVEL", AuditSeverity.Warning, mapId, 0, 0, 0, 0, $"Nivel mínimo {minLevel} mayor que máximo {maxLevel}.");
        if (map.Environment.BaseLight is < 0 or > 255)
            Add(issues, "META-LIGHT", AuditSeverity.Warning, mapId, 0, 0, 0, 0, $"Luz ambiental fuera de rango: {map.Environment.BaseLight}.");
    }

    private static void AuditExits(GlobalWorldDocument world, GlobalSectorPlacement placement, int minChunkX, int minChunkY,
        IReadOnlyDictionary<(int X, int Y), (short SectorId, int LocalX, int LocalY)> occupied, List<GlobalSemanticAuditIssue> issues)
    {
        if (!world.Maps.TryGetValue(placement.SectorId, out var map)) return;
        for (var y = 1; y <= WorldSector.Height; y++)
        for (var x = 1; x <= WorldSector.Width; x++)
        {
            var exit = map.Blocks[x, y].TileExit;
            var sourceGlobal = world.GlobalTileForPlacement(placement, x, y, minChunkX, minChunkY);
            var sourceGlobalX = sourceGlobal.X;
            var sourceGlobalY = sourceGlobal.Y;
            if (exit.Map == 0)
            {
                if (exit.X != 0 || exit.Y != 0)
                    Add(issues, "EXIT-DANGLING", AuditSeverity.Warning, placement.SectorId, x, y, sourceGlobalX, sourceGlobalY, "Salida sin mapa con coordenadas residuales.");
                continue;
            }
            if (exit.X is < 1 or > WorldSector.Width || exit.Y is < 1 or > WorldSector.Height)
            {
                Add(issues, "EXIT-LOCAL-RANGE", AuditSeverity.Error, placement.SectorId, x, y, sourceGlobalX, sourceGlobalY, $"Destino fuera de rango: {exit.Map},{exit.X},{exit.Y}.");
                continue;
            }
            if (!world.Assembly.Placements.FirstOrDefault(item => item.SectorId == exit.Map).Equals(default(GlobalSectorPlacement)))
            {
                var target = world.Assembly.Placements.First(item => item.SectorId == exit.Map);
                var targetGlobal = world.GlobalTileForPlacement(target, exit.X, exit.Y, minChunkX, minChunkY);
                var globalX = targetGlobal.X;
                var globalY = targetGlobal.Y;
                if (!occupied.ContainsKey((globalX, globalY)))
                    Add(issues, "EXIT-GLOBAL-TARGET", AuditSeverity.Error, placement.SectorId, x, y, sourceGlobalX, sourceGlobalY, $"La salida apunta a una coordenada global no ocupada: destino={globalX},{globalY}.");
            }
            else if (exit.Map > 0)
                Add(issues, "EXIT-MISSING-MAP", AuditSeverity.Error, placement.SectorId, x, y, sourceGlobalX, sourceGlobalY, $"La salida apunta al mapa inexistente {exit.Map}.");
        }
    }

    private static void Add(List<GlobalSemanticAuditIssue> issues, string code, AuditSeverity severity, short mapId,
        int localX, int localY, int globalX, int globalY, string message)
    {
        if (issues.Count < MaxIssues) issues.Add(new(code, severity, mapId, localX, localY, globalX, globalY, message));
    }
}
