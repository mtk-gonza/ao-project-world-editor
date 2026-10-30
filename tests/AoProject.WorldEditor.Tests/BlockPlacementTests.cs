using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BlockPlacementTests
{
    [Fact]
    public void Placement_increments_graphics_by_row_major_position()
    {
        var map = new WorldSector();
        var preset = new BlockPresetType { Layer = 2, TilesX = 2, TilesY = 2 };
        var item = new BlockPreset { GraphicIndex = 500 };
        var placed = new BlockPlacementService().Place(map, preset, item, 10, 20);

        Assert.Equal(4, placed);
        Assert.Equal(500, map.Blocks[10, 20].Graphics[1].GraphicIndex);
        Assert.Equal(501, map.Blocks[11, 20].Graphics[1].GraphicIndex);
        Assert.Equal(502, map.Blocks[10, 21].Graphics[1].GraphicIndex);
        Assert.Equal(503, map.Blocks[11, 21].Graphics[1].GraphicIndex);
    }

    [Fact]
    public void Placement_materializes_the_preset_mask_without_changing_graphics()
    {
        var map = new WorldSector();
        var item = new BlockPreset { GraphicIndex = 500, DefaultBlockedMask = BlockedMask.AllSides };
        new BlockPlacementService().Place(map, new BlockPresetType { Layer = 1, TilesX = 1, TilesY = 1 }, item, 2, 3);

        Assert.Equal((byte)15, map.Blocks[2, 3].Blocked);
        Assert.Equal(500, map.Blocks[2, 3].Graphics[0].GraphicIndex);
    }
}
