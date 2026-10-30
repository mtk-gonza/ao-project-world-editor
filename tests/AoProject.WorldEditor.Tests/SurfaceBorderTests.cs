using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SurfaceBorderTests
{
    [Fact]
    public void Border_surface_clears_entities_and_keeps_inner_map_untouched()
    {
        var map = new WorldSector();
        map.Blocks[1, 1].NpcIndex = 4; map.Blocks[1, 1].Object.Index = 3; map.Blocks[1, 1].TileExit.Map = 8;
        map.Blocks[20, 20].Graphics[1].GraphicIndex = 77;
        var count = new SurfaceBorderPlacementService().Place(map, 1, 500, true, 2, 2, true);

        Assert.Equal(10000 - (88 - 13 + 1) * (91 - 10 + 1), count);
        Assert.Equal(503, map.Blocks[1, 1].Graphics[1].GraphicIndex);
        Assert.Equal((short)0, map.Blocks[1, 1].NpcIndex);
        Assert.Equal((short)0, map.Blocks[1, 1].TileExit.Map);
        Assert.Equal(77, map.Blocks[20, 20].Graphics[1].GraphicIndex);
    }

    [Fact]
    public void Whole_surface_uses_csm_map_size_bounds()
    {
        var map = new WorldSector { CsmSectorSize = new CsmSectorSize(5, 2, 4, 3) };
        var count = new SurfaceWholeMapService().Place(map, 0, 700, false, 1, 1);

        Assert.Equal(8, count);
        Assert.Equal(700, map.Blocks[2, 3].Graphics[0].GraphicIndex);
        Assert.Equal(0, map.Blocks[1, 1].Graphics[0].GraphicIndex);
    }
}
