using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct GraphicInfo(
    int Index,
    int Bmp,
    short Width,
    short Height,
    float TileWidth,
    float TileHeight,
    int FrameCount,
    float Speed,
    bool Animated,
    int MiniMapColor);

internal sealed class GraphicInfoService
{
    public GraphicInfo Get(IReadOnlyList<GraphicData> graphics, int index)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (index < 1 || index >= graphics.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var graphic = graphics[index];
        return new GraphicInfo(index, graphic.FileNumber, graphic.PixelWidth, graphic.PixelHeight, graphic.TileWidth,
            graphic.TileHeight, graphic.Frames.Length, graphic.Speed, graphic.Frames.Length > 1, graphic.MiniMapColor);
    }
}
