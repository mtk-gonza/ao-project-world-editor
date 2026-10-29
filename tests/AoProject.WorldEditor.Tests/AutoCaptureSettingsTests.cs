using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AutoCaptureSettingsTests
{
    [Fact]
    public void Auto_capture_flags_round_trip_through_ini()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-settings-" + Guid.NewGuid().ToString("N"), "WorldEditor.ini");
        try
        {
            var settings = new WorldEditorSettings { AutoCaptureSurfaces = false, AutoCaptureTransfers = false };
            settings.Save(path);
            var loaded = WorldEditorSettings.Load(path);
            Assert.False(loaded.AutoCaptureSurfaces); Assert.False(loaded.AutoCaptureTransfers);
        }
        finally { var directory = Path.GetDirectoryName(path)!; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
