using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MapOptimizationOptions(
    bool RemoveBlockedTransfers = true,
    bool RemoveBlockedTriggers = false,
    bool RemoveTransferTriggers = false,
    bool RemoveOuterBorderEntities = false,
    bool MapSpecialObjects = false,
    bool BlockSpecialObjects = true);

internal readonly record struct MapOptimizationResult(
    int RemovedNpcs,
    int RemovedObjects,
    int RemovedExits,
    int RemovedTriggers,
    int MappedObjects,
    int BlockedObjects);

/// <summary>Port of frmOptimizar.Optimizar using the reference ObjType values.</summary>
internal sealed class MapOptimizationService
{
    private static readonly HashSet<short> SpecialObjectTypes = [4, 8, 10, 22];

    public MapOptimizationResult Optimize(
        WorldSector map,
        IReadOnlyList<ObjectData> objects,
        MapOptimizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(objects);
        var removedNpcs = 0; var removedObjects = 0; var removedExits = 0;
        var removedTriggers = 0; var mapped = 0; var blocked = 0;

        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var block = map.Blocks[x, y];
            if (options.RemoveOuterBorderEntities && (x < 12 || x > 88 || y < 10 || y > 91))
            {
                if (block.NpcIndex != 0) { block.NpcIndex = 0; block.CharacterIndex = 0; removedNpcs++; }
                if (block.Object.Index != 0 || block.Object.Amount != 0 || block.ObjectGraphic.GraphicIndex != 0)
                { block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default; removedObjects++; }
                if (block.TileExit.SectorId != 0) { ClearExit(block); removedExits++; }
                if (block.Trigger != 0) { block.Trigger = 0; removedTriggers++; }
            }

            if (block.Blocked == 0xF)
            {
                if (block.TileExit.SectorId != 0 && options.RemoveBlockedTransfers)
                { ClearExit(block); removedExits++; }
                else if (block.Trigger != 0 && options.RemoveBlockedTriggers)
                { block.Trigger = 0; removedTriggers++; }
            }

            if (block.TileExit.SectorId != 0 && options.RemoveTransferTriggers && block.Trigger != 0)
            { block.Trigger = 0; removedTriggers++; }

            if (block.Object.Index > 0 && block.Object.Index < objects.Count)
            {
                var data = objects[block.Object.Index];
                if (data is not null && SpecialObjectTypes.Contains(data.ObjectType))
                {
                    if (options.MapSpecialObjects && block.Graphics[2].GraphicIndex != block.ObjectGraphic.GraphicIndex)
                    { block.Graphics[2] = block.ObjectGraphic; mapped++; }
                    if (options.BlockSpecialObjects && block.Blocked == 0)
                    { block.Blocked = 1; blocked++; }
                }
            }
        }

        if (removedNpcs + removedObjects + removedExits + removedTriggers + mapped + blocked > 0)
            map.Info.Changed = true;
        return new(removedNpcs, removedObjects, removedExits, removedTriggers, mapped, blocked);
    }

    private static void ClearExit(SectorTile block)
    {
        block.TileExit.SectorId = 0; block.TileExit.X = 0; block.TileExit.Y = 0;
    }
}
