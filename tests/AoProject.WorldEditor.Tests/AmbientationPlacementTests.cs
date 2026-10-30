using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AmbientationPlacementTests
{
    [Fact]
    public void Ambientation_placement_uses_reference_area_and_layer()
    {
        var map = new WorldSector();
        var ambientiation = new Ambientation { Name = "Prueba", Type = 1, GraphicIndex = 800 };
        var count = new AmbientationPlacementService().Place(map, ambientiation, 5, 3, true, 123);
        Assert.Equal(5, count);
        var placed = map.Blocks.Cast<SectorTile>().Where(block => block is not null && block.Graphics[2].GraphicIndex == 800).ToArray();
        Assert.Equal(5, placed.Length); Assert.All(placed, block => Assert.Equal((byte)1, block.Blocked));
    }
}
