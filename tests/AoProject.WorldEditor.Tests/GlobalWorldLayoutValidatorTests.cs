using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalWorldLayoutValidatorTests
{
    [Fact]
    public void Reports_official_maps_without_reachable_tile_exits()
    {
        var maps = new Dictionary<short, WorldSector> { [1] = new(), [2] = new() };
        var official = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world-1"),
                new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "world-1")
            ]
        };

        var issues = new GlobalWorldLayoutValidator().Validate(official, maps, 1);

        Assert.Contains(issues, issue => issue.SectorId == 2);
    }

    [Fact]
    public void Does_not_throw_when_official_layout_repeats_a_map_id()
    {
        var maps = new Dictionary<short, WorldSector> { [1] = new() };
        var official = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world-1"),
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(1, 0), "world-1")
            ]
        };

        var issues = new GlobalWorldLayoutValidator().Validate(official, maps, 1);

        Assert.NotNull(issues);
    }

    [Fact]
    public void Accepts_a_pair_with_canonical_exits_in_both_directions()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        first.Blocks[88, 50].TileExit.SectorId = 2;
        first.Blocks[88, 50].TileExit.X = 14;
        first.Blocks[88, 50].TileExit.Y = 50;
        second.Blocks[13, 50].TileExit.SectorId = 1;
        second.Blocks[13, 50].TileExit.X = 87;
        second.Blocks[13, 50].TileExit.Y = 50;
        var official = new GlobalWorldAssembly
        {
            Width = 2,
            Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world-1"),
                new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "world-1")
            ]
        };

        var issues = new GlobalWorldLayoutValidator().Validate(official,
            new Dictionary<short, WorldSector> { [1] = first, [2] = second }, 1);

        Assert.Empty(issues);
    }

    [Fact]
    public void Accepts_the_horizontal_seam_when_the_world_wraps()
    {
        var first = new WorldSector();
        var last = new WorldSector();
        first.Blocks[13, 50].TileExit.SectorId = 3;
        first.Blocks[13, 50].TileExit.X = 87;
        first.Blocks[13, 50].TileExit.Y = 50;
        last.Blocks[88, 50].TileExit.SectorId = 1;
        last.Blocks[88, 50].TileExit.X = 14;
        last.Blocks[88, 50].TileExit.Y = 50;
        var official = new GlobalWorldAssembly
        {
            Width = 3,
            WrapX = true,
            Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world-1"),
                new GlobalSectorPlacement(3, new GlobalChunkCoordinate(2, 0), "world-1")
            ]
        };

        var issues = new GlobalWorldLayoutValidator().Validate(official,
            new Dictionary<short, WorldSector> { [1] = first, [3] = last }, 1);

        Assert.Empty(issues);
    }

    [Fact]
    public void Ignores_pairs_between_repeated_filler_maps()
    {
        var official = new GlobalWorldAssembly
        {
            Width = 2,
            Placements =
            [
                new GlobalSectorPlacement(300, new GlobalChunkCoordinate(0, 0), "world-1"),
                new GlobalSectorPlacement(300, new GlobalChunkCoordinate(1, 0), "world-1")
            ]
        };

        var issues = new GlobalWorldLayoutValidator().Validate(official,
            new Dictionary<short, WorldSector> { [300] = new() }, 300);

        Assert.Empty(issues);
    }
}
