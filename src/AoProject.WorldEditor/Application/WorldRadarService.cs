using System.Drawing;
using System.Drawing.Drawing2D;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Port of the VB6 client centered minimap (ModMinimap.RenderMinimapCentered):
/// the map is a set of pre-rendered minimaps (one per map cell) composed into a
/// world image; the radar is a crop of that image centered on the player and
/// clamped to its bounds (no wrap).
/// </summary>
internal sealed class WorldRadarService
{
    // ModUtils.bas: usable tile bounds inside a map.
    public const int MinTileX = 14;
    public const int MaxTileX = 87;
    public const int MinTileY = 11;
    public const int MaxTileY = 90;
    public const int TileCountX = MaxTileX - MinTileX + 1;   // 74
    public const int TileCountY = MaxTileY - MinTileY + 1;   // 80

    // The editor renders one map every 75x81 tiles (GlobalChunkGeometry), not
    // every 74x80: the east column (88) and south row (91) belong to the cell.
    // The radar must use the same stride, otherwise the crop drifts one pixel
    // per map cell and the center no longer matches what the viewport shows.
    public const int RadarStrideX = GlobalChunkGeometry.WidthTiles;   // 75
    public const int RadarStrideY = GlobalChunkGeometry.HeightTiles;  // 81

    /// <summary>Window size in pixels (the client ships 200x200 world images).</summary>
    public const int DefaultSize = 200;

    /// <summary>
    /// Crops the world image around an explicit pixel. Use this when the caller
    /// already converted the map/tile to the radar image space. Returns the crop
    /// plus the pixel inside it that marks the requested point: when the crop is
    /// clamped at the world edge the marker moves off-center instead of lying.
    /// </summary>
    public (Bitmap Image, int MarkerX, int MarkerY) CreateCenteredAt(Bitmap worldImage, int centerPxX,
        int centerPxY, int destWidth, int destHeight, int deltaWidth = 0, int deltaHeight = 0,
        bool clampToImage = true)
    {
        ArgumentNullException.ThrowIfNull(worldImage);
        deltaWidth = Math.Clamp(deltaWidth, -50, 50);
        deltaHeight = Math.Clamp(deltaHeight, -50, 50);
        var sourceWidth = Math.Clamp(destWidth + deltaWidth, 16, worldImage.Width);
        var sourceHeight = Math.Clamp(destHeight + deltaHeight, 16, worldImage.Height);
        var sourceX = centerPxX - sourceWidth / 2;
        var sourceY = centerPxY - sourceHeight / 2;
        if (clampToImage)
        {
            sourceX = Math.Clamp(sourceX, 0, worldImage.Width - sourceWidth);
            sourceY = Math.Clamp(sourceY, 0, worldImage.Height - sourceHeight);
        }
        var image = new Bitmap(Math.Max(1, destWidth), Math.Max(1, destHeight));
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
        canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
        canvas.SmoothingMode = SmoothingMode.None;
        canvas.DrawImage(worldImage,
            new Rectangle(0, 0, image.Width, image.Height),
            new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight),
            GraphicsUnit.Pixel);
        return (image, centerPxX - sourceX, centerPxY - sourceY);
    }

    /// <summary>
    /// Converts a chunk + local tile to the pixel of the composed radar image.
    /// The image is 1 px per render tile with the render stride (75x81), so the
    /// result is simply the global tile shifted by the core origin
    /// (13, 10) and there is no drift across cells.
    /// </summary>
    public static (int X, int Y) ToRadarPixel(GlobalChunkCoordinate chunk, int localX, int localY) => (
        chunk.X * RadarStrideX + (localX - MinTileX),
        chunk.Y * RadarStrideY + (localY - MinTileY));

    /// <summary>
    /// Crops the world image around the player, exactly like the client:
    /// <c>centerPx = (grid + (tile - min + 0.5) / count) * cellPx</c>, source crop
    /// equals the destination plus the zoom delta, clamped to the bitmap.
    /// </summary>
    public Bitmap CreateCentered(Bitmap worldImage, int cellsX, int cellsY,
        int mapGridX, int mapGridY, int tileX, int tileY,
        int destWidth, int destHeight, int deltaWidth = 0, int deltaHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(worldImage);
        cellsX = Math.Max(1, cellsX);
        cellsY = Math.Max(1, cellsY);
        var cellPxW = worldImage.Width / (double)cellsX;
        var cellPxH = worldImage.Height / (double)cellsY;
        tileX = Math.Clamp(tileX, MinTileX, MaxTileX);
        tileY = Math.Clamp(tileY, MinTileY, MaxTileY);
        var centerPxX = (int)Math.Round((mapGridX + (tileX - MinTileX + 0.5) / TileCountX) * cellPxW);
        var centerPxY = (int)Math.Round((mapGridY + (tileY - MinTileY + 0.5) / TileCountY) * cellPxH);
        deltaWidth = Math.Clamp(deltaWidth, -50, 50);
        deltaHeight = Math.Clamp(deltaHeight, -50, 50);
        var sourceWidth = Math.Clamp(destWidth + deltaWidth, 16, worldImage.Width);
        var sourceHeight = Math.Clamp(destHeight + deltaHeight, 16, worldImage.Height);
        var sourceX = Math.Clamp(centerPxX - sourceWidth / 2, 0, worldImage.Width - sourceWidth);
        var sourceY = Math.Clamp(centerPxY - sourceHeight / 2, 0, worldImage.Height - sourceHeight);
        var image = new Bitmap(Math.Max(1, destWidth), Math.Max(1, destHeight));
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
        canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
        canvas.SmoothingMode = SmoothingMode.None;
        canvas.DrawImage(worldImage,
            new Rectangle(0, 0, image.Width, image.Height),
            new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight),
            GraphicsUnit.Pixel);
        return image;
    }

    /// <summary>
    /// Composes the world image from the per-map minimaps: 1 px per render tile,
    /// only the playable core, so cells are contiguous without scaling or seams.
    /// The stride is the render stride (75x81) and the last core column/row of
    /// each map is kept, so <see cref="ToRadarPixel"/> is an exact translation.
    /// </summary>
    public static Bitmap ComposeWorldImage(
        IReadOnlyList<MapsWorldDataService.WorldLayout> layouts, int worldId,
        IReadOnlyDictionary<short, Bitmap> mapImages)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(mapImages);
        var layout = layouts.FirstOrDefault(item => item.WorldId == worldId);
        if (layout is null || layout.Width <= 0 || layout.Height <= 0) return new Bitmap(1, 1);
        var image = new Bitmap(layout.Width * RadarStrideX, layout.Height * RadarStrideY);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
        canvas.PixelOffsetMode = PixelOffsetMode.Half;
        for (var index = 0; index < layout.SectorIds.Count; index++)
        {
            var mapId = layout.SectorIds[index];
            if (mapId <= 0 || !mapImages.TryGetValue(mapId, out var mapImage)) continue;
            var column = index % layout.Width;
            var row = index / layout.Width;
            var sourceWidth = Math.Min(RadarStrideX, mapImage.Width - (MinTileX - 1));
            var sourceHeight = Math.Min(RadarStrideY, mapImage.Height - (MinTileY - 1));
            if (sourceWidth <= 0 || sourceHeight <= 0) continue;
            canvas.DrawImage(mapImage,
                new Rectangle(column * RadarStrideX, row * RadarStrideY, sourceWidth, sourceHeight),
                MinTileX - 1, MinTileY - 1, sourceWidth, sourceHeight, GraphicsUnit.Pixel);
        }
        return image;
    }

    /// <summary>Grid cell of a map inside the world image.</summary>
    public static (int Column, int Row) GridOf(IReadOnlyList<MapsWorldDataService.WorldLayout> layouts,
        int worldId, short mapId)
    {
        var layout = layouts.FirstOrDefault(item => item.WorldId == worldId);
        if (layout is null) return (-1, -1);
        for (var index = 0; index < layout.SectorIds.Count; index++)
            if (layout.SectorIds[index] == mapId) return (index % layout.Width, index / layout.Width);
        return (-1, -1);
    }
}
