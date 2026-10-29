using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct TileCapture(int Layer, int GraphicIndex, WorldPosition Transfer, bool HasTransfer);

internal sealed class MapTileCaptureService
{
    public TileCapture Capture(WorldMap map, int x, int y, bool captureSurface, bool captureTransfer)
    {
        if (x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
            y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
            x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height) throw new ArgumentOutOfRangeException(nameof(x));
        var block = map.Blocks[x, y];
        var layer = -1; var graphic = 0;
        if (captureSurface)
        for (var candidate = 3; candidate >= 0; candidate--)
            if (block.Graphics[candidate].GraphicIndex != 0) { layer = candidate; graphic = block.Graphics[candidate].GraphicIndex; break; }
        var transfer = block.TileExit;
        return new TileCapture(layer, graphic, new WorldPosition { Map = transfer.Map, X = transfer.X, Y = transfer.Y }, captureTransfer && transfer.Map != 0);
    }
}
