using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Bulk operations corresponding to the destructive commands in modEdicion.bas.</summary>
internal sealed class MapBulkEditingService
{
    public int ReplaceGraphic(WorldMap map, int layer, int oldGraphic, int newGraphic)
    {
        ValidateLayer(layer);
        var changed = 0;
        ForEachBlock(map, block =>
        {
            if (block.Graphics[layer].GraphicIndex != oldGraphic) return;
            block.Graphics[layer].GraphicIndex = newGraphic;
            changed++;
        });
        MarkChanged(map, changed > 0);
        return changed;
    }

    public void ClearLayer(WorldMap map, int layer)
    {
        ValidateLayer(layer);
        var emptyGraphic = layer == 0 ? 1 : 0;
        ForEachBlock(map, block => block.Graphics[layer].GraphicIndex = emptyGraphic);
        MarkChanged(map, true);
    }

    public void SetBlocked(WorldMap map, byte value)
    {
        ForEachBlock(map, block => block.Blocked = value);
        MarkChanged(map, true);
    }

    public void ClearTriggers(WorldMap map)
    {
        ForEachBlock(map, block => block.Trigger = 0);
        MarkChanged(map, true);
    }

    public void ClearObjects(WorldMap map)
    {
        ForEachBlock(map, block => { block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default; });
        MarkChanged(map, true);
    }

    public void ClearExits(WorldMap map)
    {
        ForEachBlock(map, block => { block.TileExit.Map = 0; block.TileExit.X = 0; block.TileExit.Y = 0; });
        MarkChanged(map, true);
    }

    private static void ForEachBlock(WorldMap map, Action<MapBlock> action)
    {
        ArgumentNullException.ThrowIfNull(map);
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            action(map.Blocks[x, y]);
    }

    private static void ValidateLayer(int layer)
    {
        if (layer is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(layer));
    }

    private static void MarkChanged(WorldMap map, bool changed)
    {
        if (changed) map.Info.Changed = true;
    }
}
