namespace AoProject.WorldEditor.Application;

/// <summary>Outcome of saving a world: files written and per-map failures.</summary>
internal sealed record WorldSaveResult(IReadOnlyList<string> Saved, IReadOnlyList<string> Errors);
