using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapMiniMapBmpTests
{
    [Fact]
    public void Writes_the_16bpp_format_the_client_loads()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-minimap-bmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var map = new WorldMap();
            map.Blocks[3, 4].Graphics[0].GraphicIndex = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { MiniMapColor = 0x000000FF }
            };
            var path = Path.Combine(root, "mapa1.bmp");

            new MapMiniMapService().SaveBmp(map, graphics, MapMiniMapOptions.Vb6Default, null, path);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(20056, bytes.Length);
            Assert.Equal((byte)'B', bytes[0]);
            Assert.Equal((byte)'M', bytes[1]);
            Assert.Equal(20056, BitConverter.ToInt32(bytes, 2));
            Assert.Equal(54, BitConverter.ToInt32(bytes, 10));
            Assert.Equal(40, BitConverter.ToInt32(bytes, 14));
            Assert.Equal(100, BitConverter.ToInt32(bytes, 18));
            Assert.Equal(100, BitConverter.ToInt32(bytes, 22));
            Assert.Equal(1, BitConverter.ToInt16(bytes, 26));
            Assert.Equal(16, BitConverter.ToInt16(bytes, 28));
            Assert.Equal(0, BitConverter.ToInt32(bytes, 30));
            Assert.Equal(20002, BitConverter.ToInt32(bytes, 34));
            Assert.Equal(2834, BitConverter.ToInt32(bytes, 38));
            Assert.Equal(2834, BitConverter.ToInt32(bytes, 42));

            using var reloaded = new Bitmap(path);
            Assert.Equal(100, reloaded.Width);
            Assert.Equal(100, reloaded.Height);
            var pixel = reloaded.GetPixel(2, 3);
            Assert.InRange(pixel.B, 0xF8, 0xFF);
            Assert.InRange(pixel.R, 0, 0x07);
            Assert.True(WorldOutputAuditService.IsClientMinimap(path));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Rows_are_written_bottom_up()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-minimap-bmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var image = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
            using (var canvas = Graphics.FromImage(image)) canvas.Clear(Color.Black);
            image.SetPixel(0, 0, Color.White);
            var path = Path.Combine(root, "rows.bmp");

            MapMiniMapService.WriteBmp(image, path);

            using var reloaded = new Bitmap(path);
            Assert.True(reloaded.GetPixel(0, 0).R > 0xF0);
            Assert.True(reloaded.GetPixel(99, 99).R < 0x08);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Rejects_a_png_as_a_client_minimap()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-minimap-bmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.png");
            using (var image = new Bitmap(100, 100)) image.Save(path, ImageFormat.Png);
            Assert.False(WorldOutputAuditService.IsClientMinimap(path));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
