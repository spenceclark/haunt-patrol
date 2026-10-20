#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property NoWarn=CA1416
#:property PublishAot=false
#:property PublishTrimmed=false

// Imports exported animation frames (ZIPs or folders of PNGs, e.g. from Ludo) as a Haunt Patrol character:
// one horizontal strip per clip plus animations.json (format: ART-ASSETS.md). Sources are only read.
//
//   dotnet run tools/ImportSprite.cs -- --out HauntPatrol/Assets/Characters/Skeleton --id skeleton
//       --frame 220x340 --logical 110x170 --clip idle=ART-PROOF/Skeleton/Idle.zip:15:loop --clip attack=...:20:once
//
// Exports often differ in canvas size and character scale, so each clip is normalised by its FIRST frame
// (assumed to be the same neutral pose in every clip): scaled to a common height and anchored on that frame.
// One transform per clip keeps the motion inside the clip intact. The size is the largest that fits every
// frame of every clip inside the output frame, so nothing is clipped and the scale is consistent across clips.
//
//   --anchor feet    (default) first frame's feet (lowest opaque row) and bounding-box centre; the pivot goes
//                    at the feet, as low as the clips allow. For upright characters (skeleton, zombies).
//   --anchor centre  first frame's paw centre (centre of the opaque pixels in its bottom rows) and vertical
//                    centre; the pivot is the frame centre. For characters positioned by their middle (cat).
//                    Anchoring on the paws, not the bounding box, keeps a swishing tail from shifting the body
//                    and lets the game mirror the sprite in place.
//   --anchor box     first frame's bounding-box centre; the pivot is the frame centre. For floating characters
//                    with no feet or paws (ghost).

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

const byte AlphaThreshold = 16;
const int Margin = 3; // px kept clear at the frame edges

string? outDir = null, id = null;
var anchor = "feet";
Size frame = default, logical = default;
var clips = new List<(string Name, string Source, double Fps, bool Loop)>();
for (var i = 0; i < args.Length; i++)
{
    string Next() => args[++i];
    switch (args[i])
    {
        case "--out": outDir = Next(); break;
        case "--id": id = Next(); break;
        case "--frame": frame = ParseSize(Next()); break;
        case "--logical": logical = ParseSize(Next()); break;
        case "--anchor": anchor = Next(); break;
        case "--clip":
            var spec = Next();
            var eq = spec.IndexOf('=');
            var parts = spec[(eq + 1)..].Split(':');
            // Windows drive letters contain ':' too: the last two parts are fps and loop/once.
            var source = string.Join(':', parts[..^2]);
            clips.Add((spec[..eq], source, double.Parse(parts[^2]), parts[^1] == "loop"));
            break;
        default: throw new ArgumentException($"unknown argument {args[i]}");
    }
}
if (outDir == null || id == null || frame.IsEmpty || logical.IsEmpty || clips.Count == 0)
    throw new ArgumentException("need --out, --id, --frame WxH, --logical WxH and at least one --clip name=source:fps:loop|once");

// ---- load and measure ----------------------------------------------------------------------------
var loaded = clips.Select(c =>
{
    var frames = LoadFrames(c.Source);
    var boxes = frames.Select(Bounds).ToList();
    var first = boxes[0];
    var centred = anchor is "centre" or "box";
    return (c.Name, c.Fps, c.Loop, Frames: frames, Boxes: boxes,
        Height: (double)(first.Bottom - first.Top + 1),
        AnchorX: anchor == "centre" ? PawCentreX(frames[0], first) : (first.Left + first.Right + 1) / 2.0,
        AnchorY: centred ? (first.Top + first.Bottom + 1) / 2.0 : first.Bottom + 1.0);
}).ToList();

// Extents of every frame relative to its clip's anchor, in units of first-frame height.
double maxSide = 0, maxAbove = 0, maxBelow = 0;
foreach (var c in loaded)
foreach (var b in c.Boxes)
{
    maxSide = Math.Max(maxSide, Math.Max(c.AnchorX - b.Left, b.Right + 1 - c.AnchorX) / c.Height);
    maxAbove = Math.Max(maxAbove, (c.AnchorY - b.Top) / c.Height);
    maxBelow = Math.Max(maxBelow, (b.Bottom + 1 - c.AnchorY) / c.Height);
}
double standing;
PointF pivot;
if (anchor is "centre" or "box")
{
    standing = Math.Floor(Math.Min((frame.Width / 2.0 - Margin) / maxSide, (frame.Height / 2.0 - Margin) / Math.Max(maxAbove, maxBelow)));
    pivot = new PointF(frame.Width / 2f, frame.Height / 2f);
}
else
{
    standing = Math.Floor(Math.Min((frame.Width / 2.0 - Margin) / maxSide, (frame.Height - 2.0 * Margin) / (maxAbove + maxBelow)));
    pivot = new PointF(frame.Width / 2f, (float)Math.Round(Margin + standing * maxAbove));
}
Console.WriteLine($"standing height {standing} px in a {frame.Width}x{frame.Height} frame; pivot ({pivot.X}, {pivot.Y}) px " +
                  $"= ({pivot.X * logical.Width / frame.Width}, {pivot.Y * logical.Height / frame.Height}) logical");
Console.WriteLine($"limits ({anchor} anchor): widest reach {maxSide:0.00}x height to one side, {maxAbove:0.00}x above, {maxBelow:0.00}x below");

// ---- render strips -------------------------------------------------------------------------------
Directory.CreateDirectory(outDir);
var json = new StringBuilder();
json.Append($$"""
{
  "id": "{{id}}",
  "width": {{logical.Width}}, "height": {{logical.Height}},
  "pivotX": {{Fmt(pivot.X * logical.Width / frame.Width)}}, "pivotY": {{Fmt(pivot.Y * logical.Height / frame.Height)}},
  "animations": {

""");
for (var ci = 0; ci < loaded.Count; ci++)
{
    var c = loaded[ci];
    var scale = standing / c.Height;
    using var strip = new Bitmap(frame.Width * c.Frames.Count, frame.Height, PixelFormat.Format32bppPArgb);
    using (var g = Graphics.FromImage(strip))
    {
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY); // no smeared edges from the resampler
        for (var f = 0; f < c.Frames.Count; f++)
        {
            var src = c.Frames[f];
            // Each cell is clipped to itself so a stray edge pixel can never bleed into a neighbour.
            g.SetClip(new Rectangle(f * frame.Width, 0, frame.Width, frame.Height));
            var x = f * frame.Width + pivot.X - (float)(c.AnchorX * scale);
            var y = pivot.Y - (float)(c.AnchorY * scale);
            g.DrawImage(src, new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(src.Width * scale), (int)Math.Round(src.Height * scale)),
                0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
        }
    }
    var file = c.Name + ".png";
    strip.Save(Path.Combine(outDir, file), ImageFormat.Png);
    var duration = c.Frames.Count / c.Fps;
    Console.WriteLine($"  {file}: {c.Frames.Count} frames @ {c.Fps} fps = {duration:0.00}s {(c.Loop ? "loop" : "once")}, source scale {scale:0.000}");
    json.Append($$"""
        "{{c.Name}}": { "source": "{{file}}", "frameWidth": {{frame.Width}}, "frameHeight": {{frame.Height}}, "frameCount": {{c.Frames.Count}}, "fps": {{Fmt(c.Fps)}}, "loop": {{(c.Loop ? "true" : "false")}} }{{(ci < loaded.Count - 1 ? "," : "")}}

    """);
}
json.Append("  }\n}\n");
File.WriteAllText(Path.Combine(outDir, "animations.json"), json.ToString().Replace("\r\n", "\n"));
Console.WriteLine($"wrote {Path.Combine(outDir, "animations.json")}");

// ---- helpers -------------------------------------------------------------------------------------
static Size ParseSize(string s) { var p = s.Split('x'); return new Size(int.Parse(p[0]), int.Parse(p[1])); }
static string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

// Frames in playback order: PNGs sorted by name (frame_000, frame_001, ...), from a ZIP or a folder.
static List<Bitmap> LoadFrames(string source)
{
    var frames = new List<Bitmap>();
    if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        using var zip = ZipFile.OpenRead(source);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            frames.Add(ToPArgb(new Bitmap(ms)));
        }
    }
    else
    {
        foreach (var file in Directory.GetFiles(source, "*.png").OrderBy(f => f, StringComparer.Ordinal))
            frames.Add(ToPArgb(new Bitmap(file)));
    }
    if (frames.Count == 0) throw new InvalidDataException($"no PNG frames in {source}");
    return frames;
}

// Premultiplied alpha so resampling doesn't drag dark fringes in from transparent pixels.
static Bitmap ToPArgb(Bitmap b)
{
    var p = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb);
    using (var g = Graphics.FromImage(p)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(b, 0, 0, b.Width, b.Height); }
    b.Dispose();
    return p;
}

// Centre of the opaque pixels in the bottom 6% of the figure: where the feet/paws stand.
static double PawCentreX(Bitmap bmp, Rectangle box)
{
    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * bmp.Height];
    Marshal.Copy(data.Scan0, px, 0, px.Length);
    bmp.UnlockBits(data);
    long sum = 0, n = 0;
    var band = Math.Max(1, (box.Bottom - box.Top) * 6 / 100);
    for (var y = box.Bottom - band; y <= box.Bottom; y++)
    for (var x = box.Left; x <= box.Right; x++)
        if (px[y * data.Stride + x * 4 + 3] > AlphaThreshold) { sum += x; n++; }
    return n > 0 ? (double)sum / n : (box.Left + box.Right) / 2.0;
}

static Rectangle Bounds(Bitmap bmp)
{
    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * bmp.Height];
    Marshal.Copy(data.Scan0, px, 0, px.Length);
    bmp.UnlockBits(data);
    int l = bmp.Width, t = bmp.Height, r = -1, b = -1;
    for (var y = 0; y < bmp.Height; y++)
    for (var x = 0; x < bmp.Width; x++)
        if (px[y * data.Stride + x * 4 + 3] > AlphaThreshold) { l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y); }
    if (r < 0) throw new InvalidDataException("empty frame");
    return Rectangle.FromLTRB(l, t, r, b); // inclusive: Right/Bottom are the last opaque column/row
}
