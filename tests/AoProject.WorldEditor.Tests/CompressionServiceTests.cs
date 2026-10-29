using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class CompressionServiceTests
{
    [Fact]
    public void Compress_and_decompress_round_trip_zlib_payloads()
    {
        var data = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("argentum-online-", 500)));
        var compressed = CompressionService.Compress(data);
        Assert.True(compressed.Length < data.Length);
        var restored = CompressionService.Decompress(compressed, data.Length);
        Assert.Equal(data, restored);
    }

    [Fact]
    public void File_helpers_cover_reference_utilities()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-compress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "archivo.tmp"); File.WriteAllText(path, "x");
            Assert.Single(CompressionService.FindFiles(root, "*.tmp"));
            CompressionService.DeleteFile(path);
            Assert.False(File.Exists(path));
            var values = new[] { 3, 1, 2 }; CompressionService.QuickSort(values);
            Assert.Equal(new[] { 1, 2, 3 }, values);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
