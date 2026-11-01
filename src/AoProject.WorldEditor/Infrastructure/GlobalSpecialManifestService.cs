using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Converts special-map pools into floor/global-coordinate manifests.</summary>
internal sealed class GlobalSpecialManifestService
{
    public byte[] TransformUnderworld(byte[] source, UnifiedWorldBundle bundle)
    {
        if (source.Length == 0) return [];
        var spatial = new WorldSpatialResolver(bundle);
        var sections = ReadSections(source);
        var portals = sections
            .Where(item => item.Name.StartsWith("Portal", StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                source = Position(item.Values, "SourceMap", "SourceX", "SourceY", spatial),
                destination = Position(item.Values, "DestinationMap", "DestinationX", "DestinationY", spatial)
            }).ToArray();
        var init = sections.FirstOrDefault(item => item.Name.Equals("INIT", StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            worldId = "underworld",
            lowerHour = IntValue(init.Values ?? new Dictionary<string, string>(), "UnderworldLowerLimitOfTime"),
            upperHour = IntValue(init.Values ?? new Dictionary<string, string>(), "UnderworldUpperLimitOfTime"),
            portals
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public byte[] TransformPhoenix(byte[] source, UnifiedWorldBundle bundle)
    {
        if (source.Length == 0) return [];
        var spatial = new WorldSpatialResolver(bundle);
        var sections = ReadSections(source);
        var maps = sections.FirstOrDefault(item => item.Name.Equals("Maps", StringComparison.OrdinalIgnoreCase));
        var areas = maps.Values
            .Where(item => item.Key.StartsWith("Map", StringComparison.OrdinalIgnoreCase))
            .Select(item => short.TryParse(item.Value, out var id) ? id : (short)0)
            .Where(id => id > 0).Distinct()
            .Select(id => Area(id, spatial)).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            worldId = "phoenix",
            areas
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Position(IReadOnlyDictionary<string, string> values,
        string mapKey, string xKey, string yKey, WorldSpatialResolver spatial)
    {
        var map = ReadShort(values, mapKey);
        var x = ReadInt(values, xKey);
        var y = ReadInt(values, yKey);
        return Resolve(map, x, y, spatial, mapKey);
    }

    private static object Area(short sectorId, WorldSpatialResolver spatial)
    {
        if (!spatial.TryResolveArea(sectorId, out var area))
            throw new InvalidDataException($"Pool Phoenix referencia el sector {sectorId} sin placement global.");
        return new
        {
            floorZ = area.FloorZ,
            minGlobalX = area.MinX,
            minGlobalY = area.MinY,
            maxGlobalX = area.MaxX,
            maxGlobalY = area.MaxY
        };
    }

    private static object Resolve(short sectorId, int localX, int localY,
        WorldSpatialResolver spatial, string field)
    {
        if (!spatial.TryResolve(sectorId, localX, localY, out var point))
            throw new InvalidDataException($"Pool especial referencia {field} del sector {sectorId} sin placement global.");
        return new { floorZ = point.FloorZ, globalX = point.GlobalX, globalY = point.GlobalY };
    }

    private static short ReadShort(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && short.TryParse(value, out var result) ? result : (short)0;

    private static int IntValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : 0;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : 0;

    private static IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Values)> ReadSections(byte[] source)
    {
        var result = new List<(string, IReadOnlyDictionary<string, string>)>();
        string name = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Flush() { if (name.Length > 0) result.Add((name, new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase))); }
        foreach (var raw in Encoding.Default.GetString(source).Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("''") || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { Flush(); name = line[1..^1]; values.Clear(); continue; }
            var separator = line.IndexOf('=');
            if (separator > 0) values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        Flush();
        return result;
    }
}
