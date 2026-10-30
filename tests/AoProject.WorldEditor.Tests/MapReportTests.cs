using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapReportTests
{
    [Fact]
    public void Reports_include_objects_layers_and_suspicious_transfers()
    {
        var map = new WorldSector();
        map.Blocks[3, 4].Object.Index = 1; map.Blocks[3, 4].Object.Amount = 2;
        map.Blocks[5, 6].Graphics[2].GraphicIndex = 700;
        map.Blocks[10, 10].TileExit.Map = 9; map.Blocks[10, 10].TileExit.X = 11; map.Blocks[10, 10].TileExit.Y = 10;
        var objects = new ObjectData[2]; objects[1] = new ObjectData { Name = "Poción" };
        var reports = new MapReportService();

        Assert.Contains("Poción", reports.Generate(map, MapReportKind.Objects, objects));
        Assert.Contains("700", reports.Generate(map, MapReportKind.Layer3));
        Assert.Contains("X sospechoso", reports.Generate(map, MapReportKind.Transfers));
    }
}
