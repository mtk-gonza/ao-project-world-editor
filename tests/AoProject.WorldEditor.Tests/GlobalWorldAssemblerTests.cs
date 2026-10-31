using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalWorldAssemblerTests
{
    [Fact]
    public void Border_exits_place_maps_on_a_continuous_global_grid()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        first.Blocks[88, 50].TileExit.SectorId = 2;
        first.Blocks[88, 50].TileExit.X = 14;
        first.Blocks[88, 50].TileExit.Y = 50;
        second.Blocks[13, 50].TileExit.SectorId = 1;
        second.Blocks[13, 50].TileExit.X = 87;
        second.Blocks[13, 50].TileExit.Y = 50;

        var assembly = new GlobalWorldAssembler().Assemble(new Dictionary<short, WorldSector>
        {
            [1] = first,
            [2] = second
        }, 1);

        Assert.Equal(new GlobalChunkCoordinate(0, 0), assembly.Placements.Single(item => item.SectorId == 1).Chunk);
        Assert.Equal(new GlobalChunkCoordinate(1, 0), assembly.Placements.Single(item => item.SectorId == 2).Chunk);
        Assert.Empty(assembly.Conflicts);
    }

    [Fact]
    public void Conflicting_links_are_reported_instead_of_overwriting_placement()
    {
        var root = new WorldSector();
        var east = new WorldSector();
        var north = new WorldSector();
        root.Blocks[88, 50].TileExit.SectorId = 2;
        root.Blocks[88, 50].TileExit.X = 14;
        root.Blocks[88, 50].TileExit.Y = 50;
        root.Blocks[50, 10].TileExit.SectorId = 3;
        root.Blocks[50, 10].TileExit.X = 50;
        root.Blocks[50, 10].TileExit.Y = 90;
        east.Blocks[13, 50].TileExit.SectorId = 1;
        east.Blocks[13, 50].TileExit.X = 87;
        east.Blocks[13, 50].TileExit.Y = 50;
        east.Blocks[50, 10].TileExit.SectorId = 3;
        east.Blocks[50, 10].TileExit.X = 50;
        east.Blocks[50, 10].TileExit.Y = 90;
        north.Blocks[50, 91].TileExit.SectorId = 1;
        north.Blocks[50, 91].TileExit.X = 50;
        north.Blocks[50, 91].TileExit.Y = 11;

        var assembly = new GlobalWorldAssembler().Assemble(new Dictionary<short, WorldSector>
        {
            [1] = root,
            [2] = east,
            [3] = north
        }, 1);

        Assert.Contains(assembly.Conflicts, conflict => conflict.MapToString() == "2->3");
    }

    [Fact]
    public void Horizontal_wrap_is_normalized_instead_of_reported_as_a_conflict()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        var third = new WorldSector();
        first.Blocks[88, 50].TileExit.SectorId = 2;
        first.Blocks[88, 50].TileExit.X = 14;
        first.Blocks[88, 50].TileExit.Y = 50;
        first.Blocks[13, 50].TileExit.SectorId = 3;
        first.Blocks[13, 50].TileExit.X = 87;
        first.Blocks[13, 50].TileExit.Y = 50;
        second.Blocks[88, 50].TileExit.SectorId = 3;
        second.Blocks[88, 50].TileExit.X = 14;
        second.Blocks[88, 50].TileExit.Y = 50;
        second.Blocks[13, 50].TileExit.SectorId = 1;
        second.Blocks[13, 50].TileExit.X = 87;
        second.Blocks[13, 50].TileExit.Y = 50;
        third.Blocks[88, 50].TileExit.SectorId = 1;
        third.Blocks[88, 50].TileExit.X = 14;
        third.Blocks[88, 50].TileExit.Y = 50;
        third.Blocks[13, 50].TileExit.SectorId = 2;
        third.Blocks[13, 50].TileExit.X = 87;
        third.Blocks[13, 50].TileExit.Y = 50;

        var assembly = new GlobalWorldAssembler().Assemble(new Dictionary<short, WorldSector>
        {
            [1] = first,
            [2] = second,
            [3] = third
        }, 1);

        Assert.Empty(assembly.Conflicts);
        Assert.True(assembly.WrapX);
        Assert.Equal(3, assembly.Width);
        Assert.Equal(new[] { 0, 1, 2 },
            assembly.Placements.OrderBy(placement => placement.Chunk.X).Select(placement => placement.Chunk.X));
    }
}

internal static class GlobalWorldConflictTestExtensions
{
    public static string MapToString(this GlobalWorldConflict conflict) =>
        $"{conflict.FromMap}->{conflict.ToMap}";
}
