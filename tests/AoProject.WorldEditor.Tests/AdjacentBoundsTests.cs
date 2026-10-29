using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AdjacentBoundsTests
{
    [Fact]
    public void Existing_maps_are_detected_from_the_four_reference_border_ranges()
    {
        var map = new WorldMap();
        map.Blocks[20, 10].TileExit.Map = 101;
        map.Blocks[88, 20].TileExit.Map = 102;
        map.Blocks[30, 91].TileExit.Map = 103;
        map.Blocks[13, 40].TileExit.Map = 104;

        var maps = new AdjacentTransferService().DetectExistingMaps(map, AdjacentTransferService.BasicBounds);

        Assert.Equal(new short[] { 101, 102, 103, 104 }, maps);
    }
}
