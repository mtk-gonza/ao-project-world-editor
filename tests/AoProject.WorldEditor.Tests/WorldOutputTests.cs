using System.Text;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldOutputTests
{
    [Fact]
    public void Generates_namemapa_and_transfers_without_touching_resources()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-world-output-" + Guid.NewGuid().ToString("N"));
        var maps = Path.Combine(root, "Mapas");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(maps);
        try
        {
            var service = new SectorCsmFileService();
            var first = new WorldSector();
            first.Info.Name = "Uno";
            first.Blocks[50, 91].TileExit.Map = 2;
            first.Blocks[50, 10].TileExit.Map = 3;
            first.Blocks[13, 50].TileExit.Map = 4;
            first.Blocks[88, 50].TileExit.Map = 5;
            var second = new WorldSector();
            second.Info.Name = "Dos";
            service.Save(first, Path.Combine(maps, "mapa1.csm"));
            service.Save(second, Path.Combine(maps, "mapa2.csm"));

            var result = new WorldOutputService().Generate(maps, null, output, includeMinimaps: false);

            Assert.Equal(2, result.Maps);
            Assert.Equal(0, result.Errors);
            var names = File.ReadAllLines(Path.Combine(result.Directory, "NameMapa.dat"));
            Assert.Equal("[NameMapa]", names[0]);
            Assert.Contains("mapa1=Uno", names);
            Assert.Contains("mapa2=Dos", names);
            var transfers = File.ReadAllLines(Path.Combine(result.Directory, "translados.ini"));
            Assert.Contains("[mapa1]", transfers);
            Assert.Contains("Abajo=2", transfers);
            Assert.Contains("Arriba=3", transfers);
            Assert.Contains("Izquierda=4", transfers);
            Assert.Contains("Derecha=5", transfers);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Keeps_unknown_legacy_keys_such_as_map_descriptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-world-output-" + Guid.NewGuid().ToString("N"));
        var maps = Path.Combine(root, "Mapas");
        var output = Path.Combine(root, "out");
        var reference = Path.Combine(root, "reference");
        Directory.CreateDirectory(maps);
        Directory.CreateDirectory(Path.Combine(reference, "init"));
        Directory.CreateDirectory(Path.Combine(reference, "MiniMap"));
        try
        {
            File.WriteAllLines(Path.Combine(reference, "init", "namemapa.dat"),
            [
                "[NameMapa]",
                "mapa1=Nombre viejo",
                "mapa1Desc=Dominada por la Legion Oscura",
                "mapa99=Viejo sin CSM"
            ], Encoding.Latin1);
            File.WriteAllLines(Path.Combine(reference, "MiniMap", "translados.ini"),
            [
                "[mapa1]",
                "Abajo=99",
                "Nota=legacy"
            ], Encoding.Latin1);

            var service = new SectorCsmFileService();
            var map = new WorldSector();
            map.Info.Name = "Ullathorpe";
            map.Blocks[50, 91].TileExit.Map = 7;
            service.Save(map, Path.Combine(maps, "mapa1.csm"));

            var result = new WorldOutputService().Generate(maps, null, output,
                includeMinimaps: false, referenceRoot: reference);

            var names = File.ReadAllLines(Path.Combine(result.Directory, "NameMapa.dat"), Encoding.Latin1);
            Assert.Contains("mapa1=Ullathorpe", names);
            Assert.Contains("mapa1Desc=Dominada por la Legion Oscura", names);
            Assert.Contains("mapa99=Viejo sin CSM", names);
            var transfers = File.ReadAllLines(Path.Combine(result.Directory, "translados.ini"), Encoding.Latin1);
            Assert.Contains("Abajo=7", transfers);
            Assert.Contains("Nota=legacy", transfers);
            Assert.DoesNotContain("Abajo=99", transfers);
            Assert.DoesNotContain("mapa1=Nombre viejo", names);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
