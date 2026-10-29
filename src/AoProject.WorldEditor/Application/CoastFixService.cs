using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Port of modGeneral.FixCoasts.</summary>
internal sealed class CoastFixService
{
    private static readonly HashSet<int> CoastGraphics =
    [
        7284, 7290, 7291, 7297, 7300, 7301, 7302, 7303, 7304, 7306, 7308, 7310, 7311,
        7313, 7314, 7315, 7316, 7317, 7319, 7321, 7325, 7326, 7327, 7328, 7332, 7338,
        7339, 7345, 7348, 7349, 7350, 7351, 7352, 7354, 7357, 7358, 7360, 7362, 7363,
        7365, 7366, 7367, 7368, 7369, 7371, 7373, 7375, 7376
    ];

    public static bool IsCoastGraphic(int graphicIndex) => CoastGraphics.Contains(graphicIndex);

    public bool Fix(WorldMap map, int x, int y, int graphicIndex)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
            y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
            x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height) throw new ArgumentOutOfRangeException(nameof(x));
        if (!IsCoastGraphic(graphicIndex)) return false;
        var block = map.Blocks[x, y];
        if (block.Graphics[1].GraphicIndex == 0) return false;
        block.Graphics[1].GraphicIndex = 0;
        map.Info.Changed = true;
        return true;
    }

    public int FixSelection(WorldMap map, TileSelection selection)
    {
        var normalized = selection.Normalize();
        var fixedTiles = 0;
        var minX = Math.Max(normalized.StartX, (int)map.CsmMapSize.XMin);
        var maxX = Math.Min(normalized.EndX, (int)map.CsmMapSize.XMax);
        var minY = Math.Max(normalized.StartY, (int)map.CsmMapSize.YMin);
        var maxY = Math.Min(normalized.EndY, (int)map.CsmMapSize.YMax);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            if (Fix(map, x, y, map.Blocks[x, y].Graphics[0].GraphicIndex)) fixedTiles++;
        return fixedTiles;
    }
}
