using System.Text.Json;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed class UnifiedWorldFloorAssignmentService
{
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
