using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class SurfaceBorderPlacementService
{
    public int Place(WorldMap map, int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight, bool blocked,
        int minX = 13, int maxX = 88, int minY = 10, int maxY = 91)
    {
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
        if (mosaic && (mosaicWidth <= 0 || mosaicHeight <= 0)) throw new ArgumentOutOfRangeException(nameof(mosaicWidth));
        var count = 0;
        var mapMinX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var mapMaxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var mapMinY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var mapMaxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = mapMinX; x <= mapMaxX; x++)
        for (var y = mapMinY; y <= mapMaxY; y++)
        {
            if (x >= minX && x <= maxX && y >= minY && y <= maxY) continue;
            var block = map.Blocks[x, y];
            block.Graphics[layer].GraphicIndex = mosaic ? graphic + (y % mosaicHeight) * mosaicWidth + x % mosaicWidth : graphic;
            block.Blocked = blocked ? (byte)0xF : (byte)0;
            block.NpcIndex = 0; block.CharacterIndex = 0;
            block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default;
            block.TileExit.Map = 0; block.TileExit.X = 0; block.TileExit.Y = 0;
            count++;
        }
        if (count > 0) map.Info.Changed = true;
        return count;
    }
}
