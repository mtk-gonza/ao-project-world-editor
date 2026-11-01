using System.Text.Json;
using System.Text.Json.Nodes;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Converts the settlement import manifest from legacy map coordinates to the
/// coordinates written to the compiled floors. Legacy fields are deliberately
/// not copied to the runtime manifest.
/// </summary>
internal sealed class GlobalSettlementManifestService
{
    public byte[] Transform(byte[] source, UnifiedWorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bundle);
        if (source.Length == 0) return [];

        var root = JsonNode.Parse(source) as JsonArray
            ?? throw new InvalidDataException("El manifiesto de settlements debe ser un array JSON.");
        var placementsBySector = bundle.Placements;
        var floorsByZ = bundle.Floors;
        var floorsBySector = bundle.SectorFloors;
        var minChunks = placementsBySector.Values
            .GroupBy(item => floorsBySector[item.SectorId])
            .ToDictionary(group => group.Key,
                group => (X: group.Min(item => item.Chunk.X), Y: group.Min(item => item.Chunk.Y)));
        var output = new JsonArray();

        foreach (var item in root)
        {
            if (item is not JsonObject settlement) continue;
            var converted = new JsonObject();
            foreach (var property in settlement)
            {
                if (property.Key is "arrival" or "travel" or "resurrection") continue;
                converted[property.Key] = property.Value?.DeepClone();
            }
            foreach (var name in new[] { "arrival", "travel", "resurrection" })
            {
                if (settlement[name] is not JsonObject position)
                    throw new InvalidDataException($"Settlement sin posición {name}.");
                converted[name] = ConvertPosition(position, name, placementsBySector,
                    floorsBySector, floorsByZ, minChunks);
            }
            output.Add(converted);
        }

        return JsonSerializer.SerializeToUtf8Bytes(output, new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject ConvertPosition(JsonObject source, string name,
        IReadOnlyDictionary<short, GlobalSectorPlacement> placements,
        IReadOnlyDictionary<short, int> floorsBySector,
        IReadOnlyDictionary<int, UnifiedWorldMap> floors,
        IReadOnlyDictionary<int, (int X, int Y)> minChunks)
    {
        if (source["sectorId"] is null && source["globalX"] is not null &&
            source["globalY"] is not null && source["floorZ"] is not null)
        {
            var existingZ = ReadInt(source, "floorZ", name);
            var existingX = ReadInt(source, "globalX", name);
            var existingY = ReadInt(source, "globalY", name);
            if (!floors.TryGetValue(existingZ, out var existingFloor) || existingX < 0 ||
                existingY < 0 || existingX >= existingFloor.Width || existingY >= existingFloor.Height)
                throw new InvalidDataException($"Settlement {name} referencia un tile/floor global inexistente.");
            return new JsonObject { ["floorZ"] = existingZ, ["globalX"] = existingX, ["globalY"] = existingY };
        }
        var sectorId = ReadInt(source, "sectorId", name);
        var localX = ReadInt(source, "x", name);
        var localY = ReadInt(source, "y", name);
        if (!placements.TryGetValue((short)sectorId, out var placement) ||
            !floorsBySector.TryGetValue((short)sectorId, out var floorZ) ||
            !floors.TryGetValue(floorZ, out var floor) || !minChunks.TryGetValue(floorZ, out var minChunk))
            throw new InvalidDataException($"Settlement {name} referencia el sector {sectorId} sin placement global.");
        var stitchX = placement.StitchX;
        var stitchY = placement.StitchY;
        var globalX = (placement.Chunk.X - minChunk.X) * GlobalChunkGeometry.WidthTiles + localX - 1 - stitchX;
        var globalY = (placement.Chunk.Y - minChunk.Y) * GlobalChunkGeometry.HeightTiles + localY - 1 - stitchY;
        if (globalX < 0 || globalY < 0 || globalX >= floor.Width || globalY >= floor.Height)
            throw new InvalidDataException($"Settlement {name} queda fuera del floor Z={floorZ}: ({globalX},{globalY}).");
        _ = floor.Offset(globalX, globalY); // Explicitly validate the compiled tile.
        return new JsonObject { ["floorZ"] = floorZ, ["globalX"] = globalX, ["globalY"] = globalY };
    }

    private static int ReadInt(JsonObject source, string name, string positionName)
    {
        if (source[name]?.GetValue<int>() is not int value)
            throw new InvalidDataException($"Settlement {positionName} no contiene {name}.");
        return value;
    }
}
