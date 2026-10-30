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
        var objects = new ObjectData[2]; objects[1] = new ObjectData { ObjectType = 4, GraphicIndex = 900, DefaultBlockedMask = 1 };
        var placed = new TreePlacementService().Place(map, objects, new TreePlacementOptions(3, [1], null, 123));

        Assert.Equal(3, placed);
        var trees = map.Blocks.Cast<MapBlock>().Count(block => block is not null && block.Object.Index == 1);
        Assert.Equal(3, trees);
        Assert.All(map.Blocks.Cast<MapBlock>().Where(block => block is not null && block.Object.Index == 1), block => Assert.Equal(900, block.Graphics[2].GraphicIndex));
        Assert.All(map.Blocks.Cast<MapBlock>().Where(block => block is not null && block.Object.Index == 1), block => Assert.Equal((byte)1, block.Blocked));
    }

    [Fact]
    public void Configured_objects_can_be_blocked_as_a_separate_undoable_operation()
    {
        var map = new WorldMap();
        var objects = new ObjectData[2]; objects[1] = new ObjectData { ObjectType = 8, DefaultBlockedMask = 8 };
        map.Blocks[4, 5].Object.Index = 1;

        Assert.Equal(1, new TreePlacementService().BlockConfiguredObjects(map, objects));
        Assert.Equal((byte)8, map.Blocks[4, 5].Blocked);
    }
}
