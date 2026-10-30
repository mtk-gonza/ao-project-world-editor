using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct BatchMapExportResult(string SourcePath, string DestinationPath, bool Exported);

/// <summary>Deterministic equivalent of FrmRender.SaveAll_Timer (batch map capture).</summary>
internal sealed class BatchMapExportService
{
    public IReadOnlyList<BatchMapExportResult> ExportDirectory(
        string sourceDirectory, string destinationDirectory,
        IReadOnlyList<GraphicData> graphics, string resourceRoot,
        IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<ParticleData>? particles = null,
        IReadOnlyList<NpcData>? npcs = null, IReadOnlyList<DirectionalAnimation>? bodies = null,
        IReadOnlyList<HeadIndexData>? heads = null,
        IReadOnlyList<DirectionalAnimation>? helmets = null,
        IReadOnlyList<DirectionalAnimation>? weapons = null,
        IReadOnlyList<DirectionalAnimation>? shields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(graphics);
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var maps = Directory.EnumerateFiles(sourceDirectory, "*.csm", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var capture = new MapImageCaptureService();
        var results = new List<BatchMapExportResult>(maps.Length);
        foreach (var path in maps)
        {
            var destination = Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(path) + ".png");
            try
            {
                var map = new Infrastructure.SectorCsmFileService().Load(path);
                capture.SavePng(map, graphics, resourceRoot, destination, objects: objects, particles: particles,
                    npcs: npcs, bodies: bodies, heads: heads, helmets: helmets, weapons: weapons, shields: shields);
                results.Add(new BatchMapExportResult(path, destination, true));
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError($"WorldEditor batch export failed for '{path}': {error}");
                results.Add(new BatchMapExportResult(path, destination, false));
            }
        }
        return results;
    }
}
