using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapTileCaptureTests
{
    [Fact]
    public void Capture_selects_highest_surface_and_optional_transfer()
    {
        var map = new WorldSector(); map.Blocks[2, 3].Graphics[1].GraphicIndex = 20; map.Blocks[2, 3].Graphics[3].GraphicIndex = 40;
        map.Blocks[2, 3].TileExit.Map = 7; map.Blocks[2, 3].TileExit.X = 8; map.Blocks[2, 3].TileExit.Y = 9;
        var capture = new MapTileCaptureService().Capture(map, 2, 3, true, true);
        Assert.Equal(3, capture.Layer); Assert.Equal(40, capture.GraphicIndex); Assert.True(capture.HasTransfer); Assert.Equal((short)7, capture.Transfer.Map);
    }
}
