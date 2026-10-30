using AoProject.WorldEditor.Domain;
using System.Collections.Concurrent;

namespace AoProject.WorldEditor.Engine.Rendering;

/// <summary>
/// The CSM duplicates the border design in both maps of a junction (mapa203 row 91
/// == mapa17 row 11 in 100/100 columns) in most junctions. Both copies are the same
/// art: drawing both leaves a 1-tile stripe (roofs and walls appear twice) and the
/// roof fade applies to only one copy (flicker).
///
/// Collapsing shortens the stride by one tile for that junction *line*, so both
/// copies land on the same row/column. The shift belongs to the line (all chunk
/// rows/columns are checked, never one map) and is cumulative per map column/row;
/// computing it per chunk sheared the world.
///
/// Line results are cached and computed on demand: computing everything up front
/// forced loading all 418 CSM files just to open the world.
/// </summary>
internal sealed class WorldBorderStitcher
{
    public const int FirstCoreX = 14;
    public const int LastCoreX = 88;
    public const int FirstCoreY = 11;
    public const int LastCoreY = 91;

    /// <summary>Only the four border lines of a map: comparing them needs no full map.</summary>
    public readonly record struct BorderSample(int[] FirstRow, int[] LastRow, int[] FirstColumn, int[] LastColumn);

    private readonly Dictionary<GlobalChunkCoordinate, short> _byChunk;
    private readonly Func<short, BorderSample?> _sampleOf;
    private readonly ConcurrentDictionary<short, Lazy<BorderSample?>> _samples = [];
    private readonly ConcurrentDictionary<int, bool> _verticalLines = [];
    private readonly ConcurrentDictionary<int, bool> _horizontalLines = [];
    private int _prewarming;
    private volatile bool _isComplete;

    public WorldBorderStitcher(IEnumerable<GlobalMapPlacement> placements,
        Func<short, BorderSample?> sampleOf)
    {
        _byChunk = placements.GroupBy(item => item.Chunk)
            .ToDictionary(group => group.Key, group => group.First().MapId);
        _sampleOf = sampleOf;
    }

    private BorderSample? Sample(short mapId)
    {
        var lazy = _samples.GetOrAdd(mapId,
            id => new Lazy<BorderSample?>(() => _sampleOf(id), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    /// <summary>Reads the border lines of a map and lets the caller drop the map.</summary>
    public static BorderSample Sample(WorldMap map)
    {
        var firstRow = new int[WorldMap.Width];
        var lastRow = new int[WorldMap.Width];
        var firstColumn = new int[WorldMap.Height];
        var lastColumn = new int[WorldMap.Height];
        for (var x = 1; x <= WorldMap.Width; x++)
        {
            firstRow[x - 1] = map.Blocks[x, FirstCoreY].Graphics[0].GraphicIndex;
            lastRow[x - 1] = map.Blocks[x, LastCoreY].Graphics[0].GraphicIndex;
        }
        for (var y = 1; y <= WorldMap.Height; y++)
        {
            firstColumn[y - 1] = map.Blocks[FirstCoreX, y].Graphics[0].GraphicIndex;
            lastColumn[y - 1] = map.Blocks[LastCoreX, y].Graphics[0].GraphicIndex;
        }
        return new BorderSample(firstRow, lastRow, firstColumn, lastColumn);
    }

    /// <summary>Columns/rows removed before this chunk (0 when nothing is collapsed).</summary>
    public (int X, int Y) For(GlobalChunkCoordinate chunk)
    {
        var columns = 0;
        for (var line = 1; line <= chunk.X; line++)
            if (VerticalLine(line)) columns++;
        var rows = 0;
        for (var line = 1; line <= chunk.Y; line++)
            if (HorizontalLine(line)) rows++;
        return (columns, rows);
    }

    private (int X, int Y) ForSynchronous(GlobalChunkCoordinate chunk)
    {
        var columns = 0;
        for (var line = 1; line <= chunk.X; line++)
            if (ComputeVerticalLine(line)) columns++;
        var rows = 0;
        for (var line = 1; line <= chunk.Y; line++)
            if (ComputeHorizontalLine(line)) rows++;
        return (columns, rows);
    }

    /// <summary>Collapse flags for the chunks a caller is going to draw.</summary>
    public (Dictionary<GlobalChunkCoordinate, int> Columns, Dictionary<GlobalChunkCoordinate, int> Rows)
        ForAll(IEnumerable<GlobalChunkCoordinate> chunks)
    {
        var columns = new Dictionary<GlobalChunkCoordinate, int>();
        var rows = new Dictionary<GlobalChunkCoordinate, int>();
        foreach (var chunk in chunks)
        {
            var (x, y) = ForSynchronous(chunk);
            columns[chunk] = x;
            rows[chunk] = y;
        }
        return (columns, rows);
    }

    /// <summary>Returns cached shifts without ever loading a border on the caller thread.</summary>
    public (Dictionary<GlobalChunkCoordinate, int> Columns, Dictionary<GlobalChunkCoordinate, int> Rows)
        ForAllNonBlocking(IEnumerable<GlobalChunkCoordinate> chunks)
    {
        var columns = new Dictionary<GlobalChunkCoordinate, int>();
        var rows = new Dictionary<GlobalChunkCoordinate, int>();
        foreach (var chunk in chunks)
        {
            var (x, y) = For(chunk);
            columns[chunk] = x;
            rows[chunk] = y;
        }
        return (columns, rows);
    }

    /// <summary>
    /// Computes every junction line in the background and calls
    /// <paramref name="onDone"/> once. Loading the border maps on the UI thread
    /// froze the editor (each line compares two maps), so the first paint uses 0
    /// and the world snaps into the collapsed grid when this finishes.
    /// </summary>
    public void Prewarm(Action? onDone)
    {
        if (IsComplete || Interlocked.CompareExchange(ref _prewarming, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var line in _byChunk.Keys.Select(chunk => chunk.X).Distinct().OrderBy(value => value))
                    if (line > 0) ComputeVerticalLine(line);
                foreach (var line in _byChunk.Keys.Select(chunk => chunk.Y).Distinct().OrderBy(value => value))
                    if (line > 0) ComputeHorizontalLine(line);
                _isComplete = true;
            }
            finally
            {
                Volatile.Write(ref _prewarming, 0);
                onDone?.Invoke();
            }
        });
    }

    /// <summary>True once every junction line has been evaluated.</summary>
    public bool IsComplete => _isComplete;

    private bool VerticalLine(int line)
    {
        if (_verticalLines.TryGetValue(line, out var cached)) return cached;
        if (!IsComplete) return false;
        return ComputeVerticalLine(line);
    }

    private bool HorizontalLine(int line)
    {
        if (_horizontalLines.TryGetValue(line, out var cached)) return cached;
        if (!IsComplete) return false;
        return ComputeHorizontalLine(line);
    }

    private bool ComputeVerticalLine(int line) =>
        _verticalLines.GetOrAdd(line, value => LineDuplicates(value, vertical: true));

    private bool ComputeHorizontalLine(int line) =>
        _horizontalLines.GetOrAdd(line, value => LineDuplicates(value, vertical: false));

    private bool LineDuplicates(int line, bool vertical)
    {
        var pairs = 0;
        var duplicates = 0;
        foreach (var chunk in _byChunk.Keys)
        {
            GlobalChunkCoordinate previous;
            if (vertical)
            {
                if (chunk.X != line) continue;
                previous = new GlobalChunkCoordinate(chunk.X - 1, chunk.Y);
            }
            else
            {
                if (chunk.Y != line) continue;
                previous = new GlobalChunkCoordinate(chunk.X, chunk.Y - 1);
            }
            if (!_byChunk.TryGetValue(previous, out var previousMap)) continue;
            if (!_byChunk.TryGetValue(chunk, out var currentMap)) continue;
            pairs++;
            var duplicate = vertical
                ? ColumnsDuplicate(Sample(previousMap), Sample(currentMap))
                : RowsDuplicate(Sample(previousMap), Sample(currentMap));
            if (duplicate) duplicates++;
        }
        return pairs > 0 && duplicates * 2 >= pairs;
    }

    /// <summary>True when the lower map's first row repeats the upper map's last row.</summary>
    public static bool RowsDuplicate(BorderSample? upper, BorderSample? lower)
    {
        if (upper is not { } top || lower is not { } bottom) return false;
        int same = 0, both = 0;
        for (var index = 0; index < WorldMap.Width; index++)
        {
            var a = bottom.FirstRow[index];
            var b = top.LastRow[index];
            if (a == 0 && b == 0) continue;
            both++;
            if (a == b) same++;
        }
        return both > 0 && same >= both * 0.9;
    }

    /// <summary>True when the right map's first column repeats the left map's last.</summary>
    public static bool ColumnsDuplicate(BorderSample? left, BorderSample? right)
    {
        if (left is not { } west || right is not { } east) return false;
        int same = 0, both = 0;
        for (var index = 0; index < WorldMap.Height; index++)
        {
            var a = east.FirstColumn[index];
            var b = west.LastColumn[index];
            if (a == 0 && b == 0) continue;
            both++;
            if (a == b) same++;
        }
        return both > 0 && same >= both * 0.9;
    }
}
