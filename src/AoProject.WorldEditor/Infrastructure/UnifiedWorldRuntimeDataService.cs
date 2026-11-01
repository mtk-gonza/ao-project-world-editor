using System.Security.Cryptography;
using System.Text.Json;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Writes large gameplay catalogs beside the spatial bundle.</summary>
internal sealed class UnifiedWorldRuntimeDataService
{
    private sealed record Manifest(int Schema, Dictionary<string, string> Files);

    public void Save(UnifiedWorldBundle bundle, string worldDirectory)
    {
        var directory = Path.Combine(worldDirectory, "runtime-data");
        Directory.CreateDirectory(directory);
        var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["events.dat"] = bundle.Events,
            ["treasures.dat"] = bundle.Treasures,
            ["quests.dat"] = bundle.Quests,
            ["npcs.dat"] = bundle.Npcs,
            ["objects.dat"] = bundle.Objects,
            ["castles.json"] = NormalizeCastles(bundle.Castles)
        };
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads)
        {
            if (payload.Value.Length == 0)
                throw new InvalidDataException($"El WORLD no contiene el payload requerido: {payload.Key}.");
            var path = Path.Combine(directory, payload.Key);
            File.WriteAllBytes(path, payload.Value);
            hashes[payload.Key] = Convert.ToHexString(SHA256.HashData(payload.Value));
        }
        var manifest = new Manifest(1, hashes);
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] NormalizeCastles(byte[] payload)
    {
        if (payload.Length == 0)
            return "{\"schema\":1,\"catalog\":\"castles\",\"items\":[]}"u8.ToArray();

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            HasProperty(root, "schema") && HasProperty(root, "items"))
            return payload;
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Castles.json debe contener una lista JSON de castillos.");

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            Catalog = "castles",
            Items = root
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
