using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AdjacentBoundsTests
{
    [Fact]
    public void Existing_maps_are_detected_from_the_four_reference_border_ranges()
    {
        var map = new WorldSector();
        map.Blocks[20, 10].TileExit.SectorId = 101;
        map.Blocks[88, 20].TileExit.SectorId = 102;
        map.Blocks[30, 91].TileExit.SectorId = 103;
        map.Blocks[13, 40].TileExit.SectorId = 104;

        var maps = new AdjacentTransferService().DetectExistingMaps(map, AdjacentTransferService.BasicBounds);

        Assert.Equal(new short[] { 101, 102, 103, 104 }, maps);
    }
}
