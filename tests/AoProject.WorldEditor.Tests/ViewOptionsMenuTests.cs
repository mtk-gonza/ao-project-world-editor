using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ViewOptionsMenuTests
{
    [Fact]
    public void View_options_can_toggle_each_overlay_category()
    {
        var options = new WorldRenderOptions { ShowGrid = true, ShowBlocked = true, ShowTransfers = true, ShowLights = true, ShowParticles = true };
        Assert.True(options.ShowGrid); Assert.True(options.ShowBlocked); Assert.True(options.ShowTransfers);
        Assert.True(options.ShowLights); Assert.True(options.ShowParticles);
        options.ShowGrid = false; options.ShowBlocked = false; options.ShowTransfers = false;
        Assert.False(options.ShowGrid); Assert.False(options.ShowBlocked); Assert.False(options.ShowTransfers);
    }

    [Fact]
    public void Roof_fade_is_enabled_by_default_and_can_be_disabled()
    {
        var options = new WorldRenderOptions();

        Assert.True(options.ShowRoofFade);
        options.ShowRoofFade = false;
        Assert.False(options.ShowRoofFade);
    }
}
