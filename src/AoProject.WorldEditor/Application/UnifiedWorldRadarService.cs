using System.Drawing;
using AoProject.WorldEditor.Domain;

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
        int height = WorldRadarService.DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(map);
        width = Math.Clamp(width, 16, 2000);
        height = Math.Clamp(height, 16, 2000);

        var sourceWidth = Math.Min(width, map.Width);
        var sourceHeight = Math.Min(height, map.Height);
        var sourceX = Math.Clamp(centerX - sourceWidth / 2, 0, map.Width - sourceWidth);
        var sourceY = Math.Clamp(centerY - sourceHeight / 2, 0, map.Height - sourceHeight);
        var image = new Bitmap(width, height);

        // One source tile per radar pixel. This is intentionally nearest-neighbour:
        // the caller can scale the resulting bitmap for display without changing
        // the global coordinate represented by its marker.
        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            var sx = sourceX + Math.Min(sourceWidth - 1, px * sourceWidth / width);
            var sy = sourceY + Math.Min(sourceHeight - 1, py * sourceHeight / height);
            image.SetPixel(px, py, TileColor(map, map.Offset(sx, sy)));
        }
        return (image, centerX - sourceX, centerY - sourceY);
    }

    public static Color TileColor(UnifiedWorldMap map, int offset)
    {
        if (map.NpcIndex[offset] > 0) return Color.Yellow;
        if (map.ParticleIndex[offset] > 0) return Color.White;
        if (map.ObjectIndex[offset] > 0) return Color.FromArgb(255, 220, 180, 40);
        if (map.Blocked[offset] != 0) return Color.FromArgb(255, 150, 45, 45);

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
            return Color.FromArgb(255, red / samples, green / samples, blue / samples);

        // A compiled map may not have minimap palette data. Keep the radar
        // useful and deterministic rather than hiding the tile as black.
        var graphic = map.GraphicIndices[map.GraphicOffset(offset, 0)];
        if (graphic <= 0) return Color.Black;
        var hash = unchecked(graphic * 1103515245 + 12345);
        return Color.FromArgb(255, 35 + ((hash >> 16) & 0x7F), 35 + ((hash >> 8) & 0x7F), 35 + (hash & 0x7F));
    }
}
