using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapBoundaryCleanupTests
{
    [Fact]
    public void Removing_borders_restores_base_graphic_and_clears_border_payloads_only()
    {
        var map = new WorldSector();
        map.Blocks[1, 1].Graphics[0].GraphicIndex = 88; map.Blocks[1, 1].NpcIndex = 2; map.Blocks[1, 1].Trigger = 3;
        map.Blocks[20, 20].Graphics[0].GraphicIndex = 77;
        var count = new MapCleanupService().RemoveBorders(map);

        Assert.Equal(3768, count);
        Assert.Equal(1, map.Blocks[1, 1].Graphics[0].GraphicIndex);
        Assert.Equal((short)0, map.Blocks[1, 1].NpcIndex);
        Assert.Equal(77, map.Blocks[20, 20].Graphics[0].GraphicIndex);
    }
}
