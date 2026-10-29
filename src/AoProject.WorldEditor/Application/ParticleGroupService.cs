using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class ParticleGroup
{
    public int Index { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public int Type { get; set; }
    public bool Active { get; set; }
    internal List<ParticleRuntimeState> RuntimeParticles { get; } = [];
    internal int RuntimeType { get; set; }
    internal float LastRenderSeconds { get; set; }
    internal bool HasRendered { get; set; }
}

internal sealed class ParticleRuntimeState
{
    public bool Initialized { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float AliveFrames { get; set; }
    public int SimulationFrame { get; set; }
    public float Angle { get; set; }
}

/// <summary>
/// Port of the clsDX8Engine particle-group bookkeeping
/// (Particle_Group_Next_Open/Check/Create/Remove/Remove_All/Find/Destroy/Make).
/// </summary>
internal sealed class ParticleGroupService
{
    private readonly List<ParticleGroup> _groups = [];

    public IReadOnlyList<ParticleGroup> Groups => _groups;

    public int NextOpen()
    {
        for (var index = 0; index < _groups.Count; index++)
            if (!_groups[index].Active) return index + 1;
        return _groups.Count + 1;
    }

    public bool Check(int index) => index >= 1 && index <= _groups.Count && _groups[index - 1].Active;

    public ParticleGroup Create(short x, short y, int type)
    {
        var group = new ParticleGroup { Index = NextOpen(), X = x, Y = y, Type = type, Active = true };
        if (group.Index == _groups.Count + 1) _groups.Add(group);
        else _groups[group.Index - 1] = group;
        return group;
    }

    public void Remove(int index)
    {
        if (index < 1 || index > _groups.Count) return;
        _groups[index - 1] = new ParticleGroup { Index = index };
    }

    public int RemoveAll()
    {
        var removed = _groups.Count(group => group.Active);
        _groups.Clear();
        return removed;
    }

    public int Find(short x, short y)
    {
        for (var index = 0; index < _groups.Count; index++)
            if (_groups[index].Active && _groups[index].X == x && _groups[index].Y == y) return index + 1;
        return 0;
    }

    public int Destroy(int index)
    {
        Remove(index);
        return index;
    }

    public bool Make(int index, int type)
    {
        if (index < 1 || index > _groups.Count) return false;
        var group = _groups[index - 1];
        if (!group.Active) return false;
        group.Type = type;
        return true;
    }

    public int SyncFromMap(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var created = 0;
        var known = new HashSet<short>();
        var seen = new HashSet<int>();
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            if (block.ParticleIndex == 0 && block.ParticleGroup == 0) continue;
            if (block.ParticleGroup > 0)
            {
                // particle_group is the stable runtime identity. Several map
                // tiles may point at the same group; do not create one runtime
                // group per tile and silently renumber the persisted identity.
                if (!known.Add(block.ParticleGroup)) continue;
                CreateAtIndex(block.ParticleGroup, (short)x, (short)y, block.ParticleIndex);
                seen.Add(block.ParticleGroup);
            }
            else
            {
                // Older maps can contain a particle without the bookkeeping
                // id. Preserve the legacy tile-local fallback for those maps.
                var index = Find((short)x, (short)y);
                if (index == 0) index = Create((short)x, (short)y, block.ParticleIndex).Index;
                else _groups[index - 1].Type = block.ParticleIndex;
                seen.Add(index);
            }
            created++;
        }
        for (var index = 1; index <= _groups.Count; index++)
            if (_groups[index - 1].Active && !seen.Contains(index)) Remove(index);
        return created;
    }

    private void CreateAtIndex(short index, short x, short y, int type)
    {
        if (index <= 0) return;
        while (_groups.Count < index) _groups.Add(new ParticleGroup { Index = _groups.Count + 1 });
        var current = _groups[index - 1];
        if (current.Active)
        {
            current.X = x;
            current.Y = y;
            current.Type = type;
            return;
        }
        _groups[index - 1] = new ParticleGroup { Index = index, X = x, Y = y, Type = type, Active = true };
    }
}
