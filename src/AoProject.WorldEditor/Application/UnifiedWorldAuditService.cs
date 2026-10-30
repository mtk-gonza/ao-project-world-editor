using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed record UnifiedWorldAuditIssue(string Code, string Message, int X = -1, int Y = -1);

internal sealed record UnifiedWorldAuditResult(IReadOnlyList<UnifiedWorldAuditIssue> Issues)
{
    public int Errors => Issues.Count;
}

/// <summary>Validates the single-map artifact independently of its legacy sources.</summary>
internal sealed class UnifiedWorldAuditService
{
    public UnifiedWorldAuditResult Audit(UnifiedWorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var issues = new List<UnifiedWorldAuditIssue>();
        var tileCount = checked(map.Width * map.Height);
        CheckLength(issues, "GRAPHICS-LENGTH", map.GraphicIndices.Length, tileCount * 8);
        CheckLength(issues, "LIGHTVALUES-LENGTH", map.LightValues.Length, tileCount * 4);
        CheckLength(issues, "COLORS-LENGTH", map.Colors.Length, tileCount * 4);
        CheckLength(issues, "BLOCKED-LENGTH", map.Blocked.Length, tileCount);
        CheckLength(issues, "TRIGGER-LENGTH", map.Trigger.Length, tileCount);
        CheckLength(issues, "CHARACTER-LENGTH", map.CharacterIndex.Length, tileCount);
        CheckLength(issues, "NPC-LENGTH", map.NpcIndex.Length, tileCount);
        CheckLength(issues, "PARTICLE-GROUP-LENGTH", map.ParticleGroup.Length, tileCount);
        CheckLength(issues, "PARTICLE-LENGTH", map.ParticleIndex.Length, tileCount);
        CheckLength(issues, "MARKED-LENGTH", map.Marked.Length, tileCount);
        CheckLength(issues, "OBJECT-LENGTH", map.ObjectIndex.Length, tileCount);
        CheckLength(issues, "OBJECT-AMOUNT-LENGTH", map.ObjectAmount.Length, tileCount);
        CheckLength(issues, "OBJECT-GRAPHIC-LENGTH", map.ObjectGraphicIndex.Length, tileCount);
        CheckLength(issues, "LIGHT-RANGE-LENGTH", map.LightRange.Length, tileCount);
        CheckLength(issues, "LIGHT-COLOR-LENGTH", map.LightColor.Length, tileCount);
        CheckLength(issues, "RED-LENGTH", map.Red.Length, tileCount);
        CheckLength(issues, "GREEN-LENGTH", map.Green.Length, tileCount);
        CheckLength(issues, "BLUE-LENGTH", map.Blue.Length, tileCount);
        CheckLength(issues, "ROOF-LENGTH", map.RoofZoneId.Length, tileCount);
        CheckLength(issues, "EXIT-LENGTH", map.ExitIndex.Length, tileCount);
        CheckLength(issues, "AMBIENT-LENGTH", map.AmbientLight.Length, tileCount);
        CheckLength(issues, "RAIN-LENGTH", map.Rain.Length, tileCount);
        CheckLength(issues, "SNOW-LENGTH", map.Snow.Length, tileCount);
        CheckLength(issues, "FOG-LENGTH", map.Fog.Length, tileCount);
        if (map.Conflicts.Count > 0)
            issues.Add(new("SOURCE-CONFLICT", $"El mapa conserva {map.Conflicts.Count} conflictos de unión."));

        for (var offset = 0; offset < Math.Min(tileCount, map.ExitIndex.Length); offset++)
        {
            var reference = map.ExitIndex[offset];
            if (reference == 0) continue;
            if (reference < 1 || reference > map.Exits.Count)
            {
                AddAt(issues, "EXIT-REFERENCE", "Referencia de salida fuera de rango", map, offset);
                continue;
            }
            var exit = map.Exits[reference - 1];
            var x = offset % map.Width;
            var y = offset / map.Width;
            if (exit.SourceZ != map.FloorZ)
                issues.Add(new("EXIT-SOURCE-Z", "La salida no pertenece al piso del mapa.", x, y));
            if (exit.SourceX != x || exit.SourceY != y)
                issues.Add(new("EXIT-SOURCE", $"La salida {reference} no coincide con su tile origen.", x, y));
            if (exit.Kind == UnifiedWorldExitKind.Internal && exit.TargetZ != map.FloorZ)
                issues.Add(new("EXIT-TARGET-Z", "Una salida interna apunta a otro piso.", x, y));
            if (exit.Kind is UnifiedWorldExitKind.Internal or UnifiedWorldExitKind.CrossFloor &&
                (exit.TargetX < 0 || exit.TargetX >= map.Width || exit.TargetY < 0 || exit.TargetY >= map.Height))
                issues.Add(new("EXIT-TARGET",
                    $"Destino ({exit.TargetX},{exit.TargetY}) fuera de {map.Width}x{map.Height}.", x, y));
            if (exit.Kind == UnifiedWorldExitKind.External && exit.ExternalMapId == 0)
                issues.Add(new("EXIT-EXTERNAL", "Salida externa sin identificador de destino.", x, y));
        }

        foreach (var zone in map.RoofZones)
        {
            if (zone.Id <= 0 || zone.MinX < 0 || zone.MinY < 0 || zone.MaxX >= map.Width || zone.MaxY >= map.Height ||
                zone.MinX > zone.MaxX || zone.MinY > zone.MaxY)
                issues.Add(new("ROOF-ZONE", $"Zona de techo inválida: {zone.Id}."));
        }
        for (var offset = 0; offset < Math.Min(tileCount, map.RoofZoneId.Length); offset++)
        {
            var zone = map.RoofZoneId[offset];
            if (zone > 0 && !map.RoofZones.Any(item => item.Id == zone))
                AddAt(issues, "ROOF-REFERENCE", "Tile referencia una zona de techo inexistente.", map, offset);
        }
        return new UnifiedWorldAuditResult(issues);
    }

    private static void CheckLength(List<UnifiedWorldAuditIssue> issues, string code, int actual, int expected)
    {
        if (actual != expected) issues.Add(new(code, $"Longitud {actual}; esperada {expected}."));
    }

    private static void AddAt(List<UnifiedWorldAuditIssue> issues, string code, string message,
        UnifiedWorldMap map, int offset) => issues.Add(new(code, message, offset % map.Width, offset / map.Width));
}
