using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapLight
{
    public int Index { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public short Range { get; set; }
    public int Color { get; set; }
    public bool Active { get; set; }
}

/// <summary>
/// Port of the clsDX8Engine light bookkeeping (Light_Create, Light_Remove,
/// Light_Find, Light_Next_Open, Light_Move, Light_Make, Light_Check,
/// Light_Remove_All, Light_Color_Value_Get).
/// </summary>
internal sealed class LightListService
{
    private readonly List<MapLight> _lights = [];

    public IReadOnlyList<MapLight> Lights => _lights;

    public MapLight Create(short x, short y, short range, int color)
    {
        var light = new MapLight { Index = NextOpen(), X = x, Y = y, Range = range, Color = color, Active = true };
        if (light.Index == _lights.Count + 1) _lights.Add(light);
        else _lights[light.Index - 1] = light;
        return light;
    }

    public void Remove(int index)
    {
        if (index < 1 || index > _lights.Count) return;
        _lights[index - 1] = new MapLight { Index = index };
    }

    public int Find(short x, short y)
    {
        for (var index = 0; index < _lights.Count; index++)
            if (_lights[index].Active && _lights[index].X == x && _lights[index].Y == y) return index + 1;
        return 0;
    }

    public int NextOpen()
    {
        for (var index = 0; index < _lights.Count; index++)
            if (!_lights[index].Active) return index + 1;
        return _lights.Count + 1;
    }

    public void Move(int index, short x, short y)
    {
        if (index < 1 || index > _lights.Count) return;
        var light = _lights[index - 1];
        if (!light.Active) return;
        light.X = x; light.Y = y;
    }

    public bool Make(int index, short range, int color)
    {
        if (index < 1 || index > _lights.Count) return false;
        var light = _lights[index - 1];
        if (!light.Active) return false;
        light.Range = range; light.Color = color;
        return true;
    }

    public bool Check(int index) => index >= 1 && index <= _lights.Count && _lights[index - 1].Active;
    public int RemoveAll()
    {
        var removed = _lights.Count(light => light.Active);
        _lights.Clear();
        return removed;
    }

    public static int ColorValue(int bgr) => bgr & 0x00FFFFFF;

    public void MoveByHead(int index, byte heading)
    {
        if (index < 1 || index > _lights.Count) return;
        var light = _lights[index - 1];
        if (!light.Active) return;
        var (dx, dy) = heading switch
        {
            (byte)Heading.North => (0, -1),
            (byte)Heading.East => (1, 0),
            (byte)Heading.South => (0, 1),
            (byte)Heading.West => (-1, 0),
            _ => (0, 0)
        };
        light.X = (short)Math.Clamp(light.X + dx, 1, WorldMap.Width);
        light.Y = (short)Math.Clamp(light.Y + dy, 1, WorldMap.Height);
    }

    public int Destroy(int index)
    {
        Remove(index);
        return index;
    }

    public int Erase(int index)
    {
        Remove(index);
        return index;
    }

    public IReadOnlyList<MapLight> Renderable() => _lights.Where(light => light.Active && light.Range > 0).ToArray();

    public int SyncFromMap(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RemoveAll();
        var created = 0;
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            if (block.Light.Range == 0) continue;
            Create((short)x, (short)y, block.Light.Range, block.Light.Color);
            created++;
        }
        return created;
    }
}

/// <summary>Port of clsDX8Engine.GetElapsedTime and Engine_ActFPS.</summary>
internal sealed class FrameClock
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    private double _lastSeconds;
    private int _frames;

    public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

    public double FrameDeltaSeconds()
    {
        var current = ElapsedSeconds;
        var delta = current - _lastSeconds;
        _lastSeconds = current;
        return delta <= 0 ? 0 : delta;
    }

    public int CountFrame() => ++_frames;

    public double FramesPerSecond(double windowSeconds)
    {
        if (windowSeconds <= 0) return 0;
        return _frames / windowSeconds;
    }

    public void Reset()
    {
        _frames = 0;
        _lastSeconds = ElapsedSeconds;
    }
}
