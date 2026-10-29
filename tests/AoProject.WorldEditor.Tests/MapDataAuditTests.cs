using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapDataAuditTests
{
    [Fact]
    public void A_clean_map_has_no_errors_and_survives_the_round_trip()
    {
        using var fixture = new MapFixture();
        var map = new WorldMap();
        map.Info.Name = "Limpio";
        map.Blocks[15, 13].Trigger = 54;
        fixture.Save(map, "mapa1.csm");

        var summary = new MapDataAuditService().Audit(1, fixture.FilePath("mapa1.csm"), new short[] { 1 }, true);

        Assert.Equal(0, summary.Errors);
        Assert.DoesNotContain(summary.Issues,
            issue => issue.Code == "CSM-ROUNDTRIP" && issue.Severity == AuditSeverity.Error);
    }

    [Fact]
    public void Reports_an_exit_to_a_map_that_is_not_in_the_folder()
    {
        using var fixture = new MapFixture();
        var map = new WorldMap();
        map.Info.Name = "Salida";
        map.Blocks[50, 50].TileExit.Map = 999;
        map.Blocks[50, 50].TileExit.X = 10;
        map.Blocks[50, 50].TileExit.Y = 10;
        fixture.Save(map, "mapa1.csm");

        var summary = new MapDataAuditService().Audit(1, fixture.FilePath("mapa1.csm"), new short[] { 1 }, false);

        Assert.Contains(summary.Issues, issue => issue.Code == "CSM-EXIT-DEST");
    }

    [Fact]
    public void Reports_content_outside_the_csm_size()
    {
        using var fixture = new MapFixture();
        var map = new WorldMap
        {
            CsmMapSize = new CsmMapSize(20, 10, 20, 10)
        };
        map.Info.Name = "Chico";
        map.Blocks[1, 1].Graphics[0].GraphicIndex = 5;
        fixture.Save(map, "mapa1.csm");

        var summary = new MapDataAuditService().Audit(1, fixture.FilePath("mapa1.csm"), new short[] { 1 }, false);

        Assert.Contains(summary.Issues, issue => issue.Code == "CSM-BOUNDS");
    }

    [Fact]
    public void Reports_an_empty_file()
    {
        using var fixture = new MapFixture();
        File.WriteAllBytes(fixture.FilePath("mapa1.csm"), []);
        var summary = new MapDataAuditService().Audit(1, fixture.FilePath("mapa1.csm"), new short[] { 1 }, false);
        Assert.Contains(summary.Issues, issue => issue.Code == "CSM-READ" && issue.Severity == AuditSeverity.Error);
    }

    private sealed class MapFixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ao-map-audit-" + Guid.NewGuid().ToString("N"));
        public MapFixture() => Directory.CreateDirectory(_root);
        public string FilePath(string name) => System.IO.Path.Combine(_root, name);
        public void Save(WorldMap map, string name) => new CsmMapFileService().Save(map, FilePath(name));
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    }
}

