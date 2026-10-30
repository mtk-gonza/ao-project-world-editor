using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct TreePlacementOptions(int Count, short[] ObjectIndices, byte? BlockedValue = null, int Seed = 0);

/// <summary>Port of FrmArboles.Insertar_Click with bounded deterministic attempts.</summary>
internal sealed class TreePlacementService
{
    public int Place(WorldSector map, IReadOnlyList<ObjectData> objects, TreePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(objects);
        if (options.BlockedValue is { } overrideMask && !BlockedMask.IsValid(overrideMask))
            throw new ArgumentOutOfRangeException(nameof(options), "La máscara debe usar sólo los bits 1,2,4,8.");
        if (options.Count <= 0 || options.ObjectIndices.Length == 0) return 0;
        foreach (var index in options.ObjectIndices)
            if (index <= 0 || index >= objects.Count || objects[index] is null) throw new ArgumentOutOfRangeException(nameof(options));
        var random = options.Seed == 0 ? Random.Shared : new Random(options.Seed);
        var placed = 0;
        foreach (var objectIndex in options.ObjectIndices)
        for (var i = 0; i < options.Count; i++)
        {
            var accepted = false;
            for (var attempt = 0; attempt < 10_000 && !accepted; attempt++)
            {
                var x = random.Next(14, 87); var y = random.Next(12, 90);
                if (map.Blocks[x, y].Graphics[1].GraphicIndex is >= 1505 and <= 1520 || HasTreeNearby(map, objects, x, y)) continue;
                var block = map.Blocks[x, y];
                block.Blocked = options.BlockedValue ?? objects[objectIndex].DefaultBlockedMask;
                block.ObjectGraphic = new GraphicAnimation { GraphicIndex = objects[objectIndex].GraphicIndex };
                block.Object.Index = objectIndex; block.Object.Amount = 1;
                block.Graphics[2] = block.ObjectGraphic;
                placed++; accepted = true;
            }
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }

    /// <summary>Equivalent to VB6 “Bloquear árboles, carteles, foros y yacimientos”.</summary>
    public int BlockConfiguredObjects(WorldSector map, IReadOnlyList<ObjectData> objects)
    {
        var changed = 0;
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var block = map.Blocks[x, y];
            var index = block.Object.Index;
            if (index <= 0 || index >= objects.Count || objects[index] is null) continue;
            var mask = objects[index].DefaultBlockedMask;
            if (!BlockedMask.IsValid(mask)) throw new InvalidDataException($"DefaultBlockedMask inválida para el objeto {index}.");
            if (mask == 0 || block.Blocked == mask) continue;
            block.Blocked = mask; changed++;
        }
        if (changed > 0) map.Info.Changed = true;
        return changed;
    }

    private static bool HasTreeNearby(WorldSector map, IReadOnlyList<ObjectData> objects, int x, int y)
    {
        for (var aroundX = Math.Max(1, x - 8); aroundX <= Math.Min(WorldSector.Width, x + 8); aroundX++)
        for (var aroundY = Math.Max(1, y - 8); aroundY <= Math.Min(WorldSector.Height, y + 8); aroundY++)
        {
            var index = map.Blocks[aroundX, aroundY].Object.Index;
            if (index > 0 && index < objects.Count && objects[index] is { ObjectType: 4 }) return true;
        }
        return false;
    }
}
