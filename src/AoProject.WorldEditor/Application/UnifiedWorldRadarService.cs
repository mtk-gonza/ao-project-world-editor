using System.Drawing;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Generates the radar on demand from the single global map. No legacy minimap
/// files, map ids, chunks, or stitched world bitmap are involved.
/// </summary>
internal sealed class UnifiedWorldRadarService
{
    public (Bitmap Image, int MarkerX, int MarkerY) CreateCentered(
        UnifiedWorldMap map,
        int centerX,
        int centerY,
        int width = WorldRadarService.DefaultSize,
        int height = WorldRadarService.DefaultSize,
        IReadOnlyList<GraphicData>? graphics = null,
        GraphicAverageColorService? averages = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        width = Math.Clamp(width, 16, 2000);
        height = Math.Clamp(height, 16, 2000);

        var sourceWidth = Math.Min(width, map.Width);
        var sourceHeight = Math.Min(height, map.Height);
        var sourceX = Math.Clamp(centerX - sourceWidth / 2, 0, map.Width - sourceWidth);
        var sourceY = Math.Clamp(centerY - sourceHeight / 2, 0, map.Height - sourceHeight);
        const int sourceScale = 4;
        using var sourceImage = new Bitmap(width * sourceScale, height * sourceScale);

        // One source tile per radar pixel. This is intentionally nearest-neighbour:
        // the caller can scale the resulting bitmap for display without changing
        // the global coordinate represented by its marker.
        using (var sourceCanvas = Graphics.FromImage(sourceImage))
        {
            for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var sx = sourceX + Math.Min(sourceWidth - 1, px * sourceWidth / width);
                var sy = sourceY + Math.Min(sourceHeight - 1, py * sourceHeight / height);
                var color = SmoothedTileColor(map, sx, sy, graphics, averages);
                using var brush = new SolidBrush(color);
                sourceCanvas.FillRectangle(brush, px * sourceScale, py * sourceScale, sourceScale, sourceScale);
            }
        }
        var image = new Bitmap(width, height);
        using (var canvas = Graphics.FromImage(image))
        {
            canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            canvas.DrawImage(sourceImage, new Rectangle(0, 0, width, height));
        }
        // Keep the center marker exact after the smoothing pass.
        if (centerX >= sourceX && centerX < sourceX + sourceWidth &&
            centerY >= sourceY && centerY < sourceY + sourceHeight)
            image.SetPixel(centerX - sourceX, centerY - sourceY, TileColor(map,
                map.Offset(centerX, centerY), graphics, averages));
        return (image, centerX - sourceX, centerY - sourceY);
    }

    public static Color TileColor(UnifiedWorldMap map, int offset)
        => TileColor(map, offset, null, null);

    public static Color TileColor(UnifiedWorldMap map, int offset,
        IReadOnlyList<GraphicData>? graphics,
        GraphicAverageColorService? averages = null)
    {
        if (map.NpcIndex[offset] > 0) return Color.Yellow;
        if (map.ParticleIndex[offset] > 0) return Color.White;

        var hasBaseColor = false;
        var baseColor = Color.Black;
        if (graphics is not null)
        {
            for (var layer = 0; layer < 4; layer++)
            {
                var graphicIndex = map.GraphicIndices[map.GraphicOffset(offset, layer)];
                if (graphicIndex <= 0 || graphicIndex >= graphics.Count) continue;
                var average = averages?.Get(graphics[graphicIndex]) ?? 0;
                if (average != 0)
                {
                    baseColor = Color.FromArgb(average);
                    hasBaseColor = true;
                }
                else
                {
                    var raw = unchecked((uint)graphics[graphicIndex].MiniMapColor);
                    if (raw == 0) continue;
                    baseColor = Color.FromArgb(255, (int)((raw >> 16) & 0xFF),
                        (int)((raw >> 8) & 0xFF), (int)(raw & 0xFF));
                    hasBaseColor = true;
                }
            }
        }

        if (!hasBaseColor)
        {
            var red = 0;
            var green = 0;
            var blue = 0;
            var samples = 0;
            for (var corner = 0; corner < 4; corner++)
            {
                var raw = unchecked((uint)map.Colors[map.CornerOffset(offset, corner)]);
                if (raw == 0) continue;
                red += (int)((raw >> 16) & 0xFF);
                green += (int)((raw >> 8) & 0xFF);
                blue += (int)(raw & 0xFF);
                samples++;
            }
            if (samples > 0)
                baseColor = Color.FromArgb(255, red / samples, green / samples, blue / samples);
        }

        if (map.ObjectIndex[offset] > 0)
            return Blend(baseColor, Color.FromArgb(255, 220, 180, 40), .72f);
        if (map.Blocked[offset] != 0)
            return Blend(baseColor, Color.FromArgb(255, 150, 45, 45), .35f);

        // A compiled map may not have minimap palette data. Keep the radar
        // useful and deterministic rather than hiding the tile as black.
        if (hasBaseColor || baseColor != Color.Black) return baseColor;
        var graphic = map.GraphicIndices[map.GraphicOffset(offset, 0)];
        if (graphic <= 0) return Color.Black;
        var hash = unchecked(graphic * 1103515245 + 12345);
        return Color.FromArgb(255, 35 + ((hash >> 16) & 0x7F), 35 + ((hash >> 8) & 0x7F), 35 + (hash & 0x7F));
    }

    private static Color Blend(Color baseColor, Color overlay, float amount)
    {
        var inverse = 1f - amount;
        return Color.FromArgb(255,
            (int)(baseColor.R * inverse + overlay.R * amount),
            (int)(baseColor.G * inverse + overlay.G * amount),
            (int)(baseColor.B * inverse + overlay.B * amount));
    }

    private static Color SmoothedTileColor(UnifiedWorldMap map, int centerX, int centerY,
        IReadOnlyList<GraphicData>? graphics, GraphicAverageColorService? averages)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        var samples = 0;
        for (var y = Math.Max(0, centerY - 1); y <= Math.Min(map.Height - 1, centerY + 1); y++)
        for (var x = Math.Max(0, centerX - 1); x <= Math.Min(map.Width - 1, centerX + 1); x++)
        {
            var color = TileColor(map, map.Offset(x, y), graphics, averages);
            red += color.R;
            green += color.G;
            blue += color.B;
            samples++;
        }
        return Color.FromArgb(255, red / Math.Max(1, samples), green / Math.Max(1, samples),
            blue / Math.Max(1, samples));
    }
}
