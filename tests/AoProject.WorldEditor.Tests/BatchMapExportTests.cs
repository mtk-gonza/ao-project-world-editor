using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BatchMapExportTests
{
    [Fact]
    public void Export_writes_one_png_per_csm_and_reports_failures()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-batch-" + Guid.NewGuid().ToString("N"));
        var maps = Path.Combine(root, "maps"); var output = Path.Combine(root, "out");
        Directory.CreateDirectory(maps); Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            var io = new SectorCsmFileService(); io.Save(new WorldSector(), Path.Combine(maps, "mapa1.csm")); io.Save(new WorldSector(), Path.Combine(maps, "mapa2.csm"));
            var results = new BatchMapExportService().ExportDirectory(maps, output, [new GraphicData()], root);
            Assert.Equal(2, results.Count); Assert.All(results, result => Assert.True(result.Exported));
            Assert.True(File.Exists(Path.Combine(output, "mapa1.png"))); Assert.True(File.Exists(Path.Combine(output, "mapa2.png")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
