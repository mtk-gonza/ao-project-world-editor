using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class TreePlacementTests
{
    [Fact]
    public void Tree_placement_uses_reference_area_and_populates_object_layer_three()
    {
        var map = new WorldMap();
        var objects = new ObjectData[2]; objects[1] = new ObjectData { ObjectType = 4, GraphicIndex = 900 };
        var placed = new TreePlacementService().Place(map, objects, new TreePlacementOptions(3, [1], 1, 123));

        Assert.Equal(3, placed);
        var trees = map.Blocks.Cast<MapBlock>().Count(block => block is not null && block.Object.Index == 1);
        Assert.Equal(3, trees);
        Assert.All(map.Blocks.Cast<MapBlock>().Where(block => block is not null && block.Object.Index == 1), block => Assert.Equal(900, block.Graphics[2].GraphicIndex));
    }
}
