using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class LazyWorldMapCatalogTests
{
    [Fact]
    public void Catalog_loads_a_map_only_when_requested()
    {
        var loads = 0;
        var catalog = new LazyWorldMapCatalog(
            new Dictionary<short, string> { [1] = "1.csm", [2] = "2.csm" },
            _ => { loads++; return new WorldMap(); });

        Assert.Equal(0, loads);
        _ = catalog[2];
        Assert.Equal(1, loads);
        _ = catalog[2];
        Assert.Equal(1, loads);
    }

    [Fact]
    public void Trim_keeps_modified_maps_and_discards_clean_maps()
    {
        var catalog = new LazyWorldMapCatalog(
            new Dictionary<short, string> { [1] = "1.csm", [2] = "2.csm" },
            _ => new WorldMap());
        var first = catalog[1];
        var second = catalog[2];
        second.Info.Changed = true;

        catalog.Trim([1]);

        Assert.Equal(2, catalog.LoadedCount);
        catalog.Trim([]);
        Assert.Equal(1, catalog.LoadedCount);
        Assert.Same(second, catalog[2]);
        Assert.NotSame(first, catalog[1]);
    }
}
