using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class SurfaceBorderPlacementService
{
    public int Place(WorldSector map, int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight, bool blocked,
        int minX = 13, int maxX = 88, int minY = 10, int maxY = 91)
    {
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
        if (mosaic && (mosaicWidth <= 0 || mosaicHeight <= 0)) throw new ArgumentOutOfRangeException(nameof(mosaicWidth));
        var count = 0;
        var mapMinX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var mapMaxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var mapMinY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var mapMaxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = mapMinX; x <= mapMaxX; x++)
        for (var y = mapMinY; y <= mapMaxY; y++)
        {
            if (x >= minX && x <= maxX && y >= minY && y <= maxY) continue;
            var block = map.Blocks[x, y];
            block.Graphics[layer].GraphicIndex = mosaic ? graphic + (y % mosaicHeight) * mosaicWidth + x % mosaicWidth : graphic;
            block.Blocked = blocked ? (byte)0xF : (byte)0;
            block.NpcIndex = 0; block.CharacterIndex = 0;
            block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default;
            block.TileExit.SectorId = 0; block.TileExit.X = 0; block.TileExit.Y = 0;
            count++;
        }
        if (count > 0) map.Info.Changed = true;
        return count;
    }
}
