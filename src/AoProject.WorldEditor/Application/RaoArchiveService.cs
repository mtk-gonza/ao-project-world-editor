using System.Text;

namespace AoProject.WorldEditor.Application;

internal readonly record struct RaoEntry(string Name, int Start, int Size, int UncompressedSize);

/// <summary>
/// Port of modCompression Compress_Files / Extract_File / Extract_All_Files /
/// Extract_Patch / General_Load_Picture_From_Resource.
/// Container layout (FILEHEADER + INFOHEADER[] + zlib blobs):
///   int32 fileSize, int16 numFiles,
///   numFiles * (int32 start, int32 size, char[32] name, int32 uncompressedSize).
/// </summary>
internal sealed class RaoArchiveService
{
    private const int InfoSize = 44;
    private const int NameSize = 32;

    public void Create(string archivePath, IEnumerable<(string Name, byte[] Data)> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(files);
        var entries = files.Select(file => (file.Name, Compressed: CompressionService.Compress(file.Data), file.Data.Length)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);

        var infoStart = 6;
        var dataStart = infoStart + entries.Length * InfoSize;
        using var writer = new BinaryWriter(File.Create(archivePath), Encoding.Default, leaveOpen: false);
        writer.Write(0);
        writer.Write((short)entries.Length);

        var cursor = dataStart;
        foreach (var entry in entries)
        {
            writer.Write(cursor);
            writer.Write(entry.Compressed.Length);
            writer.Write(FixedName(entry.Name));
            writer.Write(entry.Length);
            cursor += entry.Compressed.Length;
        }
        foreach (var entry in entries) writer.Write(entry.Compressed);

        writer.Flush();
        var total = cursor;
        writer.Seek(0, SeekOrigin.Begin);
        writer.Write(total);
    }

    public IReadOnlyList<RaoEntry> List(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        using var reader = new BinaryReader(File.OpenRead(archivePath), Encoding.Default, leaveOpen: false);
        var fileSize = reader.ReadInt32();
        var count = reader.ReadInt16();
        if (count < 0) throw new InvalidDataException("Cantidad de archivos invalida.");
        var entries = new List<RaoEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var start = reader.ReadInt32();
            var size = reader.ReadInt32();
            var name = ReadFixedName(reader.ReadBytes(NameSize));
            var uncompressed = reader.ReadInt32();
            if (start < 0 || size < 0 || start + size > fileSize) throw new InvalidDataException($"Entrada invalida: {name}.");
            entries.Add(new RaoEntry(name, start, size, uncompressed));
        }
        return entries;
    }

    public string Extract(string archivePath, string fileName, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        var entry = List(archivePath).FirstOrDefault(item => string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(entry.Name)) throw new FileNotFoundException($"No existe '{fileName}' en el archivo.", archivePath);
        return WriteEntry(archivePath, entry, outputDirectory);
    }

    public IReadOnlyList<string> ExtractAll(string archivePath, string outputDirectory)
    {
        var written = new List<string>();
        foreach (var entry in List(archivePath)) written.Add(WriteEntry(archivePath, entry, outputDirectory));
        return written;
    }

    public IReadOnlyList<string> ExtractPatch(string patchArchivePath, string outputDirectory) => ExtractAll(patchArchivePath, outputDirectory);

    public System.Drawing.Bitmap? LoadPicture(string archivePath, string fileName, string temporaryDirectory)
    {
        var path = Extract(archivePath, fileName, temporaryDirectory);
        using var stream = File.OpenRead(path);
        return new System.Drawing.Bitmap(stream);
    }

    private static string WriteEntry(string archivePath, RaoEntry entry, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var reader = new BinaryReader(File.OpenRead(archivePath), Encoding.Default, leaveOpen: false);
        reader.BaseStream.Seek(entry.Start, SeekOrigin.Begin);
        var compressed = reader.ReadBytes(entry.Size);
        var data = CompressionService.Decompress(compressed, entry.UncompressedSize);
        var destination = Path.Combine(outputDirectory, entry.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, data);
        return destination;
    }

    private static byte[] FixedName(string name)
    {
        var buffer = new byte[NameSize];
        var bytes = Encoding.Default.GetBytes(name);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length));
        return buffer;
    }

    private static string ReadFixedName(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return Encoding.Default.GetString(buffer, 0, length);
    }
}
