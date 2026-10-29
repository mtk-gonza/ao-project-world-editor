using System.IO.Compression;

namespace AoProject.WorldEditor.Application;

/// <summary>Port of modCompression.Compress_Data/Decompress_Data (zlib).</summary>
internal static class CompressionService
{
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data);
        return output.ToArray();
    }

    public static byte[] Decompress(ReadOnlySpan<byte> data, int originalSize)
    {
        if (originalSize < 0) throw new ArgumentOutOfRangeException(nameof(originalSize));
        using var input = new MemoryStream(data.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var result = new byte[originalSize];
        var read = 0;
        while (read < originalSize)
        {
            var count = zlib.Read(result, read, originalSize - read);
            if (count == 0) break;
            read += count;
        }
        return result;
    }

    public static int FreeDriveBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(root)) return 0;
        return (int)Math.Clamp(new DriveInfo(root).AvailableFreeSpace, 0, int.MaxValue);
    }

    public static IReadOnlyList<string> FindFiles(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void QuickSort(Span<int> values) => values.Sort();
}
