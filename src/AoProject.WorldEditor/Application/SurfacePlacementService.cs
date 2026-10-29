using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class SurfacePlacementService
{
    public void PaintAt(WorldMap map, int x, int y, int layer, int graphic, int width, int height, bool blocked)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (layer is < 0 or >= 4 || graphic < 0) throw new ArgumentOutOfRangeException(nameof(layer));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
            y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
            x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height)
            throw new ArgumentOutOfRangeException(nameof(x));

        var block = map.Blocks[x, y];
        block.Graphics[layer].GraphicIndex = graphic + ((y % height) * width) + (x % width);
        block.Blocked = blocked ? (byte)0xF : (byte)0;
        map.Info.Changed = true;
    }

    public int AutoCompleteAt(WorldMap map, int startX, int startY, int layer, int graphic, int width, int height, bool blocked)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (layer is < 0 or >= 4 || graphic < 0) throw new ArgumentOutOfRangeException(nameof(layer));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        var placed = 0;
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var x = startX + column;
            var y = startY + row;
            if (x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
                y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
                x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height)
                continue;

            map.Blocks[x, y].Graphics[layer].GraphicIndex = graphic + row * width + column;
            map.Blocks[x, y].Blocked = blocked ? (byte)0xF : (byte)0;
            placed++;
        }

        if (placed > 0) map.Info.Changed = true;
        return placed;
    }

    public int Place(WorldMap map, SurfaceData surface, int startX, int startY, bool mosaic = false)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(surface);
        if (surface.Layer is < 1 or > 4 || surface.Width == 0 || surface.Height == 0)
            throw new InvalidDataException("Superficie invalida.");
        var placed = 0;
        for (var y = 0; y < surface.Height; y++)
        for (var x = 0; x < surface.Width; x++)
        {
            var targetX = startX + x; var targetY = startY + y;
            if (targetX < map.CsmMapSize.XMin || targetX > map.CsmMapSize.XMax ||
                targetY < map.CsmMapSize.YMin || targetY > map.CsmMapSize.YMax) continue;
            map.Blocks[targetX, targetY].Graphics[surface.Layer - 1].GraphicIndex = mosaic
                ? surface.GraphicIndex + ((targetY % surface.Height) * surface.Width) + (targetX % surface.Width)
                : surface.GraphicIndex;
            if (surface.Blocked) map.Blocks[targetX, targetY].Blocked = 0xF;
            placed++;
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }
}
