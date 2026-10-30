using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AdjacentMapStitchTests
{
    [Fact]
    public void Stitcher_detects_north_map_and_copies_the_north_strip()
    {
        var source = new WorldSector(); var destination = new WorldSector();
        source.Blocks[40, 10].TileExit.Map = 7;
        source.Blocks[20, 10].Graphics[0].GraphicIndex = 321;
        var service = new AdjacentMapStitchService();
        Assert.Equal((short)7, service.DetectNeighborMap(source, StitchSide.North));
        Assert.Equal(1100, service.CopySide(source, destination, StitchSide.North));
        Assert.Equal(321, destination.Blocks[20, 90].Graphics[0].GraphicIndex);
    }
}
