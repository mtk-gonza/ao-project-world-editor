using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SurfacePlacementTests
{
    [Fact]
    public void Surface_placement_applies_dimensions_layer_mosaic_and_blocking()
    {
        var map = new WorldMap();
        var surface = new SurfaceData { GraphicIndex = 500, Width = 2, Height = 2, Layer = 3, Blocked = true };
        var placed = new SurfacePlacementService().Place(map, surface, 10, 20, true);

        Assert.Equal(4, placed);
        Assert.Equal(500, map.Blocks[10, 20].Graphics[2].GraphicIndex);
        Assert.Equal(503, map.Blocks[11, 21].Graphics[2].GraphicIndex);
        Assert.Equal(502, map.Blocks[10, 21].Graphics[2].GraphicIndex);
        Assert.Equal((byte)0xF, map.Blocks[10, 20].Blocked);
    }

    [Fact]
    public void PaintAt_uses_legacy_tile_based_mosaic_and_blocking()
    {
        var map = new WorldMap();
        new SurfacePlacementService().PaintAt(map, 5, 6, 0, 100, 4, 3, true);

        Assert.Equal(100 + (6 % 3) * 4 + (5 % 4), map.Blocks[5, 6].Graphics[0].GraphicIndex);
        Assert.Equal((byte)0xF, map.Blocks[5, 6].Blocked);
    }

    [Fact]
    public void PaintAt_rejects_tiles_outside_csm_domain()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(5, 2, 5, 2) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SurfacePlacementService().PaintAt(map, 1, 1, 0, 100, 1, 1, false));
    }

    [Fact]
    public void AutoCompleteAt_fills_only_the_effective_domain()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(3, 2, 3, 2) };

        var placed = new SurfacePlacementService().AutoCompleteAt(map, 2, 2, 0, 500, 3, 2, true);

        Assert.Equal(4, placed);
        Assert.Equal(500, map.Blocks[2, 2].Graphics[0].GraphicIndex);
        Assert.Equal(501, map.Blocks[3, 2].Graphics[0].GraphicIndex);
        Assert.Equal((byte)0xF, map.Blocks[3, 2].Blocked);
        Assert.Equal(0, map.Blocks[4, 2].Graphics[0].GraphicIndex);
    }

    [Fact]
    public void AutoCompleteAt_supports_single_row_or_column_mosaics()
    {
        var map = new WorldMap();

        var placed = new SurfacePlacementService().AutoCompleteAt(map, 4, 4, 0, 700, 1, 3, false);

        Assert.Equal(3, placed);
        Assert.Equal(700, map.Blocks[4, 4].Graphics[0].GraphicIndex);
        Assert.Equal(701, map.Blocks[4, 5].Graphics[0].GraphicIndex);
        Assert.Equal(702, map.Blocks[4, 6].Graphics[0].GraphicIndex);
    }
}
