using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapClipboardTests
{
    [Fact]
    public void Copy_and_paste_preserve_all_graphic_layers()
    {
        var map = new WorldMap();
        for (var layer = 0; layer < 4; layer++) map.Blocks[2, 2].Graphics[layer].GraphicIndex = 100 + layer;
        var clipboard = new MapClipboardService();

        clipboard.Copy(map, new TileSelection(2, 2, 2, 2));
        clipboard.Paste(map, 5, 6);

        for (var layer = 0; layer < 4; layer++)
            Assert.Equal(100 + layer, map.Blocks[5, 6].Graphics[layer].GraphicIndex);
        Assert.True(map.Info.Changed);
    }
}
