using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BlockPlacementTests
{
    [Fact]
    public void Placement_increments_graphics_by_row_major_position()
    {
        var map = new WorldMap();
        var preset = new BlockPresetType { Layer = 2, TilesX = 2, TilesY = 2 };
        var item = new BlockPreset { GraphicIndex = 500 };
        var placed = new BlockPlacementService().Place(map, preset, item, 10, 20);

        Assert.Equal(4, placed);
        Assert.Equal(500, map.Blocks[10, 20].Graphics[1].GraphicIndex);
        Assert.Equal(501, map.Blocks[11, 20].Graphics[1].GraphicIndex);
        Assert.Equal(502, map.Blocks[10, 21].Graphics[1].GraphicIndex);
        Assert.Equal(503, map.Blocks[11, 21].Graphics[1].GraphicIndex);
    }
}
