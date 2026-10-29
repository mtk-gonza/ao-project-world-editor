using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RenderOptionsTests
{
    [Fact]
    public void Layer_visibility_filters_render_geometry_without_mutating_map()
    {
        var map = new WorldMap();
        map.Blocks[2, 2].Graphics[0].GraphicIndex = 10;
        map.Blocks[2, 2].Graphics[1].GraphicIndex = 20;
        var options = new WorldRenderOptions { ShowLayer2 = false };
        var geometry = MapRenderGeometry.Build(map, options);

        Assert.Contains(geometry.Tiles, tile => tile.Layer == 0);
        Assert.DoesNotContain(geometry.Tiles, tile => tile.Layer == 1);
        Assert.Equal(20, map.Blocks[2, 2].Graphics[1].GraphicIndex);
    }
}
