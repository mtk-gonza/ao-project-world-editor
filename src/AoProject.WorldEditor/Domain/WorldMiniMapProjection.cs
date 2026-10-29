namespace AoProject.WorldEditor.Domain;

/// <summary>A pixel inside the composed world minimap.</summary>
internal readonly record struct WorldMiniMapPoint(int X, int Y);

/// <summary>The slice of the world minimap currently visible.</summary>
internal readonly record struct WorldMiniMapViewport(int Left, int Top, int Width, int Height, bool WrapsX);

/// <summary>
/// Pixel projection of the world minimap: each WORLDMAP cell is a full 100x100
/// CSM minimap, placed row-major at <c>chunk * 100</c>. This is the projection
/// used by the generated <c>worldmap&lt;id&gt;.png</c> and by the per-map
/// <c>Minimapas/mapaN.bmp</c> the client loads.
///
/// It is deliberately independent from the playable world geometry
/// (<see cref="GlobalChunkGeometry"/>: 75x81 with a 13..88 / 10..91 core), which
/// is why a "world minimap coordinate" is needed to center a viewport on a tile:
/// the same tile has a render coordinate and a minimap coordinate.
/// </summary>
internal sealed class WorldMiniMapProjection
{
    public WorldMiniMapProjection(int chunksX, int chunksY, bool wrapX)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunksX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunksY);
        ChunksX = chunksX;
        ChunksY = chunksY;
        WrapX = wrapX;
    }

    public int ChunksX { get; }
    public int ChunksY { get; }
    public bool WrapX { get; }
    public int Width => ChunksX * WorldMap.Width;
    public int Height => ChunksY * WorldMap.Height;

    /// <summary>Maps a local tile (1..100) of a chunk to its minimap pixel.</summary>
    public WorldMiniMapPoint ToPixel(GlobalChunkCoordinate chunk, int localX, int localY)
    {
        if (localX is < 1 or > WorldMap.Width)
            throw new ArgumentOutOfRangeException(nameof(localX), localX, "Tile local fuera de 1..100.");
        if (localY is < 1 or > WorldMap.Height)
            throw new ArgumentOutOfRangeException(nameof(localY), localY, "Tile local fuera de 1..100.");
        var x = chunk.X * WorldMap.Width + (localX - 1);
        var y = chunk.Y * WorldMap.Height + (localY - 1);
        return new WorldMiniMapPoint(Wrap(x, Width), y);
    }

    /// <summary>
    /// Centers a viewport on a minimap pixel. X wraps around the seam when
    /// <see cref="WrapX"/> is set (the image is a cylinder); Y clamps to the
    /// image so the top and bottom never show empty space.
    /// </summary>
    public WorldMiniMapViewport CenterOn(WorldMiniMapPoint point, int viewWidth, int viewHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewHeight);
        var width = Math.Min(viewWidth, Width);
        var height = Math.Min(viewHeight, Height);
        var left = point.X - width / 2;
        var top = Math.Clamp(point.Y - height / 2, 0, Math.Max(0, Height - height));
        return WrapX
            ? new WorldMiniMapViewport(Wrap(left, Width), top, width, height, true)
            : new WorldMiniMapViewport(
                Math.Clamp(left, 0, Math.Max(0, Width - width)), top, width, height, false);
    }

    /// <summary>
    /// Source rectangles needed to draw a viewport. With wrap the seam can touch
    /// both borders, so the viewport may need to be drawn in two pieces.
    /// </summary>
    public IReadOnlyList<(WorldMiniMapViewport Source, int DestinationX)> Split(WorldMiniMapViewport viewport)
    {
        if (!viewport.WrapsX || viewport.Width <= 0) return [(viewport, 0)];
        if (viewport.Left + viewport.Width <= Width) return [(viewport, 0)];
        var firstWidth = Width - viewport.Left;
        var secondWidth = viewport.Width - firstWidth;
        return
        [
            (new WorldMiniMapViewport(viewport.Left, viewport.Top, firstWidth, viewport.Height, false), 0),
            (new WorldMiniMapViewport(0, viewport.Top, secondWidth, viewport.Height, false), firstWidth)
        ];
    }

    private static int Wrap(int value, int size)
    {
        if (size <= 0) return 0;
        var result = value % size;
        return result < 0 ? result + size : result;
    }
}
