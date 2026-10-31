using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Persists the compiled, single-grid world artifact.</summary>
internal sealed class UnifiedWorldFileService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AOGMAP01");
    private const int Version = 4;
    private const int MaxDimension = 100_000;
    private const int MaxRecords = 20_000_000;

    public void Save(UnifiedWorldMap map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (map.Conflicts.Count > 0)
            throw new InvalidDataException($"No se puede guardar un mundo con {map.Conflicts.Count} conflictos.");
        var audit = new Application.UnifiedWorldAuditService().Audit(map);
        if (audit.Errors > 0)
            throw new InvalidDataException("El mapa global no supera la auditoría: " +
                string.Join(" | ", audit.Issues.Take(5).Select(issue => issue.Code + ": " + issue.Message)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8, leaveOpen: false);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(map.Width);
        writer.Write(map.Height);
        writer.Write(map.FloorZ);
        WriteArray(writer, map.GraphicIndices);
        WriteArray(writer, map.LightValues);
        WriteArray(writer, map.Colors);
        WriteArray(writer, map.Blocked);
        WriteArray(writer, map.Trigger);
        WriteArray(writer, map.CharacterIndex);
        WriteArray(writer, map.NpcIndex);
        WriteArray(writer, map.ParticleGroup);
        WriteArray(writer, map.ParticleIndex);
        WriteArray(writer, map.Marked);
        WriteArray(writer, map.ObjectIndex);
        WriteArray(writer, map.ObjectAmount);
        WriteArray(writer, map.ObjectGraphicIndex);
        WriteArray(writer, map.LightRange);
        WriteArray(writer, map.LightColor);
        WriteArray(writer, map.Red);
        WriteArray(writer, map.Green);
        WriteArray(writer, map.Blue);
        WriteArray(writer, map.RoofZoneId);
        WriteArray(writer, map.ExitIndex);
        WriteArray(writer, map.AmbientLight);
        WriteArray(writer, map.Rain);
        WriteArray(writer, map.Snow);
        WriteArray(writer, map.Fog);
        WriteArray(writer, map.Water);
        WriteArray(writer, map.Lava);

        writer.Write(map.Exits.Count);
        foreach (var exit in map.Exits)
        {
            writer.Write((byte)exit.Kind);
            writer.Write(exit.SourceX); writer.Write(exit.SourceY);
            writer.Write(exit.TargetX); writer.Write(exit.TargetY);
            writer.Write(exit.ExternalSectorId); writer.Write(exit.ExternalX); writer.Write(exit.ExternalY);
            writer.Write(exit.SourceZ); writer.Write(exit.TargetZ); writer.Write(exit.ExternalZ);
        }
        writer.Write(map.RoofZones.Count);
        foreach (var zone in map.RoofZones)
        {
            writer.Write(zone.Id); writer.Write(zone.Trigger);
            writer.Write(zone.MinX); writer.Write(zone.MinY);
            writer.Write(zone.MaxX); writer.Write(zone.MaxY); writer.Write(zone.Z);
        }
        writer.Write(map.EnvironmentProfiles.Count);
        foreach (var profile in map.EnvironmentProfiles)
        {
            writer.Write(profile.MinX); writer.Write(profile.MinY);
            writer.Write(profile.MaxX); writer.Write(profile.MaxY);
            writer.Write(profile.BaseLight); writer.Write(profile.Rain);
            writer.Write(profile.Snow); writer.Write(profile.Fog);
            writer.Write(profile.Music); writer.Write(profile.Zone);
            writer.Write(profile.Restriction); writer.Write(profile.Safety); writer.Write(profile.Z); writer.Write(profile.LegacyLevel);
        }
        writer.Write(map.Npcs.Count);
        foreach (var npc in map.Npcs)
        {
            writer.Write(npc.X); writer.Write(npc.Y); writer.Write(npc.Body); writer.Write(npc.Head);
            writer.Write(npc.Heading); writer.Write(npc.Weapon); writer.Write(npc.Shield); writer.Write(npc.Helmet); writer.Write(npc.Z);
        }
        writer.Write(map.ParticleGroups.Count);
        foreach (var group in map.ParticleGroups)
        {
            writer.Write(group.Index); writer.Write(group.X); writer.Write(group.Y); writer.Write(group.Type); writer.Write(group.Z);
        }
    }

    public UnifiedWorldMap Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8, leaveOpen: false);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Firma AOGMAP inválida.");
        var version = reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException("Se requiere AOGMAP01 v4.");
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var floorZ = version >= 2 ? reader.ReadInt32() : 0;
        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension ||
            (long)width * height > MaxRecords)
            throw new InvalidDataException("Dimensiones AOGMAP inválidas.");
        var map = new UnifiedWorldMap(width, height, floorZ);
        ReadArray(reader, map.GraphicIndices);
        ReadArray(reader, map.LightValues);
        ReadArray(reader, map.Colors);
        ReadArray(reader, map.Blocked);
        ReadArray(reader, map.Trigger);
        ReadArray(reader, map.CharacterIndex);
        ReadArray(reader, map.NpcIndex);
        ReadArray(reader, map.ParticleGroup);
        ReadArray(reader, map.ParticleIndex);
        ReadArray(reader, map.Marked);
        ReadArray(reader, map.ObjectIndex);
        ReadArray(reader, map.ObjectAmount);
        ReadArray(reader, map.ObjectGraphicIndex);
        ReadArray(reader, map.LightRange);
        ReadArray(reader, map.LightColor);
        ReadArray(reader, map.Red);
        ReadArray(reader, map.Green);
        ReadArray(reader, map.Blue);
        ReadArray(reader, map.RoofZoneId);
        ReadArray(reader, map.ExitIndex);
        ReadArray(reader, map.AmbientLight);
        ReadArray(reader, map.Rain);
        ReadArray(reader, map.Snow);
        ReadArray(reader, map.Fog);
        if (version >= 4)
        {
            ReadArray(reader, map.Water);
            ReadArray(reader, map.Lava);
        }

        var exitCount = ReadCount(reader, "salidas");
        for (var i = 0; i < exitCount; i++)
        {
            var kind = (UnifiedWorldExitKind)reader.ReadByte();
            var sourceX = reader.ReadInt32(); var sourceY = reader.ReadInt32();
            var targetX = reader.ReadInt32(); var targetY = reader.ReadInt32();
            var externalMap = reader.ReadInt16(); var externalX = reader.ReadInt16(); var externalY = reader.ReadInt16();
            var sourceZ = version >= 2 ? reader.ReadInt32() : 0;
            var targetZ = version >= 2 ? reader.ReadInt32() : 0;
            var externalZ = version >= 2 ? reader.ReadInt32() : 0;
            map.Exits.Add(new UnifiedWorldExit(kind, sourceX, sourceY, targetX, targetY,
                externalMap, externalX, externalY, sourceZ, targetZ, externalZ));
        }
        var roofCount = ReadCount(reader, "zonas de techo");
        for (var i = 0; i < roofCount; i++)
        {
            var id = reader.ReadInt32(); var trigger = reader.ReadInt32();
            var minX = reader.ReadInt32(); var minY = reader.ReadInt32();
            var maxX = reader.ReadInt32(); var maxY = reader.ReadInt32();
            map.RoofZones.Add(new UnifiedWorldRoofZone(id, trigger, minX, minY, maxX, maxY,
                version >= 2 ? reader.ReadInt32() : 0));
        }
        var environmentCount = ReadCount(reader, "perfiles ambientales");
        for (var i = 0; i < environmentCount; i++)
        {
            var minX = reader.ReadInt32(); var minY = reader.ReadInt32();
            var maxX = reader.ReadInt32(); var maxY = reader.ReadInt32(); var baseLight = reader.ReadInt32();
            var rain = reader.ReadByte(); var snow = reader.ReadByte(); var fog = reader.ReadByte();
            var music = reader.ReadString(); var zone = reader.ReadString(); var restriction = reader.ReadString();
            var safety = reader.ReadByte();
            var z = version >= 2 ? reader.ReadInt32() : 0;
            var legacyLevel = version >= 3 ? reader.ReadInt32() : 0;
            map.EnvironmentProfiles.Add(new UnifiedWorldEnvironmentProfile(minX, minY, maxX, maxY, baseLight,
                rain, snow, fog, music, zone, restriction, safety, z, legacyLevel));
        }
        var npcCount = ReadCount(reader, "NPCs");
        for (var i = 0; i < npcCount; i++)
        {
            var x = reader.ReadInt32(); var y = reader.ReadInt32(); var body = reader.ReadInt16(); var head = reader.ReadInt16();
            var heading = reader.ReadByte(); var weapon = reader.ReadInt16(); var shield = reader.ReadInt16(); var helmet = reader.ReadInt16();
            map.Npcs.Add(new UnifiedWorldNpcPlacement(x, y, body, head, heading, weapon, shield, helmet,
                version >= 2 ? reader.ReadInt32() : 0));
        }
        var particleCount = ReadCount(reader, "grupos de partículas");
        for (var i = 0; i < particleCount; i++)
        {
            var index = reader.ReadInt32(); var x = reader.ReadInt32(); var y = reader.ReadInt32(); var type = reader.ReadInt32();
            map.ParticleGroups.Add(new UnifiedWorldParticleGroup(index, x, y, type,
                version >= 2 ? reader.ReadInt32() : 0));
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("AOGMAP contiene datos adicionales.");
        return map;
    }

    private static void WriteArray(BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values) writer.Write(value);
    }

    private static void WriteArray(BinaryWriter writer, short[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values) writer.Write(value);
    }

    private static void WriteArray(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values) writer.Write(value);
    }

    private static void WriteArray(BinaryWriter writer, bool[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values) writer.Write(value);
    }

    private static void ReadArray(BinaryReader reader, byte[] destination)
    {
        ValidateLength(reader, destination.Length, nameof(Byte));
        for (var i = 0; i < destination.Length; i++) destination[i] = reader.ReadByte();
    }

    private static void ReadArray(BinaryReader reader, short[] destination)
    {
        ValidateLength(reader, destination.Length, nameof(Int16));
        for (var i = 0; i < destination.Length; i++) destination[i] = reader.ReadInt16();
    }

    private static void ReadArray(BinaryReader reader, int[] destination)
    {
        ValidateLength(reader, destination.Length, nameof(Int32));
        for (var i = 0; i < destination.Length; i++) destination[i] = reader.ReadInt32();
    }

    private static void ReadArray(BinaryReader reader, bool[] destination)
    {
        ValidateLength(reader, destination.Length, nameof(Boolean));
        for (var i = 0; i < destination.Length; i++) destination[i] = reader.ReadBoolean();
    }

    private static void ValidateLength(BinaryReader reader, int expected, string type)
    {
        if (reader.ReadInt32() != expected)
            throw new InvalidDataException($"Longitud de array {type} inválida.");
    }

    private static int ReadCount(BinaryReader reader, string name)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxRecords) throw new InvalidDataException($"Cantidad de {name} inválida.");
        return count;
    }
}
