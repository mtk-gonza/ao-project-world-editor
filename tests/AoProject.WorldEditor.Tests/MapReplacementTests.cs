using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapReplacementTests
{
    [Fact]
    public void Replacement_moves_graphics_and_updates_entities_like_reference()
    {
        var map = new WorldSector();
        map.Blocks[2, 2].Graphics[0].GraphicIndex = 10;
        map.Blocks[3, 3].Object.Index = 1;
        map.Blocks[4, 4].NpcIndex = 1;
        map.Blocks[5, 5].Trigger = 7;
        var objects = new ObjectData[3]; objects[1] = new ObjectData { GraphicIndex = 88 }; objects[2] = new ObjectData { GraphicIndex = 99 };
        var npcs = new NpcData[3]; npcs[1] = new NpcData(); npcs[2] = new NpcData();
        var service = new MapReplacementService();

        Assert.Equal(1, service.ReplaceGraphic(map, 0, 2, 10, 20));
        Assert.Equal(1, service.ReplaceObject(map, objects, 1, 2));
        Assert.Equal(1, service.ReplaceNpc(map, npcs, 1, 2));
        Assert.Equal(1, service.ReplaceTrigger(map, 7, 8));
        Assert.Equal(0, map.Blocks[2, 2].Graphics[0].GraphicIndex);
        Assert.Equal(20, map.Blocks[2, 2].Graphics[2].GraphicIndex);
        Assert.Equal(99, map.Blocks[3, 3].ObjectGraphic.GraphicIndex);
        Assert.True(map.Info.Changed);
    }
}
