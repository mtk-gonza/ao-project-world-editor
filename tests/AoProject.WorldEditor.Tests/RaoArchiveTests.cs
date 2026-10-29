using System.Text;
using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RaoArchiveTests
{
    [Fact]
    public void Archive_round_trips_named_payloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-rao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "init.rao");
            var payloadA = Encoding.UTF8.GetBytes("particles-data");
            var payloadB = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("x", 4000)));
            new RaoArchiveService().Create(archive, [("a.ind", payloadA), ("b.ini", payloadB)]);

            var entries = new RaoArchiveService().List(archive);
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, entry => entry.Name == "a.ind");

            var output = Path.Combine(root, "out");
            var extracted = new RaoArchiveService().Extract(archive, "a.ind", output);
            Assert.Equal(payloadA, File.ReadAllBytes(extracted));

            var all = new RaoArchiveService().ExtractAll(archive, Path.Combine(root, "all"));
            Assert.Equal(2, all.Count);
            Assert.Equal(payloadB, File.ReadAllBytes(Path.Combine(root, "all", "b.ini")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_entry_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-rao-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "init.rao");
            new RaoArchiveService().Create(archive, [("only.txt", [1, 2, 3])]);
            Assert.Throws<FileNotFoundException>(() => new RaoArchiveService().Extract(archive, "other.txt", root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
