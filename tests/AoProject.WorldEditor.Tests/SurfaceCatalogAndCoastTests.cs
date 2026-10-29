using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SurfaceCatalogAndCoastTests
{
    [Fact]
    public void DameGrhIndex_reports_mosaic_metadata()
    {
        var surfaces = new List<SurfaceData>
        {
            new() { Name = "Pasto", GraphicIndex = 1, Width = 0, Height = 0 },
            new() { Name = "Agua", GraphicIndex = 1505, Width = 4, Height = 3 }
        };
        var service = new SurfaceCatalogService();
        var flat = service.DameGrhIndex(surfaces, 0);
        Assert.Equal(1, flat.GraphicIndex); Assert.False(flat.Mosaic);
        var water = service.DameGrhIndex(surfaces, 1);
        Assert.Equal(1505, water.GraphicIndex); Assert.True(water.Mosaic); Assert.Equal(4, water.Width); Assert.Equal(3, water.Height);
    }

    [Fact]
    public void Filter_matches_by_name_and_graphic_index()
    {
        var surfaces = new List<SurfaceData>
        {
            new() { Name = "Pasto", GraphicIndex = 1 },
            new() { Name = "Agua", GraphicIndex = 1505 }
        };
        var service = new SurfaceCatalogService();
        Assert.Single(service.Filtrar(surfaces, "agu"));
        Assert.Single(service.Filtrar(surfaces, "1505"));
    }

    [Fact]
    public void FixCoasts_clears_second_layer_only_for_coast_graphics()
    {
        var map = new WorldMap();
        map.Blocks[10, 10].Graphics[0].GraphicIndex = 7284;
        map.Blocks[10, 10].Graphics[1].GraphicIndex = 55;
        map.Blocks[10, 11].Graphics[0].GraphicIndex = 1;
        map.Blocks[10, 11].Graphics[1].GraphicIndex = 55;
        var service = new CoastFixService();
        Assert.True(service.Fix(map, 10, 10, 7284));
        Assert.Equal(0, map.Blocks[10, 10].Graphics[1].GraphicIndex);
        Assert.False(service.Fix(map, 10, 11, 1));
        Assert.Equal(55, map.Blocks[10, 11].Graphics[1].GraphicIndex);
    }
}
