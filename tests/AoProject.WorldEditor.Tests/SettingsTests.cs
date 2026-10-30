using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Settings_load_reference_defaults_and_roundtrip()
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "argentum-online-worldeditor", "WorldEditor.ini"));
        var settings = WorldEditorSettings.Load(source);
        Assert.Equal(49488, settings.MaxGraphics);
        Assert.Equal(50, settings.LastX);
        Assert.True(settings.UseUndo);
        var target = Path.Combine(Path.GetTempPath(), $"world-editor-{Guid.NewGuid():N}.ini");
        try
        {
            settings.ShowParticles = true;
            settings.ShowLights = true;
            settings.ShowRoofFade = false;
            settings.ZoomPercent = 175;
            settings.PaletteTab = 5;
            settings.Save(target);
            var loaded = WorldEditorSettings.Load(target);
            Assert.Equal(settings.MaxGraphics, loaded.MaxGraphics);
            Assert.True(loaded.ShowParticles);
            Assert.True(loaded.ShowLights);
            Assert.False(loaded.ShowRoofFade);
            Assert.Equal(175, loaded.ZoomPercent);
            Assert.Equal(5, loaded.PaletteTab);
        }
        finally { File.Delete(target); }
    }
}
