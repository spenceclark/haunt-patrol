using System.IO;
using System.Windows.Media;
using HauntPatrol.Rendering;

namespace HauntPatrol.Audio;

/// <summary>Fire-and-forget WAVs from Assets/Audio. MediaPlayer (not SoundPlayer) so sounds can overlap.</summary>
public sealed class AudioService
{
    readonly List<MediaPlayer> _playing = [];
    bool _enabled = true;

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
        if (!_enabled) return;
        var path = Path.Combine(SpriteAssetLoader.Root, "Audio", name + ".wav");
        if (!File.Exists(path)) return; // missing sounds are fine during development

        var player = new MediaPlayer();
        void Done()
        {
            player.Close();
            _playing.Remove(player);
        }
        player.MediaEnded += (_, _) => Done();
        player.MediaFailed += (_, _) => Done();
        player.Open(new Uri(path));
        player.Play();
        _playing.Add(player);
    }

    public void StopAll()
    {
        foreach (var p in _playing)
        {
            p.Stop();
            p.Close();
        }
        _playing.Clear();
    }
}
