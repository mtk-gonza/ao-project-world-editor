using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapEntityUiWorkflowTests
{
    [Fact]
    public void Entity_editing_service_supports_object_npc_trigger_and_transfer_payloads()
    {
        var map = new WorldSector();
        var objects = new[] { new ObjectData(), new ObjectData { GraphicIndex = 700 } };
        var npcs = new[] { new NpcData(), new NpcData { Body = 2 } };
        var service = new MapEntityEditingService();
        service.SetObject(map, objects, 2, 3, 1, 4); service.SetNpc(map, npcs, 2, 3, 1); service.SetTrigger(map, 2, 3, 9); service.SetTransfer(map, 2, 3, 7, 20, 21);
        Assert.Equal((short)1, map.Blocks[2, 3].Object.Index); Assert.Equal((short)1, map.Blocks[2, 3].NpcIndex);
        Assert.Equal((short)9, map.Blocks[2, 3].Trigger); Assert.Equal((short)7, map.Blocks[2, 3].TileExit.Map);
    }
}
