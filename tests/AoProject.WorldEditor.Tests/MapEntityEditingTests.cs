using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapEntityEditingTests
{
    [Fact]
    public void Entity_editing_updates_object_npc_trigger_and_transfer_payloads()
    {
        var map = new WorldSector();
        var objects = new ObjectData[2]; objects[1] = new ObjectData { GraphicIndex = 88 };
        var npcs = new NpcData[2]; npcs[1] = new NpcData();
        var service = new MapEntityEditingService();
        service.SetObject(map, objects, 2, 3, 1, 4);
        service.SetNpc(map, npcs, 4, 5, 1);
        service.SetTrigger(map, 6, 7, 9);
        service.SetTransfer(map, 8, 9, 10, 20, 21);

        Assert.Equal((short)4, map.Blocks[2, 3].Object.Amount);
        Assert.Equal(88, map.Blocks[2, 3].ObjectGraphic.GraphicIndex);
        Assert.Equal((short)1, map.Blocks[4, 5].NpcIndex);
        Assert.Equal((short)9, map.Blocks[6, 7].Trigger);
        Assert.Equal((short)20, map.Blocks[8, 9].TileExit.X);
        Assert.True(map.Info.Changed);
    }
}
