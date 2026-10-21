using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using HauntPatrol.Rendering;

namespace HauntPatrol.Audio;

/// <summary>
/// Short WAV effects from Assets/Audio, played with winmm waveOut on a worker thread.
/// WPF's MediaPlayer was measured stalling the (process-wide) render thread for 100-500 ms per open,
/// and SoundPlayer can only play one sound at a time. Here each Play() opens its own waveOut handle
/// on preloaded PCM, so sounds overlap (Windows mixes them), repeats of one sound overlap rather than
/// restart, and nothing touches WPF. At most <see cref="MaxVoices"/> play at once.
/// </summary>
public sealed class AudioService
{
    sealed record Clip(Wave.Format Format, IntPtr Data, int Length);
    sealed record Voice(IntPtr Handle, IntPtr Header);

    // Busiest moment: 7 handprint slaps plus a few gameplay sounds. Each voice is a waveOut handle.
    const int MaxVoices = 16;

    readonly Dictionary<string, Clip> _clips = new(StringComparer.OrdinalIgnoreCase); // worker thread only
    readonly List<Voice> _voices = [];                                                // worker thread only
    readonly BlockingCollection<Action> _work = [];
    volatile bool _enabled = true;

    public AudioService()
    {
        new Thread(Run) { IsBackground = true, Name = "Audio" }.Start();
        _work.Add(LoadAll);
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
        if (_enabled) _work.Add(() => Start(name));
    }

    public void StopAll() => _work.Add(() =>
    {
        foreach (var v in _voices) Wave.waveOutReset(v.Handle);
        Reap(force: true);
    });

    void Run()
    {
        while (true)
        {
            if (_work.TryTake(out var job, 100))
            {
                try { job(); }
                catch (Exception) { /* a broken sound must never take the game down */ }
            }
            Reap(force: false);
        }
    }

    void LoadAll()
    {
        var dir = Path.Combine(SpriteAssetLoader.Root, "Audio");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.wav"))
        {
            if (Wave.Parse(File.ReadAllBytes(file)) is not var (format, pcm)) continue; // not PCM: skip
            var data = Marshal.AllocHGlobal(pcm.Length);
            Marshal.Copy(pcm, 0, data, pcm.Length);
            _clips[Path.GetFileNameWithoutExtension(file)] = new Clip(format, data, pcm.Length);
        }
    }

    void Start(string name)
    {
        if (!_clips.TryGetValue(name, out var clip)) return; // missing sounds are fine during development
        Reap(force: false);
        if (_voices.Count >= MaxVoices)
        {
            // At the cap the oldest voice gives way: the newest sound is the one that matches what's on screen.
            Wave.waveOutReset(_voices[0].Handle);
            Reap(force: false);
        }
        var format = clip.Format;
        if (Wave.waveOutOpen(out var handle, Wave.WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
        var header = Marshal.AllocHGlobal(Wave.HeaderSize);
        Marshal.StructureToPtr(new Wave.Header { Data = clip.Data, Length = (uint)clip.Length }, header, false);
        Wave.waveOutPrepareHeader(handle, header, Wave.HeaderSize);
        Wave.waveOutWrite(handle, header, Wave.HeaderSize);
        _voices.Add(new Voice(handle, header));
    }

    /// <summary>Close voices that finished (or all of them after a reset).</summary>
    void Reap(bool force)
    {
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            var flags = Marshal.PtrToStructure<Wave.Header>(v.Header).Flags;
            if (!force && (flags & Wave.WHDR_DONE) == 0) continue;
            Wave.waveOutUnprepareHeader(v.Handle, v.Header, Wave.HeaderSize);
            Wave.waveOutClose(v.Handle);
            Marshal.FreeHGlobal(v.Header);
            _voices.RemoveAt(i);
        }
    }
}

static class Wave
{
    public const uint WAVE_MAPPER = unchecked((uint)-1);
    public const uint WHDR_DONE = 1;
    public static readonly int HeaderSize = Marshal.SizeOf<Header>();

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct Format
    {
        public ushort Tag, Channels;
        public uint SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        public IntPtr Data;
        public uint Length, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr handle, uint device, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr handle);
    [DllImport("winmm.dll")] public static extern uint waveOutGetNumDevs();

    /// <summary>Asks the default output device whether it can play this format, without opening it.</summary>
    public static bool CanPlay(Format format) => waveOutOpen(out _, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, WAVE_FORMAT_QUERY) == 0;
    const uint WAVE_FORMAT_QUERY = 1;

    /// <summary>Format and sample bytes of a RIFF/WAVE PCM file, or null.</summary>
    public static (Format, byte[])? Parse(byte[] file)
    {
        if (file.Length < 12 || BitConverter.ToUInt32(file, 0) != 0x46464952 || BitConverter.ToUInt32(file, 8) != 0x45564157) return null; // "RIFF", "WAVE"
        Format? format = null;
        for (var pos = 12; pos + 8 <= file.Length;)
        {
            var id = BitConverter.ToUInt32(file, pos);
            var size = BitConverter.ToInt32(file, pos + 4);
            var body = pos + 8;
            if (size < 0 || body + size > file.Length) break;
            if (id == 0x20746D66) // "fmt "
                format = new Format
                {
                    Tag = BitConverter.ToUInt16(file, body),
                    Channels = BitConverter.ToUInt16(file, body + 2),
                    SamplesPerSec = BitConverter.ToUInt32(file, body + 4),
                    AvgBytesPerSec = BitConverter.ToUInt32(file, body + 8),
                    BlockAlign = BitConverter.ToUInt16(file, body + 12),
                    BitsPerSample = BitConverter.ToUInt16(file, body + 14),
                };
            else if (id == 0x61746164 && format is { Tag: 1 } f) // "data", PCM
                return (f, file[body..(body + size)]);
            pos = body + size + (size & 1);
        }
        return null;
    }
}
