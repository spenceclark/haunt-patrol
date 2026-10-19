#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property NoWarn=CA1416
#:property PublishAot=false
#:property PublishTrimmed=false

// Generates the placeholder art and sound effects in HauntPatrol/Assets.
// Run from the repo root:  dotnet run tools/MakeAssets.cs
// Production art replaces these files in place: same ids, logical sizes and pivots (see animations.json).

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var root = Path.GetFullPath(Path.Combine(args.Length > 0 ? args[0] : ".", "HauntPatrol", "Assets"));
Directory.CreateDirectory(root);

// ---------------------------------------------------------------- images

void Png(string relative, int w, int h, int frames, Action<Graphics, int> draw)
{
    using var bmp = new Bitmap(w * frames, h, PixelFormat.Format32bppArgb);
    bmp.SetResolution(96, 96);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        for (var f = 0; f < frames; f++)
        {
            var state = g.Save();
            g.TranslateTransform(w * f, 0);
            draw(g, f);
            g.Restore(state);
        }
    }
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    bmp.Save(path, ImageFormat.Png);
    Console.WriteLine($"  {relative}");
}

void Json(string relative, string text)
{
    File.WriteAllText(Path.Combine(root, relative), text);
    Console.WriteLine($"  {relative}");
}

Pen P(Color c, float w) => new(c, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
var outline = P(Color.FromArgb(255, 30, 20, 30), 3);

// Skeleton: white body with a skull, 110x170, pivot at the feet.
void Skeleton(Graphics g, Color tint, bool jawOpen, bool armUp, float squash)
{
    var bone = new SolidBrush(tint);
    var top = 170 - 170 * squash;
    var bodyTop = top + 72 * squash;
    g.FillRectangle(bone, 25, bodyTop, 60, 168 - bodyTop);
    g.DrawRectangle(outline, 25, bodyTop, 60, 168 - bodyTop);
    for (var y = bodyTop + 14; y < 150; y += 16) g.DrawLine(P(Color.FromArgb(120, 60, 60, 70), 3), 33, y, 77, y);
    g.FillEllipse(bone, 21, top + 4, 68, 64);
    g.DrawEllipse(outline, 21, top + 4, 68, 64);
    g.FillEllipse(Brushes.Black, 32, top + 24, 18, 18);
    g.FillEllipse(Brushes.Black, 60, top + 24, 18, 18);
    g.FillPolygon(Brushes.Black, new PointF[] { new(55, top + 44), new(50, top + 54), new(60, top + 54) });
    for (var x = 38; x <= 70; x += 8) g.DrawLine(P(Color.Black, 2), x, top + 58, x, top + (jawOpen ? 70 : 64));
    var shoulder = new PointF(85, bodyTop + 10);
    g.DrawLine(P(tint, 9), shoulder, armUp ? new PointF(104, bodyTop - 40) : new PointF(100, bodyTop + 52));
}

Png("Characters/Skeleton/idle.png", 110, 170, 2, (g, f) => Skeleton(g, Color.WhiteSmoke, f == 1, false, 1));
Png("Characters/Skeleton/attack.png", 110, 170, 1, (g, _) => Skeleton(g, Color.FromArgb(255, 255, 190, 180), true, true, 1));
Png("Characters/Skeleton/hit.png", 110, 170, 1, (g, _) => Skeleton(g, Color.FromArgb(255, 255, 240, 120), true, false, 1));
Png("Characters/Skeleton/defeat.png", 110, 170, 1, (g, _) => Skeleton(g, Color.Silver, true, false, 0.55f));
Json("Characters/Skeleton/animations.json", """
{
  "id": "skeleton",
  "width": 110, "height": 170,
  "pivotX": 55, "pivotY": 170,
  "animations": {
    "idle":   { "source": "idle.png",   "frameWidth": 110, "frameHeight": 170, "frameCount": 2, "fps": 3,   "loop": true },
    "attack": { "source": "attack.png", "frameWidth": 110, "frameHeight": 170, "frameCount": 1, "fps": 2.5, "loop": false },
    "hit":    { "source": "hit.png",    "frameWidth": 110, "frameHeight": 170, "frameCount": 1, "fps": 4,   "loop": false },
    "defeat": { "source": "defeat.png", "frameWidth": 110, "frameHeight": 170, "frameCount": 1, "fps": 2.5, "loop": false }
  }
}
""");

// Cat: black ellipse with green eyes, 96x64, faces right, pivot at the centre.
void Cat(Graphics g, int legPhase, bool hiss)
{
    var body = Brushes.Black;
    var arch = hiss ? 10 : 0;
    g.DrawBezier(P(Color.Black, 6), 14, 34, 2, 30, 4, 8, 12, 4);                     // tail
    g.FillEllipse(body, 10, 22 - arch, 62, 30 + arch);                                    // body
    g.FillEllipse(body, 56, 10, 34, 30);                                                  // head
    g.FillPolygon(body, new PointF[] { new(60, 16), new(62, 0), new(72, 12) });          // ears
    g.FillPolygon(body, new PointF[] { new(76, 12), new(86, 0), new(88, 18) });
    var eye = hiss ? Brushes.OrangeRed : Brushes.LimeGreen;
    g.FillEllipse(eye, 66, 18, 8, hiss ? 9 : 7);
    g.FillEllipse(eye, 78, 18, 8, hiss ? 9 : 7);
    if (hiss) g.FillEllipse(Brushes.Crimson, 76, 30, 8, 6);
    var legs = legPhase switch { 1 => new[] { 14, 30, 46, 62 }, 2 => new[] { 20, 26, 52, 58 }, _ => new[] { 18, 28, 50, 60 } };
    foreach (var x in legs) g.DrawLine(P(Color.Black, 6), x + 4, 46, x + (legPhase == 1 ? 0 : 6), 62);
}

Png("Characters/Cat/idle.png", 96, 64, 1, (g, _) => Cat(g, 0, false));
Png("Characters/Cat/run.png", 96, 64, 2, (g, f) => Cat(g, f + 1, false));
Png("Characters/Cat/hiss.png", 96, 64, 1, (g, _) => Cat(g, 0, true));
Json("Characters/Cat/animations.json", """
{
  "id": "cat",
  "width": 96, "height": 64,
  "pivotX": 48, "pivotY": 32,
  "animations": {
    "idle": { "source": "idle.png", "frameWidth": 96, "frameHeight": 64, "frameCount": 1, "fps": 1,  "loop": true },
    "run":  { "source": "run.png",  "frameWidth": 96, "frameHeight": 64, "frameCount": 2, "fps": 8,  "loop": true },
    "hiss": { "source": "hiss.png", "frameWidth": 96, "frameHeight": 64, "frameCount": 1, "fps": 1,  "loop": true }
  }
}
""");

// Props (logical size = pixel size, pivot = centre).
Png("Props/catapult.png", 150, 110, 1, (g, _) =>
{
    var wood = new SolidBrush(Color.FromArgb(255, 139, 90, 43));
    g.FillPolygon(wood, new PointF[] { new(10, 108), new(75, 28), new(140, 108) });
    g.DrawPolygon(outline, new PointF[] { new(10, 108), new(75, 28), new(140, 108) });
    g.DrawLine(P(Color.FromArgb(255, 100, 62, 30), 8), 75, 34, 75, 6);
    g.FillEllipse(new SolidBrush(Color.FromArgb(255, 100, 62, 30)), 60, 0, 30, 14);
    g.FillEllipse(Brushes.DimGray, 20, 92, 22, 18);
    g.FillEllipse(Brushes.DimGray, 108, 92, 22, 18);
});
Png("Props/projectile.png", 36, 36, 1, (g, _) =>
{
    g.FillEllipse(Brushes.Gold, 2, 2, 32, 32);
    g.DrawEllipse(P(Color.DarkGoldenrod, 3), 2, 2, 32, 32);
    g.FillEllipse(Brushes.LightYellow, 9, 8, 9, 7);
});
Png("Props/bone.png", 70, 26, 1, (g, _) =>
{
    var b = Brushes.WhiteSmoke;
    g.FillRectangle(b, 12, 9, 46, 8);
    foreach (var (x, y) in new[] { (2, 1), (2, 13), (56, 1), (56, 13) }) g.FillEllipse(b, x, y, 12, 12);
    g.DrawRectangle(P(Color.Gray, 1.5f), 12, 9, 46, 8);
});
Png("Props/broom.png", 44, 180, 1, (g, _) =>
{
    g.FillRectangle(new SolidBrush(Color.FromArgb(255, 150, 95, 45)), 18, 0, 8, 120);
    g.FillPolygon(Brushes.Goldenrod, new PointF[] { new(14, 114), new(30, 114), new(42, 178), new(2, 178) });
    g.DrawPolygon(P(Color.SaddleBrown, 2), new PointF[] { new(14, 114), new(30, 114), new(42, 178), new(2, 178) });
    g.DrawLine(P(Color.SaddleBrown, 4), 12, 122, 32, 122);
});
Png("Props/portal.png", 200, 200, 1, (g, _) =>
{
    using var path = new GraphicsPath();
    path.AddEllipse(4, 4, 192, 192);
    using var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(255, 30, 0, 50), SurroundColors = [Color.FromArgb(200, 170, 70, 255)] };
    g.FillEllipse(glow, 4, 4, 192, 192);
    for (var r = 20; r < 96; r += 22) g.DrawArc(P(Color.FromArgb(200, 220, 160, 255), 4), 100 - r, 100 - r, r * 2, r * 2, r * 3, 250);
    g.DrawEllipse(P(Color.FromArgb(255, 200, 120, 255), 5), 4, 4, 192, 192);
});
Png("Props/cauldron.png", 120, 96, 1, (g, _) =>
{
    g.FillEllipse(Brushes.DarkSlateGray, 8, 20, 104, 74);
    g.FillRectangle(Brushes.DarkSlateGray, 14, 18, 92, 16);
    g.FillEllipse(Brushes.LimeGreen, 14, 10, 92, 18);
    g.FillEllipse(Brushes.GreenYellow, 40, 6, 14, 12);
    g.FillEllipse(Brushes.GreenYellow, 70, 8, 10, 9);
    g.DrawEllipse(outline, 8, 20, 104, 74);
});
Png("Effects/slime.png", 260, 200, 1, (g, _) =>
{
    var rng = new Random(7);
    var slime = new SolidBrush(Color.FromArgb(235, 90, 200, 30));
    g.FillEllipse(slime, 60, 40, 140, 120);
    for (var i = 0; i < 14; i++)
    {
        var a = i / 14.0 * Math.PI * 2;
        var d = 70 + rng.Next(40);
        var s = 18 + rng.Next(26);
        g.FillEllipse(slime, (float)(130 + Math.Cos(a) * d - s / 2), (float)(100 + Math.Sin(a) * d * 0.8 - s / 2), s, s);
    }
    g.FillEllipse(new SolidBrush(Color.FromArgb(150, 200, 255, 140)), 95, 65, 40, 22);
});

// ---------------------------------------------------------------- sounds (22.05 kHz, 16-bit mono)

const int Rate = 22050;
var noise = new Random(99);

void Wav(string name, double seconds, Func<double, double> sample)
{
    var n = (int)(seconds * Rate);
    var path = Path.Combine(root, "Audio", name + ".wav");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var w = new BinaryWriter(File.Create(path));
    w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
    w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8); w.Write(n * 2);
    for (var i = 0; i < n; i++)
    {
        var t = (double)i / Rate;
        var fade = Math.Min(1, Math.Min(t / 0.005, (seconds - t) / 0.02)); // no clicks
        w.Write((short)(Math.Clamp(sample(t) * fade, -1, 1) * 0.6 * short.MaxValue));
    }
    Console.WriteLine($"  Audio/{name}.wav");
}

double Sine(double f, double t) => Math.Sin(2 * Math.PI * f * t);
double Noise() => noise.NextDouble() * 2 - 1;
double Decay(double t, double rate) => Math.Exp(-t * rate);

Wav("encounter", 0.9, t => Sine(440 - 260 * t, t) * (0.6 + 0.4 * Sine(9, t)) * Decay(t, 1.5));
Wav("pickup", 0.12, t => Sine(600 + 2500 * t, t) * Decay(t, 12));
Wav("launch", 0.35, t => Noise() * Decay(t, 8) * (0.4 + 0.6 * Sine(3, t)));
Wav("impact", 0.35, t => (Sine(90 * Decay(t, 4) + 40, t) + Noise() * Decay(t, 25)) * Decay(t, 9));
Wav("hiss", 0.5, t => Noise() * Sine(1.5, t) * Decay(t, 2) * 0.8);
Wav("success", 0.7, t => Sine(new[] { 523.25, 659.25, 783.99, 1046.5 }[Math.Min(3, (int)(t / 0.12))], t) * Decay(t % 0.12 + t * 0.3, 3));
Wav("failure", 0.8, t => Sine(300 - 180 * t, t) * (0.5 + 0.5 * Sine(6, t)) * Decay(t, 2));
Wav("glass", 0.9, t => (Noise() * Decay(t, 14) + Sine(3100, t) * Decay(t, 6) * 0.4 + Sine(4700, t) * Decay(Math.Max(0, t - 0.15), 7) * (t > 0.15 ? 0.3 : 0)));
Wav("slime", 0.7, t => Sine(70 + 40 * Sine(11, t), t) * Decay(t, 3) + Noise() * Decay(t, 20) * 0.5);

Console.WriteLine($"Assets written to {root}");
