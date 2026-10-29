using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldMapSnapshotTests
{
    [Fact]
    public void Snapshot_restores_bulk_mutations_atomically()
    {
        var map = new WorldMap();
        map.Blocks[7, 8].Graphics[2].GraphicIndex = 123;
        map.Blocks[7, 8].Blocked = 4;
        var snapshot = WorldMapSnapshot.Capture(map);
        map.Blocks[7, 8].Graphics[2].GraphicIndex = 999;
        map.Blocks[7, 8].Blocked = 0;

        snapshot.Restore(map);

        Assert.Equal(123, map.Blocks[7, 8].Graphics[2].GraphicIndex);
        Assert.Equal(4, map.Blocks[7, 8].Blocked);
    }
}
