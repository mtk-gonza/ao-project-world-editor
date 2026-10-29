using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AlphaRoofsSettingsTests
{
    [Fact]
    public void Alpha_roofs_is_off_by_default_like_the_vb6_toggle()
    {
        Assert.False(new WorldRenderOptions().AlphaRoofs);
        Assert.False(new WorldEditorSettings().AlphaRoofs);
    }

    [Fact]
    public void Settings_round_trip_the_alpha_roofs_flag()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-alpha-roofs-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            new WorldEditorSettings { AlphaRoofs = true }.Save(path);
            Assert.True(WorldEditorSettings.Load(path).AlphaRoofs);
            Assert.Contains("AlphaTechos=1", File.ReadAllLines(path));

            new WorldEditorSettings { AlphaRoofs = false }.Save(path);
            Assert.False(WorldEditorSettings.Load(path).AlphaRoofs);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Options_apply_and_update_copy_the_flag_both_ways()
    {
        var settings = new WorldEditorSettings { AlphaRoofs = true };
        var options = new WorldRenderOptions();
        settings.ApplyTo(options);
        Assert.True(options.AlphaRoofs);

        options.AlphaRoofs = false;
        settings.UpdateFrom(options);
        Assert.False(settings.AlphaRoofs);
    }
}
