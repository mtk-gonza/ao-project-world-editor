using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal enum MapReplacementKind { Graphic, Object, Npc, Trigger }

internal sealed class MapReplacementService
{
    public int ReplaceGraphic(WorldSector map, int fromLayer, int toLayer, int fromGraphic, int toGraphic)
    {
        ValidateLayer(fromLayer); ValidateLayer(toLayer);
        var count = 0;
        ForEach(map, block =>
        {
            if (block.Graphics[fromLayer].GraphicIndex != fromGraphic) return;
            block.Graphics[fromLayer].GraphicIndex = 0;
            block.Graphics[toLayer].GraphicIndex = toGraphic;
            count++;
        });
        Mark(map, count); return count;
    }

    public int ReplaceObject(WorldSector map, IReadOnlyList<ObjectData> objects, short from, short to)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ValidateIndex(from, objects.Count); ValidateIndex(to, objects.Count);
        var count = 0;
        ForEach(map, block =>
        {
            if (block.Object.Index != from) return;
            block.Object.Index = to;
            block.ObjectGraphic = to == 0 ? default : new GraphicAnimation { GraphicIndex = objects[to].GraphicIndex };
            count++;
        });
        Mark(map, count); return count;
    }

    public int ReplaceNpc(WorldSector map, IReadOnlyList<NpcData> npcs, short from, short to)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ValidateIndex(from, npcs.Count); ValidateIndex(to, npcs.Count);
        var count = 0;
        ForEach(map, block => { if (block.NpcIndex == from) { block.NpcIndex = to; block.CharacterIndex = 0; count++; } });
        Mark(map, count); return count;
    }

    public int ReplaceTrigger(WorldSector map, short from, short to)
    {
        var count = 0;
        ForEach(map, block => { if (block.Trigger == from) { block.Trigger = to; count++; } });
        Mark(map, count); return count;
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

    private static void ValidateLayer(int layer) { if (layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(layer)); }
    private static void ValidateIndex(short index, int count) { if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index)); }
    private static void Mark(WorldSector map, int count) { if (count > 0) map.Info.Changed = true; }
}
