using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct SurfaceLookup(int SurfaceIndex, int GraphicIndex, byte Width, byte Height, bool Mosaic, bool Blocked);

/// <summary>Port of modPaneles.DameGrhIndex and modPaneles.Filtrar.</summary>
internal sealed class SurfaceCatalogService
{
    public SurfaceLookup DameGrhIndex(IReadOnlyList<SurfaceData> surfaces, int surfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        if (surfaceIndex < 0 || surfaceIndex >= surfaces.Count) throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
        var surface = surfaces[surfaceIndex];
        return new SurfaceLookup(surfaceIndex, surface.GraphicIndex, surface.Width, surface.Height,
            surface.Width > 0 && surface.Height > 0, surface.Blocked);
    }

    public IReadOnlyList<SurfaceData> Filtrar(IReadOnlyList<SurfaceData> surfaces, string text)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        if (string.IsNullOrWhiteSpace(text)) return surfaces;
        var term = text.Trim();
        return surfaces
            .Where(surface => surface.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || surface.GraphicIndex.ToString().Contains(term, StringComparison.Ordinal))
            .ToArray();
    }
}
