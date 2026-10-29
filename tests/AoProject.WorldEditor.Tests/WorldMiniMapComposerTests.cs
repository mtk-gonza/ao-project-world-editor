using System.Drawing;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldMiniMapComposerTests
{
    [Fact]
    public void Composes_one_image_per_world_at_grid_scale()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-world-minimap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layouts = new[]
            {
                new MapsWorldDataService.WorldLayout(1, 2, 1, new short[] { 1, 2 }),
                new MapsWorldDataService.WorldLayout(2, 2, 1, new short[] { 1, 0 })
            };
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { MiniMapColor = 0x000000FF },
                new GraphicData { MiniMapColor = 0x0000FF00 }
            };
            var first = new WorldMap();
            first.Blocks[1, 1].Graphics[0].GraphicIndex = 1;
            var second = new WorldMap();
            second.Blocks[1, 1].Graphics[0].GraphicIndex = 2;

            using var composer = new WorldMiniMapComposer(layouts, MapMiniMapOptions.Vb6Default);
            Assert.Equal(2, composer.Worlds);
            Assert.True(composer.Add(1, first, graphics, null));
            Assert.True(composer.Add(2, second, graphics, null));
            Assert.False(composer.Add(99, first, graphics, null));

            var files = composer.Save(Path.Combine(root, "worldmap"));
            Assert.Equal(4, files.Count);

            using var world1 = new Bitmap(Path.Combine(root, "worldmap", "worldmap1.png"));
            using var world2 = new Bitmap(Path.Combine(root, "worldmap", "worldmap2.png"));
            Assert.Equal(200, world1.Width);
            Assert.Equal(100, world1.Height);
            Assert.True(world1.GetPixel(0, 0).B > 0xF0);
            Assert.True(world1.GetPixel(100, 0).G > 0xF0);
            Assert.True(world1.GetPixel(50, 50).G < 0x08);
            // WORLDMAP2 uses map 1 in the first cell and leaves the second empty.
            Assert.True(world2.GetPixel(0, 0).B > 0xF0);
            Assert.True(world2.GetPixel(150, 50).R < 0x08);

            using var preview = new Bitmap(Path.Combine(root, "worldmap", "worldmap1-preview.png"));
            Assert.Equal(50, preview.Width);
            Assert.Equal(25, preview.Height);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Filler_maps_are_drawn_in_every_cell_that_references_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-world-minimap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layouts = new[]
            {
                new MapsWorldDataService.WorldLayout(1, 2, 1, new short[] { 7, 7 })
            };
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { MiniMapColor = 0x0000FF00 }
            };
            var map = new WorldMap();
            map.Blocks[1, 1].Graphics[0].GraphicIndex = 1;

            using var composer = new WorldMiniMapComposer(layouts, MapMiniMapOptions.Vb6Default);
            Assert.True(composer.Add(7, map, graphics, null));
            composer.Save(Path.Combine(root, "worldmap"));

            using var image = new Bitmap(Path.Combine(root, "worldmap", "worldmap1.png"));
            Assert.True(image.GetPixel(0, 0).G > 0xF0);
            Assert.True(image.GetPixel(100, 0).G > 0xF0);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
