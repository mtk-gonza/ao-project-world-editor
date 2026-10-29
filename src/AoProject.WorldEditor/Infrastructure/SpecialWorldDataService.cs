using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed class SpecialWorldDataService
{
    public SpecialWorldCatalog LoadUnderworld(string path)
    {
        if (!File.Exists(path)) return new([], []);
        var values = ReadSections(path);
        var portals = values.Where(item => item.Section.StartsWith("Portal", StringComparison.OrdinalIgnoreCase))
            .Select(item => new SpecialWorldPortal("underworld",
                GetShort(item.Values, "SourceMap"), GetShort(item.Values, "SourceX"), GetShort(item.Values, "SourceY"),
                GetShort(item.Values, "DestinationMap"), GetShort(item.Values, "DestinationX"), GetShort(item.Values, "DestinationY")))
            .Where(portal => portal.SourceMap > 0 && portal.DestinationMap > 0).ToArray();
        return new(portals, portals.Select(item => item.DestinationMap).Distinct().ToArray());
    }

    public SpecialWorldCatalog LoadPhoenix(string path)
    {
        if (!File.Exists(path)) return new([], []);
        var values = ReadSections(path).FirstOrDefault(item => item.Section.Equals("Maps", StringComparison.OrdinalIgnoreCase));
        var maps = values.Values.Where(item => item.Key.StartsWith("Map", StringComparison.OrdinalIgnoreCase))
            .Select(item => short.TryParse(item.Value, out var map) ? map : (short)0)
            .Where(map => map > 0).Distinct().ToArray();
        return new([], maps);
    }

    private static short GetShort(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && short.TryParse(value, out var result) ? result : (short)0;

    private static IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Values)> ReadSections(string path)
    {
        var result = new List<(string, IReadOnlyDictionary<string, string>)>();
        string section = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Flush() { if (section.Length > 0) result.Add((section, new Dictionary<string, string>(values))); }
        foreach (var raw in File.ReadLines(path, Encoding.Default))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("''", StringComparison.Ordinal) || line[0] == ';') continue;
            if (line is ['[', .., ']']) { Flush(); section = line[1..^1]; values.Clear(); continue; }
            var separator = line.IndexOf('=');
            if (separator > 0) values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        Flush();
        return result;
    }
}
