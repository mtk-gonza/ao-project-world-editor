using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class SurfaceWholeMapService
{
    public int Place(WorldSector map, int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
        if (mosaic && (mosaicWidth <= 0 || mosaicHeight <= 0)) throw new ArgumentOutOfRangeException(nameof(mosaicWidth));
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            map.Blocks[x, y].Graphics[layer].GraphicIndex = mosaic ? graphic + (y % mosaicHeight) * mosaicWidth + x % mosaicWidth : graphic;
        map.Info.Changed = true;
        return Math.Max(0, maxX - minX + 1) * Math.Max(0, maxY - minY + 1);
    }
}
