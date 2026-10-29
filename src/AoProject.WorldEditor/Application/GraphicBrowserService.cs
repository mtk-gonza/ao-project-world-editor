using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct GraphicBrowserEntry(int Index, GraphicData Data);

/// <summary>Functional equivalent of Form1's GRH list.</summary>
internal sealed class GraphicBrowserService
{
    public IReadOnlyList<GraphicBrowserEntry> Enumerate(IReadOnlyList<GraphicData> graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        return Enumerable.Range(1, Math.Max(0, graphics.Count - 1))
            .Select(index => new GraphicBrowserEntry(index, graphics[index]))
            .ToArray();
    }
}
