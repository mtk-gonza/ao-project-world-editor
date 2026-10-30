using System.Text.Json;
using AoProject.WorldEditor.Application;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed class UnifiedWorldFloorAssignmentService
{
    public IReadOnlyDictionary<short, int> Detect(GlobalWorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        // Floor assignment is deliberately derived from the imported world.
        // This temporary conservative detector keeps the normal assembly on Z=0;
        // special disconnected components and extended exits are handled by the
        // world assembler/compiler, not by an external manifest.
        return world.Assembly.Placements
            .Select(item => item.MapId)
            .Distinct()
            .ToDictionary(mapId => mapId, _ => 0);
    }

    public IReadOnlyDictionary<short, int> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return new Dictionary<short, int>();
        var assignments = JsonSerializer.Deserialize<Dictionary<short, int>>(File.ReadAllText(path))
            ?? new Dictionary<short, int>();
        if (assignments.Any(item => item.Key <= 0))
            throw new InvalidDataException("La asignación de pisos contiene un MapId inválido.");
        return assignments;
    }

    public void Save(string path, IReadOnlyDictionary<short, int> assignments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Any(item => item.Key <= 0))
            throw new InvalidDataException("La asignación de pisos contiene un MapId inválido.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(assignments,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
