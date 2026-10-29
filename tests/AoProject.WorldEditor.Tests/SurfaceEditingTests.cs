using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SurfaceEditingTests
{
    [Fact]
    public void Fill_and_borders_follow_reference_coordinates()
    {
        var map = new WorldMap();
        var service = new SurfaceEditingService();
        service.Fill(map, 0, 100, 2, 2);
        service.FillBorders(map, 1, 200);
        service.LockBorders(map);

        Assert.Equal(102, map.Blocks[2, 3].Graphics[0].GraphicIndex);
        Assert.Equal(200, map.Blocks[1, 1].Graphics[1].GraphicIndex);
        Assert.Equal(0xF, map.Blocks[1, 1].Blocked);
        Assert.True(map.Info.Changed);
    }

    [Fact]
    public void Lock_borders_use_the_effective_csm_domain()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(5, 2, 5, 2) };

        new SurfaceEditingService().LockBorders(map);

        Assert.Equal((byte)0xF, map.Blocks[2, 2].Blocked);
        Assert.Equal((byte)0xF, map.Blocks[5, 5].Blocked);
    }
}
