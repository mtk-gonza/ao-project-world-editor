using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct AmbientationEntry(int Index, Ambientation Ambientation);

/// <summary>Port of Ambientaciones.Combo1_Click (type filter) and List1 listing.</summary>
internal sealed class AmbientationCatalogService
{
    public IReadOnlyList<byte> Types(IReadOnlyList<Ambientation> ambientations)
    {
        ArgumentNullException.ThrowIfNull(ambientations);
        return ambientations.Select(item => item.Type).Distinct().OrderBy(type => type).ToArray();
    }

    public IReadOnlyList<AmbientationEntry> FilterByType(IReadOnlyList<Ambientation> ambientations, byte type)
    {
        ArgumentNullException.ThrowIfNull(ambientations);
        var result = new List<AmbientationEntry>();
        for (var index = 0; index < ambientations.Count; index++)
            if (ambientations[index].Type == type) result.Add(new AmbientationEntry(index, ambientations[index]));
        return result;
    }
}
