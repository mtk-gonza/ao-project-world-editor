using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AudioPlaybackTests
{
    [Fact]
    public void Missing_midi_is_rejected_before_playback()
    {
        using var audio = new AudioPlaybackService();
        Assert.Throws<FileNotFoundException>(() => audio.PlayMidi(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mid")));
        Assert.False(audio.IsPlaying);
    }
}
