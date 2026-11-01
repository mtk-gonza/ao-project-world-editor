using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed record UnifiedWorldCompileResult(
    UnifiedWorldMap Map,
    int WrittenTiles,
    int MergedTiles,
    int InternalExits,
    int CrossFloorExits,
    int ExternalExits,
    int Npcs,
    int Objects,
    int Particles,
    int Lights,
    int RoofZones,
    IReadOnlyList<UnifiedWorldConflict> Conflicts)
{
    public UnifiedWorldMap SectorId => Map;
}

/// <summary>
/// Materializes the legacy sector layout into one authoritative rectangular
/// world. This is an import/compile step; the resulting map has no sector
/// identity and does not require stitching while rendering or simulating.
/// </summary>
internal sealed class UnifiedWorldCompiler
{
    public UnifiedWorldBundle CompileFloors(GlobalWorldDocument world,
        WorldEditorResourceCatalog? resources = null,
        IReadOnlyDictionary<short, int>? floorAssignments = null,
        bool applyStitching = false,
        byte[]? settlements = null,
        byte[]? sector = null,
        byte[]? underworldPool = null,
        byte[]? phoenixPool = null,
        byte[]? events = null,
        byte[]? treasures = null,
        byte[]? quests = null,
        byte[]? npcs = null,
        byte[]? objects = null,
        byte[]? castles = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var bundle = new UnifiedWorldBundle();
        var stitchByChunk = applyStitching
            ? new GlobalStitchAuditService().Audit(world)
                .ToDictionary(item => new GlobalChunkCoordinate(item.ChunkX, item.ChunkY),
                    item => (item.ShiftX, item.ShiftY))
            : new Dictionary<GlobalChunkCoordinate, (int X, int Y)>();
        IReadOnlyList<(int FloorZ, GlobalWorldAssembly Assembly)> floorAssemblies;
        if (world.FloorAssemblies.Count > 0)
        {
            floorAssemblies = world.FloorAssemblies;
        }
        else if (floorAssignments is not null)
        {
            floorAssemblies = world.Assembly.Placements
                .GroupBy(item => floorAssignments.GetValueOrDefault(item.SectorId))
                .Select(group => (group.Key, new GlobalWorldAssembly
                {
                    Width = world.Assembly.Width,
                    Height = world.Assembly.Height,
                    WrapX = world.Assembly.WrapX,
                    Placements = group.ToArray()
                }))
                .ToArray();
        }
        else
        {
            floorAssemblies = [(0, world.Assembly)];
        }
        floorAssemblies = AddVirtualSpecialSourceMaps(world, floorAssemblies);
        var exitPlacements = floorAssemblies.SelectMany(item => item.Assembly.Placements)
            .GroupBy(item => item.SectorId).Select(group => group.First()).ToArray();
        var availableFloorZs = floorAssemblies.Select(item => item.FloorZ).ToHashSet();
        var floorByMap = exitPlacements.ToDictionary(item => item.SectorId,
            item => floorAssemblies.First(floor => floor.Assembly.Placements.Any(p => p.SectorId == item.SectorId)).FloorZ);
        foreach (var floorAssembly in floorAssemblies)
        {
            var sourceAssembly = floorAssembly.Assembly;
            var floorZ = floorAssembly.Item1;
            // A WORLDMAP may use the same filler map more than once. The source
            // catalog is keyed by SectorId, so keep one deterministic placement for
            // compilation while preserving the official layout dimensions.
            var placementsForFloor = sourceAssembly.Placements
                .GroupBy(item => item.SectorId)
                .Select(group => group.First())
                .ToArray();
            var assembly = new GlobalWorldAssembly
            {
                Width = sourceAssembly.Width,
                Height = sourceAssembly.Height,
                WrapX = sourceAssembly.WrapX,
                Placements = placementsForFloor
            };
            var floorWorld = new GlobalWorldDocument(assembly, world.Maps, world.Paths,
                placementsForFloor.First().SectorId);
            bundle.Add(Compile(floorWorld, resources, exitPlacements,
                placementsForFloor, floorZ, floorByMap, stitchByChunk, applyStitching,
                availableFloorZs).Map);
        }
        var allPlacements = floorAssemblies.SelectMany(item => item.Assembly.Placements)
            .GroupBy(item => item.SectorId).Select(group => group.First()).ToArray();
        bundle.SetPlacements(allPlacements.Select(placement =>
            applyStitching && stitchByChunk.TryGetValue(placement.Chunk, out var stitch)
                ? placement with { StitchX = stitch.Item1, StitchY = stitch.Item2 }
                : placement));
        bundle.SetSectorFloors(allPlacements.Select(item =>
            new KeyValuePair<short, int>(item.SectorId,
                floorAssemblies.First(floor => floor.Assembly.Placements.Any(p => p.SectorId == item.SectorId)).FloorZ)));
        foreach (var placement in allPlacements)
        {
            if (!world.Maps.TryGetValue(placement.SectorId, out var source)) continue;
            var metadata = new UnifiedWorldSectorMetadata
            {
                SectorId = placement.SectorId,
                FloorZ = floorAssemblies.First(floor => floor.Assembly.Placements.Any(p => p.SectorId == placement.SectorId)).FloorZ,
                MapSize = source.CsmSectorSize
            };
            CopyMapInfo(metadata.Info, source.Info);
            metadata.Environment.BaseLight = source.Environment.BaseLight;
            metadata.Environment.Rain = source.Environment.Rain;
            metadata.Environment.Snow = source.Environment.Snow;
            metadata.Environment.Fog = source.Environment.Fog;
            bundle.SectorMetadata.Add(metadata);
        }
        foreach (var source in world.SourcePaths.OrderBy(item => item.Key))
        {
            var infPath = Path.ChangeExtension(source.Value, ".inf");
            var datPath = Path.ChangeExtension(source.Value, ".dat");
            bundle.SectorSources.Add(new UnifiedWorldSectorSource(
                source.Key,
                Path.GetFileName(source.Value),
                File.Exists(source.Value) ? File.ReadAllBytes(source.Value) : [],
                File.Exists(infPath) ? File.ReadAllBytes(infPath) : [],
                File.Exists(datPath) ? File.ReadAllBytes(datPath) : []));
        }
        bundle.Settlements = settlements is { Length: > 0 }
            ? new GlobalSettlementManifestService().Transform(settlements, bundle)
            : [];
        bundle.Sector = sector ?? [];
        bundle.UnderworldPool = underworldPool is { Length: > 0 }
            ? new GlobalSpecialManifestService().TransformUnderworld(underworldPool, bundle) : [];
        bundle.PhoenixPool = phoenixPool is { Length: > 0 }
            ? new GlobalSpecialManifestService().TransformPhoenix(phoenixPool, bundle) : [];
        bundle.Events = events ?? [];
        bundle.Treasures = treasures ?? [];
        bundle.Quests = quests ?? [];
        bundle.Npcs = npcs ?? [];
        bundle.Objects = objects ?? [];
        bundle.Castles = castles ?? [];
        return bundle;
    }

    private static IReadOnlyList<(int FloorZ, GlobalWorldAssembly Assembly)> AddVirtualSpecialSourceMaps(
        GlobalWorldDocument world,
        IReadOnlyList<(int FloorZ, GlobalWorldAssembly Assembly)> floorAssemblies)
    {
        var official = floorAssemblies.SelectMany(item => item.Assembly.Placements)
            .Select(item => item.SectorId).ToHashSet();
        var sourceSectorIds = world.SourcePaths
            .Where(item => File.Exists(item.Value))
            .Select(item => item.Key)
            .ToHashSet();
        var pending = new Queue<short>(world.Maps.Keys
            .Where(id => sourceSectorIds.Contains(id))
            .Where(id => id > 0 && !official.Contains(id))
            .OrderBy(id => id));

        var reachable = new HashSet<short>();
        while (pending.Count > 0)
        {
            var sectorId = pending.Dequeue();
            if (official.Contains(sectorId) || !sourceSectorIds.Contains(sectorId) ||
                !world.Maps.ContainsKey(sectorId) || !reachable.Add(sectorId)) continue;
            EnqueuePositiveExits(world, sectorId, official, pending);
        }
        if (reachable.Count == 0) return floorAssemblies;

        // Keep every source map playable on its own virtual floor while avoiding
        // a fake column attached to WORLDMAP1. This includes disconnected maps:
        // the VB6 client can receive any valid map ID and then load its CSM on
        // demand. The ordering is stable by SectorId, so coordinates are
        // reproducible.
        const int columns = 19;
        var placements = reachable.OrderBy(item => item)
            .Select((sectorId, index) => new GlobalSectorPlacement(
                sectorId,
                new GlobalChunkCoordinate(index % columns, index / columns),
                "world--3"))
            .ToArray();
        var rows = (placements.Length + columns - 1) / columns;
        var assembly = new GlobalWorldAssembly
        {
            Width = columns,
            Height = rows,
            WrapX = false,
            Placements = placements
        };
        return floorAssemblies.Append((-3, assembly)).ToArray();
    }

    private static void EnqueuePositiveExits(
        GlobalWorldDocument world,
        short sectorId,
        IReadOnlySet<short> official,
        Queue<short> pending)
    {
        if (!world.Maps.TryGetValue(sectorId, out var map)) return;
        for (var y = 1; y <= WorldSector.Height; y++)
        for (var x = 1; x <= WorldSector.Width; x++)
        {
            var target = map.Blocks[x, y]?.TileExit.SectorId ?? 0;
            if (target > 0 && !official.Contains(target)) pending.Enqueue(target);
        }
    }

    private static bool ContainsVirtualSpecialExit(WorldSector map)
    {
        for (var y = 1; y <= WorldSector.Height; y++)
        for (var x = 1; x <= WorldSector.Width; x++)
        {
            var target = map.Blocks[x, y]?.TileExit.SectorId ?? 0;
            if (target is >= 3000 and <= 3004) return true;
        }
        return false;
    }

    private static void CopyMapInfo(SectorInfo target, SectorInfo source)
    {
        target.Music = source.Music; target.MusicNumberHigh = source.MusicNumberHigh;
        target.MusicNumberLow = source.MusicNumberLow; target.Name = source.Name;
        target.MapVersion = source.MapVersion; target.PlayerKilling = source.PlayerKilling;
        target.MagicDisabled = source.MagicDisabled; target.InvisibilityDisabled = source.InvisibilityDisabled;
        target.ResurrectionDisabled = source.ResurrectionDisabled; target.NoEncryptMap = source.NoEncryptMap;
        target.Terrain = source.Terrain; target.Zone = source.Zone; target.Restriction = source.Restriction;
        target.Backup = source.Backup; target.Safety = source.Safety; target.Ambient = source.Ambient;
        target.LetterGraphic = source.LetterGraphic; target.Level = source.Level; target.Extra2 = source.Extra2;
        target.Exit = source.Exit; target.Changed = source.Changed; target.Light = source.Light;
    }

    public UnifiedWorldCompileResult Compile(GlobalWorldDocument world,
        WorldEditorResourceCatalog? resources = null,
        IReadOnlyList<GlobalSectorPlacement>? exitPlacements = null,
        IReadOnlyList<GlobalSectorPlacement>? coordinatePlacements = null,
        int? forcedFloorZ = null,
        IReadOnlyDictionary<short, int>? floorAssignments = null,
        IReadOnlyDictionary<GlobalChunkCoordinate, (int X, int Y)>? stitchByChunk = null,
        bool applyStitching = false,
        IReadOnlySet<int>? availableFloorZs = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var placements = world.Assembly.Placements
            .OrderBy(item => item.Chunk.Y).ThenBy(item => item.Chunk.X).ThenBy(item => item.SectorId)
            .ToArray();
        if (placements.Length == 0) throw new InvalidDataException("El mundo no contiene mapas.");
        var occupiedChunks = placements.Select(item => item.Chunk).ToHashSet();
        var exitPlacementByMap = (exitPlacements ?? placements).ToDictionary(item => item.SectorId);

        var coordinateSource = coordinatePlacements is { Count: > 0 } ? coordinatePlacements : placements;
        var minChunkX = coordinateSource.Min(item => item.Chunk.X);
        var minChunkY = coordinateSource.Min(item => item.Chunk.Y);
        var maxChunkX = coordinateSource.Max(item => item.Chunk.X);
        var maxChunkY = coordinateSource.Max(item => item.Chunk.Y);
        var floorValues = placements.Select(item =>
            forcedFloorZ is { } explicitFloor
                ? explicitFloor
                : floorAssignments is not null && floorAssignments.TryGetValue(item.SectorId, out var z) ? z : 0)
            .Distinct().ToArray();
        if (floorValues.Length > 1)
            throw new InvalidDataException("La compilación de un .gmap debe contener un único piso Z.");
        var floorZ = forcedFloorZ ?? floorValues.SingleOrDefault();
        var maxStitchedX = applyStitching
            ? coordinateSource.Max(item =>
                (item.Chunk.X - minChunkX) * GlobalChunkGeometry.WidthTiles + WorldSector.Width -
                (stitchByChunk?.GetValueOrDefault(item.Chunk).Item1 ?? 0))
            : (maxChunkX - minChunkX + 1) * GlobalChunkGeometry.WidthTiles + WorldSector.Width;
        var maxStitchedY = applyStitching
            ? coordinateSource.Max(item =>
                (item.Chunk.Y - minChunkY) * GlobalChunkGeometry.HeightTiles + WorldSector.Height -
                (stitchByChunk?.GetValueOrDefault(item.Chunk).Item2 ?? 0))
            : (maxChunkY - minChunkY + 1) * GlobalChunkGeometry.HeightTiles + WorldSector.Height;
        if (applyStitching)
        {
            foreach (var sourceEntry in world.Maps)
            {
                if (!exitPlacementByMap.ContainsKey(sourceEntry.Key)) continue;
                var source = sourceEntry.Value;
                for (var exitY = 1; exitY <= WorldSector.Height; exitY++)
                for (var exitX = 1; exitX <= WorldSector.Width; exitX++)
                {
                    var block = source.Blocks[exitX, exitY];
                    if (block is null) continue;
                    var exit = block.TileExit;
                    if (exit.SectorId <= 0 || !exitPlacementByMap.TryGetValue(exit.SectorId, out var targetPlacement)) continue;
                    var target = RawGlobalTile(targetPlacement, exit.X, exit.Y, minChunkX, minChunkY,
                        stitchByChunk, applyStitching);
                    maxStitchedX = Math.Max(maxStitchedX, target.X + 1);
                    maxStitchedY = Math.Max(maxStitchedY, target.Y + 1);
                }
            }
            // Legacy exits may use the compatibility band beyond the 100x100
            // source extent. Keep that band addressable in the experimental
            // compact floor instead of truncating a valid destination.
            maxStitchedX = checked(maxStitchedX + GlobalChunkGeometry.WidthTiles);
            maxStitchedY = checked(maxStitchedY + GlobalChunkGeometry.HeightTiles);
        }
        var map = new UnifiedWorldMap(checked(maxStitchedX), checked(maxStitchedY), floorZ);
        var owners = new Dictionary<int, TileOwner>();
        var roofIds = new Dictionary<(short SectorId, short Trigger), int>();
        var nextRoofId = 1;
        var written = 0;
        var merged = 0;
        var internalExits = 0;
        var crossFloorExits = 0;
        var externalExits = 0;
        var npcs = 0;
        var objects = 0;
        var particles = 0;
        var lights = 0;

        foreach (var placement in placements)
        {
            if (!world.Maps.TryGetValue(placement.SectorId, out var source))
                throw new InvalidDataException($"El mapa {placement.SectorId} no pudo materializarse.");

            var hasWest = occupiedChunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
            var hasNorth = occupiedChunks.Contains(new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
            var environment = source.Info;
            var firstGlobal = RawGlobalTile(placement,
                hasWest ? GlobalChunkGeometry.CoreMinX + 1 : GlobalChunkGeometry.CoreMinX,
                hasNorth ? GlobalChunkGeometry.CoreMinY + 1 : GlobalChunkGeometry.CoreMinY,
                minChunkX, minChunkY, stitchByChunk, applyStitching);
            var lastGlobal = RawGlobalTile(placement,
                GlobalChunkGeometry.CoreMaxX, GlobalChunkGeometry.CoreMaxY, minChunkX, minChunkY,
                stitchByChunk, applyStitching);
            map.EnvironmentProfiles.Add(new UnifiedWorldEnvironmentProfile(
                firstGlobal.X, firstGlobal.Y, lastGlobal.X, lastGlobal.Y,
                source.Environment.BaseLight, source.Environment.Rain, source.Environment.Snow,
                source.Environment.Fog, environment.Music, environment.Zone,
                environment.Restriction, environment.Safety, floorZ, source.Info.Level));

            for (var localY = 1; localY <= WorldSector.Height; localY++)
            for (var localX = 1; localX <= WorldSector.Width; localX++)
            {
                if (!GlobalChunkGeometry.IsCoreTile(localX, localY, hasWest, hasNorth)) continue;
                var global = RawGlobalTile(placement, localX, localY, minChunkX, minChunkY,
                    stitchByChunk, applyStitching);
                var gx = global.X;
                var gy = global.Y;
                var offset = map.Offset(gx, gy);
                var sourceBlock = source.Blocks[localX, localY];
                if (owners.TryGetValue(offset, out var owner))
                {
                    var diagonalSeam = owner.Chunk.X != placement.Chunk.X && owner.Chunk.Y != placement.Chunk.Y;
                    if (diagonalSeam)
                    {
                        // The outer corner of two diagonal sectors maps to the
                        // same dense pixel, but is not a shared playable tile.
                        // Keep the first canonical corner and do not turn this
                        // geometric seam into a gameplay conflict.
                        merged++;
                        continue;
                    }
                    var duplicateExit = AppendExit(map, offset, gx, gy, localX, localY, sourceBlock, placement, floorZ,
                        world, exitPlacementByMap, minChunkX, minChunkY, floorAssignments,
                        stitchByChunk, applyStitching, availableFloorZs);
                    if (duplicateExit == UnifiedWorldExitKind.Internal) internalExits++;
                    if (duplicateExit == UnifiedWorldExitKind.CrossFloor) crossFloorExits++;
                    if (duplicateExit == UnifiedWorldExitKind.External) externalExits++;
                    if (applyStitching && IsSeamDuplicate(owner, placement, localX, localY))
                        MergeSeamTile(map, offset, sourceBlock, source.Environment);
                    if (TilesEquivalent(owner.Block, owner.Environment, sourceBlock, source.Environment) ||
                        (applyStitching && IsSeamDuplicate(owner, placement, localX, localY)))
                        merged++;
                    else
                        AddConflict(map, gx, gy, owner.SectorId, placement.SectorId,
                            DifferenceField(owner.Block, owner.Environment, sourceBlock, source.Environment),
                            owner.LocalX, owner.LocalY, localX, localY, placement.Chunk, floorZ);
                    continue;
                }
                owners[offset] = new TileOwner(placement.SectorId, placement.Chunk, localX, localY, sourceBlock, source.Environment);
                CopyTile(map, offset, sourceBlock, source.Environment);
                written++;

                if (sourceBlock.NpcIndex > 0) npcs++;
                if (sourceBlock.Object.Index > 0) objects++;
                if (sourceBlock.ParticleIndex > 0) particles++;
                if (sourceBlock.Light.Range > 0) lights++;

                if (resources is not null && sourceBlock.NpcIndex > 0 &&
                    sourceBlock.NpcIndex < resources.Npcs.Count)
                {
                    var npc = resources.Npcs[sourceBlock.NpcIndex];
                    map.Npcs.Add(new UnifiedWorldNpcPlacement(gx, gy, npc.Body, npc.Head,
                        npc.Heading, npc.Weapon, npc.Shield, npc.Helmet, floorZ));
                }
                if (sourceBlock.ParticleGroup > 0)
                {
                    if (map.ParticleGroups.All(item => item.Index != sourceBlock.ParticleGroup))
                        map.ParticleGroups.Add(new UnifiedWorldParticleGroup(sourceBlock.ParticleGroup,
                            gx, gy, sourceBlock.ParticleIndex, floorZ));
                }

                if (sourceBlock.Graphics[3].GraphicIndex > 0 && sourceBlock.Trigger > 0)
                {
                    var key = (placement.SectorId, sourceBlock.Trigger);
                    if (!roofIds.TryGetValue(key, out var roofId))
                    {
                        roofId = nextRoofId++;
                        roofIds[key] = roofId;
                        map.RoofZones.Add(new UnifiedWorldRoofZone(roofId, sourceBlock.Trigger,
                            gx, gy, gx, gy, floorZ));
                    }
                    map.RoofZoneId[offset] = roofId;
                    var zone = map.RoofZones.Single(item => item.Id == roofId);
                    map.RoofZones[map.RoofZones.IndexOf(zone)] = zone with
                    {
                        MinX = Math.Min(zone.MinX, gx), MinY = Math.Min(zone.MinY, gy),
                        MaxX = Math.Max(zone.MaxX, gx), MaxY = Math.Max(zone.MaxY, gy)
                    };
                }

                var exitKind = AppendExit(map, offset, gx, gy, localX, localY, sourceBlock, placement, floorZ,
                    world, exitPlacementByMap, minChunkX, minChunkY, floorAssignments,
                    stitchByChunk, applyStitching, availableFloorZs);
                if (exitKind == UnifiedWorldExitKind.Internal) internalExits++;
                if (exitKind == UnifiedWorldExitKind.CrossFloor) crossFloorExits++;
                if (exitKind == UnifiedWorldExitKind.External) externalExits++;
            }
        }

        return new UnifiedWorldCompileResult(map, written, merged, internalExits, crossFloorExits, externalExits,
            npcs, objects, particles, lights, map.RoofZones.Count, map.Conflicts);
    }

    private static void CopyTile(UnifiedWorldMap target, int offset, SectorTile source, MapEnvironment environment)
    {
        target.Blocked[offset] = source.Blocked;
        target.Trigger[offset] = source.Trigger;
        target.CharacterIndex[offset] = source.CharacterIndex;
        target.NpcIndex[offset] = source.NpcIndex;
        target.ParticleGroup[offset] = source.ParticleGroup;
        target.ParticleIndex[offset] = source.ParticleIndex;
        target.Marked[offset] = source.Marked;
        target.ObjectIndex[offset] = source.Object.Index;
        target.ObjectAmount[offset] = source.Object.Amount;
        target.ObjectGraphicIndex[offset] = source.ObjectGraphic.GraphicIndex;
        target.LightRange[offset] = source.Light.Range;
        target.LightColor[offset] = source.Light.Color;
        target.Red[offset] = source.Red;
        target.Green[offset] = source.Green;
        target.Blue[offset] = source.Blue;
        target.AmbientLight[offset] = environment.BaseLight;
        target.Rain[offset] = environment.Rain;
        target.Snow[offset] = environment.Snow;
        target.Fog[offset] = environment.Fog;
        target.Water[offset] = IsWater(source);
        target.Lava[offset] = IsLava(source.Graphics[0].GraphicIndex);
        for (var layer = 0; layer < 8; layer++)
            target.GraphicIndices[target.GraphicOffset(offset, layer)] = source.Graphics[layer].GraphicIndex;
        for (var corner = 0; corner < 4; corner++)
        {
            target.LightValues[target.CornerOffset(offset, corner)] = source.LightValues[corner];
            target.Colors[target.CornerOffset(offset, corner)] = source.Colors[corner];
        }
    }

    /// <summary>
    /// A stitched border is one playable tile represented by two CSM records.
    /// Do not discard metadata from the incoming record just because the first
    /// record already owns the graphics. Preserve missing values and combine
    /// directional blocking masks so either source cannot erase gameplay data.
    /// </summary>
    private static void MergeSeamTile(UnifiedWorldMap target, int offset,
        SectorTile source, MapEnvironment environment)
    {
        target.Blocked[offset] |= source.Blocked;
        if (target.Trigger[offset] == 0) target.Trigger[offset] = source.Trigger;
        if (target.CharacterIndex[offset] == 0) target.CharacterIndex[offset] = source.CharacterIndex;
        if (target.NpcIndex[offset] == 0) target.NpcIndex[offset] = source.NpcIndex;
        if (target.ParticleGroup[offset] == 0) target.ParticleGroup[offset] = source.ParticleGroup;
        if (target.ParticleIndex[offset] == 0) target.ParticleIndex[offset] = source.ParticleIndex;
        target.Marked[offset] |= source.Marked;
        if (target.ObjectIndex[offset] == 0) target.ObjectIndex[offset] = source.Object.Index;
        if (target.ObjectAmount[offset] == 0) target.ObjectAmount[offset] = source.Object.Amount;
        if (target.ObjectGraphicIndex[offset] == 0) target.ObjectGraphicIndex[offset] = source.ObjectGraphic.GraphicIndex;
        if (target.LightRange[offset] == 0) target.LightRange[offset] = source.Light.Range;
        if (target.LightColor[offset] == 0) target.LightColor[offset] = source.Light.Color;
        if (target.Red[offset] == 0) target.Red[offset] = source.Red;
        if (target.Green[offset] == 0) target.Green[offset] = source.Green;
        if (target.Blue[offset] == 0) target.Blue[offset] = source.Blue;
        if (target.AmbientLight[offset] == 0) target.AmbientLight[offset] = environment.BaseLight;
        if (target.Rain[offset] == 0) target.Rain[offset] = environment.Rain;
        if (target.Snow[offset] == 0) target.Snow[offset] = environment.Snow;
        if (target.Fog[offset] == 0) target.Fog[offset] = environment.Fog;
        target.Water[offset] |= IsWater(source);
        target.Lava[offset] |= IsLava(source.Graphics[0].GraphicIndex);
        for (var layer = 0; layer < 8; layer++)
        {
            var graphicOffset = target.GraphicOffset(offset, layer);
            if (target.GraphicIndices[graphicOffset] == 0)
                target.GraphicIndices[graphicOffset] = source.Graphics[layer].GraphicIndex;
        }
        for (var corner = 0; corner < 4; corner++)
        {
            var cornerOffset = target.CornerOffset(offset, corner);
            if (target.LightValues[cornerOffset] == 0) target.LightValues[cornerOffset] = source.LightValues[corner];
            if (target.Colors[cornerOffset] == 0) target.Colors[cornerOffset] = source.Colors[corner];
        }
    }

    private static (int X, int Y) RawGlobalTile(GlobalSectorPlacement placement, int localX, int localY,
        int minChunkX, int minChunkY,
        IReadOnlyDictionary<GlobalChunkCoordinate, (int X, int Y)>? stitchByChunk = null,
        bool applyStitching = false)
    {
        var stitch = applyStitching ? stitchByChunk?.GetValueOrDefault(placement.Chunk) ?? (0, 0) : (0, 0);
        return ((placement.Chunk.X - minChunkX) * GlobalChunkGeometry.WidthTiles + localX - 1 - stitch.Item1,
            (placement.Chunk.Y - minChunkY) * GlobalChunkGeometry.HeightTiles + localY - 1 - stitch.Item2);
    }

    private static UnifiedWorldExitKind? AppendExit(UnifiedWorldMap map, int offset, int gx, int gy,
        int sourceX, int sourceY, SectorTile sourceBlock, GlobalSectorPlacement sourcePlacement,
        int floorZ, GlobalWorldDocument world,
        IReadOnlyDictionary<short, GlobalSectorPlacement> placements, int minChunkX, int minChunkY,
        IReadOnlyDictionary<short, int>? floorAssignments,
        IReadOnlyDictionary<GlobalChunkCoordinate, (int X, int Y)>? stitchByChunk = null,
        bool applyStitching = false,
        IReadOnlySet<int>? availableFloorZs = null)
    {
        // A global tile has one canonical exit.  Stitched borders can expose
        // the same tile through more than one source sector; once the selected
        // exit has been assigned, later records must not append a different
        // representation for that tile (for example Internal after
        // CrossFloor).  ExitIndex is deliberately the authority here rather
        // than value equality, because the duplicate may differ in kind or
        // destination while still referring to the same global tile.
        if (map.ExitIndex[offset] != 0)
            return null;

        var exit = sourceBlock.TileExit;
        // VB6 uses negative map ids for special destinations (for example
        // underworld/return routes). Preserve them as external runtime exits;
        // they are not ordinary placements and must not be silently dropped.
        if (exit.SectorId == 0) return null;
        UnifiedWorldExit candidate;
        if (world.Maps.TryGetValue(exit.SectorId, out var targetMap) && placements.TryGetValue(exit.SectorId, out var targetPlacement))
        {
            var target = RawGlobalTile(targetPlacement, exit.X, exit.Y, minChunkX, minChunkY,
                stitchByChunk, applyStitching);
            var targetZ = floorAssignments is not null && floorAssignments.TryGetValue(exit.SectorId, out var assignedTargetZ)
                ? assignedTargetZ : floorZ;
            if (targetZ == floorZ && IsPhysicalSectorSeam(sourcePlacement, targetPlacement,
                    sourceX, sourceY, exit))
                return null;
            candidate = new UnifiedWorldExit(targetZ == floorZ ? UnifiedWorldExitKind.Internal : UnifiedWorldExitKind.CrossFloor,
                gx, gy, target.X, target.Y, SourceZ: floorZ, TargetZ: targetZ);
        }
        else
        {
            // The original data has ten exits from map 266 to 3000..3004.
            // Those IDs are not maps: the VB6 server rejects them because they
            // are above NumMaps. Their coordinates are valid, however, so the
            // unified world promotes them to virtual destinations on Z=-1 when
            // that floor exists. No CSM is invented and the legacy coordinates
            // are converted from 1-based to the runtime's 0-based contract.
            if (IsVirtualSpecialFloorExit(exit) && availableFloorZs?.Contains(-1) == true && floorZ != -1)
            {
                candidate = new UnifiedWorldExit(UnifiedWorldExitKind.CrossFloor, gx, gy,
                    exit.X - 1, exit.Y - 1, SourceZ: floorZ, TargetZ: -1);
            }
            else
            {
                candidate = new UnifiedWorldExit(UnifiedWorldExitKind.External, gx, gy, -1, -1,
                    exit.SectorId, exit.X, exit.Y, SourceZ: floorZ,
                    ExternalZ: IsSpecialExternalMap(exit.SectorId) ? -1 : floorZ);
            }
        }
        if (!map.Exits.Contains(candidate)) map.Exits.Add(candidate);
        var reference = map.Exits.IndexOf(candidate) + 1;
        if (map.ExitIndex[offset] == 0) map.ExitIndex[offset] = reference;
        return candidate.Kind;
    }

    private static bool IsSeamDuplicate(TileOwner owner, GlobalSectorPlacement placement, int localX, int localY) =>
        (owner.LocalX == GlobalChunkGeometry.CoreMaxX && localX == GlobalChunkGeometry.CoreMinX + 1) ||
        (owner.LocalY == GlobalChunkGeometry.CoreMaxY && localY == GlobalChunkGeometry.CoreMinY + 1);

    private static bool IsSpecialExternalMap(short mapId) => mapId < 0 || mapId >= 3000;

    private static bool IsVirtualSpecialFloorExit(WorldPosition exit) =>
        exit.SectorId is >= 3000 and <= 3004 &&
        exit.X is >= 1 and <= WorldSector.Width &&
        exit.Y is >= 1 and <= WorldSector.Height;

    private static bool IsWater(SectorTile block) =>
        (IsWaterGraphic(block.Graphics[0].GraphicIndex) && block.Graphics[1].GraphicIndex == 0) ||
        block.Trigger is 8 or 11 or 16 or 18;

    private static bool IsWaterGraphic(int graphic) =>
        graphic is >= 1505 and <= 1520 or
        >= 124 and <= 139 or
        >= 24223 and <= 24238 or
        >= 24303 and <= 24318 or
        >= 468 and <= 483 or
        >= 44668 and <= 44683 or
        >= 24143 and <= 24158 or
        >= 12628 and <= 12643 or
        >= 2948 and <= 2963;

    private static bool IsLava(int graphic) =>
        graphic is >= 16101 and <= 16116 or >= 26767 and <= 26782;

    private static bool IsPhysicalSectorSeam(GlobalSectorPlacement source, GlobalSectorPlacement target,
        int sourceX, int sourceY, WorldPosition exit)
    {
        var dx = target.Chunk.X - source.Chunk.X;
        var dy = target.Chunk.Y - source.Chunk.Y;
        var horizontalWest = (dx == -1 || Math.Abs(dx) == 18) && dy == 0 &&
            sourceX == GlobalChunkGeometry.CoreMinX && exit.X == 87;
        var horizontalEast = (dx == 1 || Math.Abs(dx) == 18) && dy == 0 &&
            sourceX == GlobalChunkGeometry.CoreMaxX && exit.X == 14;
        return horizontalEast || horizontalWest ||
               (dx == 0 && dy == 1 && sourceY == GlobalChunkGeometry.CoreMaxY && exit.Y == 11) ||
               (dx == 0 && dy == -1 && sourceY == GlobalChunkGeometry.CoreMinY && exit.Y == 90);
    }

    private static void AddConflict(UnifiedWorldMap target, int x, int y, short firstMap, short secondMap,
        string field, int firstLocalX, int firstLocalY, int secondLocalX, int secondLocalY,
        GlobalChunkCoordinate secondChunk, int z)
    {
        target.Conflicts.Add(new UnifiedWorldConflict(x, y, firstMap, secondMap,
            field, $"Solapamiento Z={z}: mapa {firstMap} local ({firstLocalX},{firstLocalY}) " +
            $"vs mapa {secondMap} local ({secondLocalX},{secondLocalY}) en chunk " +
            $"({secondChunk.X},{secondChunk.Y}); los datos persistentes difieren."));
    }

    private static string DifferenceField(SectorTile first, MapEnvironment firstEnvironment,
        SectorTile second, MapEnvironment secondEnvironment)
    {
        if (first.Blocked != second.Blocked) return "Blocked";
        if (first.Trigger != second.Trigger) return "Trigger";
        if (first.CharacterIndex != second.CharacterIndex) return "CharacterIndex";
        if (first.NpcIndex != second.NpcIndex) return "NpcIndex";
        if (first.ParticleGroup != second.ParticleGroup || first.ParticleIndex != second.ParticleIndex) return "Particles";
        if (first.Object.Index != second.Object.Index || first.Object.Amount != second.Object.Amount ||
            first.ObjectGraphic.GraphicIndex != second.ObjectGraphic.GraphicIndex) return "Object";
        if (first.Light.Range != second.Light.Range || first.Light.Color != second.Light.Color) return "Light";
        if (first.Red != second.Red || first.Green != second.Green || first.Blue != second.Blue) return "Color";
        for (var layer = 0; layer < first.Graphics.Length; layer++)
            if (!AnimationsEqual(first.Graphics[layer], second.Graphics[layer])) return $"Graphic[{layer}]";
        for (var corner = 0; corner < 4; corner++)
            if (first.LightValues[corner] != second.LightValues[corner] || first.Colors[corner] != second.Colors[corner]) return $"Corner[{corner}]";
        if (firstEnvironment.BaseLight != secondEnvironment.BaseLight || firstEnvironment.Rain != secondEnvironment.Rain ||
            firstEnvironment.Snow != secondEnvironment.Snow || firstEnvironment.Fog != secondEnvironment.Fog) return "Environment";
        return "Unknown";
    }

    private static bool TilesEquivalent(SectorTile first, MapEnvironment firstEnvironment,
        SectorTile second, MapEnvironment secondEnvironment)
    {
        if (first.Blocked != second.Blocked || first.Trigger != second.Trigger ||
            first.CharacterIndex != second.CharacterIndex || first.NpcIndex != second.NpcIndex ||
            first.ParticleGroup != second.ParticleGroup || first.ParticleIndex != second.ParticleIndex ||
            first.Marked != second.Marked || first.Object.Index != second.Object.Index ||
            first.Object.Amount != second.Object.Amount || first.ObjectGraphic.GraphicIndex != second.ObjectGraphic.GraphicIndex ||
            first.Light.Range != second.Light.Range || first.Light.Color != second.Light.Color ||
            first.Red != second.Red || first.Green != second.Green || first.Blue != second.Blue ||
            firstEnvironment.BaseLight != secondEnvironment.BaseLight ||
            firstEnvironment.Rain != secondEnvironment.Rain || firstEnvironment.Snow != secondEnvironment.Snow ||
            firstEnvironment.Fog != secondEnvironment.Fog)
            return false;
        if (!AnimationsEqual(first.ObjectGraphic, second.ObjectGraphic)) return false;
        for (var layer = 0; layer < first.Graphics.Length; layer++)
            if (!AnimationsEqual(first.Graphics[layer], second.Graphics[layer])) return false;
        for (var corner = 0; corner < 4; corner++)
            if (first.LightValues[corner] != second.LightValues[corner] || first.Colors[corner] != second.Colors[corner]) return false;
        return true;
    }

    private static bool AnimationsEqual(GraphicAnimation first, GraphicAnimation second) =>
        first.GraphicIndex == second.GraphicIndex && first.FrameCounter == second.FrameCounter &&
        first.Speed == second.Speed && first.Started == second.Started && first.Loops == second.Loops &&
        first.Angle == second.Angle;

    private sealed record TileOwner(short SectorId, GlobalChunkCoordinate Chunk, int LocalX, int LocalY,
        SectorTile Block, MapEnvironment Environment);
}
