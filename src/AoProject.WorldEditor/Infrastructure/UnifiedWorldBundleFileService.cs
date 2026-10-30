using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Persists multiple floor maps while keeping each floor independently addressable.</summary>
internal sealed class UnifiedWorldBundleFileService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AOGBND01");
    private const int Version = 3;
    private readonly UnifiedWorldFileService _floorFiles = new();

    public void Save(UnifiedWorldBundle bundle, string directory)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var audit = new Application.UnifiedWorldBundleAuditService().Audit(bundle);
        if (audit.Errors > 0)
            throw new InvalidDataException("El bundle no supera la auditoría: " +
                string.Join(" | ", audit.Issues.Take(5).Select(item => item.Code + ": " + item.Message)));
        Directory.CreateDirectory(directory);
        var floors = bundle.Floors.Keys.ToArray();
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "bundle.aog")), Encoding.UTF8))
        {
            writer.Write(Magic); writer.Write(Version); writer.Write(bundle.ActiveFloorZ ?? floors[0]);
            writer.Write(floors.Length);
            foreach (var z in floors) writer.Write(z);
            writer.Write(bundle.Placements.Count);
            foreach (var placement in bundle.Placements.Values.OrderBy(item => item.MapId))
            {
                writer.Write(placement.MapId);
                writer.Write(placement.Chunk.X);
                writer.Write(placement.Chunk.Y);
                writer.Write(placement.LevelId ?? string.Empty);
                writer.Write(placement.StitchX);
                writer.Write(placement.StitchY);
            }
            writer.Write(bundle.MapFloors.Count);
            foreach (var item in bundle.MapFloors.OrderBy(item => item.Key))
            {
                writer.Write(item.Key);
                writer.Write(item.Value);
            }
        }
        foreach (var z in floors)
            _floorFiles.Save(bundle.Floors[z], Path.Combine(directory, FloorFileName(z)));
    }

    public UnifiedWorldBundle Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "bundle.aog")), Encoding.UTF8);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Firma o versión de bundle inválida.");
        var version = reader.ReadInt32();
        if (version is < 1 or > Version)
            throw new InvalidDataException("Firma o versión de bundle inválida.");
        var active = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (count is <= 0 or > 256) throw new InvalidDataException("Cantidad de pisos inválida.");
        var bundle = new UnifiedWorldBundle();
        var floorZs = new int[count];
        for (var i = 0; i < count; i++) floorZs[i] = reader.ReadInt32();
        var placementCount = version >= 2 ? reader.ReadInt32() : 0;
        if (placementCount is < 0 or > 100000) throw new InvalidDataException("Cantidad de placements inválida.");
        var placements = new List<GlobalMapPlacement>(placementCount);
        for (var i = 0; i < placementCount; i++)
        {
            var mapId = reader.ReadInt16();
            var chunk = new GlobalChunkCoordinate(reader.ReadInt32(), reader.ReadInt32());
            var levelId = reader.ReadString();
            var stitchX = version >= 3 ? reader.ReadInt32() : 0;
            var stitchY = version >= 3 ? reader.ReadInt32() : 0;
            placements.Add(new GlobalMapPlacement(mapId, chunk, levelId, stitchX, stitchY));
        }
        bundle.SetPlacements(placements);
        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var mapFloorCount = reader.ReadInt32();
            if (mapFloorCount is < 0 or > 100000) throw new InvalidDataException("Cantidad de pisos por mapa inválida.");
            var mapFloors = new List<KeyValuePair<short, int>>(mapFloorCount);
            for (var i = 0; i < mapFloorCount; i++)
                mapFloors.Add(new KeyValuePair<short, int>(reader.ReadInt16(), reader.ReadInt32()));
            bundle.SetMapFloors(mapFloors);
        }
        foreach (var z in floorZs)
        {
            var floor = _floorFiles.Load(Path.Combine(directory, FloorFileName(z)));
            if (floor.FloorZ != z) throw new InvalidDataException("El piso no coincide con su manifiesto.");
            bundle.Add(floor);
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("El manifiesto del bundle contiene datos adicionales.");
        bundle.Select(active);
        var audit = new Application.UnifiedWorldBundleAuditService().Audit(bundle);
        if (audit.Errors > 0)
            throw new InvalidDataException("El bundle cargado no supera la auditoría: " +
                string.Join(" | ", audit.Issues.Take(5).Select(item => item.Code + ": " + item.Message)));
        return bundle;
    }

    private static string FloorFileName(int z) => $"floor-{z:+#;-#;0}.gmap";
}
