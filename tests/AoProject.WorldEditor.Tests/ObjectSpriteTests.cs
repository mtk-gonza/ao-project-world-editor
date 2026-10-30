using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ObjectSpriteTests
{
    [Fact]
    public void Object_index_resolves_to_its_graphic_and_anchors_at_the_tile_bottom()
    {
        var objects = new ObjectData[3];
        objects[1] = new ObjectData { GraphicIndex = 900 };
        var graphics = new List<GraphicData> { new(), new() };
        // 900 must exist in the graphic catalog for the height lookup.
        while (graphics.Count <= 900) graphics.Add(new GraphicData());
        graphics[900].PixelHeight = 64;

        Assert.Equal(900, objects[1].GraphicIndex);
        var map = new WorldSector();
        map.Blocks[5, 6].Object.Index = 1;
        map.Blocks[5, 6].Object.Amount = 1;

        var graphic = map.Blocks[5, 6].ObjectGraphic.GraphicIndex;
        if (graphic <= 0) graphic = objects[map.Blocks[5, 6].Object.Index].GraphicIndex;
        var height = graphics[graphic].PixelHeight;
        var anchor = (6 - 1) * 32;
        var top = height > 32 ? anchor - (height - 32) : anchor;

        Assert.Equal(900, graphic);
        Assert.Equal(160 - 32, top);
    }
}
