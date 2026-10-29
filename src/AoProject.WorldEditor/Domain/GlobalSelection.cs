namespace AoProject.WorldEditor.Domain;

internal readonly record struct GlobalTileSelection(int StartX, int StartY, int EndX, int EndY)
{
    public GlobalTileSelection Normalize() => new(Math.Min(StartX, EndX), Math.Min(StartY, EndY),
        Math.Max(StartX, EndX), Math.Max(StartY, EndY));
}
