using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Binary map I/O for the legacy V3 map layout used by modMapIO.bas.
/// VB6 Integer/Long values are explicitly decoded as little-endian values.
/// </summary>
internal sealed class MapFileService
{
    private const int HeaderDescriptionLength = 255;
    private readonly MapMetadataService _metadata = new();
    public WorldMap LoadV3(string mapPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapPath);
        if (!File.Exists(mapPath))
            throw new FileNotFoundException("No existe el archivo de mapa.", mapPath);

        var infPath = Path.ChangeExtension(mapPath, ".inf");
        if (!File.Exists(infPath))
            throw new FileNotFoundException("No existe el archivo INF asociado al mapa.", infPath);

        using var map = new BinaryReader(File.OpenRead(mapPath));
        using var inf = new BinaryReader(File.OpenRead(infPath));
        var world = new WorldMap();

        var mapVersion = map.ReadInt16();
        var header = ReadHeader(map);
        var mapReserved = new[] { map.ReadInt16(), map.ReadInt16(), map.ReadInt16(), map.ReadInt16() };
        world.Info.MapVersion = mapVersion;

        // The VB6 loader consumes five Integer values from the INF header.
        var infReserved = new[] { inf.ReadInt16(), inf.ReadInt16(), inf.ReadInt16(), inf.ReadInt16(), inf.ReadInt16() };
        world.Header = new LegacyMapHeader(
            mapVersion,
            header.Description,
            header.Crc,
            header.MagicWord,
            mapReserved,
            infReserved);

        for (var y = 1; y <= WorldMap.Height; y++)
        for (var x = 1; x <= WorldMap.Width; x++)
        {
            var block = world.Blocks[x, y];
            var flags = map.ReadByte();
            block.Blocked = (byte)(flags & 1);
            block.Graphics[0].GraphicIndex = map.ReadInt32();
            block.Graphics[1].GraphicIndex = (flags & 2) != 0 ? map.ReadInt32() : 0;
            block.Graphics[2].GraphicIndex = (flags & 4) != 0 ? map.ReadInt32() : 0;
            block.Graphics[3].GraphicIndex = (flags & 8) != 0 ? map.ReadInt32() : 0;
            block.Trigger = (flags & 16) != 0 ? map.ReadInt16() : (short)0;

            var infoFlags = inf.ReadByte();
            if ((infoFlags & 1) != 0)
            {
                block.TileExit.Map = inf.ReadInt16();
                block.TileExit.X = inf.ReadInt16();
                block.TileExit.Y = inf.ReadInt16();
            }

            block.NpcIndex = (infoFlags & 2) != 0 ? inf.ReadInt16() : (short)0;
            if ((infoFlags & 4) != 0)
            {
                block.Object.Index = inf.ReadInt16();
                block.Object.Amount = inf.ReadInt16();
            }
        }

        _metadata.Load(world, Path.ChangeExtension(mapPath, ".dat"));
        world.Info.Changed = false;
        return world;
    }

    public void SaveV3(WorldMap world, string mapPath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapPath);
        var infPath = Path.ChangeExtension(mapPath, ".inf");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(mapPath))!);

        using var map = new BinaryWriter(File.Create(mapPath));
        using var inf = new BinaryWriter(File.Create(infPath));
        var header = world.Header;
        map.Write(world.Info.MapVersion);
        map.Write(FixedBytes(header.Description, HeaderDescriptionLength));
        map.Write(header.Crc);
        map.Write(header.MagicWord);
        foreach (var value in FixedValues(header.MapReserved, 4))
            map.Write(value);
        foreach (var value in FixedValues(header.InfReserved, 5))
            inf.Write(value);

        for (var y = 1; y <= WorldMap.Height; y++)
        for (var x = 1; x <= WorldMap.Width; x++)
        {
            var block = world.Blocks[x, y];
            byte flags = 0;
            if (block.Blocked != 0) flags |= 1;
            if (block.Graphics[1].GraphicIndex != 0) flags |= 2;
            if (block.Graphics[2].GraphicIndex != 0) flags |= 4;
            if (block.Graphics[3].GraphicIndex != 0) flags |= 8;
            if (block.Trigger != 0) flags |= 16;
            map.Write(flags);
            map.Write(block.Graphics[0].GraphicIndex);
            if ((flags & 2) != 0) map.Write(block.Graphics[1].GraphicIndex);
            if ((flags & 4) != 0) map.Write(block.Graphics[2].GraphicIndex);
            if ((flags & 8) != 0) map.Write(block.Graphics[3].GraphicIndex);
            if ((flags & 16) != 0) map.Write(block.Trigger);

            byte infoFlags = 0;
            if (block.TileExit.Map != 0) infoFlags |= 1;
            if (block.NpcIndex != 0) infoFlags |= 2;
            if (block.Object.Index != 0) infoFlags |= 4;
            inf.Write(infoFlags);
            if ((infoFlags & 1) != 0)
            {
                inf.Write(block.TileExit.Map);
                inf.Write(block.TileExit.X);
                inf.Write(block.TileExit.Y);
            }
            if ((infoFlags & 2) != 0) inf.Write(block.NpcIndex);
            if ((infoFlags & 4) != 0)
            {
                inf.Write(block.Object.Index);
                inf.Write(block.Object.Amount);
            }
        }

        _metadata.Save(world, Path.ChangeExtension(mapPath, ".dat"));
        world.Info.Changed = false;
    }

    private static LegacyHeader ReadHeader(BinaryReader reader)
    {
        var description = reader.ReadBytes(HeaderDescriptionLength);
        if (description.Length != HeaderDescriptionLength)
            throw new EndOfStreamException("Cabecera de mapa incompleta.");

        return new LegacyHeader(
            description,
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    private static byte[] FixedBytes(byte[] value, int length)
    {
        var result = new byte[length];
        Array.Copy(value, result, Math.Min(value.Length, result.Length));
        return result;
    }

    private static IEnumerable<short> FixedValues(short[] values, int length)
    {
        for (var i = 0; i < length; i++)
            yield return i < values.Length ? values[i] : (short)0;
    }

    private readonly record struct LegacyHeader(byte[] Description, int Crc, int MagicWord);
}
