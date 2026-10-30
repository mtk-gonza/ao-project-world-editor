using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapEffectsEditingService
{
    public void SetLight(WorldSector map, int x, int y, short range, int color)
    {
        Validate(map, x, y);
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        map.Blocks[x, y].Light.Range = range;
        map.Blocks[x, y].Light.Color = color;
        map.Info.Changed = true;
    }

    public int ClearLights(WorldSector map)
    {
        var count = 0;
        ForEach(map, block => { if (block.Light.Range != 0) { block.Light.Range = 0; block.Light.Color = 0; count++; } });
        if (count > 0) map.Info.Changed = true;
        return count;
    }

    public void SetParticle(WorldSector map, int x, int y, int particle)
    {
        Validate(map, x, y);
        map.Blocks[x, y].ParticleIndex = particle;
        map.Info.Changed = true;
    }

    public int ClearParticles(WorldSector map)
    {
        var count = 0;
        ForEach(map, block => { if (block.ParticleIndex != 0) { block.ParticleIndex = 0; block.ParticleGroup = 0; count++; } });
        if (count > 0) map.Info.Changed = true;
        return count;
    }

    private static void Validate(WorldSector map, int x, int y)
    {
        if (x < map.CsmSectorSize.XMin || x > map.CsmSectorSize.XMax ||
            y < map.CsmSectorSize.YMin || y > map.CsmSectorSize.YMax ||
            x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height)
            throw new ArgumentOutOfRangeException(nameof(x));
    }

    private static void ForEach(WorldSector map, Action<SectorTile> action)
    {
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = minX; x <= maxX; x++) for (var y = minY; y <= maxY; y++) action(map.Blocks[x, y]);
    }
}
