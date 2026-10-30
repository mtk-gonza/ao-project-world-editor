namespace AoProject.WorldEditor.Domain;

internal enum GlobalWorldSide
{
    North,
    South,
    East,
    West
}

internal readonly record struct GlobalChunkCoordinate(int X, int Y);

internal static class GlobalChunkGeometry
{
    // Legacy world exits join the playable map bands, not the full 100x100
    // file extent: east (88) reaches west (14), while south (91) reaches
    // north (11). These strides place the destination on the tile directly
    // beyond the source boundary.
    public const int WidthTiles = 75;
    public const int HeightTiles = 81;
    public const int CoreMinX = 13;
    public const int CoreMaxX = 88;
    public const int CoreMinY = 10;
    public const int CoreMaxY = 91;

    public static bool IsCoreTile(int x, int y, bool hasWestNeighbor = false, bool hasNorthNeighbor = false) =>
        x >= (hasWestNeighbor ? CoreMinX + 1 : CoreMinX) && x <= CoreMaxX &&
        y >= (hasNorthNeighbor ? CoreMinY + 1 : CoreMinY) && y <= CoreMaxY;

    /// <summary>
    /// Converts a visible rect in world geometry tiles (0-based) to the local
    /// 1-based tiles of one chunk, matching the overlay offset
    /// <c>localX + chunk.X * WidthTiles</c>.
    /// </summary>
    public static (int MinX, int MaxX, int MinY, int MaxY) ToLocalTileBounds(
        (int MinX, int MaxX, int MinY, int MaxY) global, GlobalChunkCoordinate chunk) => (
        global.MinX - chunk.X * WidthTiles + 1,
        global.MaxX - chunk.X * WidthTiles + 1,
        global.MinY - chunk.Y * HeightTiles + 1,
        global.MaxY - chunk.Y * HeightTiles + 1);

    /// <summary>
    /// Same test as <see cref="IsCoreTile"/> but for a pixel anchor (sprite
    /// origin) instead of a tile index. Negative pixel values are floored so a
    /// sprite just outside the west/north border is filtered, not wrapped.
    /// </summary>
    public static bool IsCorePixel(int pixelX, int pixelY, int tilePixels,
        bool hasWestNeighbor = false, bool hasNorthNeighbor = false) =>
        IsCoreTile((int)MathF.Floor(pixelX / (float)tilePixels) + 1,
            (int)MathF.Floor(pixelY / (float)tilePixels) + 1,
            hasWestNeighbor, hasNorthNeighbor);
}

internal readonly record struct GlobalMapLink(
    short FromMap,
    short ToMap,
    GlobalWorldSide Side,
    int Evidence,
    short SourceX,
    short SourceY,
    short TargetX,
    short TargetY);

internal readonly record struct GlobalSectorPlacement(
    short SectorId,
    GlobalChunkCoordinate Chunk,
    string LevelId,
    int StitchX = 0,
    int StitchY = 0);

internal readonly record struct GlobalWorldConflict(
    short FromMap,
    short ToMap,
    GlobalWorldSide Side,
    GlobalChunkCoordinate Expected,
    GlobalChunkCoordinate Actual,
    string Reason);

internal sealed class GlobalWorldAssembly
{
    public IReadOnlyList<GlobalSectorPlacement> Placements { get; init; } = [];
    public IReadOnlyList<GlobalMapLink> Links { get; init; } = [];
    public IReadOnlyList<GlobalWorldConflict> Conflicts { get; init; } = [];
    /// <summary>Official grid width in maps (0 when inferred/unavailable).</summary>
    public int Width { get; init; }
    /// <summary>Official grid height in maps (0 when inferred/unavailable).</summary>
    public int Height { get; init; }
    /// <summary>True when the east edge connects to the west edge (legacy cylinder).</summary>
    public bool WrapX { get; init; }
}
