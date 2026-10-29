using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MultiMapReplacementTests
{
    [Fact]
    public void Replacement_iterates_and_saves_all_changed_csm_maps()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-replace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var io = new CsmMapFileService();
            for (var index = 1; index <= 2; index++) { var map = new WorldMap(); map.Blocks[index, index].Graphics[0].GraphicIndex = 10; io.Save(map, Path.Combine(root, $"mapa{index}.csm")); }
            var results = new MultiMapReplacementService().ReplaceDirectory(root, MapReplacementKind.Graphic, 10, 20, 0, 1);
            Assert.Equal(2, results.Count); Assert.All(results, result => Assert.Equal(1, result.Changed));
            Assert.All(Directory.EnumerateFiles(root, "*.csm"), path => Assert.Equal(20, io.Load(path).Blocks[Path.GetFileName(path)[4] - '0', Path.GetFileName(path)[4] - '0'].Graphics[1].GraphicIndex));
        }
        finally { Directory.Delete(root, true); }
    }
}
