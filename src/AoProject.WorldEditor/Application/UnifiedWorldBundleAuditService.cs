using AoProject.WorldEditor.Domain;
using System.Text.Json;

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
        AuditGlobalSettlements(bundle, issues);
        return new UnifiedWorldBundleAuditResult(issues);
    }

    private static void AuditGlobalSettlements(UnifiedWorldBundle bundle,
        List<UnifiedWorldBundleAuditIssue> issues)
    {
        if (bundle.Settlements.Length == 0) return;
        try
        {
            using var document = JsonDocument.Parse(bundle.Settlements);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new("SETTLEMENTS-FORMAT", "Settlements no contiene un array global."));
                return;
            }
            foreach (var settlement in document.RootElement.EnumerateArray())
            foreach (var name in new[] { "arrival", "travel", "resurrection" })
            {
                if (!settlement.TryGetProperty(name, out var position)) continue;
                if (!position.TryGetProperty("floorZ", out var z) ||
                    !position.TryGetProperty("globalX", out var x) ||
                    !position.TryGetProperty("globalY", out var y))
                {
                    issues.Add(new("SETTLEMENTS-LEGACY-POSITION", $"Settlement {name} no usa coordenadas globales."));
                    continue;
                }
                if (!bundle.Floors.TryGetValue(z.GetInt32(), out var floor) ||
                    x.GetInt32() < 0 || y.GetInt32() < 0 ||
                    x.GetInt32() >= floor.Width || y.GetInt32() >= floor.Height)
                    issues.Add(new("SETTLEMENTS-TILE", $"Settlement {name} referencia un tile/floor inexistente."));
                if (position.TryGetProperty("sectorId", out _) || position.TryGetProperty("x", out _) ||
                    position.TryGetProperty("y", out _))
                    issues.Add(new("SETTLEMENTS-LEGACY-FIELDS", $"Settlement {name} conserva campos legacy."));
            }
        }
        catch (JsonException)
        {
            issues.Add(new("SETTLEMENTS-FORMAT", "Settlements no contiene JSON válido."));
        }
    }
}
