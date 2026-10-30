using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed record GlobalBlockingAuditIssue(
    short SectorId, int X, int Y, string Code, string Message, byte Mask);

internal sealed record GlobalBlockingAuditReport(
    IReadOnlyList<GlobalBlockingAuditIssue> Issues,
    int MapsVisited,
    int CellsVisited)
{
    public string ToTsv() => "SectorId\tX\tY\tCode\tMask\tMessage\r\n" +
        string.Join("\r\n", Issues.Select(i => $"{i.SectorId}\t{i.X}\t{i.Y}\t{i.Code}\t{i.Mask}\t{i.Message}"));
}

/// <summary>Read-only audit of materialized global CSM maps.</summary>
internal sealed class GlobalBlockingAuditService
{
    public GlobalBlockingAuditReport Audit(
        GlobalWorldDocument world,
        IReadOnlyList<ObjectData>? objects = null,
        IReadOnlyList<BlockPresetType>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var graphicMasks = (presets ?? []).SelectMany(type => type.Items
                .Where(item => item.DefaultBlockedMask != 0)
                .Select(item => (item.GraphicIndex, item.DefaultBlockedMask)))
            .GroupBy(item => item.GraphicIndex).ToDictionary(group => group.Key, group => group.First().DefaultBlockedMask);
        var issues = new List<GlobalBlockingAuditIssue>();
        var maps = 0; var cells = 0;
        foreach (var entry in world.Maps)
        {
            var mapId = entry.Key;
            var map = entry.Value;
            maps++;
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var y = 1; y <= WorldSector.Height; y++)
            {
                cells++;
                var block = map.Blocks[x, y];
                if (!BlockedMask.IsValid(block.Blocked))
                    issues.Add(new(mapId, x, y, "INVALID_MASK", "La máscara contiene bits fuera del nibble direccional.", block.Blocked));
                foreach (var graphic in block.Graphics.Select(g => g.GraphicIndex).Where(g => g != 0))
                    if (graphicMasks.TryGetValue(graphic, out var expected) && block.Blocked == 0)
                        issues.Add(new(mapId, x, y, "GRAPHIC_WITHOUT_BLOCK", $"El gráfico {graphic} requiere máscara {expected}.", block.Blocked));
                if (objects is null) continue;
                var index = block.Object.Index;
                if (index <= 0 || index >= objects.Count || objects[index] is null) continue;
                var expectedObject = objects[index].DefaultBlockedMask;
                if (expectedObject != 0 && block.Blocked == 0)
                    issues.Add(new(mapId, x, y, "OBJECT_WITHOUT_BLOCK", $"El objeto {index} requiere máscara {expectedObject}.", block.Blocked));
                else if (expectedObject != 0 && block.Blocked != expectedObject)
                    issues.Add(new(mapId, x, y, "OBJECT_MASK_MISMATCH", $"El objeto {index} requiere máscara {expectedObject}.", block.Blocked));
            }
        }
        return new(issues, maps, cells);
    }

    public string WriteReport(GlobalWorldDocument world, string path,
        IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<BlockPresetType>? presets = null)
    {
        var report = Audit(world, objects, presets);
        return WriteReport(report, path);
    }

    public string WriteReport(GlobalBlockingAuditReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, report.ToTsv());
        return path;
    }

    /// <summary>Applies only to copied CSMs; source paths are never written.</summary>
    public void ApplyCorrectionsToCopy(GlobalWorldDocument world, string destinationRoot,
        IReadOnlyList<ObjectData> objects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        var destination = Path.GetFullPath(destinationRoot);
        if (world.Paths.Values.Any(path => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
            destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("La auditoría correctiva requiere una carpeta distinta de los CSM originales.");
        Directory.CreateDirectory(destination);
        var csm = new SectorCsmFileService();
        foreach (var mapId in world.Paths.Keys.OrderBy(id => id))
        {
            var copy = Path.Combine(destination, $"mapa{mapId}.csm");
            if (world.Paths.TryGetValue(mapId, out var path)) File.Copy(path, copy, true);
            else continue;
            var map = csm.Load(copy);
            new TreePlacementService().BlockConfiguredObjects(map, objects);
            csm.Save(map, copy);
        }
    }
}
