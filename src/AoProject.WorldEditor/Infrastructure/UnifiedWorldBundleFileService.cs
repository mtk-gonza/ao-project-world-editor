using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Persists multiple floor maps while keeping each floor independently addressable.</summary>
internal sealed class UnifiedWorldBundleFileService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AOGBND01");
    private const int Version = 4;
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
        // A generated bundle is authoritative for its manifest. Remove stale
        // floor artifacts from an older compile so client/server cannot discover
        // an obsolete floor by directory enumeration.
        foreach (var stale in Directory.EnumerateFiles(directory, "floor-*.gmap"))
            File.Delete(stale);
        var imageDirectory = Path.Combine(directory, "worldmap");
        if (Directory.Exists(imageDirectory))
            foreach (var stale in Directory.EnumerateFiles(imageDirectory, "floor-*.png"))
                File.Delete(stale);
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
            writer.Write(bundle.MapMetadata.Count);
            foreach (var metadata in bundle.MapMetadata.OrderBy(item => item.MapId))
                WriteMetadata(writer, metadata);
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
        if (version >= 4 && reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var metadataCount = reader.ReadInt32();
            if (metadataCount is < 0 or > 100000) throw new InvalidDataException("Cantidad de metadatos inválida.");
            for (var i = 0; i < metadataCount; i++) bundle.MapMetadata.Add(ReadMetadata(reader));
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

    private static void WriteMetadata(BinaryWriter writer, UnifiedWorldMapMetadata metadata)
    {
        var info = metadata.Info;
        writer.Write(metadata.MapId); writer.Write(metadata.FloorZ);
        writer.Write(metadata.MapSize.XMax); writer.Write(metadata.MapSize.XMin);
        writer.Write(metadata.MapSize.YMax); writer.Write(metadata.MapSize.YMin);
        writer.Write(info.Music); writer.Write(info.MusicNumberHigh); writer.Write(info.MusicNumberLow);
        writer.Write(info.Name); writer.Write(info.MapVersion); writer.Write(info.PlayerKilling);
        writer.Write(info.MagicDisabled); writer.Write(info.InvisibilityDisabled);
        writer.Write(info.ResurrectionDisabled); writer.Write(info.NoEncryptMap);
        writer.Write(info.Terrain); writer.Write(info.Zone); writer.Write(info.Restriction);
        writer.Write(info.Backup); writer.Write(info.Safety); writer.Write(info.Ambient);
        writer.Write(info.LetterGraphic); writer.Write(info.Level); writer.Write(info.Extra2);
        writer.Write(info.Exit); writer.Write(info.Changed); writer.Write(info.Light);
        writer.Write(metadata.Environment.BaseLight); writer.Write(metadata.Environment.Rain);
        writer.Write(metadata.Environment.Snow); writer.Write(metadata.Environment.Fog);
    }

    private static UnifiedWorldMapMetadata ReadMetadata(BinaryReader reader)
    {
        var metadata = new UnifiedWorldMapMetadata
        {
            MapId = reader.ReadInt16(), FloorZ = reader.ReadInt32(),
            MapSize = new CsmMapSize(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16())
        };
        var info = metadata.Info;
        info.Music = reader.ReadString(); info.MusicNumberHigh = reader.ReadInt32(); info.MusicNumberLow = reader.ReadInt32();
        info.Name = reader.ReadString(); info.MapVersion = reader.ReadInt16(); info.PlayerKilling = reader.ReadBoolean();
        info.MagicDisabled = reader.ReadByte(); info.InvisibilityDisabled = reader.ReadByte();
        info.ResurrectionDisabled = reader.ReadByte(); info.NoEncryptMap = reader.ReadByte();
        info.Terrain = reader.ReadString(); info.Zone = reader.ReadString(); info.Restriction = reader.ReadString();
        info.Backup = reader.ReadByte(); info.Safety = reader.ReadByte(); info.Ambient = reader.ReadString();
        info.LetterGraphic = reader.ReadInt32(); info.Level = reader.ReadInt32(); info.Extra2 = reader.ReadInt32();
        info.Exit = reader.ReadString(); info.Changed = reader.ReadBoolean(); info.Light = reader.ReadString();
        metadata.Environment.BaseLight = reader.ReadInt32(); metadata.Environment.Rain = reader.ReadByte();
        metadata.Environment.Snow = reader.ReadByte(); metadata.Environment.Fog = reader.ReadByte();
        return metadata;
    }
}
