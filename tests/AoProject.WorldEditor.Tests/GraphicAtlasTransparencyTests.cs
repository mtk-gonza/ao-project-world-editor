using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicAtlasTransparencyTests
{
    [Fact]
    public void Legacy_black_color_key_is_copied_as_transparent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-atlas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        var path = Path.Combine(root, "Graficos", "1.png");
        using (var bitmap = new Bitmap(2, 1, PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(0, 0, Color.Black);
            bitmap.SetPixel(1, 0, Color.FromArgb(255, 220, 180, 120));
            bitmap.Save(path, ImageFormat.Png);
        }

        try
        {
            using var atlas = new GraphicAtlas(root);
            var graphic = new Domain.GraphicData
            {
                Active = true, FileNumber = 1, SourceX = 0, SourceY = 0,
                PixelWidth = 2, PixelHeight = 1, Frames = [1]
            };
            graphic.TileWidth = 2;
            graphic.TileHeight = 1.5f;
            Assert.True(atlas.TryResolve(graphic, out var region));
            Assert.Equal(2f, region.TileWidth);
            Assert.Equal(1.5f, region.TileHeight);
            Assert.Equal(2f / GraphicAtlas.PageSize, region.U1 - region.U0);
            Assert.Equal(1f / GraphicAtlas.PageSize, region.V1 - region.V0);
            var pixels = atlas.Pages[0].Pixels;
            Assert.Equal(0, pixels[3]);
            Assert.Equal(255, pixels[7]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Adjacent_regions_keep_a_transparent_padding_pixel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-atlas-padding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        var path = Path.Combine(root, "Graficos", "1.png");
        using (var bitmap = new Bitmap(4, 1, PixelFormat.Format32bppArgb))
        {
            for (var x = 0; x < 4; x++) bitmap.SetPixel(x, 0, Color.FromArgb(255, 20 + x, 30, 40));
            bitmap.Save(path, ImageFormat.Png);
        }

        try
        {
            using var atlas = new GraphicAtlas(root);
            var first = new Domain.GraphicData { Active = true, FileNumber = 1, SourceX = 0, SourceY = 0, PixelWidth = 2, PixelHeight = 1, Frames = [1] };
            var second = new Domain.GraphicData { Active = true, FileNumber = 1, SourceX = 2, SourceY = 0, PixelWidth = 2, PixelHeight = 1, Frames = [1] };

            Assert.True(atlas.TryResolve(first, out var firstRegion));
            Assert.True(atlas.TryResolve(second, out var secondRegion));
            Assert.Equal(1f / GraphicAtlas.PageSize, secondRegion.U0 - firstRegion.U1);
            Assert.Equal(0, atlas.Pages[0].Pixels[(0 * GraphicAtlas.PageSize + 2) * 4 + 3]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
