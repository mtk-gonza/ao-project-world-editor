using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MultiMapValidationTests
{
    [Fact]
    public void Validation_report_contains_each_map_and_its_issues()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-validation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var io = new CsmMapFileService();
            var first = new WorldMap(); first.Blocks[4, 5].NpcIndex = 9; io.Save(first, Path.Combine(root, "mapa1.csm"));
            io.Save(new WorldMap(), Path.Combine(root, "mapa2.csm"));
            var report = new MultiMapValidationService().ValidateDirectory(root, null, new NpcData[1]);
            Assert.Contains("mapa1.csm", report); Assert.Contains("NPC inválido: 9", report); Assert.Contains("mapa2.csm", report);
        }
        finally { Directory.Delete(root, true); }
    }
}
