using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct TreePlacementOptions(int Count, short[] ObjectIndices, byte BlockedValue = 1, int Seed = 0);

/// <summary>Port of FrmArboles.Insertar_Click with bounded deterministic attempts.</summary>
internal sealed class TreePlacementService
{
    public int Place(WorldMap map, IReadOnlyList<ObjectData> objects, TreePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(objects);
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
                block.Blocked = options.BlockedValue;
                block.ObjectGraphic = new GraphicAnimation { GraphicIndex = objects[objectIndex].GraphicIndex };
                block.Object.Index = objectIndex; block.Object.Amount = 1;
                block.Graphics[2] = block.ObjectGraphic;
                placed++; accepted = true;
            }
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }

    private static bool HasTreeNearby(WorldMap map, IReadOnlyList<ObjectData> objects, int x, int y)
    {
        for (var aroundX = Math.Max(1, x - 8); aroundX <= Math.Min(WorldMap.Width, x + 8); aroundX++)
        for (var aroundY = Math.Max(1, y - 8); aroundY <= Math.Min(WorldMap.Height, y + 8); aroundY++)
        {
            var index = map.Blocks[aroundX, aroundY].Object.Index;
            if (index > 0 && index < objects.Count && objects[index] is { ObjectType: 4 }) return true;
        }
        return false;
    }
}
