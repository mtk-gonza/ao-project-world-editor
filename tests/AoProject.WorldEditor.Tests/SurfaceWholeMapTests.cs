using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SurfaceWholeMapTests
{
    [Fact]
    public void Whole_map_surface_applies_mosaic_without_changing_other_payloads()
    {
        var map = new WorldSector();
        map.Blocks[50, 50].NpcIndex = 3;
        var placed = new SurfaceWholeMapService().Place(map, 0, 1000, true, 2, 2);

        Assert.Equal(10000, placed);
        Assert.Equal(1000, map.Blocks[50, 50].Graphics[0].GraphicIndex);
        Assert.Equal((short)3, map.Blocks[50, 50].NpcIndex);
        Assert.Equal(1003, map.Blocks[51, 51].Graphics[0].GraphicIndex);
        Assert.True(map.Info.Changed);
    }
}
