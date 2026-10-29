namespace AoProject.WorldEditor.Engine.Rendering;

internal sealed class WorldCamera
{
    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }
    public float Zoom { get; private set; } = 1f;

    public void Pan(float x, float y)
    {
        OffsetX += x;
        OffsetY += y;
    }

    public void CenterOnTile(int tileX, int tileY, int viewportWidth, int viewportHeight)
    {
        if (tileX is < 1 or > 100 || tileY is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(tileX));
        // VB6's UserPos is the tile at the logical viewport origin. Its
        // ConvertCPtoTP formula maps the viewport center to (UserPos.X - 1,
        // UserPos.Y). Keep that legacy convention instead of centering the
        // zero-based render coordinate directly on tileX/tileY.
        OffsetX = (tileX - 2) * MapRenderGeometry.TilePixels - viewportWidth / (2f * Zoom);
        OffsetY = (tileY - 1) * MapRenderGeometry.TilePixels - viewportHeight / (2f * Zoom);
    }

    public void CenterOnGlobalTile(int tileX, int tileY, int viewportWidth, int viewportHeight)
    {
        OffsetX = tileX * MapRenderGeometry.TilePixels - viewportWidth / (2f * Zoom);
        OffsetY = tileY * MapRenderGeometry.TilePixels - viewportHeight / (2f * Zoom);
    }

    public void SetZoom(float zoom, float focusX, float focusY)
    {
        var next = Math.Clamp(zoom, 0.25f, 4f);
        var worldX = focusX / Zoom + OffsetX;
        var worldY = focusY / Zoom + OffsetY;
        Zoom = next;
        OffsetX = worldX - focusX / Zoom;
        OffsetY = worldY - focusY / Zoom;
    }

    public (int X, int Y)? ScreenToTile(int x, int y)
    {
        var tileX = (int)MathF.Floor(x / Zoom / MapRenderGeometry.TilePixels + OffsetX / MapRenderGeometry.TilePixels) + 1;
        var tileY = (int)MathF.Floor(y / Zoom / MapRenderGeometry.TilePixels + OffsetY / MapRenderGeometry.TilePixels) + 1;
        return tileX is >= 1 and <= 100 && tileY is >= 1 and <= 100 ? (tileX, tileY) : null;
    }

    public (int X, int Y) ScreenToGlobalTile(int x, int y) =>
        ((int)MathF.Floor(x / Zoom / MapRenderGeometry.TilePixels + OffsetX / MapRenderGeometry.TilePixels),
         (int)MathF.Floor(y / Zoom / MapRenderGeometry.TilePixels + OffsetY / MapRenderGeometry.TilePixels));

    /// <summary>Tile at the center of the viewport (1-based, like the map grid).</summary>
    public (int X, int Y) CenterTile(int viewportWidth, int viewportHeight) => (
        (int)MathF.Floor((viewportWidth / (2f * Zoom) + OffsetX) / MapRenderGeometry.TilePixels) + 1,
        (int)MathF.Floor((viewportHeight / (2f * Zoom) + OffsetY) / MapRenderGeometry.TilePixels) + 1);

    /// <summary>World tile (0-based geometry index) at the center of the viewport.</summary>
    public (int X, int Y) CenterGlobalTile(int viewportWidth, int viewportHeight) => (
        (int)MathF.Floor((viewportWidth / (2f * Zoom) + OffsetX) / MapRenderGeometry.TilePixels),
        (int)MathF.Floor((viewportHeight / (2f * Zoom) + OffsetY) / MapRenderGeometry.TilePixels));
}
