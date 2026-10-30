using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapNavigationService
{
    public string? ResolveTransferPath(string currentMapPath, WorldPosition transfer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentMapPath);
        if (transfer.Map <= 0) return null;
        if (transfer.X is < 1 or > WorldSector.Width || transfer.Y is < 1 or > WorldSector.Height) throw new InvalidDataException("El destino del traslado está fuera del mapa.");
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(currentMapPath))!, $"mapa{transfer.Map}.csm");
        return File.Exists(path) ? path : null;
    }
}
