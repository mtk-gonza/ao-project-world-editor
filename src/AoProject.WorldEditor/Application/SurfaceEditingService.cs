using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Surface and border operations from modEdicion.bas.</summary>
internal sealed class SurfaceEditingService
{
    public void Fill(WorldSector map, int layer, int graphic, int mosaicWidth = 0, int mosaicHeight = 0)
    {
        ValidateLayer(layer);
        ForEach(map, (x, y, block) => block.Graphics[layer].GraphicIndex = Mosaic(graphic, x, y, mosaicWidth, mosaicHeight));
        map.Info.Changed = true;
    }

    public void FillBorders(WorldSector map, int layer, int graphic, int minX = 13, int maxX = 88, int minY = 10, int maxY = 91)
    {
        ValidateLayer(layer);
        minX = Math.Max(minX, map.CsmSectorSize.XMin); maxX = Math.Min(maxX, map.CsmSectorSize.XMax);
        minY = Math.Max(minY, map.CsmSectorSize.YMin); maxY = Math.Min(maxY, map.CsmSectorSize.YMax);
        ForEach(map, (x, y, block) =>
        {
            if (x < minX || x > maxX || y < minY || y > maxY)
                block.Graphics[layer].GraphicIndex = graphic;
        });
        map.Info.Changed = true;
    }

    public void LockBorders(WorldSector map)
    {
        var domainMinX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var domainMaxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var domainMinY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var domainMaxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        var innerMinX = Math.Clamp(13, domainMinX, domainMaxX);
        var innerMaxX = Math.Clamp(88, domainMinX, domainMaxX);
        var innerMinY = Math.Clamp(10, domainMinY, domainMaxY);
        var innerMaxY = Math.Clamp(91, domainMinY, domainMaxY);
        ForEach(map, (x, y, block) =>
        {
            if (innerMinX > innerMaxX || innerMinY > innerMaxY ||
                x < innerMinX || x > innerMaxX || y < innerMinY || y > innerMaxY)
                block.Blocked = 0xF;
        });
        foreach (var point in new[] { (innerMinX, innerMinY), (innerMinX, innerMaxY), (innerMaxX, innerMinY), (innerMaxX, innerMaxY) })
            if (InMapDomain(map, point.Item1, point.Item2) && map.Blocks[point.Item1, point.Item2].TileExit.Map == 0)
                map.Blocks[point.Item1, point.Item2].Blocked = 0xF;
        map.Info.Changed = true;
    }

    private static int Mosaic(int graphic, int x, int y, int width, int height) =>
        width > 0 && height > 0 ? graphic + ((y % height) * width) + (x % width) : graphic;

    private static void ForEach(WorldSector map, Action<int, int, SectorTile> action)
    {
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++) action(x, y, map.Blocks[x, y]);
    }

    private static void ValidateLayer(int layer)
    {
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
    }

    private static bool InMapDomain(WorldSector map, int x, int y) =>
        x >= map.CsmSectorSize.XMin && x <= map.CsmSectorSize.XMax &&
        y >= map.CsmSectorSize.YMin && y <= map.CsmSectorSize.YMax &&
        x is >= 1 and <= WorldSector.Width && y is >= 1 and <= WorldSector.Height;
}
