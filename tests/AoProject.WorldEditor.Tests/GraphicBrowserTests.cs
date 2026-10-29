using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicBrowserTests
{
    [Fact]
    public void Browser_lists_all_grh_indices_except_reserved_zero()
    {
        var graphics = new[] { new GraphicData(), new GraphicData { Active = true }, new GraphicData() };
        var entries = new GraphicBrowserService().Enumerate(graphics);
        Assert.Equal(new[] { 1, 2 }, entries.Select(entry => entry.Index));
    }
}
