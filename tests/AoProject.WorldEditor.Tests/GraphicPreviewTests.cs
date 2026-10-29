using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicPreviewTests
{
    [Fact]
    public void Preview_returns_null_for_non_renderable_graphic()
    {
        var graphic = new GraphicData();
        Assert.Null(new GraphicPreviewService(Path.GetTempPath()).Create(graphic));
    }
}
