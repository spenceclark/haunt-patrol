using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HauntPatrol.Rendering;

/// <summary>Look of a silhouette glow. Sizes are DIPs; opacities 0-1.</summary>
public sealed record GlowOptions
{
    public bool Enabled { get; init; } = true;
    public Color Colour { get; init; } = Color.FromRgb(0xA7, 0x79, 0xE8);
    public double OutlineWidth { get; init; } = 2.5;   // thin, fairly crisp rim hugging the silhouette
    public double OutlineOpacity { get; init; } = 0.5;
    public double GlowRadius { get; init; } = 8;       // soft falloff beyond the rim
    public double GlowOpacity { get; init; } = 0.3;
}

/// <summary>
/// Edge-light for a sprite, built from each animation frame's alpha: dilate it a little (the rim), blur that
/// (the glow), colourise, and cut the sprite's own pixels back out, so drawn on the click-through layer, which
/// sits above the sprite, it only ever shows around the silhouette. Built once per frame on first use, cached.
/// </summary>
public static class SpriteGlow
{
    static readonly Dictionary<(ImageSource Frame, GlowOptions Options), BitmapSource> Cache = [];

    /// <summary>DIPs of margin the halo needs around the sprite's logical box.</summary>
    public static int Padding(GlowOptions o) => (int)Math.Ceiling(o.OutlineWidth + o.GlowRadius * 1.5) + 1;

    /// <summary>The halo for one frame, sized (width + 2*pad) x (height + 2*pad) DIPs at 1 px per DIP.</summary>
    public static BitmapSource For(BitmapSource frame, double width, double height, GlowOptions o)
    {
        if (Cache.TryGetValue((frame, o), out var cached)) return cached;

        // Alpha at 1 px per DIP: the glow is soft, so the frame's full resolution isn't needed.
        var scaled = new TransformedBitmap(frame, new ScaleTransform(width / frame.PixelWidth, height / frame.PixelHeight));
        var pixels = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        int w = pixels.PixelWidth, h = pixels.PixelHeight;
        var bgra = new byte[w * h * 4];
        pixels.CopyPixels(bgra, w * 4, 0);
        var alpha = new float[w * h];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = bgra[i * 4 + 3] / 255f;

        var halo = Halo(alpha, w, h, o, out var hw, out var hh);
        var bitmap = BitmapSource.Create(hw, hh, 96, 96, PixelFormats.Pbgra32, null, halo, hw * 4);
        bitmap.Freeze();
        Cache[(frame, o)] = bitmap;
        return bitmap;
    }

    /// <summary>Premultiplied BGRA halo for an alpha mask (0-1, w x h), padded by <see cref="Padding"/> all round.</summary>
    internal static byte[] Halo(float[] alpha, int w, int h, GlowOptions o, out int width, out int height)
    {
        var pad = Padding(o);
        width = w + 2 * pad;
        height = h + 2 * pad;
        var sprite = new float[width * height];
        for (var y = 0; y < h; y++)
            Array.Copy(alpha, y * w, sprite, (y + pad) * width + pad, w);

        var rim = Dilate(sprite, width, height, Math.Max(1, (int)Math.Round(o.OutlineWidth)));
        var glow = (float[])rim.Clone();
        var boxRadius = Math.Max(1, (int)Math.Round(o.GlowRadius / 2)); // three box passes ~ a Gaussian, sigma ~ radius/2
        for (var pass = 0; pass < 3; pass++) BoxBlur(glow, width, height, boxRadius);

        var c = o.Colour;
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < sprite.Length; i++)
        {
            var a = Math.Max(rim[i] * o.OutlineOpacity, glow[i] * o.GlowOpacity) * (1 - sprite[i]); // knock the sprite out
            bgra[i * 4] = (byte)(c.B * a);
            bgra[i * 4 + 1] = (byte)(c.G * a);
            bgra[i * 4 + 2] = (byte)(c.R * a);
            bgra[i * 4 + 3] = (byte)(255 * a);
        }
        return bgra;
    }

    /// <summary>Max filter (square kernel), separable.</summary>
    static float[] Dilate(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            float m = 0;
            for (int k = Math.Max(0, x - r), end = Math.Min(w - 1, x + r); k <= end; k++) m = Math.Max(m, src[y * w + k]);
            tmp[y * w + x] = m;
        }
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            float m = 0;
            for (int k = Math.Max(0, y - r), end = Math.Min(h - 1, y + r); k <= end; k++) m = Math.Max(m, tmp[k * w + x]);
            dst[y * w + x] = m;
        }
        return dst;
    }

    /// <summary>In-place box blur (horizontal then vertical) with running sums; edges treated as empty.</summary>
    static void BoxBlur(float[] data, int w, int h, int r)
    {
        var line = new float[Math.Max(w, h)];
        var norm = 1f / (2 * r + 1);
        for (var y = 0; y < h; y++)
        {
            float sum = 0;
            for (var k = 0; k <= Math.Min(r, w - 1); k++) sum += data[y * w + k];
            for (var x = 0; x < w; x++)
            {
                line[x] = sum * norm;
                if (x + r + 1 < w) sum += data[y * w + x + r + 1];
                if (x - r >= 0) sum -= data[y * w + x - r];
            }
            Array.Copy(line, 0, data, y * w, w);
        }
        for (var x = 0; x < w; x++)
        {
            float sum = 0;
            for (var k = 0; k <= Math.Min(r, h - 1); k++) sum += data[k * w + x];
            for (var y = 0; y < h; y++)
            {
                line[y] = sum * norm;
                if (y + r + 1 < h) sum += data[(y + r + 1) * w + x];
                if (y - r >= 0) sum -= data[(y - r) * w + x];
            }
            for (var y = 0; y < h; y++) data[y * w + x] = line[y];
        }
    }
}
