namespace AoProject.WorldEditor.Domain;

/// <summary>
/// Canonical runtime position. A WORLD tile is identified only by its global
/// coordinates; map ids belong to legacy source/import data.
/// </summary>
internal readonly record struct GlobalTileCoordinate(int X, int Y);

/// <summary>Destination of a runtime transfer in the global coordinate space.</summary>
internal readonly record struct GlobalExitTarget(int X, int Y);
