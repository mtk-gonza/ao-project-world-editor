using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RandomEntityPlacementTests
{
    [Fact]
    public void Random_npcs_and_objects_use_reference_ranges_and_payloads()
    {
        var map = new WorldSector();
        var service = new RandomEntityPlacementService();
        var npcs = new[] { new NpcData(), new NpcData { Body = 1 } };
        var objects = new[] { new ObjectData(), new ObjectData { GraphicIndex = 900 } };
        Assert.True(service.PlaceNpcs(map, npcs, new RandomNpcPlacementOptions(5, 1, 3)) > 0);
        Assert.True(service.PlaceObjects(map, objects, new RandomObjectPlacementOptions(5, 1, true, 3)) > 0);
        Assert.Contains(map.Blocks.Cast<SectorTile>(), block => block is not null && block.NpcIndex == 1);
        Assert.Contains(map.Blocks.Cast<SectorTile>(), block => block is not null && block.Object.Index == 1 && block.Blocked == 0xF);
    }
}
