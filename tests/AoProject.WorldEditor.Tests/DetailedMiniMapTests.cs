using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class DetailedMiniMapTests
{
    [Fact]
    public void Draws_a_real_miniature_of_the_tile_art()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-detail-minimap-" + Guid.NewGuid().ToString("N"));
        var graphicsDir = Path.Combine(root, "Graficos");
        Directory.CreateDirectory(graphicsDir);
        try
        {
            using (var texture = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var canvas = Graphics.FromImage(texture))
                {
                    canvas.Clear(Color.Black);
                    using var brush = new SolidBrush(Color.Red);
                    canvas.FillRectangle(brush, 0, 0, 32, 32);
                }
                texture.Save(Path.Combine(graphicsDir, "1.png"), ImageFormat.Png);
            }

            var map = new WorldSector();
            map.Blocks[1, 1].Graphics[0].GraphicIndex = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData
                {
                    Active = true,
                    FileNumber = 1,
                    SourceX = 0,
                    SourceY = 0,
                    PixelWidth = 32,
                    PixelHeight = 32,
                    Frames = new[] { 1 },
                    MiniMapColor = 0x0000FF
                }
            };

            using var detailed = new MapMiniMapService().CreateDetailed(map, graphics, root, scale: 2);

            Assert.Equal(200, detailed.Width);
            Assert.Equal(200, detailed.Height);
            var pixel = detailed.GetPixel(0, 0);
            Assert.True(pixel.R > 0xF0 && pixel.G < 0x10 && pixel.B < 0x10,
                $"esperaba rojo del tile real, obtuve {pixel}");
            // Tiles without art stay black.
            Assert.Equal(Color.Black.ToArgb(), detailed.GetPixel(50, 50).ToArgb());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Falls_back_to_the_flat_palette_when_the_png_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-detail-minimap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            var map = new WorldSector();
            map.Blocks[2, 2].Graphics[0].GraphicIndex = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData
                {
                    Active = true,
                    FileNumber = 999,
                    PixelWidth = 32,
                    PixelHeight = 32,
                    Frames = new[] { 1 },
                    MiniMapColor = 0x0000FF
                }
            };

            using var detailed = new MapMiniMapService().CreateDetailed(map, graphics, root, scale: 2);

            var pixel = detailed.GetPixel(2, 2);
            Assert.True(pixel.B > 0xF0 && pixel.R < 0x10, $"esperaba el color plano, obtuve {pixel}");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Marks_npcs_with_yellow_dots_if_requested()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-detail-minimap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            var map = new WorldSector();
            map.Blocks[3, 4].NpcIndex = 7;

            using var withNpcs = new MapMiniMapService().CreateDetailed(map, [], root, scale: 2,
                new MapMiniMapOptions(ShowNpcs: true));
            using var without = new MapMiniMapService().CreateDetailed(map, [], root, scale: 2,
                new MapMiniMapOptions(ShowNpcs: false));

            var marked = withNpcs.GetPixel(4, 6);
            Assert.True(marked.R > 0xE0 && marked.G > 0xE0 && marked.B < 0x20,
                $"esperaba amarillo, obtuve {marked}");
            Assert.Equal(Color.Black.ToArgb(), without.GetPixel(4, 6).ToArgb());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Clamps_the_scale_and_keeps_the_bitmap_square()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-detail-minimap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using var huge = new MapMiniMapService().CreateDetailed(new WorldSector(), [], root, scale: 99);
            Assert.Equal(WorldSector.Width * 8, huge.Width);
            using var tiny = new MapMiniMapService().CreateDetailed(new WorldSector(), [], root, scale: 0);
            Assert.Equal(WorldSector.Width, tiny.Width);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
