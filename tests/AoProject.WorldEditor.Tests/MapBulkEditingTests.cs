using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapBulkEditingTests
{
    [Fact]
    public void Bulk_operations_modify_expected_map_fields()
    {
        var map = new WorldSector();
        map.Blocks[2, 3].Graphics[1].GraphicIndex = 10;
        map.Blocks[4, 5].Trigger = 7;
        map.Blocks[6, 7].Object.Index = 3;
        map.Blocks[8, 9].TileExit.SectorId = 2;
        var service = new MapBulkEditingService();

        Assert.Equal(1, service.ReplaceGraphic(map, 1, 10, 20));
        service.ClearTriggers(map);
        service.ClearObjects(map);
        service.ClearExits(map);

        Assert.Equal(20, map.Blocks[2, 3].Graphics[1].GraphicIndex);
        Assert.Equal(0, map.Blocks[4, 5].Trigger);
        Assert.Equal(0, map.Blocks[6, 7].Object.Index);
        Assert.Equal(0, map.Blocks[8, 9].TileExit.SectorId);
        Assert.True(map.Info.Changed);
    }
}
