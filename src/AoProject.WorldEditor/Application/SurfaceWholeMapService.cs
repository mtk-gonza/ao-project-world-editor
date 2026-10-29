using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class SurfaceWholeMapService
{
    public int Place(WorldMap map, int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
        if (mosaic && (mosaicWidth <= 0 || mosaicHeight <= 0)) throw new ArgumentOutOfRangeException(nameof(mosaicWidth));
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            map.Blocks[x, y].Graphics[layer].GraphicIndex = mosaic ? graphic + (y % mosaicHeight) * mosaicWidth + x % mosaicWidth : graphic;
        map.Info.Changed = true;
        return Math.Max(0, maxX - minX + 1) * Math.Max(0, maxY - minY + 1);
    }
}
