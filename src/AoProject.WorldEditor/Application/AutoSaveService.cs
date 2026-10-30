using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed class AutoSaveService
{
    public const int MinimumMinutes = 5;
    public const int MaximumMinutes = 120;

    public int IntervalMinutes { get; private set; }
    public bool Enabled => IntervalMinutes > 0;

    public void Configure(int minutes)
    {
        if (minutes is < MinimumMinutes or > MaximumMinutes)
            throw new ArgumentOutOfRangeException(nameof(minutes), $"El intervalo debe estar entre {MinimumMinutes} y {MaximumMinutes} minutos.");
        IntervalMinutes = minutes;
    }

    public void Stop() => IntervalMinutes = 0;

    public string? Save(WorldSector? map, string? sourcePath, string destinationRoot)
    {
        if (!Enabled || map is null || string.IsNullOrWhiteSpace(sourcePath)) return null;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".csm";
        var destination = Path.Combine(Path.GetFullPath(destinationRoot), fileName);
        new SectorCsmFileService().Save(map, destination);
        return destination;
    }
}
