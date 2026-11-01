using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Converts special-map pools into floor/global-coordinate manifests.</summary>
internal sealed class GlobalSpecialManifestService
{
    public byte[] TransformUnderworld(byte[] source, UnifiedWorldBundle bundle)
    {
        if (source.Length == 0) return [];
        var sections = ReadSections(source);
        var portals = sections
            .Where(item => item.Name.StartsWith("Portal", StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                source = Position(item.Values, "SourceMap", "SourceX", "SourceY", bundle),
                destination = Position(item.Values, "DestinationMap", "DestinationX", "DestinationY", bundle)
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
        var sections = ReadSections(source);
        var maps = sections.FirstOrDefault(item => item.Name.Equals("Maps", StringComparison.OrdinalIgnoreCase));
        var areas = maps.Values
            .Where(item => item.Key.StartsWith("Map", StringComparison.OrdinalIgnoreCase))
            .Select(item => short.TryParse(item.Value, out var id) ? id : (short)0)
            .Where(id => id > 0).Distinct()
            .Select(id => Area(id, bundle)).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            worldId = "phoenix",
            areas
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Position(IReadOnlyDictionary<string, string> values,
        string mapKey, string xKey, string yKey, UnifiedWorldBundle bundle)
    {
        var map = ReadShort(values, mapKey);
        var x = ReadInt(values, xKey);
        var y = ReadInt(values, yKey);
        return Resolve(map, x, y, bundle, mapKey);
    }

    private static object Area(short sectorId, UnifiedWorldBundle bundle)
    {
        if (!bundle.Placements.TryGetValue(sectorId, out var placement) ||
            !bundle.SectorFloors.TryGetValue(sectorId, out var floorZ) ||
            !bundle.Floors.TryGetValue(floorZ, out var floor))
            throw new InvalidDataException($"Pool Phoenix referencia el sector {sectorId} sin placement global.");
        var min = GlobalTile(placement, 1, 1, bundle);
        var max = GlobalTile(placement, WorldSector.Width, WorldSector.Height, bundle);
        return new
        {
            floorZ,
            minGlobalX = Math.Max(0, min.X),
            minGlobalY = Math.Max(0, min.Y),
            maxGlobalX = Math.Min(floor.Width - 1, max.X),
            maxGlobalY = Math.Min(floor.Height - 1, max.Y)
        };
    }

    private static object Resolve(short sectorId, int localX, int localY,
        UnifiedWorldBundle bundle, string field)
    {
        if (!bundle.Placements.TryGetValue(sectorId, out var placement) ||
            !bundle.SectorFloors.TryGetValue(sectorId, out var floorZ) ||
            !bundle.Floors.TryGetValue(floorZ, out var floor))
            throw new InvalidDataException($"Pool especial referencia {field} del sector {sectorId} sin placement global.");
        var global = GlobalTile(placement, localX, localY, bundle);
        if (global.X < 0 || global.Y < 0 || global.X >= floor.Width || global.Y >= floor.Height)
            throw new InvalidDataException($"Pool especial referencia un tile fuera de Z={floorZ}: ({global.X},{global.Y}).");
        return new { floorZ, globalX = global.X, globalY = global.Y };
    }

    private static (int X, int Y) GlobalTile(GlobalSectorPlacement placement, int x, int y,
        UnifiedWorldBundle bundle)
    {
        var floorZ = bundle.SectorFloors[placement.SectorId];
        var minX = bundle.Placements.Values.Where(item => bundle.SectorFloors[item.SectorId] == floorZ).Min(item => item.Chunk.X);
        var minY = bundle.Placements.Values.Where(item => bundle.SectorFloors[item.SectorId] == floorZ).Min(item => item.Chunk.Y);
        return ((placement.Chunk.X - minX) * GlobalChunkGeometry.WidthTiles + x - 1 - placement.StitchX,
            (placement.Chunk.Y - minY) * GlobalChunkGeometry.HeightTiles + y - 1 - placement.StitchY);
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
