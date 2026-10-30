using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MultiMapReplacementResult(string Path, int Changed);

/// <summary>Deterministic, non-timer equivalent of frmRemplazo.IterateMaps_Timer.</summary>
internal sealed class MultiMapReplacementService
{
    public IReadOnlyList<MultiMapReplacementResult> ReplaceDirectory(
        string directory, MapReplacementKind kind, int from, int to, int fromLayer, int toLayer,
        IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<NpcData>? npcs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var replacement = new MapReplacementService();
        var maps = Directory.EnumerateFiles(directory, "*.csm", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new List<MultiMapReplacementResult>(maps.Length);
        var io = new SectorCsmFileService();
        foreach (var path in maps)
        {
            var map = io.Load(path);
            var changed = kind switch
            {
                MapReplacementKind.Graphic => replacement.ReplaceGraphic(map, fromLayer, toLayer, from, to),
                MapReplacementKind.Object => replacement.ReplaceObject(map, objects ?? throw new ArgumentNullException(nameof(objects)), checked((short)from), checked((short)to)),
                MapReplacementKind.Npc => replacement.ReplaceNpc(map, npcs ?? throw new ArgumentNullException(nameof(npcs)), checked((short)from), checked((short)to)),
                _ => replacement.ReplaceTrigger(map, checked((short)from), checked((short)to))
            };
            if (changed > 0) io.Save(map, path);
            results.Add(new MultiMapReplacementResult(path, changed));
        }
        return results;
    }
}
