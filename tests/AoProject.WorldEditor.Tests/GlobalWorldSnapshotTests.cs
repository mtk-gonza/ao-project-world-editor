using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalWorldSnapshotTests
{
    [Fact]
    public void Snapshot_restores_changes_in_multiple_maps_atomically()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        first.Blocks[1, 1].Graphics[0].GraphicIndex = 10;
        second.Blocks[1, 1].Graphics[0].GraphicIndex = 20;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly { Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ] },
            new Dictionary<short, WorldSector> { [1] = first, [2] = second },
            new Dictionary<short, string> { [1] = "1.csm", [2] = "2.csm" }, 1);

        var snapshot = GlobalWorldSnapshot.Capture(world, new short[] { 1, 2 });
        first.Blocks[1, 1].Graphics[0].GraphicIndex = 99;
        second.Blocks[1, 1].Graphics[0].GraphicIndex = 88;

        snapshot.Restore(world);

        Assert.Equal(10, first.Blocks[1, 1].Graphics[0].GraphicIndex);
        Assert.Equal(20, second.Blocks[1, 1].Graphics[0].GraphicIndex);
    }

    [Fact]
    public void Snapshot_only_restores_the_maps_it_was_asked_to_capture()
    {
        var first = new WorldSector();
        var second = new WorldSector();
        first.Blocks[1, 1].Graphics[0].GraphicIndex = 10;
        second.Blocks[1, 1].Graphics[0].GraphicIndex = 20;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly { Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ] },
            new Dictionary<short, WorldSector> { [1] = first, [2] = second },
            new Dictionary<short, string> { [1] = "1.csm", [2] = "2.csm" }, 1);

        var snapshot = GlobalWorldSnapshot.Capture(world, new short[] { 1 });
        first.Blocks[1, 1].Graphics[0].GraphicIndex = 99;
        second.Blocks[1, 1].Graphics[0].GraphicIndex = 88;

        snapshot.Restore(world);

        Assert.Equal(1, snapshot.MapCount);
        Assert.Equal(10, first.Blocks[1, 1].Graphics[0].GraphicIndex);
        Assert.Equal(88, second.Blocks[1, 1].Graphics[0].GraphicIndex);
    }
}
