using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SelectedEntityRemovalTests
{
    [Fact]
    public void Removing_one_entity_kind_preserves_the_other_payloads()
    {
        var map = new WorldSector(); var block = map.Blocks[5, 6];
        block.Object.Index = 2; block.Object.Amount = 1; block.NpcIndex = 3; block.Trigger = 4; block.TileExit.SectorId = 8;
        var service = new MapEntityEditingService();
        service.SetObject(map, new[] { new ObjectData(), new ObjectData(), new ObjectData { GraphicIndex = 90 } }, 5, 6, 0);
        Assert.Equal((short)0, block.Object.Index); Assert.Equal((short)3, block.NpcIndex); Assert.Equal((short)4, block.Trigger); Assert.Equal((short)8, block.TileExit.SectorId);
    }
}
