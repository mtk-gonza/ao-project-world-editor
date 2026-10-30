using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapExportFormatTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".bmp")]
    [InlineData(".jpg")]
    public void Save_writes_the_requested_format(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            var path = Path.Combine(root, "mapa" + extension);
            new MapImageCaptureService().Save(new WorldSector(), [new GraphicData()], root, path);
            Assert.True(File.Exists(path));
            using var image = System.Drawing.Image.FromFile(path);
            Assert.Equal(3200, image.Width); Assert.Equal(3200, image.Height);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
