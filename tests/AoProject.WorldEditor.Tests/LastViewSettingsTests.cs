using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class LastViewSettingsTests
{
    [Fact]
    public void Last_view_coordinates_round_trip_through_ini()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-view-" + Guid.NewGuid().ToString("N"), "WorldEditor.ini");
        try
        {
            var settings = new WorldEditorSettings { LastX = 77, LastY = 12 }; settings.Save(path);
            var loaded = WorldEditorSettings.Load(path);
            Assert.Equal(77, loaded.LastX); Assert.Equal(12, loaded.LastY);
        }
        finally { var directory = Path.GetDirectoryName(path)!; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
