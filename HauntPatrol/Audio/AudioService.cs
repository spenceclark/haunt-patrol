using System.IO;
using System.Windows.Media;
using HauntPatrol.Rendering;

namespace HauntPatrol.Audio;

/// <summary>
/// WAVs from Assets/Audio, one MediaPlayer per sound, opened once at startup. Opening a MediaPlayer
/// spins up Media Foundation on the UI thread and stalled the frame loop for 100-500 ms per sound,
/// so Play() only rewinds and starts an already-open player. Different sounds overlap; replaying the
/// same sound restarts it.
/// </summary>
public sealed class AudioService
{
    readonly Dictionary<string, MediaPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    bool _enabled = true;

    public AudioService()
    {
        var dir = Path.Combine(SpriteAssetLoader.Root, "Audio");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.wav"))
        {
            var player = new MediaPlayer();
            player.Open(new Uri(file));
            _players[Path.GetFileNameWithoutExtension(file)] = player;
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) StopAll();
        }
    }

    public void Play(string name)
    {
        if (!_enabled || !_players.TryGetValue(name, out var player)) return; // missing sounds are fine during development
        player.Position = TimeSpan.Zero;
        player.Play();
    }

    public void StopAll()
    {
        foreach (var p in _players.Values) p.Stop();
    }
}
