using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;

namespace AoProject.WorldEditor.Engine.Rendering;

internal readonly record struct MapTileQuad(
    int X,
    int Y,
    int Layer,
    int GraphicIndex,
    int PixelX,
    int PixelY,
    int RoofTrigger = 0,
    int ClipLeft = -1,
    int ClipTop = -1,
    int ClipRight = -1,
    int ClipBottom = -1);

internal readonly record struct AnimationInfo(int[] Frames, float Speed)
{
    public int FrameAt(double seconds)
    {
        if (Frames.Length <= 1 || Speed <= 0) return Frames.Length == 0 ? 0 : Frames[0];
        // VB6 Draw_Grh advances FrameCounter with:
        // timerElapsedTime(milliseconds) * NumFrames / GrhData.speed.
        // Speed is the complete animation duration in milliseconds, not a
        // frames-per-second value.
        var index = (int)(seconds * 1000d * Frames.Length / Speed) % Frames.Length;
        if (index < 0) index += Frames.Length;
        return Frames[index];
    }
}

/// <summary>Converts the map model into deterministic render input.</summary>
internal sealed class MapRenderGeometry
{
    public const int TilePixels = 32;
    public IReadOnlyList<MapTileQuad> Tiles { get; private init; } = [];

    public static MapRenderGeometry Build(WorldSector map)
        => Build(map, new WorldRenderOptions());

    public static MapRenderGeometry Build(WorldSector map, WorldRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        var tiles = new List<MapTileQuad>();
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        for (var layer = 0; layer < map.Blocks[x, y].Graphics.Length; layer++)
        {
            if (!options.IsLayerVisible(layer))
                continue;
            var graphic = map.Blocks[x, y].Graphics[layer].GraphicIndex;
            if (graphic == 0)
                continue;
            // VB6 NearRoof(x,y) returns the trigger of the first roof zone in
            // the surrounding 3x3 area; it does not read the roof tile's own
            // trigger. RoofsLight is keyed by that zone trigger.
            // VB6 applies RoofsLight to Graphic(4), which is index 3 here.
            var roofTrigger = layer == 3 ? NearRoof(map, x, y) : 0;
            tiles.Add(new MapTileQuad(x, y, layer, graphic, (x - 1) * TilePixels, (y - 1) * TilePixels, roofTrigger));
        }
        return new MapRenderGeometry { Tiles = tiles };
    }

    /// <summary>Builds render input directly from the compiled single-map grid.</summary>
    public static MapRenderGeometry Build(UnifiedWorldMap map, WorldRenderOptions options)
        => Build(map, options, 0, map.Width - 1, 0, map.Height - 1);

    /// <summary>Builds only the tile rectangle currently visible on screen.</summary>
    public static MapRenderGeometry Build(UnifiedWorldMap map, WorldRenderOptions options,
        int minX, int maxX, int minY, int maxY)
    {
        ArgumentNullException.ThrowIfNull(map);
        var tiles = new List<MapTileQuad>();
        minX = Math.Clamp(minX, 0, map.Width - 1);
        maxX = Math.Clamp(maxX, minX, map.Width - 1);
        minY = Math.Clamp(minY, 0, map.Height - 1);
        maxY = Math.Clamp(maxY, minY, map.Height - 1);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var offset = map.Offset(x, y);
            for (var layer = 0; layer < 8; layer++)
            {
                if (!options.IsLayerVisible(layer)) continue;
                var graphic = map.GraphicIndices[map.GraphicOffset(offset, layer)];
                if (graphic <= 0) continue;
                tiles.Add(new MapTileQuad(x, y, layer, graphic,
                    x * TilePixels, y * TilePixels,
                    layer == 3 ? map.Trigger[offset] : 0));
            }
        }
        return new MapRenderGeometry { Tiles = tiles };
    }

    public static MapRenderGeometry Combine(
        IEnumerable<(WorldSector Map, GlobalChunkCoordinate Chunk)> maps,
        WorldRenderOptions options)
        => Combine(maps.Select(item => (Build(item.Map, options), item.Chunk)));

    /// <summary>
    /// Collapsing the duplicated border in the renderer was tried (ADR-WM-022) and
    /// discarded: the duplicate occupies *two adjacent global rows*, so skipping
    /// one leaves that row undrawn and the world shows black bands. A real collapse
    /// has to shorten the world grid for the renderer *and* the radar image.
    /// </summary>
    public static MapRenderGeometry Combine(
        IEnumerable<(MapRenderGeometry Geometry, GlobalChunkCoordinate Chunk)> maps,
        IReadOnlyDictionary<GlobalChunkCoordinate, int>? collapsedColumns = null,
        IReadOnlyDictionary<GlobalChunkCoordinate, int>? collapsedRows = null)
    {
        var entries = maps.ToArray();
        var chunks = entries.Select(item => item.Chunk).ToHashSet();
        var tiles = new List<MapTileQuad>();
        foreach (var (geometry, chunk) in entries)
        {
            var trimColumns = collapsedColumns is not null && collapsedColumns.TryGetValue(chunk, out var columnShift)
                ? columnShift : 0;
            var trimRows = collapsedRows is not null && collapsedRows.TryGetValue(chunk, out var rowShift)
                ? rowShift : 0;
            var offsetX = chunk.X * GlobalChunkGeometry.WidthTiles - trimColumns;
            var offsetY = chunk.Y * GlobalChunkGeometry.HeightTiles - trimRows;
            var hasWestNeighbor = chunks.Contains(new GlobalChunkCoordinate(chunk.X - 1, chunk.Y));
            var hasNorthNeighbor = chunks.Contains(new GlobalChunkCoordinate(chunk.X, chunk.Y - 1));
            // VB6 draws the whole visible map without clipping graphics at the map
            // border: a big GRH (tower, roof) anchored on the first core row grows
            // over the neighbouring map and is drawn complete. Shared border tiles
            // are excluded by IsCoreTile.
            foreach (var tile in geometry.Tiles.Where(tile =>
                GlobalChunkGeometry.IsCoreTile(tile.X, tile.Y, hasWestNeighbor, hasNorthNeighbor)))
                tiles.Add(tile with
                {
                    X = tile.X + offsetX,
                    Y = tile.Y + offsetY,
                    PixelX = tile.PixelX + offsetX * TilePixels,
                    PixelY = tile.PixelY + offsetY * TilePixels
                });
        }
        return new MapRenderGeometry { Tiles = tiles };
    }

    public static bool IsRoofTrigger(int trigger) =>
        trigger >= 60 || trigger is 1 or 4 or 16;

    private static int NearRoof(WorldSector map, int centerX, int centerY)
    {
        for (var y = Math.Max(1, centerY - 1); y <= Math.Min(WorldSector.Height, centerY + 1); y++)
        for (var x = Math.Max(1, centerX - 1); x <= Math.Min(WorldSector.Width, centerX + 1); x++)
        {
            var trigger = map.Blocks[x, y].Trigger;
            if (IsRoofTrigger(trigger)) return trigger;
        }
        return 0;
    }
}
