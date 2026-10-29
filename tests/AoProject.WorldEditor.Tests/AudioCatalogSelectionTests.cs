using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AudioCatalogSelectionTests
{
    [Fact]
    public void Catalog_orders_midi_and_wav_entries_for_selector_workflows()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-audio-catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "z.mid"), []); File.WriteAllBytes(Path.Combine(root, "a.mid"), []); File.WriteAllBytes(Path.Combine(root, "rain.wav"), []);
            var catalog = new AudioCatalog(); catalog.Load(root);
            Assert.Equal("a.mid", Path.GetFileName(catalog.MusicFiles[0])); Assert.Single(catalog.AmbientWaveFiles);
        }
        finally { Directory.Delete(root, true); }
    }
}
