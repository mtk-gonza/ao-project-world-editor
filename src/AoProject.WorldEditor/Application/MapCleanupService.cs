using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Non-destructive cleanup commands from modEdicion.bas.</summary>
internal sealed class MapCleanupService
{
    public int RemoveNpcs(WorldSector map, bool hostileOnly = false, IReadOnlySet<int>? hostileIds = null)
    {
        var count = 0;
        ForEach(map, block =>
        {
            if (block.NpcIndex == 0 || (hostileOnly && (hostileIds is null || !hostileIds.Contains(block.NpcIndex)))) return;
            block.NpcIndex = 0;
            block.CharacterIndex = 0;
            count++;
        });
        Mark(map, count > 0);
        return count;
    }

    public int RemoveObjects(WorldSector map)
    {
        var count = 0;
        ForEach(map, block =>
        {
            if (block.Object.Index == 0) return;
            if (block.Graphics[2].GraphicIndex == block.ObjectGraphic.GraphicIndex)
                block.Graphics[2].GraphicIndex = 0;
            block.Object.Index = 0;
            block.Object.Amount = 0;
            count++;
        });
        Mark(map, count > 0);
        return count;
    }

    public int RemoveTriggers(WorldSector map)
    {
        var count = 0;
        ForEach(map, block => { if (block.Trigger != 0) { block.Trigger = 0; count++; } });
        Mark(map, count > 0);
        return count;
    }

    public int RemoveExits(WorldSector map)
    {
        var count = 0;
        ForEach(map, block =>
        {
            if (block.TileExit.Map == 0) return;
            block.TileExit.Map = 0; block.TileExit.X = 0; block.TileExit.Y = 0; count++;
        });
        Mark(map, count > 0);
        return count;
    }

    public void ClearLayer(WorldSector map, int layer)
    {
        if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer));
        ForEach(map, block => block.Graphics[layer].GraphicIndex = 0);
        map.Info.Changed = true;
    }

    public int ClearMap(WorldSector map, int baseGraphic = 1)
    {
        ArgumentNullException.ThrowIfNull(map);
        var clearMinX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var clearMaxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var clearMinY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var clearMaxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = clearMinX; x <= clearMaxX; x++)
        for (var y = clearMinY; y <= clearMaxY; y++)
        {
            var block = map.Blocks[x, y];
            block.Graphics[0].GraphicIndex = baseGraphic;
            for (var layer = 1; layer < 4; layer++) block.Graphics[layer].GraphicIndex = 0;
            block.Blocked = 0; block.CharacterIndex = 0; block.NpcIndex = 0;
            block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default;
            block.TileExit.Map = 0; block.TileExit.X = 0; block.TileExit.Y = 0;
            block.Light.Range = 0; block.Light.Color = 0; block.ParticleIndex = 0; block.ParticleGroup = 0; block.Trigger = 0;
        }
        map.Info.Changed = true;
        return Math.Max(0, clearMaxX - clearMinX + 1) * Math.Max(0, clearMaxY - clearMinY + 1);
    }

    public int RemoveBorders(WorldSector map, int minX = 13, int maxX = 88, int minY = 10, int maxY = 91)
    {
        ArgumentNullException.ThrowIfNull(map);
        var count = 0;
        var borderMinX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var borderMaxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var borderMinY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var borderMaxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = borderMinX; x <= borderMaxX; x++)
        for (var y = borderMinY; y <= borderMaxY; y++)
        {
            if (x >= minX && x <= maxX && y >= minY && y <= maxY) continue;
            var block = map.Blocks[x, y]; block.Graphics[0].GraphicIndex = 1; block.Blocked = 0;
            block.NpcIndex = 0; block.CharacterIndex = 0; block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default;
            block.TileExit.Map = 0; block.TileExit.X = 0; block.TileExit.Y = 0; block.Trigger = 0; count++;
        }
        if (count > 0) map.Info.Changed = true;
        return count;
    }

    private static void ForEach(WorldSector map, Action<SectorTile> action)
    {
        ArgumentNullException.ThrowIfNull(map);
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++) action(map.Blocks[x, y]);
    }

    private static void Mark(WorldSector map, bool changed) { if (changed) map.Info.Changed = true; }
}
