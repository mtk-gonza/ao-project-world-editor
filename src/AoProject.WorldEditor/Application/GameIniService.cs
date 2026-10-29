using System.Text;

namespace AoProject.WorldEditor.Application;

internal sealed class GameIniConfiguration
{
    public long Port { get; set; }
    public byte Music { get; set; }
    public byte Fx { get; set; }
    public byte Tip { get; set; }
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GraphicsDirectory { get; set; } = string.Empty;
    public string SoundsDirectory { get; set; } = string.Empty;
    public string MusicDirectory { get; set; } = string.Empty;
    public string MapsDirectory { get; set; } = string.Empty;
    public long BitmapCount { get; set; }
    public short MapCount { get; set; }
}

internal sealed record GameIniHeader(string Description, int Crc, int MagicWord);

/// <summary>Port of modGameIni: Inicio.con header plus client configuration.</summary>
internal sealed class GameIniService
{
    public const string DefaultDescription =
        "Argentum Online by Noland Studios. Copyright Noland-Studios 2001, pablomarquez@noland-studios.com.ar";

    public GameIniHeader IniciarCabecera(int crc = 0, int magicWord = 0) => new(DefaultDescription, crc, magicWord);

    public GameIniConfiguration Leer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.Default, leaveOpen: false);
        _ = ReadHeader(reader);
        return new GameIniConfiguration
        {
            Port = reader.ReadInt32(),
            Music = reader.ReadByte(),
            Fx = reader.ReadByte(),
            Tip = reader.ReadByte(),
            Password = ReadString(reader),
            Name = ReadString(reader),
            GraphicsDirectory = ReadString(reader),
            SoundsDirectory = ReadString(reader),
            MusicDirectory = ReadString(reader),
            MapsDirectory = ReadString(reader),
            BitmapCount = reader.ReadInt32(),
            MapCount = reader.ReadInt16()
        };
    }

    public void Escribir(string path, GameIniConfiguration configuration, GameIniHeader? header = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path), Encoding.Default, leaveOpen: false);
        WriteHeader(writer, header ?? IniciarCabecera());
        writer.Write((int)configuration.Port);
        writer.Write(configuration.Music);
        writer.Write(configuration.Fx);
        writer.Write(configuration.Tip);
        WriteString(writer, configuration.Password);
        WriteString(writer, configuration.Name);
        WriteString(writer, configuration.GraphicsDirectory);
        WriteString(writer, configuration.SoundsDirectory);
        WriteString(writer, configuration.MusicDirectory);
        WriteString(writer, configuration.MapsDirectory);
        writer.Write((int)configuration.BitmapCount);
        writer.Write(configuration.MapCount);
    }

    private static GameIniHeader ReadHeader(BinaryReader reader)
    {
        var description = Encoding.Default.GetString(reader.ReadBytes(255)).TrimEnd('\0');
        return new GameIniHeader(description, reader.ReadInt32(), reader.ReadInt32());
    }

    private static void WriteHeader(BinaryWriter writer, GameIniHeader header)
    {
        var bytes = new byte[255];
        var text = Encoding.Default.GetBytes(header.Description);
        Array.Copy(text, bytes, Math.Min(text.Length, bytes.Length));
        writer.Write(bytes);
        writer.Write(header.Crc);
        writer.Write(header.MagicWord);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return Encoding.Default.GetString(reader.ReadBytes(length));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Default.GetBytes(value ?? string.Empty);
        if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("Texto Inicio.con demasiado largo.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
