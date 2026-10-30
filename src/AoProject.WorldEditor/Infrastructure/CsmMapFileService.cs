using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Reader for the current CSM map format used by Recursos/Mapas.</summary>
internal sealed class CsmMapFileService
{
    private const int MaxRecords = 1_000_000;

    public WorldMap Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.Latin1, leaveOpen: false);
        return Load(reader);
    }

    public WorldMap Load(string path, short mapId)
    {
        if (GlobalCsmArchiveService.TryRead(path, mapId, out var payload))
        {
            using var reader = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.Latin1, leaveOpen: false);
            return Load(reader);
        }
        return Load(path);
    }

    private static WorldMap Load(BinaryReader reader)
    {
        var counts = new Counts(reader);
        var worldMapSize = new CsmMapSize(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()).Normalize();

        var world = new WorldMap();
        world.CsmMapSize = worldMapSize;
        ReadMapMetadata(reader, world);
        ReadBlocked(reader, world, counts.Blocked);
        ReadLayer(reader, world, counts.Layers[0], 0);
        ReadLayer(reader, world, counts.Layers[1], 1);
        ReadLayer(reader, world, counts.Layers[2], 2);
        ReadLayer(reader, world, counts.Layers[3], 3);
        ReadTriggers(reader, world, counts.Triggers);
        ReadParticles(reader, world, counts.Particles);
        ReadLights(reader, world, counts.Lights);
        ReadObjects(reader, world, counts.Objects);
        ReadNpcs(reader, world, counts.Npcs);
        ReadTileExits(reader, world, counts.TileExits);
        world.Info.Changed = false;
        return world;
    }

    public void Save(WorldMap world, string path)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path), Encoding.Latin1, leaveOpen: false);
        var records = Enumerate(world).ToArray();
        writer.Write(records.Count(record => record.Blocked));
        for (var layer = 0; layer < 4; layer++) writer.Write(records.Count(record => record.Graphics[layer] != 0));
        writer.Write(records.Count(record => record.Trigger != 0));
        // VB6 header order is lights, particles; the payload order below is
        // particles, lights. They intentionally differ in the legacy format.
        writer.Write(records.Count(record => record.LightRange != 0));
        writer.Write(records.Count(record => record.Particle != 0));
        writer.Write(records.Count(record => record.Npc != 0));
        writer.Write(records.Count(record => record.ObjectIndex != 0));
        writer.Write(records.Count(record => record.ExitMap != 0));
        writer.Write(world.CsmMapSize.XMax); writer.Write(world.CsmMapSize.XMin);
        writer.Write(world.CsmMapSize.YMax); writer.Write(world.CsmMapSize.YMin);
        WriteString(writer, world.Info.Name);
        writer.Write(world.Info.Backup);
        WriteString(writer, world.Info.Restriction);
        writer.Write(world.Info.MusicNumberHigh);
        writer.Write(int.TryParse(world.Info.Music, out var musicLow) ? musicLow : world.Info.MusicNumberLow);
        writer.Write(world.Info.Safety);
        WriteString(writer, world.Info.Zone);
        WriteString(writer, world.Info.Terrain);
        WriteString(writer, world.Info.Ambient);
        writer.Write(world.Environment.BaseLight);
        writer.Write(world.Info.LetterGraphic);
        writer.Write(world.Info.Level);
        writer.Write(world.Info.Extra2);
        WriteString(writer, world.Info.Exit);
        writer.Write(world.Environment.Rain);
        writer.Write(world.Environment.Snow);
        writer.Write(world.Environment.Fog);

        foreach (var record in records.Where(record => record.Blocked)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.BlockedValue); }
        for (var layer = 0; layer < 4; layer++)
            foreach (var record in records.Where(record => record.Graphics[layer] != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.Graphics[layer]); }
        foreach (var record in records.Where(record => record.Trigger != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write((short)record.Trigger); }
        foreach (var record in records.Where(record => record.Particle != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.Particle); }
        foreach (var record in records.Where(record => record.LightRange != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.LightColor); writer.Write(record.LightRange); }
        foreach (var record in records.Where(record => record.ObjectIndex != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.ObjectIndex); writer.Write(record.ObjectAmount); }
        foreach (var record in records.Where(record => record.Npc != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.Npc); }
        foreach (var record in records.Where(record => record.ExitMap != 0)) { writer.Write(record.X); writer.Write(record.Y); writer.Write(record.ExitMap); writer.Write(record.ExitX); writer.Write(record.ExitY); }
        world.Info.Changed = false;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value ?? string.Empty);
        if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("Texto CSM demasiado largo.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static IEnumerable<MapRecord> Enumerate(WorldMap world)
    {
        for (short y = 1; y <= WorldMap.Height; y++)
        for (short x = 1; x <= WorldMap.Width; x++)
        {
            var block = world.Blocks[x, y];
            yield return new MapRecord(x, y, block);
        }
    }

    private sealed class MapRecord
    {
        private readonly MapBlock _block;
        public MapRecord(short x, short y, MapBlock block) { X = x; Y = y; _block = block; }
        public short X { get; }
        public short Y { get; }
        public bool Blocked => _block.Blocked != 0;
        public byte BlockedValue => _block.Blocked;
        public int[] Graphics => _block.Graphics.Select(graphic => graphic.GraphicIndex).ToArray();
        public int Trigger => _block.Trigger;
        public byte LightRange => (byte)Math.Clamp((int)_block.Light.Range, 0, 255);
        public int LightColor => _block.Light.Color;
        public int Particle => _block.ParticleIndex;
        public short Npc => _block.NpcIndex;
        public short ObjectIndex => _block.Object.Index;
        public short ObjectAmount => _block.Object.Amount;
        public short ExitMap => _block.TileExit.Map;
        public short ExitX => _block.TileExit.X;
        public short ExitY => _block.TileExit.Y;
    }

    private static void ReadMapMetadata(BinaryReader reader, WorldMap world)
    {
        var info = world.Info;
        info.Name = ReadString(reader);
        info.Backup = reader.ReadByte();
        info.Restriction = ReadString(reader);
        info.MusicNumberHigh = reader.ReadInt32();
        info.MusicNumberLow = reader.ReadInt32();
        info.Music = info.MusicNumberLow.ToString();
        info.Safety = reader.ReadByte();
        info.Zone = ReadString(reader);
        info.Terrain = ReadString(reader);
        info.Ambient = ReadString(reader);
        world.Environment.BaseLight = reader.ReadInt32();
        info.LetterGraphic = reader.ReadInt32();
        info.Level = reader.ReadInt32();
        info.Extra2 = reader.ReadInt32();
        info.Exit = ReadString(reader);
        world.Environment.Rain = reader.ReadByte();
        world.Environment.Snow = reader.ReadByte();
        world.Environment.Fog = reader.ReadByte();
    }

    private static void ReadBlocked(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var sides = reader.ReadByte();
            if (Inside(x, y)) world.Blocks[x, y].Blocked = sides;
        }
    }

    private static void ReadLayer(BinaryReader reader, WorldMap world, int count, int layer)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var graphic = reader.ReadInt32();
            if (Inside(x, y)) world.Blocks[x, y].Graphics[layer].GraphicIndex = graphic;
        }
    }

    private static void ReadTriggers(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var trigger = reader.ReadInt16();
            if (Inside(x, y)) world.Blocks[x, y].Trigger = trigger;
        }
    }

    private static void ReadLights(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var color = reader.ReadInt32();
            var range = reader.ReadByte();
            if (Inside(x, y))
            {
                world.Blocks[x, y].Light.Color = color;
                world.Blocks[x, y].Light.Range = range;
            }
        }
    }

    private static void ReadParticles(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var particle = reader.ReadInt32();
            if (Inside(x, y)) world.Blocks[x, y].ParticleIndex = particle;
        }
    }

    private static void ReadObjects(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var index = reader.ReadInt16();
            var amount = reader.ReadInt16();
            if (Inside(x, y))
            {
                world.Blocks[x, y].Object.Index = index;
                world.Blocks[x, y].Object.Amount = amount;
            }
        }
    }

    private static void ReadNpcs(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var index = reader.ReadInt16();
            if (Inside(x, y)) world.Blocks[x, y].NpcIndex = index;
        }
    }

    private static void ReadTileExits(BinaryReader reader, WorldMap world, int count)
    {
        ValidateCount(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var map = reader.ReadInt16();
            var destX = reader.ReadInt16();
            var destY = reader.ReadInt16();
            if (Inside(x, y))
            {
                world.Blocks[x, y].TileExit.Map = map;
                world.Blocks[x, y].TileExit.X = destX;
                world.Blocks[x, y].TileExit.Y = destY;
            }
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        // VB6 variable-length strings inside this binary UDT use a 16-bit
        // byte length followed by ANSI bytes.
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("String CSM incompleta.");
        return Encoding.Latin1.GetString(bytes);
    }

    private static bool Inside(short x, short y) => x is >= 1 and <= WorldMap.Width && y is >= 1 and <= WorldMap.Height;
    private static void ValidateCount(int count)
    {
        if (count is < 0 or > MaxRecords)
            throw new InvalidDataException($"Cantidad de registros CSM invalida: {count}.");
    }

    private sealed class Counts
    {
        public Counts(BinaryReader reader)
        {
            Blocked = reader.ReadInt32();
            Layers = [reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()];
            Triggers = reader.ReadInt32();
            Lights = reader.ReadInt32();
            Particles = reader.ReadInt32();
            Npcs = reader.ReadInt32();
            Objects = reader.ReadInt32();
            TileExits = reader.ReadInt32();
        }

        public int Blocked { get; }
        public int[] Layers { get; }
        public int Triggers { get; }
        public int Lights { get; }
        public int Particles { get; }
        public int Npcs { get; }
        public int Objects { get; }
        public int TileExits { get; }
    }
}
