using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HauntPatrol.Rendering;

/// <summary>
/// An Image on a Canvas positioned by its pivot, with a small animation player (named clips,
/// loop/one-shot, completion callback, playback speed) and procedural transforms (bob, shake, scale,
/// rotation, fade) so static placeholders still feel alive.
/// </summary>
public sealed class Sprite
{
    public SpriteDefinition Definition { get; }
    public Image Element { get; }

    public double X, Y, Rotation, ScaleX = 1, ScaleY = 1, Opacity = 1;
    public bool FlipX;
    public double BobAmplitude, BobFrequency = 1.5;
    /// <summary>Continuous jitter in DIPs (e.g. a creature winding up).</summary>
    public double ShakeAmount;
    public double PlaybackSpeed = 1;

    public double Width => Definition.Width;
    public double Height => Definition.Height;
    public string Animation => _clip.Name;

    AnimationClip _clip;
    double _clipTime, _age, _shakeTimer, _shakeBurst;
    double? _endAt;
    bool _holdLastFrame;
    Action? _onComplete;

    readonly ScaleTransform _scale = new();
    readonly RotateTransform _rotate = new();
    readonly TranslateTransform _translate = new();

    public Sprite(SpriteDefinition definition)
    {
        Definition = definition;
        Element = new Image { Width = definition.Width, Height = definition.Height, Stretch = Stretch.Fill, Focusable = false };
        _scale.CenterX = _rotate.CenterX = definition.Pivot.X;
        _scale.CenterY = _rotate.CenterY = definition.Pivot.Y;
        Element.RenderTransform = new TransformGroup { Children = { _scale, _rotate, _translate } };
        _clip = definition.Default;
        Element.Source = _clip.Frames[0];
    }

    /// <summary>
    /// Switch clip. A missing clip falls back to idle; if a callback was given it still fires (after a short beat),
    /// so gameplay never waits on artwork that doesn't exist yet.
    /// </summary>
    public void Play(string name, Action? onComplete = null)
    {
        var clip = Definition.Clips.GetValueOrDefault(name);
        if (clip != null && clip == _clip && onComplete == null && !_holdLastFrame) return;
        _endAt = clip == null ? (onComplete != null ? 0.35 : null) : clip.Loop ? null : clip.Duration;
        _clip = clip ?? Definition.Default;
        _clipTime = 0;
        _holdLastFrame = false;
        _onComplete = onComplete;
        Element.Source = _clip.Frames[0];
    }

    public void Shake(double seconds, double amount)
    {
        _shakeTimer = seconds;
        _shakeBurst = amount;
    }

    /// <summary>Bounding box in canvas DIPs (ignores rotation).</summary>
    public Rect Bounds
    {
        get
        {
            double sx = Math.Abs(ScaleX), sy = Math.Abs(ScaleY);
            return new Rect(X - Definition.Pivot.X * sx, Y - Definition.Pivot.Y * sy, Width * sx, Height * sy);
        }
    }

    public Point Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);

    public void Update(double dt)
    {
        _age += dt;
        _clipTime += dt * PlaybackSpeed;

        if (_endAt is double end && _clipTime >= end)
        {
            _endAt = null;
            _holdLastFrame = !_clip.Loop;
            var done = _onComplete;
            _onComplete = null;
            done?.Invoke(); // may Play() another clip
        }

        var frames = _clip.Frames;
        var index = (int)(_clipTime * _clip.Fps);
        index = _clip.Loop ? index % frames.Length : Math.Min(index, frames.Length - 1);
        if (!ReferenceEquals(Element.Source, frames[index])) Element.Source = frames[index];

        double ox = 0, oy = Math.Sin(_age * BobFrequency * 2 * Math.PI) * BobAmplitude;
        var shake = ShakeAmount;
        if (_shakeTimer > 0)
        {
            _shakeTimer -= dt;
            shake += _shakeBurst;
        }
        if (shake > 0)
        {
            ox += Math.Sin(_age * 97) * shake;
            oy += Math.Cos(_age * 71) * shake;
        }

        _scale.ScaleX = ScaleX * (FlipX ? -1 : 1);
        _scale.ScaleY = ScaleY;
        _rotate.Angle = Rotation;
        _translate.X = X - Definition.Pivot.X + ox;
        _translate.Y = Y - Definition.Pivot.Y + oy;
        Element.Opacity = Opacity;
    }
}

public static class Ease
{
    public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double InQuad(double t) => t * t;

    public static double OutBack(double t)
    {
        const double c = 1.70158;
        return 1 + (c + 1) * Math.Pow(t - 1, 3) + c * Math.Pow(t - 1, 2);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static Point Lerp(Point a, Point b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));
}
