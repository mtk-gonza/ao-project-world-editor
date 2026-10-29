using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SettingsRenderIntegrationTests
{
    [Fact]
    public void Settings_apply_and_capture_render_visibility()
    {
        var settings = new WorldEditorSettings { ShowLayer2 = false, ShowGrid = true, ShowNpcs = false, ShowRoofFade = false };
        var options = new WorldRenderOptions();
        settings.ApplyTo(options);
        Assert.False(options.ShowLayer2); Assert.True(options.ShowGrid); Assert.False(options.ShowNpcs); Assert.False(options.ShowRoofFade);
        options.ShowLayer3 = false;
        settings.UpdateFrom(options);
        Assert.False(settings.ShowLayer3);
    }
}
