using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class LastMapSettingsTests
{
    [Fact]
    public void Last_map_path_round_trips_through_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-last-map-" + Guid.NewGuid().ToString("N"), "WorldEditor.ini");
        try
        {
            var settings = new WorldEditorSettings { LastMap = "C:\\maps\\mapa7.csm" }; settings.Save(path);
            Assert.Equal("C:\\maps\\mapa7.csm", WorldEditorSettings.Load(path).LastMap);
        }
        finally { var directory = Path.GetDirectoryName(path)!; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
