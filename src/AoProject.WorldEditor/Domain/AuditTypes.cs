namespace AoProject.WorldEditor.Domain;

internal enum AuditSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A finding for one CSM file or one cell of it.</summary>
internal sealed record MapAuditIssue(
    short MapId,
    string Code,
    AuditSeverity Severity,
    string Message,
    int X = 0,
    int Y = 0,
    int Layer = -1);

/// <summary>Canonical border exit indexed from a map (used by the world audit).</summary>
internal sealed record MapBorderExit(
    GlobalWorldSide Side,
    short Target,
    int X,
    int Y,
    int TargetX,
    int TargetY);

/// <summary>Compact per-map data kept after the map audit for the world coordinate pass.</summary>
internal sealed record MapCoordinateInfo(
    short MapId,
    short XMin,
    short XMax,
    short YMin,
    short YMax,
    IReadOnlyList<MapBorderExit> BorderExits);

internal sealed record MapAuditSummary(
    short MapId,
    string File,
    int FileSize,
    string Name,
    int Errors,
    int Warnings,
    int Infos,
    short XMin,
    short XMax,
    short YMin,
    short YMax,
    int Blocked,
    int Triggers,
    int Lights,
    int Particles,
    int Npcs,
    int Objects,
    int Exits,
    IReadOnlyList<MapAuditIssue> Issues,
    IReadOnlyList<MapBorderExit> BorderExits);

/// <summary>A finding for the official world grid or the local/global coordinate mapping.</summary>
internal sealed record WorldAuditIssue(
    int WorldId,
    short MapId,
    string Code,
    AuditSeverity Severity,
    string Message,
    int ChunkX = 0,
    int ChunkY = 0,
    int GlobalX = 0,
    int GlobalY = 0);

internal sealed record WorldAuditSummary(
    int WorldId,
    int Width,
    int Height,
    int Placements,
    int Errors,
    int Warnings,
    int Infos,
    IReadOnlyList<WorldAuditIssue> Issues);
