using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicIndexLoaderTests
{
    [Fact]
    public void Legacy_minimap_bin_color_wins_over_the_png_average()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-minimap-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var image = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var canvas = Graphics.FromImage(image)) canvas.Clear(Color.FromArgb(255, 10, 20, 30));
                image.Save(Path.Combine(root, "1.png"), ImageFormat.Png);
            }

            var graphics = Enumerable.Range(0, 4).Select(_ => new GraphicData()).ToArray();
            graphics[1] = Graphic(legacyColor: 0x0A0B0C);
            graphics[2] = Graphic(legacyColor: 0);
            graphics[3] = new GraphicData
            {
                Active = true,
                Frames = new[] { 2 },
                PixelWidth = 32,
                PixelHeight = 32,
                MiniMapColor = 0
            };

            GraphicIndexLoader.FillMissingMiniMapColorsFromPng(graphics, root);

            Assert.Equal(0x0A0B0C, graphics[1].MiniMapColor);
            Assert.Equal(0x0A141E, graphics[2].MiniMapColor);
            Assert.Equal(0x0A141E, graphics[3].MiniMapColor);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static GraphicData Graphic(int legacyColor) => new()
    {
        Active = true,
        FileNumber = 1,
        SourceX = 0,
        SourceY = 0,
        PixelWidth = 32,
        PixelHeight = 32,
        Frames = new[] { 0 },
        MiniMapColor = legacyColor
    };
}
