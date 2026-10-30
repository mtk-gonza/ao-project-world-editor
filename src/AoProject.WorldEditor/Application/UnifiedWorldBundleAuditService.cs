using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed record UnifiedWorldBundleAuditIssue(string Code, string Message, int? Z = null);

internal sealed record UnifiedWorldBundleAuditResult(IReadOnlyList<UnifiedWorldBundleAuditIssue> Issues)
{
    public int Errors => Issues.Count;
}

internal sealed class UnifiedWorldBundleAuditService
{
    public UnifiedWorldBundleAuditResult Audit(UnifiedWorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var issues = new List<UnifiedWorldBundleAuditIssue>();
        if (bundle.Floors.Count == 0)
            issues.Add(new("EMPTY-BUNDLE", "El bundle no contiene pisos."));
        if (bundle.ActiveFloorZ is not null && !bundle.Floors.ContainsKey(bundle.ActiveFloorZ.Value))
            issues.Add(new("ACTIVE-FLOOR", "El piso activo no existe en el bundle.", bundle.ActiveFloorZ));
        var duplicateChunks = bundle.Placements.Values
            .Where(item => bundle.SectorFloors.ContainsKey(item.SectorId))
            .GroupBy(item => (FloorZ: bundle.SectorFloors[item.SectorId], item.Chunk))
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateChunks)
            issues.Add(new("DUPLICATE-PLACEMENT", $"Hay placements duplicados en Z={group.Key.FloorZ}, ChunkX={group.Key.Chunk.X}, ChunkY={group.Key.Chunk.Y}."));
        foreach (var placement in bundle.Placements.Values)
        {
            if (placement.Chunk.X < 0 || placement.Chunk.X > 18 || placement.Chunk.Y < 0)
                issues.Add(new("PLACEMENT-BOUNDS", $"El placement {placement.SectorId} excede el layout válido."));
            if (!bundle.SectorFloors.TryGetValue(placement.SectorId, out var z) || !bundle.Floors.ContainsKey(z))
                issues.Add(new("MISSING-MAP-FLOOR", $"El placement {placement.SectorId} no tiene un floor declarado."));
        }

        var floorAudit = new UnifiedWorldAuditService();
        foreach (var (z, floor) in bundle.Floors)
        {
            if (floor.FloorZ != z)
                issues.Add(new("FLOOR-IDENTITY", "La identidad Z del mapa no coincide con el bundle.", z));
            issues.AddRange(floorAudit.Audit(floor).Issues.Select(item =>
                new UnifiedWorldBundleAuditIssue(item.Code, item.Message, z)));
            foreach (var exit in floor.Exits.Where(item => item.Kind == UnifiedWorldExitKind.CrossFloor))
                if (!bundle.Floors.ContainsKey(exit.TargetZ))
                    issues.Add(new("CROSS-FLOOR-TARGET", $"La salida apunta a Z={exit.TargetZ}, que no existe.", z));
        }
        return new UnifiedWorldBundleAuditResult(issues);
    }
}
