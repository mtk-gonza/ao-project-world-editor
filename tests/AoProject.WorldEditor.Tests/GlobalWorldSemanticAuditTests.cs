using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalWorldSemanticAuditTests
{
    [Fact]
    public void Coherent_single_sector_passes_semantic_audit()
    {
        var map = new WorldMap();
        var world = CreateWorld([(1, new GlobalChunkCoordinate(0, 0), map)]);

        var result = new GlobalWorldSemanticAuditService().Audit(world);

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Invalid_block_mask_is_reported_with_local_and_global_coordinates()
    {
        var map = new WorldMap();
        map.Blocks[20, 20].Blocked = 0x80;
        var world = CreateWorld([(1, new GlobalChunkCoordinate(0, 0), map)]);

        var issue = Assert.Single(new GlobalWorldSemanticAuditService().Audit(world).Issues,
            item => item.Code == "BLOCK-MASK");

        Assert.Equal(1, issue.MapId);
        Assert.Equal(20, issue.LocalX);
        Assert.Equal(20, issue.LocalY);
        Assert.Equal(19, issue.GlobalX);
        Assert.Equal(19, issue.GlobalY);
    }

    [Fact]
    public void Duplicate_chunks_and_global_overlap_are_reported()
    {
        var first = new WorldMap();
        var second = new WorldMap();
        var world = CreateWorld([
            (1, new GlobalChunkCoordinate(0, 0), first),
            (2, new GlobalChunkCoordinate(0, 0), second)]);

        var issues = new GlobalWorldSemanticAuditService().Audit(world).Issues;

        Assert.Contains(issues, issue => issue.Code == "WORLD-DUPLICATE-CHUNK");
        Assert.Contains(issues, issue => issue.Code == "WORLD-OVERLAP");
    }

    [Fact]
    public void Saved_resolved_snapshot_matches_all_logical_tile_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-semantic-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "mapa1.csm");
            var map = new WorldMap();
            var block = map.Blocks[20, 20];
            block.Blocked = BlockedMask.AllSides;
            block.CharacterIndex = 7;
            block.NpcIndex = 8;
            block.ParticleGroup = 2;
            block.ParticleIndex = 3;
            block.Marked = true;
            block.Object.Index = 4;
            block.Object.Amount = 5;
            var objectGraphic = block.ObjectGraphic;
            objectGraphic.GraphicIndex = 6;
            objectGraphic.Speed = 1.5f;
            block.ObjectGraphic = objectGraphic;
            block.Light.Range = 9;
            block.Light.Color = 0x112233;
            block.LightValues[0] = 12;
            block.Colors[1] = 34;
            block.Red = 1; block.Green = 2; block.Blue = 3;
            block.Graphics[0].GraphicIndex = 10;
            block.Graphics[0].FrameCounter = 2.5f;
            block.Graphics[0].Started = 1;
            map.Info.Changed = true;
            new CsmMapFileService().Save(map, source);

            var world = CreateWorld([(1, new GlobalChunkCoordinate(0, 0), map)], source);
            var archive = Path.Combine(root, "mapaGlobal.csm");
            new GlobalCsmArchiveService().Save(world, archive);

            var result = new GlobalWorldSemanticAuditService().Audit(world, globalCsmPath: archive);

            Assert.DoesNotContain(result.Issues, issue => issue.Code.StartsWith("SNAPSHOT-", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static GlobalWorldDocument CreateWorld((short Id, GlobalChunkCoordinate Chunk, WorldMap Map)[] entries, string? source = null)
    {
        var placements = entries.Select(entry => new GlobalMapPlacement(entry.Id, entry.Chunk, "WORLDMAP1")).ToArray();
        var maps = entries.ToDictionary(entry => entry.Id, entry => entry.Map);
        var paths = entries.ToDictionary(entry => entry.Id, entry => source ?? $"mapa{entry.Id}.csm");
        return new GlobalWorldDocument(
            new GlobalWorldAssembly { Placements = placements, Width = 1, Height = 1 },
            maps, paths, entries[0].Id);
    }
}
