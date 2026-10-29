namespace AoProject.WorldEditor.Application;

internal sealed record WorldLoadProgress(int Total, int Current, string FileName, string? Error);
