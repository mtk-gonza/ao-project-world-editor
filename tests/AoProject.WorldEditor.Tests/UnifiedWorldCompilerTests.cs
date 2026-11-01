using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using System.Drawing;
using System.Text;
using Xunit;
using System.Text.Json;

namespace AoProject.WorldEditor.Tests;

public sealed class UnifiedWorldCompilerTests
{
    [Fact]
    public void Settlement_manifest_is_converted_to_global_floor_coordinates()
    {
        var bundle = new UnifiedWorldBundle();
        var floor = new UnifiedWorldMap(75, 81, -1);
        bundle.Add(floor);
        bundle.SetPlacements([new GlobalSectorPlacement(7, new GlobalChunkCoordinate(4, 2), "world", 3, 5)]);
        bundle.SetSectorFloors([new KeyValuePair<short, int>(7, -1)]);

        var source = "[{\"homeId\":1,\"arrival\":{\"sectorId\":7,\"x\":14,\"y\":16},\"travel\":{\"sectorId\":7,\"x\":14,\"y\":16},\"resurrection\":{\"sectorId\":7,\"x\":14,\"y\":16}}]"u8.ToArray();
        var result = new GlobalSettlementManifestService().Transform(source, bundle);
        using var json = JsonDocument.Parse(result);
        var position = json.RootElement[0].GetProperty("arrival");

        Assert.Equal(-1, position.GetProperty("floorZ").GetInt32());
        Assert.Equal(10, position.GetProperty("globalX").GetInt32());
        Assert.Equal(10, position.GetProperty("globalY").GetInt32());
        Assert.False(position.TryGetProperty("sectorId", out _));
    }
    [Fact]
    public void Compiles_one_rectangular_map_and_preserves_entities()
    {
        var first = new WorldSector();
        first.Blocks[20, 20].Graphics[0].GraphicIndex = 100;
        first.Blocks[20, 20].Blocked = BlockedMask.AllSides;
        first.Blocks[20, 20].NpcIndex = 3;
        first.Blocks[20, 20].Object.Index = 4;
        first.Blocks[20, 20].ParticleIndex = 5;
        first.Blocks[20, 20].Light.Range = 4;
        first.Blocks[20, 20].Trigger = 60;
        first.Blocks[20, 20].Graphics[3].GraphicIndex = 200;
        var second = new WorldSector();
        second.Blocks[20, 20].Graphics[0].GraphicIndex = 300;

        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 2, Height = 1,
                Placements =
                [
                    new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world"),
                    new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "world")
                ]
            },
            new Dictionary<short, WorldSector> { [1] = first, [2] = second },
            new Dictionary<short, string> { [1] = "mapa1.csm", [2] = "mapa2.csm" }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);
        var offset = result.SectorId.Offset(19, 19);

        Assert.Equal(250, result.SectorId.Width);
        Assert.Equal(181, result.SectorId.Height);
        Assert.Equal(100, result.SectorId.GraphicIndices[result.SectorId.GraphicOffset(offset, 0)]);
        Assert.Equal((byte)15, result.SectorId.Blocked[offset]);
        Assert.Equal((short)3, result.SectorId.NpcIndex[offset]);
        Assert.Equal((short)4, result.SectorId.ObjectIndex[offset]);
        Assert.Equal(5, result.SectorId.ParticleIndex[offset]);
        Assert.Equal((short)4, result.SectorId.LightRange[offset]);
        Assert.Equal(1, result.Npcs);
        Assert.Equal(1, result.Objects);
        Assert.Equal(1, result.Particles);
        Assert.Equal(1, result.Lights);
        Assert.Equal(1, result.RoofZones);
        Assert.True(result.MergedTiles >= 0);
    }

    [Fact]
    public void Keeps_an_exit_to_a_map_outside_the_world_as_external()
    {
        var map = new WorldSector();
        map.Blocks[20, 20].TileExit.SectorId = 999;
        map.Blocks[20, 20].TileExit.X = 40;
        map.Blocks[20, 20].TileExit.Y = 50;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly { Width = 1, Height = 1, Placements = [new(1, new(0, 0), "world")] },
            new Dictionary<short, WorldSector> { [1] = map },
            new Dictionary<short, string> { [1] = "mapa1.csm" }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);

        var exit = Assert.Single(result.SectorId.Exits);
        Assert.Equal(UnifiedWorldExitKind.External, exit.Kind);
        Assert.Equal((short)999, exit.ExternalSectorId);
        Assert.Equal((short)40, exit.ExternalX);
        Assert.Equal((short)50, exit.ExternalY);
        Assert.Equal(1, result.SectorId.ExitIndex[result.SectorId.Offset(19, 19)]);
    }

    [Fact]
    public void Keeps_only_the_first_canonical_exit_for_a_merged_tile()
    {
        var first = new WorldSector();
        first.Blocks[20, 20].TileExit.SectorId = 999;
        first.Blocks[20, 20].TileExit.X = 40;
        first.Blocks[20, 20].TileExit.Y = 50;

        var duplicate = new WorldSector();
        duplicate.Blocks[20, 20].TileExit.SectorId = 3;
        duplicate.Blocks[20, 20].TileExit.X = 20;
        duplicate.Blocks[20, 20].TileExit.Y = 20;

        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 1, Height = 1,
                Placements =
                [
                    new(1, new(0, 0), "first"),
                    new(2, new(0, 0), "duplicate"),
                    new(3, new(0, 0), "target")
                ]
            },
            new Dictionary<short, WorldSector>
            {
                [1] = first, [2] = duplicate, [3] = new()
            },
            new Dictionary<short, string>
            {
                [1] = "mapa1.csm", [2] = "mapa2.csm", [3] = "mapa3.csm"
            }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);

        var exit = Assert.Single(result.SectorId.Exits);
        Assert.Equal(UnifiedWorldExitKind.External, exit.Kind);
        Assert.Equal((short)999, exit.ExternalSectorId);
        var offset = result.SectorId.Offset(19, 19);
        Assert.Equal(1, result.SectorId.ExitIndex[offset]);
    }

    [Fact]
    public void Removes_legacy_sector_exit_when_maps_are_physically_adjacent()
    {
        var west = new WorldSector();
        west.Blocks[88, 20].TileExit.SectorId = 2;
        west.Blocks[88, 20].TileExit.X = 14;
        west.Blocks[88, 20].TileExit.Y = 20;
        var east = new WorldSector();
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 2, Height = 1,
                Placements = [new(1, new(0, 0), "world"), new(2, new(1, 0), "world")]
            },
            new Dictionary<short, WorldSector> { [1] = west, [2] = east },
            new Dictionary<short, string> { [1] = "mapa1.csm", [2] = "mapa2.csm" }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);

        Assert.Empty(result.SectorId.Exits);
        Assert.Equal(0, result.SectorId.ExitIndex[result.SectorId.Offset(87, 19)]);
    }

    [Fact]
    public void Stitched_border_preserves_blocking_from_both_source_maps()
    {
        var west = new WorldSector();
        var east = new WorldSector();
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            west.Blocks[88, y].Graphics[0].GraphicIndex = 10;
            east.Blocks[14, y].Graphics[0].GraphicIndex = 10;
        }
        east.Blocks[14, 20].Blocked = BlockedMask.AllSides;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 2, Height = 1,
                Placements = [new(1, new(0, 0), "world"), new(2, new(1, 0), "world")]
            },
            new Dictionary<short, WorldSector> { [1] = west, [2] = east },
            new Dictionary<short, string> { [1] = "mapa1.csm", [2] = "mapa2.csm" }, 1);

        var result = new UnifiedWorldCompiler().CompileFloors(world, applyStitching: true);

        Assert.Contains((byte)BlockedMask.AllSides, result.Floors[0].Blocked);
    }

    [Fact]
    public void Keeps_negative_special_exit_on_underground_external_floor()
    {
        var map = new WorldSector();
        map.Blocks[20, 20].TileExit.SectorId = -1;
        map.Blocks[20, 20].TileExit.X = 36;
        map.Blocks[20, 20].TileExit.Y = 76;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly { Width = 1, Height = 1, Placements = [new(1, new(0, 0), "world")] },
            new Dictionary<short, WorldSector> { [1] = map },
            new Dictionary<short, string> { [1] = "mapa1.csm" }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);

        var exit = Assert.Single(result.SectorId.Exits);
        Assert.Equal(UnifiedWorldExitKind.External, exit.Kind);
        Assert.Equal((short)-1, exit.ExternalSectorId);
        Assert.Equal(-1, exit.ExternalZ);
        Assert.Equal(0, exit.SourceZ);
    }

    [Fact]
    public void Does_not_promote_missing_3000_exit_into_surface_geometry()
    {
        var source = new WorldSector();
        source.Blocks[20, 20].TileExit.SectorId = 3000;
        source.Blocks[20, 20].TileExit.X = 36;
        source.Blocks[20, 20].TileExit.Y = 76;
        var lower = new WorldSector();
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Placements = [new(1, new(0, 0), "surface"), new(266, new(0, 0), "special"), new(2, new(0, 0), "lower")]
            },
            new Dictionary<short, WorldSector> { [1] = new(), [266] = source, [2] = lower },
            new Dictionary<short, string> { [1] = "mapa1.csm", [266] = "mapa266.csm", [2] = "mapa2.csm" }, 1);
        world.FloorAssemblies =
        [
            (0, new GlobalWorldAssembly { Placements = [new(1, new(0, 0), "surface")] }),
            (-1, new GlobalWorldAssembly { Placements = [new(2, new(0, 0), "lower")] })
        ];

        var bundle = new UnifiedWorldCompiler().CompileFloors(world);

        Assert.Empty(bundle.Floors[0].Exits);
        Assert.DoesNotContain(bundle.Placements.Keys, id => id == 266);
        Assert.Empty(new UnifiedWorldBundleAuditService().Audit(bundle).Issues);
    }

    [Fact]
    public void Global_map_file_roundtrips_the_compiled_world()
    {
        var map = new UnifiedWorldMap(3, 2, -1);
        var offset = map.Offset(1, 1);
        map.GraphicIndices[map.GraphicOffset(offset, 3)] = 700;
        map.Blocked[offset] = BlockedMask.North;
            map.NpcIndex[offset] = 9;
            map.Water[offset] = true;
            map.Lava[offset] = true;
        map.Exits.Add(new UnifiedWorldExit(UnifiedWorldExitKind.External, 1, 1, -1, -1, 800, 10, 11, -1, 0, -1));
        map.RoofZones.Add(new UnifiedWorldRoofZone(1, 60, 0, 0, 2, 1));
        map.Npcs.Add(new UnifiedWorldNpcPlacement(1, 1, 2, 3, 1, 4, 5, 6));
        map.ParticleGroups.Add(new UnifiedWorldParticleGroup(7, 1, 1, 8));
        map.EnvironmentProfiles.Add(new UnifiedWorldEnvironmentProfile(0, 0, 2, 1, 0x112233, 1, 0, 1, "music", "zone", "restriction", 1, -1, 6400));
        var path = Path.Combine(Path.GetTempPath(), "aogmap-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var service = new UnifiedWorldFileService();
            service.Save(map, path);
            var loaded = service.Load(path);
            var loadedOffset = loaded.Offset(1, 1);
            Assert.Equal(700, loaded.GraphicIndices[loaded.GraphicOffset(loadedOffset, 3)]);
            Assert.Equal(-1, loaded.FloorZ);
            Assert.Equal((byte)BlockedMask.North, loaded.Blocked[loadedOffset]);
            Assert.Equal((short)9, loaded.NpcIndex[loadedOffset]);
            Assert.True(loaded.Water[loadedOffset]);
            Assert.True(loaded.Lava[loadedOffset]);
            Assert.Equal((short)800, Assert.Single(loaded.Exits).ExternalSectorId);
            Assert.Equal("music", Assert.Single(loaded.EnvironmentProfiles).Music);
            Assert.Equal(6400, Assert.Single(loaded.EnvironmentProfiles).LegacyLevel);
            Assert.Equal((short)2, Assert.Single(loaded.Npcs).Body);
            Assert.Equal(8, Assert.Single(loaded.ParticleGroups).Type);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Overlapping_border_data_is_merged_only_when_every_field_matches()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        first.Blocks[20, 20].Graphics[0].GraphicIndex = 10;
        second.Blocks[20, 20].Graphics[0].GraphicIndex = 10;
        second.Blocks[20, 20].Light.Range = 3;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 1, Height = 1,
                Placements = [new(1, new(0, 0), "world"), new(2, new(0, 0), "world")]
            },
            new Dictionary<short, WorldSector> { [1] = first, [2] = second },
            new Dictionary<short, string> { [1] = "mapa1.csm", [2] = "mapa2.csm" }, 1);

        var result = new UnifiedWorldCompiler().Compile(world);

        Assert.NotEmpty(result.Conflicts);
        Assert.True(result.MergedTiles > 0);
        Assert.Contains(result.Conflicts, item => item.Field == "Light");
    }

    [Fact]
    public void Unified_world_audit_rejects_an_invalid_exit_reference()
    {
        var map = new UnifiedWorldMap(2, 2);
        map.ExitIndex[map.Offset(1, 1)] = 7;

        var result = new UnifiedWorldAuditService().Audit(map);

        Assert.Contains(result.Issues, issue => issue.Code == "EXIT-REFERENCE");
    }

    [Fact]
    public void Unified_radar_is_generated_on_demand_in_global_coordinates()
    {
        var map = new UnifiedWorldMap(500, 400);
        var center = map.Offset(250, 200);
        map.Colors[map.CornerOffset(center, 0)] = 0x102030;
        map.Colors[map.CornerOffset(center, 1)] = 0x102030;
        map.Colors[map.CornerOffset(center, 2)] = 0x102030;
        map.Colors[map.CornerOffset(center, 3)] = 0x102030;
        map.NpcIndex[center] = 1;

        var (image, markerX, markerY) = new UnifiedWorldRadarService().CreateCentered(map, 250, 200, 200, 200);
        try
        {
            Assert.Equal(200, image.Width);
            Assert.Equal(200, image.Height);
            Assert.Equal(100, markerX);
            Assert.Equal(100, markerY);
            Assert.Equal(Color.Yellow.ToArgb(), image.GetPixel(markerX, markerY).ToArgb());
        }
        finally { image.Dispose(); }
    }

    [Fact]
    public void Unified_world_bundle_roundtrips_multiple_floors()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(2, 2, -1));
        bundle.Add(new UnifiedWorldMap(2, 2, 0));
        bundle.Select(-1);
        var directory = Path.Combine(Path.GetTempPath(), "aogbundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UnifiedWorldBundleFileService();
            service.Save(bundle, directory);
            var loaded = service.Load(directory);
            Assert.Equal(-1, loaded.ActiveFloorZ);
            Assert.Equal(new[] { -1, 0 }, loaded.Floors.Keys);
            Assert.Equal(0, loaded.Select(0).FloorZ);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Unified_world_bundle_roundtrips_stitch_offsets()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(2, 2, 0));
        bundle.SetPlacements([
            new GlobalSectorPlacement(42, new GlobalChunkCoordinate(3, 4), "world", 7, 9)
        ]);
        bundle.SetSectorFloors([new KeyValuePair<short, int>(42, 0)]);
        var directory = Path.Combine(Path.GetTempPath(), "aogbundle-stitch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UnifiedWorldBundleFileService();
            service.Save(bundle, directory);
            var loaded = service.Load(directory);
            var placement = Assert.Single(loaded.Placements.Values);
            Assert.Equal(7, placement.StitchX);
            Assert.Equal(9, placement.StitchY);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Unified_world_bundle_roundtrips_complete_source_metadata()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(2, 2, 0));
        var metadata = new UnifiedWorldSectorMetadata { SectorId = 842, FloorZ = 0,
            MapSize = new CsmSectorSize(88, 13, 91, 10) };
        metadata.Info.Name = "Mapa de prueba";
        metadata.Info.MapVersion = 7;
        metadata.Info.PlayerKilling = true;
        metadata.Info.MagicDisabled = 1;
        metadata.Info.Terrain = "bosque";
        metadata.Info.LetterGraphic = 1234;
        metadata.Info.Extra2 = 5678;
        metadata.Info.Exit = "salida";
        metadata.Environment.BaseLight = 0x112233;
        metadata.Environment.Rain = 1;
        bundle.SectorMetadata.Add(metadata);
        var directory = Path.Combine(Path.GetTempPath(), "aogbundle-meta-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UnifiedWorldBundleFileService();
            service.Save(bundle, directory);
            var loaded = service.Load(directory);
            var actual = Assert.Single(loaded.SectorMetadata);
            Assert.Equal((short)842, actual.SectorId);
            Assert.Equal("Mapa de prueba", actual.Info.Name);
            Assert.Equal((short)7, actual.Info.MapVersion);
            Assert.True(actual.Info.PlayerKilling);
            Assert.Equal((byte)1, actual.Info.MagicDisabled);
            Assert.Equal(1234, actual.Info.LetterGraphic);
            Assert.Equal(0x112233, actual.Environment.BaseLight);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Unified_world_bundle_roundtrips_embedded_sector_source()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(2, 2, 0));
        bundle.SectorSources.Add(new UnifiedWorldSectorSource(17, "sector.csm",
            [1, 2, 3], [4, 5], [6]));
        var directory = Path.Combine(Path.GetTempPath(), "aogbundle-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UnifiedWorldBundleFileService();
            service.Save(bundle, directory);
            var source = Assert.Single(service.Load(directory).SectorSources);
            Assert.Equal((short)17, source.SectorId);
            Assert.Equal("sector.csm", source.SourceName);
            Assert.Equal(new byte[] { 1, 2, 3 }, source.Csm);
            Assert.Equal(new byte[] { 4, 5 }, source.Inf);
            Assert.Equal(new byte[] { 6 }, source.Dat);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Unified_world_bundle_roundtrips_embedded_settlements()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(2, 2, 0));
        bundle.Settlements = "[{\"homeId\":1}]"u8.ToArray();
        bundle.Sector = "[ForceUpdateAi]\n1=1"u8.ToArray();
        bundle.UnderworldPool = "[INIT]\nUnderworldMapPool=1"u8.ToArray();
        bundle.PhoenixPool = "[INIT]\nMaxPhoenixMaps=1"u8.ToArray();
        var directory = Path.Combine(Path.GetTempPath(), "aogbundle-settlements-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UnifiedWorldBundleFileService();
            service.Save(bundle, directory);
            var loaded = service.Load(directory);
            Assert.Equal("[{\"homeId\":1}]", Encoding.UTF8.GetString(loaded.Settlements));
            Assert.Equal(bundle.Sector, loaded.Sector);
            Assert.Equal(bundle.UnderworldPool, loaded.UnderworldPool);
            Assert.Equal(bundle.PhoenixPool, loaded.PhoenixPool);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Compiler_preserves_a_transfer_between_global_floors()
    {
        var surface = new WorldSector();
        surface.Info.Level = 0;
        surface.Blocks[20, 20].TileExit.SectorId = 2;
        surface.Blocks[20, 20].TileExit.X = 30;
        surface.Blocks[20, 20].TileExit.Y = 30;
        var underground = new WorldSector();
        underground.Info.Level = -1;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
            Placements = [new(1, new(0, 0), "surface"), new(2, new(1, 0), "underground")]
            },
            new Dictionary<short, WorldSector> { [1] = surface, [2] = underground },
            new Dictionary<short, string> { [1] = "surface.csm", [2] = "underground.csm" }, 1);

        var bundle = new UnifiedWorldCompiler().CompileFloors(world, null,
            new Dictionary<short, int> { [1] = 0, [2] = -1 });
        var map = bundle.Floors[0];
        var exit = Assert.Single(map.Exits);

        Assert.Equal(UnifiedWorldExitKind.CrossFloor, exit.Kind);
        Assert.Equal(0, exit.SourceZ);
        Assert.Equal(-1, exit.TargetZ);
        Assert.Equal(GlobalChunkGeometry.WidthTiles + 29, exit.TargetX);
        Assert.Equal(29, exit.TargetY);
    }

    [Fact]
    public void Bundle_audit_rejects_a_cross_floor_exit_to_a_missing_floor()
    {
        var bundle = new UnifiedWorldBundle();
        var floor = new UnifiedWorldMap(2, 2, 0);
        floor.Exits.Add(new UnifiedWorldExit(UnifiedWorldExitKind.CrossFloor, 0, 0, 0, 0,
            SourceZ: 0, TargetZ: -1));
        floor.ExitIndex[0] = 1;
        bundle.Add(floor);

        var result = new UnifiedWorldBundleAuditService().Audit(bundle);

        Assert.Contains(result.Issues, issue => issue.Code == "CROSS-FLOOR-TARGET");
    }

    [Fact]
    public void Transfer_service_resolves_a_global_cross_floor_position()
    {
        var bundle = new UnifiedWorldBundle();
        var surface = new UnifiedWorldMap(2, 2, 0);
        surface.Exits.Add(new UnifiedWorldExit(UnifiedWorldExitKind.CrossFloor, 0, 0, 1, 1,
            SourceZ: 0, TargetZ: -1));
        surface.ExitIndex[0] = 1;
        bundle.Add(surface);
        bundle.Add(new UnifiedWorldMap(2, 2, -1));

        var transfer = new UnifiedWorldTransferService().Resolve(bundle, new UnifiedWorldPosition(0, 0, 0));

        Assert.NotNull(transfer);
        Assert.Equal(UnifiedWorldTransferKind.CrossFloor, transfer.Kind);
        Assert.Equal(new UnifiedWorldPosition(1, 1, -1), transfer.Target);
    }
}
