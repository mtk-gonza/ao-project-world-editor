using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapOptimizationTests
{
    [Fact]
    public void Optimization_applies_reference_cleanup_and_special_object_rules()
    {
        var map = new WorldSector();
        map.Blocks[1, 1].NpcIndex = 4;
        map.Blocks[1, 1].TileExit.SectorId = 9;
        map.Blocks[1, 1].Trigger = 3;
        map.Blocks[20, 20].Object.Index = 1;
        var objectGraphic = map.Blocks[20, 20].ObjectGraphic;
        objectGraphic.GraphicIndex = 77;
        map.Blocks[20, 20].ObjectGraphic = objectGraphic;
        var objects = new ObjectData[2];
        objects[1] = new ObjectData { ObjectType = 4 };

        var result = new MapOptimizationService().Optimize(map, objects,
            new MapOptimizationOptions(RemoveOuterBorderEntities: true, MapSpecialObjects: true));

        Assert.Equal(1, result.RemovedNpcs);
        Assert.Equal(1, result.RemovedExits);
        Assert.Equal(77, map.Blocks[20, 20].Graphics[2].GraphicIndex);
        Assert.Equal((byte)1, map.Blocks[20, 20].Blocked);
        Assert.True(map.Info.Changed);
    }
}
