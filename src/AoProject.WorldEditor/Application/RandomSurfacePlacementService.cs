using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct RandomSurfaceOptions(int Count, int Layer, int GraphicIndex, bool Mosaic, int MosaicWidth, int MosaicHeight, bool CompleteMosaic, bool Blocked, int Seed = 0);

internal sealed class RandomSurfacePlacementService
{
    public int Place(WorldMap map, RandomSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (options.Count <= 0) return 0;
        if (options.Layer is < 0 or >= 4 || options.GraphicIndex < 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Mosaic && (options.MosaicWidth <= 0 || options.MosaicHeight <= 0)) throw new ArgumentOutOfRangeException(nameof(options));
        var random = options.Seed == 0 ? Random.Shared : new Random(options.Seed);
        var placed = 0;
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        if (minX > maxX || minY > maxY) return 0;
        for (var i = 0; i < options.Count; i++)
        {
            var x = random.Next(minX, maxX + 1); var y = random.Next(minY, maxY + 1);
            if (!options.Mosaic || !options.CompleteMosaic)
            {
                Set(map, x, y, options.Layer, options.GraphicIndex + (options.Mosaic ? (y % options.MosaicHeight) * options.MosaicWidth + x % options.MosaicWidth : 0), options.Blocked);
                placed++; continue;
            }
            for (var dy = 0; dy < options.MosaicHeight; dy++)
            for (var dx = 0; dx < options.MosaicWidth; dx++)
            {
                var tx = x + dx; var ty = y + dy;
                if (tx is < 1 or > WorldMap.Width || ty is < 1 or > WorldMap.Height) continue;
                Set(map, tx, ty, options.Layer, options.GraphicIndex + dy * options.MosaicWidth + dx, options.Blocked);
                placed++;
            }
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }

    private static void Set(WorldMap map, int x, int y, int layer, int graphic, bool blocked)
    {
        map.Blocks[x, y].Graphics[layer].GraphicIndex = graphic;
        map.Blocks[x, y].Blocked = blocked ? (byte)0xF : (byte)0;
    }
}
