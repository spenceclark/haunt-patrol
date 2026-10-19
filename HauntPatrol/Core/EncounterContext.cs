using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HauntPatrol.Audio;
using HauntPatrol.Rendering;

namespace HauntPatrol.Core;

/// <summary>Everything an encounter may use: its two layers, sprites, particles, audio, time, completion.</summary>
public sealed class EncounterContext
{
    /// <summary>Interactive layer: visible pixels here receive the mouse.</summary>
    public Canvas Stage { get; }
    /// <summary>Click-through layer: trajectories, particles, projectiles aimed at the viewer.</summary>
    public Canvas Effects { get; }
    public double Width { get; }
    public double Height { get; }
    public Random Random { get; }
    public bool Demo { get; }
    public AudioService Audio { get; }
    public ParticleSystem Particles { get; }
    public double Duration { get; }

    public double Elapsed { get; internal set; }
    public double TimeRemaining => Math.Max(0, Duration - Elapsed);

    public EncounterResult? Outcome { get; private set; }
    /// <summary>Where a failure hits the screen; the penalty starts there.</summary>
    public Point? ImpactPoint { get; set; }
    public bool IsComplete { get; private set; }

    readonly SpriteAssetLoader _assets;
    readonly Action _onComplete;
    readonly List<Sprite> _sprites = [];
    readonly List<Tween> _tweens = [];

    sealed class Tween(double duration, Action<double> step, Action? done)
    {
        public double Time;
        public readonly double Duration = duration;
        public readonly Action<double> Step = step;
        public readonly Action? Done = done;
    }

    public EncounterContext(Canvas stage, Canvas effects, Size size, SpriteAssetLoader assets, AudioService audio,
        Random random, bool demo, double duration, Action onComplete)
    {
        Stage = stage;
        Effects = effects;
        Width = size.Width;
        Height = size.Height;
        _assets = assets;
        Audio = audio;
        Random = random;
        Demo = demo;
        Duration = duration;
        _onComplete = onComplete;
        Particles = new ParticleSystem(effects, random);
    }

    /// <summary>Decide the outcome. Only the first call wins; returns false if already resolved.</summary>
    public bool Resolve(EncounterResult result)
    {
        if (Outcome != null) return false;
        Outcome = result;
        Mouse.Capture(null); // the game is decided: end any drag so desktop input is normal again
        return true;
    }

    /// <summary>Outro finished: the controller tears down and applies the outcome. Idempotent.</summary>
    public void Complete()
    {
        if (IsComplete) return;
        Outcome ??= EncounterResult.Cancelled;
        IsComplete = true;
        _onComplete();
    }

    public Sprite Spawn(string id, double x, double y, bool onEffectsLayer = false)
    {
        var sprite = new Sprite(_assets.Get(id)) { X = x, Y = y };
        if (onEffectsLayer) sprite.Element.IsHitTestVisible = false;
        (onEffectsLayer ? Effects : Stage).Children.Add(sprite.Element);
        _sprites.Add(sprite);
        sprite.Update(0);
        return sprite;
    }

    /// <summary>Big centred caption on the click-through layer that fades out after <paramref name="seconds"/>.</summary>
    public void Banner(string text, double y, double seconds, Color color)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 34,
            FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(color),
        };
        var box = new Border
        {
            Child = label,
            Padding = new Thickness(22, 8, 22, 10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 10, 30)),
            IsHitTestVisible = false,
        };
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(box, (Width - box.DesiredSize.Width) / 2);
        Canvas.SetTop(box, y - box.DesiredSize.Height / 2);
        Effects.Children.Add(box);
        After(seconds, () => Animate(0.4, k => box.Opacity = 1 - k, () => Effects.Children.Remove(box)));
    }

    /// <summary>Run <paramref name="step"/> with t from 0 to 1 over <paramref name="seconds"/>, then <paramref name="done"/>.</summary>
    public void Animate(double seconds, Action<double> step, Action? done = null) =>
        _tweens.Add(new Tween(Math.Max(seconds, 0.0001), step, done));

    public void After(double seconds, Action action) => Animate(seconds, _ => { }, action);

    internal void Update(double dt)
    {
        foreach (var t in _tweens.ToArray())
        {
            if (IsComplete) return;
            t.Time += dt;
            var k = Math.Min(1, t.Time / t.Duration);
            t.Step(k);
            if (k < 1) continue;
            _tweens.Remove(t);
            t.Done?.Invoke();
        }
        if (IsComplete) return;
        foreach (var s in _sprites.ToArray()) s.Update(dt);
        Particles.Update(dt);
    }
}
