using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicLookupTests
{
    [Fact]
    public void Lookup_returns_direct_bmp_for_a_real_graphic()
    {
        var root = Environment.GetEnvironmentVariable("AO_RESOURCES") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
        var graphics = new Infrastructure.GraphicIndexLoader().Load(root);
        var index = Enumerable.Range(1, graphics.Count - 1).First(item => graphics[item].Active && graphics[item].FileNumber > 0);
        Assert.True(new GraphicLookupService().ResolveBmp(graphics, index) > 0);
    }

    [Fact]
    public void Catalog_completes_map_graphics_missing_from_binary_index()
    {
        var root = Environment.GetEnvironmentVariable("AO_RESOURCES") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
        var graphics = new Infrastructure.GraphicIndexLoader().Load(root);

        // mapa401 uses this animated GRH, while the shipped graficos.ind
        // header stops at 61947. The text catalog contains its frames.
        Assert.True(graphics.Count > 85483);
        Assert.True(graphics[85483].Active);
        Assert.Equal(10, graphics[85483].Frames.Length);
        Assert.Equal(85390, graphics[85483].Frames[0]);
    }

    [Fact]
    public void Catalog_fills_missing_floor_minimap_colors_from_png()
    {
        var root = Environment.GetEnvironmentVariable("AO_RESOURCES") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
        var graphics = new Infrastructure.GraphicIndexLoader().Load(root);

        Assert.NotEqual(0, graphics[1511].MiniMapColor);
        Assert.NotEqual(0, graphics[1514].MiniMapColor);
    }
}
