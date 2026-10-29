using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AudioServiceTests
{
    [Fact]
    public void Audio_catalog_separates_midi_music_and_ambient_waves()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "theme.mid"), []);
            File.WriteAllBytes(Path.Combine(root, "rain.wav"), []);
            var catalog = new AudioCatalog(); catalog.Load(root);
            Assert.Single(catalog.MusicFiles); Assert.Single(catalog.AmbientWaveFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Audio_playback_rejects_missing_files()
    {
        using var playback = new AudioPlaybackService();
        Assert.Throws<FileNotFoundException>(() => playback.PlayWave("missing.wav"));
    }
}
