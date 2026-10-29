using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct RandomNpcPlacementOptions(int Count, short NpcIndex, int Seed = 0);
internal readonly record struct RandomObjectPlacementOptions(int Count, short ObjectIndex, bool Blocked, int Seed = 0);

internal sealed class RandomEntityPlacementService
{
    public int PlaceNpcs(WorldMap map, IReadOnlyList<NpcData> npcs, RandomNpcPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(npcs);
        if (options.Count <= 0) return 0;
        if (options.NpcIndex <= 0 || options.NpcIndex >= npcs.Count) throw new ArgumentOutOfRangeException(nameof(options));
        var random = options.Seed == 0 ? Random.Shared : new Random(options.Seed); var placed = 0;
        for (var i = 0; i < options.Count; i++)
        {
            var x = random.Next(15, 88); var y = random.Next(15, 88); var block = map.Blocks[x, y];
            if ((block.Blocked & 0xF) == 0xF || block.NpcIndex == options.NpcIndex) continue;
            block.NpcIndex = options.NpcIndex; block.CharacterIndex = 0; placed++;
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }

    public int PlaceObjects(WorldMap map, IReadOnlyList<ObjectData> objects, RandomObjectPlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(objects);
        if (options.Count <= 0) return 0;
        if (options.ObjectIndex <= 0 || options.ObjectIndex >= objects.Count) throw new ArgumentOutOfRangeException(nameof(options));
        var random = options.Seed == 0 ? Random.Shared : new Random(options.Seed); var placed = 0;
        for (var i = 0; i < options.Count; i++)
        {
            var x = random.Next(10, 92); var y = random.Next(8, 94); var block = map.Blocks[x, y];
            if (block.Graphics[1].GraphicIndex is >= 1505 and <= 1520) continue;
            block.Blocked = options.Blocked ? (byte)0xF : (byte)0;
            block.Object.Index = options.ObjectIndex; block.Object.Amount = 1;
            block.ObjectGraphic = new GraphicAnimation { GraphicIndex = objects[options.ObjectIndex].GraphicIndex };
            placed++;
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }
}
