using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class GraphicLookupService
{
    public int ResolveBmp(IReadOnlyList<GraphicData> graphics, int graphicIndex)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphicIndex < 1 || graphicIndex >= graphics.Count)
            throw new ArgumentOutOfRangeException(nameof(graphicIndex));
        var graphic = graphics[graphicIndex];
        if (!graphic.Active || graphic.FileNumber <= 0)
            throw new InvalidDataException($"El GRH {graphicIndex} no tiene un BMP directo.");
        return graphic.FileNumber;
    }
}
