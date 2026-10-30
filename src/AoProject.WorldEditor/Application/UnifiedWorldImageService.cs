using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>Exports the resolved global tile grid as a PNG, independently of legacy Mapas.</summary>
internal sealed class UnifiedWorldImageService
{
    private const int RadarScale = 4;
    public IReadOnlyList<string> Save(UnifiedWorldBundle bundle, WorldEditorResourceCatalog resources,
        string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        foreach (var floor in bundle.Floors.Values)
        {
            using var source = Create(floor, resources.Graphics);
            using var image = new Bitmap(source.Width * RadarScale, source.Height * RadarScale,
                PixelFormat.Format32bppArgb);
            using (var canvas = Graphics.FromImage(image))
            {
                canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                canvas.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                canvas.DrawImage(source, new Rectangle(0, 0, image.Width, image.Height));
            }
            var path = Path.Combine(directory, FloorName(floor.FloorZ));
            image.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        return files;
    }

    private static Bitmap Create(UnifiedWorldMap map, IReadOnlyList<GraphicData> graphics)
    {
        var image = new Bitmap(map.Width, map.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var offset = map.Offset(x, y);
            var color = Color.Black;
            for (var layer = 0; layer < 4; layer++)
            {
                var graphic = map.GraphicIndices[map.GraphicOffset(offset, layer)];
                if (graphic > 0 && graphic < graphics.Count && graphics[graphic].MiniMapColor != 0)
                {
                    // minimap.bin stores 0x00RRGGBB. Keep the same channel
                    // order used by MapMiniMapService.FromColorRef; reversing
                    // it makes grass/water appear as red or brown in the global
                    // radar even though the editor preview is correct.
                    var raw = unchecked((uint)graphics[graphic].MiniMapColor);
                    color = Color.FromArgb(255,
                        (int)((raw >> 16) & 0xFF),
                        (int)((raw >> 8) & 0xFF),
                        (int)(raw & 0xFF));
                }
            }
            image.SetPixel(x, y, color);
        }
        return image;
    }

    private static string FloorName(int z) => $"floor-{z:+#;-#;0}.png";
}
