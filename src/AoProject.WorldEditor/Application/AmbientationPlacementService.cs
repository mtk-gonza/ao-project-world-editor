using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class AmbientationPlacementService
{
    public int Place(WorldMap map, Ambientation ambientiation, int count, int layer, bool blocked, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(ambientiation);
        if (count <= 0) return 0;
        if (layer is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(layer));
        var random = seed == 0 ? Random.Shared : new Random(seed); var placed = 0;
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < 10_000; attempt++)
            {
                var x = random.Next(13, 88); var y = random.Next(11, 91);
                if (map.Blocks[x, y].Graphics[0].GraphicIndex is >= 1505 and <= 1520) continue;
                var block = map.Blocks[x, y]; block.Blocked = blocked ? (byte)1 : (byte)0; block.Graphics[layer - 1].GraphicIndex = ambientiation.GraphicIndex; placed++; break;
            }
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }
}
