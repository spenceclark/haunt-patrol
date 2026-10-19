using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HauntPatrol.Rendering;

public sealed record AnimationClip(string Name, BitmapSource[] Frames, double Fps, bool Loop)
{
    public double Duration => Frames.Length / Fps;
}

/// <summary>
/// Asset contract shared by placeholders and production art. Width/Height are logical DIPs; frames are
/// stretched to them, so higher-resolution artwork drops in without code changes. Pivot is in logical DIPs.
/// Convention: character art faces right.
/// </summary>
public sealed record SpriteDefinition(string Id, double Width, double Height, Point Pivot, IReadOnlyDictionary<string, AnimationClip> Clips)
{
    public AnimationClip Default => Clips.GetValueOrDefault("idle") ?? Clips.Values.First();
}

/// <summary>
/// Loads Assets/Characters/{id}/animations.json, or a single-image prop from Assets/Props|Effects/{id}.png
/// (logical size = pixel size, pivot = centre). Knows nothing about specific games or artwork.
/// </summary>
public sealed class SpriteAssetLoader
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    readonly Dictionary<string, SpriteDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpriteDefinition Get(string id)
    {
        if (!_cache.TryGetValue(id, out var def))
            _cache[id] = def = Load(id);
        return def;
    }

    static SpriteDefinition Load(string id)
    {
        var json = Path.Combine(Root, "Characters", id, "animations.json");
        if (File.Exists(json)) return LoadCharacter(json);

        foreach (var folder in new[] { "Props", "Effects" })
        {
            var png = Path.Combine(Root, folder, id + ".png");
            if (!File.Exists(png)) continue;
            var image = LoadBitmap(png);
            var clip = new AnimationClip("idle", [image], 1, true);
            return new SpriteDefinition(id, image.PixelWidth, image.PixelHeight,
                new Point(image.PixelWidth / 2.0, image.PixelHeight / 2.0), new Dictionary<string, AnimationClip> { ["idle"] = clip });
        }
        throw new FileNotFoundException($"No sprite asset '{id}' under {Root}");
    }

    sealed record ClipJson(string Source, int FrameWidth, int FrameHeight, int FrameCount = 1, double Fps = 8, bool Loop = true);
    sealed record SpriteJson(string Id, double Width, double Height, double PivotX, double PivotY, Dictionary<string, ClipJson> Animations);

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    static SpriteDefinition LoadCharacter(string jsonPath)
    {
        var spec = JsonSerializer.Deserialize<SpriteJson>(File.ReadAllText(jsonPath), JsonOptions)
                   ?? throw new InvalidDataException(jsonPath);
        var dir = Path.GetDirectoryName(jsonPath)!;
        var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, c) in spec.Animations)
        {
            var sheet = LoadBitmap(Path.Combine(dir, c.Source));
            var columns = Math.Max(1, sheet.PixelWidth / c.FrameWidth);
            var frames = new BitmapSource[c.FrameCount];
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = new CroppedBitmap(sheet, new Int32Rect(i % columns * c.FrameWidth, i / columns * c.FrameHeight, c.FrameWidth, c.FrameHeight));
                frame.Freeze();
                frames[i] = frame;
            }
            clips[name] = new AnimationClip(name, frames, c.Fps, c.Loop);
        }
        return new SpriteDefinition(spec.Id, spec.Width, spec.Height, new Point(spec.PivotX, spec.PivotY), clips);
    }

    static BitmapImage LoadBitmap(string path)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri(path);
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        return b;
    }
}
