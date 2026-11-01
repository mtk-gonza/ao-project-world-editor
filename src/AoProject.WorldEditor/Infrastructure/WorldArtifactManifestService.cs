using System.Security.Cryptography;
using System.Text.Json;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Publishes one manifest for every runtime WORLD artifact.</summary>
internal sealed class WorldArtifactManifestService
{
    private sealed record Artifact(string File, string Sha256, long Length);
    private sealed record Manifest(int Schema, string BundleFormat, string FloorFormat,
        Artifact Bundle, IReadOnlyList<Artifact> Floors, Artifact RuntimeData);

    public void Save(string worldDirectory)
    {
        var bundlePath = Path.Combine(worldDirectory, "bundle.aog");
        var runtimeManifestPath = Path.Combine(worldDirectory, "runtime-data", "manifest.json");
        if (!File.Exists(bundlePath) || !File.Exists(runtimeManifestPath))
            throw new InvalidDataException("No se puede publicar el manifest WORLD antes de generar bundle y runtime-data.");
        var floors = Directory.EnumerateFiles(worldDirectory, "floor-*.gmap")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CreateArtifact(worldDirectory, path))
            .ToArray();
        if (floors.Length == 0) throw new InvalidDataException("El WORLD no contiene floors .gmap.");
        var manifest = new Manifest(1, "AOGBND01/v5", "AOGMAP01/v4",
            CreateArtifact(worldDirectory, bundlePath), floors, CreateArtifact(worldDirectory, runtimeManifestPath));
        File.WriteAllText(Path.Combine(worldDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Artifact CreateArtifact(string root, string path) =>
        new(Path.GetRelativePath(root, path).Replace('\\', '/'),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length);
}
