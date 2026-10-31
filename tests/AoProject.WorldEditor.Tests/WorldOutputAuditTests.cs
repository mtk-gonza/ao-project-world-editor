using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldOutputAuditTests
{
    [Fact]
    public void Audits_namemapa_and_transfers_against_the_maps()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-output-audit-" + Guid.NewGuid().ToString("N"));
        var maps = Path.Combine(root, "Mapas");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(maps);
        try
        {
            var service = new SectorCsmFileService();
            var map = new WorldSector();
            map.Info.Name = "Uno";
            map.Blocks[50, 91].TileExit.SectorId = 2;
            map.Blocks[50, 10].TileExit.SectorId = 3;
            map.Blocks[13, 50].TileExit.SectorId = 4;
            map.Blocks[88, 50].TileExit.SectorId = 5;
            service.Save(map, Path.Combine(maps, "mapa1.csm"));
            var generated = new WorldOutputService().Generate(maps, null, output, includeMinimaps: false);

            var issues = new WorldOutputAuditService().Audit(maps, generated.Directory, checkMinimaps: false);
            Assert.DoesNotContain(issues, issue => issue.Severity == AuditSeverity.Error);
            Assert.DoesNotContain(issues, issue => issue.Code == "OUT-TRANSFER");
            Assert.DoesNotContain(issues, issue => issue.Code == "OUT-NAME");

            var report = new WorldOutputAuditService().WriteReport(issues, generated.Directory);
            Assert.True(File.Exists(report));
            Assert.StartsWith("SectorId\tCode\tSeverity", File.ReadAllLines(report)[0]);

            map.Blocks[50, 91].TileExit.SectorId = 9;
            service.Save(map, Path.Combine(maps, "mapa1.csm"));
            var after = new WorldOutputAuditService().Audit(maps, generated.Directory, checkMinimaps: false);
            Assert.Contains(after, issue => issue.Code == "OUT-TRANSFER" && issue.Message.StartsWith("Abajo"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
