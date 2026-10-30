using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed record UnifiedWorldConflictReportEntry(
    int Z,
    int GlobalX,
    int GlobalY,
    short FirstMapId,
    short SecondMapId,
    string Field,
    string Message);

internal sealed class UnifiedWorldConflictReportService
{
    public IReadOnlyList<UnifiedWorldConflictReportEntry> Build(UnifiedWorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.Floors.Values.SelectMany(map => map.Conflicts.Select(conflict =>
            new UnifiedWorldConflictReportEntry(map.FloorZ, conflict.GlobalX, conflict.GlobalY,
                conflict.FirstMapId, conflict.SecondMapId, conflict.Field, conflict.Message)))
            .OrderBy(item => item.Z).ThenBy(item => item.GlobalY).ThenBy(item => item.GlobalX)
            .ThenBy(item => item.Field).ToArray();
    }

    public void Write(UnifiedWorldBundle bundle, string directory)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var entries = Build(bundle);
        var summary = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            floors = bundle.Floors.Keys,
            totalConflicts = entries.Count,
            byField = entries.GroupBy(item => item.Field).OrderByDescending(group => group.Count())
                .ToDictionary(group => group.Key, group => group.Count()),
            byMapPair = entries.GroupBy(item => $"{item.FirstMapId}/{item.SecondMapId}")
                .OrderByDescending(group => group.Count())
                .ToDictionary(group => group.Key, group => group.Count()),
            conflicts = entries
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "unified-world-conflicts.json"),
            JsonSerializer.Serialize(summary, options), Encoding.UTF8);

        using var writer = new StreamWriter(Path.Combine(directory, "unified-world-conflicts.csv"), false, Encoding.UTF8);
        writer.WriteLine("Z,GlobalX,GlobalY,FirstMapId,SecondMapId,Field,Message");
        foreach (var item in entries)
            writer.WriteLine(string.Join(",", item.Z, item.GlobalX, item.GlobalY, item.FirstMapId,
                item.SecondMapId, Csv(item.Field), Csv(item.Message)));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
