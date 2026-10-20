#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property NoWarn=CA1416
#:property PublishAot=false
#:property PublishTrimmed=false

// Imports one high-resolution transparent PNG (e.g. ART-PROOF/Props/broom.png) as a Haunt Patrol prop or
// effect image of a fixed size. The source is only read.
//
//   dotnet run tools/ImportProp.cs -- ART-PROOF/Props/broom.png HauntPatrol/Assets/Props/broom.png 44x180 --clean
//
// The visible pixels (alpha > 16) are scaled uniformly to the largest size that fits, never stretched, and
// padded with transparency. Placement:
//   (default)         centred.
//   --bottom          resting on the bottom edge, centred horizontally (cut-off wrists, cauldron feet).
//   --pin sx,sy:dx,dy source pixel (sx,sy) lands on output point (dx,dy) in logical pixels, e.g. a pen nib on
//                     the bottom centre, where the game expects the tip.
//   --res k           k output pixels per logical pixel, for hi-res sprites (Characters/<Name>/, §2.4).
//   --clean           for the interactive (alpha hit-tested) layer: drops the faint haze around the art before
//                     scaling and near-invisible fringe pixels after it, so it can't swallow desktop clicks.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

const byte AlphaThreshold = 16, FringeAlpha = 10;

var (srcPath, outPath, size) = (args[0], args[1], ParseSize(args[2]));
var (bottom, clean, res) = (false, false, 1);
(PointF From, PointF To)? pin = null;
for (var i = 3; i < args.Length; i++)
    switch (args[i])
    {
        case "--bottom": bottom = true; break;
        case "--clean": clean = true; break;
        case "--res": res = int.Parse(args[++i]); break;
        case "--pin":
            var p = args[++i].Split(':').Select(s => s.Split(',').Select(float.Parse).ToArray()).ToArray();
            pin = (new PointF(p[0][0], p[0][1]), new PointF(p[1][0], p[1][1]));
            break;
        default: throw new ArgumentException($"unknown argument {args[i]}");
    }

using var source = new Bitmap(srcPath);
var argb = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
if (clean) ClearBelow(argb, AlphaThreshold);
var box = Bounds(argb);
using var src = ToPArgb(argb); // premultiplied, so resampling doesn't drag dark fringes in from transparent pixels

var (w, h) = (size.Width * res, size.Height * res);
var scale = Math.Min((double)w / box.Width, (double)h / box.Height);
// Where the bounding box's top-left lands, in output pixels.
double x = (w - box.Width * scale) / 2, y = bottom ? h - box.Height * scale : (h - box.Height * scale) / 2;
if (pin is var (from, to))
{
    x = to.X * res - (from.X - box.Left) * scale;
    y = to.Y * res - (from.Y - box.Top) * scale;
}

using var output = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
using (var g = Graphics.FromImage(output))
{
    g.Clear(Color.Transparent);
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    using var wrap = new ImageAttributes();
    wrap.SetWrapMode(WrapMode.TileFlipXY); // no smeared edges from the resampler
    var (left, top) = ((float)(x - box.Left * scale), (float)(y - box.Top * scale));
    var (right, below) = (left + (float)(src.Width * scale), top + (float)(src.Height * scale));
    g.DrawImage(src, [new PointF(left, top), new PointF(right, top), new PointF(left, below)],
        new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel, wrap);
}
using var final = output.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb);
if (clean) ClearBelow(final, FringeAlpha);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
final.Save(outPath, ImageFormat.Png);

var placed = new RectangleF((float)x, (float)y, (float)(box.Width * scale), (float)(box.Height * scale));
var clipped = placed.Left < -0.5 || placed.Top < -0.5 || placed.Right > w + 0.5 || placed.Bottom > h + 0.5;
Console.WriteLine($"{Path.GetFileName(outPath)}: {w}x{h}, art {placed.Width:0.0}x{placed.Height:0.0} at ({placed.X:0.0},{placed.Y:0.0}), " +
                  $"scale {scale:0.0000}, {new FileInfo(outPath).Length / 1024.0:0.0} KB{(clipped ? "  WARNING: art extends past the canvas" : "")}");

static Size ParseSize(string s) { var p = s.Split('x'); return new Size(int.Parse(p[0]), int.Parse(p[1])); }

static Bitmap ToPArgb(Bitmap b)
{
    var p = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb);
    using (var g = Graphics.FromImage(p)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(b, 0, 0, b.Width, b.Height); }
    b.Dispose();
    return p;
}

// Fully clears every pixel whose alpha is below the limit (colour too, so nothing reappears when resampled).
static void ClearBelow(Bitmap bmp, byte limit)
{
    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * bmp.Height];
    Marshal.Copy(data.Scan0, px, 0, px.Length);
    for (var i = 0; i < px.Length; i += 4)
        if (px[i + 3] < limit) px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0;
    Marshal.Copy(px, 0, data.Scan0, px.Length);
    bmp.UnlockBits(data);
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
    if (r < 0) throw new InvalidDataException("empty image");
    return Rectangle.FromLTRB(l, t, r + 1, b + 1);
}
