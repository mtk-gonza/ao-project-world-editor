using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Punto resuelto en el contrato espacial del WORLD (FloorZ + global).</summary>
internal readonly record struct SpatialPoint(int FloorZ, int GlobalX, int GlobalY);

/// <summary>Area rectangular resuelta de un sector en el WORLD.</summary>
internal readonly record struct SpatialArea(
    short SectorId,
    int FloorZ,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY);

/// <summary>
/// Traduce referencias legacy (SectorId + coordenada local 1..100) al contrato
/// espacial del WORLD compilado: piso (FloorZ) y coordenadas globales. Usa los
/// placements y el stitching del bundle, con la misma formula que
/// <c>SectorCoordinateService</c> del servidor:
/// <c>global = (chunk - minChunkDelPiso) * stride - stitch + local - 1</c>.
///
/// Las claves legacy se conservan aparte para provenance; este resolver solo
/// agrega la lectura ya acomodada al WORLD.
/// </summary>
internal sealed class WorldSpatialResolver
{
    private readonly IReadOnlyDictionary<short, GlobalSectorPlacement> placements;
    private readonly IReadOnlyDictionary<short, int> sectorFloors;
    private readonly IReadOnlyDictionary<int, (int X, int Y)> floorMinChunks;
    private readonly IReadOnlyDictionary<int, (int Width, int Height)> floorSizes;

    public WorldSpatialResolver(UnifiedWorldBundle bundle)
    {
        placements = bundle.Placements;
        sectorFloors = bundle.SectorFloors;
        floorMinChunks = placements.Values
            .Where(item => sectorFloors.ContainsKey(item.SectorId))
            .GroupBy(item => sectorFloors[item.SectorId])
            .ToDictionary(group => group.Key,
                group => (group.Min(item => item.Chunk.X), group.Min(item => item.Chunk.Y)));
        floorSizes = bundle.Floors.ToDictionary(
            item => item.Key, item => (item.Value.Width, item.Value.Height));
    }

    /// <summary>Sector resoluble: tiene placement, piso y piso compilado.</summary>
    public bool Contains(short sectorId) =>
        placements.ContainsKey(sectorId) &&
        sectorFloors.TryGetValue(sectorId, out var floorZ) &&
        floorSizes.ContainsKey(floorZ);

    public bool TryGetFloorZ(short sectorId, out int floorZ) => sectorFloors.TryGetValue(sectorId, out floorZ);

    /// <summary>Resuelve un tile local exacto a su posicion global en el WORLD.</summary>
    public bool TryResolve(short sectorId, int localX, int localY, out SpatialPoint point)
    {
        point = default;
        if (!Contains(sectorId)) return false;
        var global = GlobalTile(placements[sectorId], localX, localY);
        point = new SpatialPoint(sectorFloors[sectorId], global.X, global.Y);
        return true;
    }

    /// <summary>Resuelve los limites de un mapa legacy a un rectangulo global.</summary>
    public bool TryResolveArea(short sectorId, out SpatialArea area)
    {
        area = default;
        if (!Contains(sectorId)) return false;
        var placement = placements[sectorId];
        var floorZ = sectorFloors[sectorId];
        var min = GlobalTile(placement, 1, 1);
        var max = GlobalTile(placement, WorldSector.Width, WorldSector.Height);
        var (width, height) = floorSizes[floorZ];
        area = new SpatialArea(sectorId, floorZ,
            Math.Max(0, min.X), Math.Max(0, min.Y),
            Math.Min(width - 1, max.X), Math.Min(height - 1, max.Y));
        return true;
    }

    private (int X, int Y) GlobalTile(GlobalSectorPlacement placement, int localX, int localY)
    {
        var (minChunkX, minChunkY) = floorMinChunks[sectorFloors[placement.SectorId]];
        return ((placement.Chunk.X - minChunkX) * GlobalChunkGeometry.WidthTiles - placement.StitchX + localX - 1,
            (placement.Chunk.Y - minChunkY) * GlobalChunkGeometry.HeightTiles - placement.StitchY + localY - 1);
    }
}
