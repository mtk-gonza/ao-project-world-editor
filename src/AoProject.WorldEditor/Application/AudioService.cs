using System.Media;
using System.Runtime.InteropServices;

namespace AoProject.WorldEditor.Application;

internal sealed class AudioCatalog
{
    public IReadOnlyList<string> MusicFiles { get; private set; } = [];
    public IReadOnlyList<string> AmbientWaveFiles { get; private set; } = [];

    public void Load(string resourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        if (!Directory.Exists(resourceRoot)) throw new DirectoryNotFoundException(resourceRoot);
        MusicFiles = Directory.EnumerateFiles(resourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".mid", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(path), ".midi", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        AmbientWaveFiles = Directory.EnumerateFiles(resourceRoot, "*.wav", SearchOption.AllDirectories)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed class AudioPlaybackService : IDisposable
{
    private const string MidiAlias = "ao_world_editor_midi";
    private SoundPlayer? _wavePlayer;
    public string? CurrentPath { get; private set; }
    public bool IsPlaying { get; private set; }

    public void PlayWave(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("No existe el sonido.", path);
        Stop();
        _wavePlayer = new SoundPlayer(path);
        _wavePlayer.PlayLooping();
        CurrentPath = path; IsPlaying = true;
    }

    public void PlayMidi(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("No existe la música MIDI.", path);
        Stop();
        var escaped = path.Replace("\"", "\\\"");
        var openResult = mciSendString($"open \"{escaped}\" type sequencer alias {MidiAlias}", null, 0, IntPtr.Zero);
        if (openResult != 0) throw new InvalidOperationException($"No se pudo abrir el MIDI (MCI {openResult}).");
        var playResult = mciSendString($"play {MidiAlias} repeat", null, 0, IntPtr.Zero);
        if (playResult != 0) { mciSendString($"close {MidiAlias}", null, 0, IntPtr.Zero); throw new InvalidOperationException($"No se pudo reproducir el MIDI (MCI {playResult})."); }
        CurrentPath = path; IsPlaying = true;
    }

    public void Stop()
    {
        _wavePlayer?.Stop(); _wavePlayer?.Dispose(); _wavePlayer = null;
        mciSendString($"stop {MidiAlias}", null, 0, IntPtr.Zero);
        mciSendString($"close {MidiAlias}", null, 0, IntPtr.Zero);
        CurrentPath = null; IsPlaying = false;
    }

    public void Dispose() => Stop();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, string? returnValue, int returnLength, IntPtr callback);
}
