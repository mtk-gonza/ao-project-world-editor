using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AmbientationCatalogTests
{
    [Fact]
    public void Types_are_distinct_and_filter_keeps_original_indices()
    {
        var ambientations = new List<Ambientation>
        {
            new() { Name = "A", Type = 2, GraphicIndex = 10 },
            new() { Name = "B", Type = 1, GraphicIndex = 20 },
            new() { Name = "C", Type = 2, GraphicIndex = 30 }
        };
        var catalog = new AmbientationCatalogService();
        Assert.Equal(new byte[] { 1, 2 }, catalog.Types(ambientations));
        var filtered = catalog.FilterByType(ambientations, 2);
        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filtered[0].Index);
        Assert.Equal(2, filtered[1].Index);
        Assert.Equal("C", filtered[1].Ambientation.Name);
    }
}
