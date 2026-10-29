using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using System.Text;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class IndexAuditTests
{
    [Fact]
    public void Finds_declared_count_mismatches_duplicates_and_missing_fields()
    {
        var root = NewRoot();
        try
        {
            Write(root, "Dat/obj.dat",
                "[INIT]", "NumOBJs=4",
                "[OBJ1]", "GrhIndex=100",
                "[OBJ2]", "GrhIndex=0",
                "[OBJ3]", "GrhIndex=200",
                "[OBJ3]", "GrhIndex=201");
            Write(root, "init/triggers.ini",
                "[INIT]", "NumTriggers=3",
                "[Trig1]", "Name=Uno");

            var result = new IndexAuditService().Audit(root);

            Assert.Contains(result.Issues, issue => issue.File == "obj.dat" && issue.Code == "IDX-COUNT");
            Assert.Contains(result.Issues, issue => issue.File == "obj.dat" && issue.Code == "IDX-DUP");
            Assert.Contains(result.Issues, issue => issue.File == "obj.dat" && issue.Code == "IDX-EMPTY");
            Assert.Contains(result.Issues, issue => issue.File == "triggers.ini" && issue.Code == "IDX-COUNT");
            Assert.Equal("4", result.Stats["obj.dat.NumOBJs"]);
        }
        finally { Delete(root); }
    }

    [Fact]
    public void Audits_world_data_indices_and_unknown_maps()
    {
        var root = NewRoot();
        var maps = Path.Combine(root, "Mapas");
        Directory.CreateDirectory(maps);
        try
        {
            File.WriteAllBytes(Path.Combine(maps, "mapa1.csm"), []);
            File.WriteAllBytes(Path.Combine(maps, "mapa2.csm"), []);
            Write(root, "init/mapsworlddata.dat",
                "[INIT]", "TOTALWORLDS=1",
                "[WORLDMAP1]", "ANCHO=2", "ALTO=1", "1=1", "2=9", "3=5");

            var result = new IndexAuditService().Audit(root, maps);

            Assert.Contains(result.Issues, issue => issue.Code == "IDX-WORLD" && issue.Message.Contains("9"));
            Assert.Contains(result.Issues, issue => issue.Code == "IDX-WORLD" && issue.Message.Contains("fuera de 1..2"));
            Assert.Equal("2", result.Stats["mapsworlddata.world1"]?.Split(' ')[0].Split('x')[0]);
        }
        finally { Delete(root); }
    }

    [Fact]
    public void Reports_a_missing_graphics_index_as_absent_data()
    {
        var root = NewRoot();
        try
        {
            Write(root, "init/graficos.ini", "[INIT]", "NumGrh=2",
                "[Graphics]", "Grh1=1-1-64-0-32-32", "Grh2=1-1-65-0-32-32");

            var result = new IndexAuditService().Audit(root);

            Assert.Equal("2", result.Stats["graficos.ini.entries"]);
            Assert.DoesNotContain(result.Issues, issue => issue.File == "graficos.ini" && issue.Severity == AuditSeverity.Error);
        }
        finally { Delete(root); }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-index-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relative, params string[] lines)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, Encoding.Latin1);
    }

    private static void Delete(string root)
    {
        try { Directory.Delete(root, true); } catch { }
    }
}
