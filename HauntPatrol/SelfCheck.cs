using System.Windows;
using System.Windows.Controls;
using HauntPatrol.Audio;
using HauntPatrol.Core;
using HauntPatrol.Encounters;
using HauntPatrol.Rendering;

namespace HauntPatrol;

/// <summary>`HauntPatrol.exe --selftest`: exit code 0 if the game rules hold. No windows are shown.</summary>
static class SelfCheck
{
    public static bool Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok) failures.Add(what);
        }

        // Ballistics: v=(1000,-1000), g=1500 from (200,800) lands back at y=800 near x=1533.
        bool Shoot(Rect target)
        {
            var p = new Point(200, 800);
            var v = new Vector(1000, -1000);
            for (var t = 0.0; t < 3; t += 1 / 240.0)
            {
                (p, v) = Ballistics.Step(p, v, 1500, 1 / 240.0);
                if (Ballistics.Hits(p, 18, target)) return true;
                if (p.Y > 800) return false;
            }
            return false;
        }
        Check(Shoot(new Rect(1480, 630, 110, 170)), "projectile should hit a skeleton at its landing point");
        Check(!Shoot(new Rect(560, 630, 110, 170)), "projectile should sail over a skeleton close to the catapult");

        // Steering: a broom just left of the cat drives it right; the cap holds.
        var cat = new Point(500, 500);
        var vel = new Vector();
        for (var i = 0; i < 30; i++)
        {
            vel = WitchsFamiliarEncounter.Steer(cat, vel, new Point(440, 500), new Point(2000, 500), Math.PI / 2, 1 / 60.0);
            cat += vel / 60.0;
        }
        Check(cat.X > 560, $"cat should flee the broom (x={cat.X:0})");
        Check(vel.Length <= WitchsFamiliarEncounter.MaxSpeed + 1e-6, "cat speed should be capped");

        // Ghost Trap loop validation: rough circle around the ghost succeeds; open, missed and tiny loops don't.
        List<Point> Circle(Point c, double r, double sweep) =>
            Enumerable.Range(0, 60).Select(i => c + new Vector(Math.Cos(i / 59.0 * sweep), Math.Sin(i / 59.0 * sweep)) * r).ToList();
        double Length(List<Point> pts) => pts.Zip(pts.Skip(1), (a, b) => (b - a).Length).Sum();
        var ghost = new Point(600, 400);
        var loop = Circle(ghost, 120, 2 * Math.PI);
        Check(LoopCheck.Evaluate(loop, Length(loop), ghost) == LoopVerdict.Captured, "closed loop around the ghost captures it");
        var arc = Circle(ghost, 120, 1.5 * Math.PI);
        Check(LoopCheck.Evaluate(arc, Length(arc), ghost) == LoopVerdict.Open, "three-quarter arc is an open loop");
        var elsewhere = Circle(new Point(1200, 400), 120, 2 * Math.PI);
        Check(LoopCheck.Evaluate(elsewhere, Length(elsewhere), ghost) == LoopVerdict.Missed, "closed loop elsewhere misses");
        var overshoot = Circle(ghost, 120, 2.4 * Math.PI);
        Check(LoopCheck.Evaluate(overshoot, Length(overshoot), ghost) == LoopVerdict.Captured, "loop that overshoots its start still captures");
        // Lasso: a tail along y=520 under the ghost, then an arc up over it and back down across the tail.
        var lasso = Enumerable.Range(0, 41).Select(i => new Point(300 + i * 10, 520))
            .Concat(Enumerable.Range(0, 60).Select(i =>
            {
                var a = (45 - i * 295.0 / 59) * Math.PI / 180;
                return new Point(600 + 140 * Math.Cos(a), 420 + 140 * Math.Sin(a));
            })).ToList();
        Check(LoopCheck.Evaluate(lasso, Length(lasso), ghost) == LoopVerdict.Captured, "lasso with a lead-in tail captures");
        var hook = lasso.Take(41 + 30).ToList(); // tail plus half the arc: never closes
        Check(LoopCheck.Evaluate(hook, Length(hook), ghost) == LoopVerdict.Open, "hook that never closes stays open");
        var tiny = Circle(ghost, 20, 2 * Math.PI);
        Check(LoopCheck.Evaluate(tiny, Length(tiny), ghost) == LoopVerdict.TooSmall, "tiny scribble is ignored");

        // Ghost Trap stage 2: smaller loops count for small ghosts, and one loop catches at most one ghost.
        var smallLoop = Circle(ghost, 50, 2 * Math.PI);
        Check(LoopCheck.Evaluate(smallLoop, Length(smallLoop), [ghost], new GhostTrapOptions().SmallLoop).Verdict == LoopVerdict.Captured, "small loop captures a small ghost");
        Check(LoopCheck.Evaluate(smallLoop, Length(smallLoop), ghost) == LoopVerdict.TooSmall, "same loop is too small for the big ghost");
        var both = Circle(ghost, 250, 2 * Math.PI);
        Check(LoopCheck.Evaluate(both, Length(both), [ghost + new Vector(-60, 0), ghost + new Vector(60, 0)], new GhostTrapOptions().SmallLoop) == (LoopVerdict.Captured, 0),
            "one loop around both ghosts captures only one");

        // Skeleton patrol: stays inside its bounds and turns around at both ends.
        double px = 500;
        int dir = 1, turns = 0;
        double pause = 0;
        for (var i = 0; i < 60 * 15; i++)
        {
            var before = dir;
            (px, dir, pause) = SkeletonSiegeEncounter.PatrolStep(px, dir, pause, 1 / 60.0, 400, 700, 90, 0.28);
            if (dir != before) turns++;
            if (px < 400 || px > 700) { failures.Add($"skeleton left its patrol ({px:0})"); break; }
        }
        Check(turns >= 3, $"skeleton should turn at both ends ({turns} turns in 15 s)");

        // Zombie Invasion rules and threat.
        var wave = new ZombieInvasionOptions().TotalZombies;
        Check(wave == 8, $"default roster is 8 zombies ({wave})");
        Check(ZombieInvasionEncounter.Judge(wave, wave, 0, 1, 5) == EncounterResult.Victory, "all zombies dealt with, brain alive: victory");
        Check(ZombieInvasionEncounter.Judge(wave, wave, 0, 0, 5) == EncounterResult.Failure, "brain destroyed: failure");
        Check(ZombieInvasionEncounter.Judge(6, wave, 2, 2, 0) == EncounterResult.Failure, "timer out with zombies left: failure");
        Check(ZombieInvasionEncounter.Judge(5, wave, 0, 2, 5) == null, "wave still spawning: keep playing");
        Check(ZombieInvasionEncounter.ThreatFor(1.0) >= 70 && ZombieInvasionEncounter.ThreatFor(8) < 30, "threat: near is critical, far is safe");
        Check(ZombieInvasionEncounter.CombinedThreat([1.0, 7, 8]) > ZombieInvasionEncounter.CombinedThreat([7, 8]), "killing the closest zombie lowers the threat");

        // Silhouette glow: nothing over the sprite itself, a rim just outside it, fading to nothing.
        var glowOptions = new GlowOptions();
        var square = new float[40 * 40];
        for (var y = 10; y < 30; y++) for (var x = 10; x < 30; x++) square[y * 40 + x] = 1;
        var halo = SpriteGlow.Halo(square, 40, 40, glowOptions, out var hw, out _);
        var hp = SpriteGlow.Padding(glowOptions);
        byte HaloAlpha(int x, int y) => halo[((y + hp) * hw + x + hp) * 4 + 3];
        Check(HaloAlpha(20, 20) == 0, "glow never covers the sprite's own pixels");
        Check(HaloAlpha(31, 20) >= 100, $"crisp rim just outside the silhouette ({HaloAlpha(31, 20)})");
        Check(HaloAlpha(36, 20) is > 0 and < 100, $"soft glow further out ({HaloAlpha(36, 20)})");
        Check(HaloAlpha(29 + hp, 20) < 8, $"glow fades out within its padding ({HaloAlpha(29 + hp, 20)})");

        // Exactly-once resolution and completion.
        var completions = 0;
        var ctx = new EncounterContext(new Canvas(), new Canvas(), new Size(800, 600), new SpriteAssetLoader(), new AudioService(),
            new Random(1), demo: true, duration: 20, () => completions++);
        Check(ctx.Resolve(EncounterResult.Victory), "first resolve wins");
        Check(!ctx.Resolve(EncounterResult.Failure), "second resolve is ignored");
        ctx.Complete();
        ctx.Complete();
        Check(ctx.Outcome == EncounterResult.Victory && completions == 1, "outcome fixed and completion fires once");

        // Every asset the games ask for exists and loads.
        var assets = new SpriteAssetLoader();
        foreach (var id in new[] { "skeleton", "cat", "catapult", "projectile", "bone", "broom", "portal", "cauldron", "slime",
                     "ghost", "pen", "ghostface", "zombie", "brute", "runner", "brain", "blaster", "bonehand", "handprint" })
        {
            try { assets.Get(id); }
            catch (Exception ex) { failures.Add($"asset {id}: {ex.Message}"); }
        }

        // Every sound parses as PCM for waveOut.
        var audioDir = System.IO.Path.Combine(SpriteAssetLoader.Root, "Audio");
        foreach (var wav in System.IO.Directory.EnumerateFiles(audioDir, "*.wav"))
            Check(Wave.Parse(System.IO.File.ReadAllBytes(wav)) is { Item2.Length: > 0 }, $"sound {System.IO.Path.GetFileName(wav)} parses as PCM");

        // WinExe has no console; leave the verdict next to the exe.
        System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "selftest.log"),
            failures.Count == 0 ? "selftest: all checks passed" : "selftest FAILED:\n  " + string.Join("\n  ", failures));
        return failures.Count == 0;
    }
}
