using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RandomSurfaceTests
{
    [Fact]
    public void Random_complete_mosaic_places_sequential_graphics_and_blocking()
    {
        var map = new WorldMap();
        var options = new RandomSurfaceOptions(1, 1, 100, true, 2, 2, true, true, 123);
        var placed = new RandomSurfacePlacementService().Place(map, options);

        Assert.Equal(4, placed);
        var graphics = map.Blocks.Cast<MapBlock>().Where(block => block is not null && block.Graphics[1].GraphicIndex >= 100 && block.Graphics[1].GraphicIndex <= 103).ToArray();
        Assert.Equal(4, graphics.Length);
        Assert.All(graphics, block => Assert.Equal((byte)0xF, block.Blocked));
    }
}
