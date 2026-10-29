using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapEffectsEditingService
{
    public void SetLight(WorldMap map, int x, int y, short range, int color)
    {
        Validate(map, x, y);
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        map.Blocks[x, y].Light.Range = range;
        map.Blocks[x, y].Light.Color = color;
        map.Info.Changed = true;
    }

    public int ClearLights(WorldMap map)
    {
        var count = 0;
        ForEach(map, block => { if (block.Light.Range != 0) { block.Light.Range = 0; block.Light.Color = 0; count++; } });
        if (count > 0) map.Info.Changed = true;
        return count;
    }

    public void SetParticle(WorldMap map, int x, int y, int particle)
    {
        Validate(map, x, y);
        map.Blocks[x, y].ParticleIndex = particle;
        map.Info.Changed = true;
    }

    public int ClearParticles(WorldMap map)
    {
        var count = 0;
        ForEach(map, block => { if (block.ParticleIndex != 0) { block.ParticleIndex = 0; block.ParticleGroup = 0; count++; } });
        if (count > 0) map.Info.Changed = true;
        return count;
    }

    private static void Validate(WorldMap map, int x, int y)
    {
        if (x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
            y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
            x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height)
            throw new ArgumentOutOfRangeException(nameof(x));
    }

    private static void ForEach(WorldMap map, Action<MapBlock> action)
    {
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++) for (var y = minY; y <= maxY; y++) action(map.Blocks[x, y]);
    }
}
