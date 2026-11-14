using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodingEffect;

/// <summary>
/// キー入力なしでエフェクトを再生し、描画時間の計測と静止画の保存を行う。
/// 使い方: CodingEffect.exe --bench &lt;出力フォルダ&gt; [派手さ]
/// </summary>
internal static class Bench
{
    private const int W = 1600, H = 1000;

    public static unsafe void Run(string outDir, double intensity)
    {
        Directory.CreateDirectory(outDir);
        var settings = new Settings { Intensity = intensity }; // 保存はしない
        var engine = new EffectEngine(settings);

        // 再生する入力: 文字 20 → Enter → 文字 30（50 文字目で大爆発）→ Backspace 8
        var events = new List<(double t, KeyKind kind)>();
        double t = 0.2;
        for (int i = 0; i < 20; i++) { events.Add((t, KeyKind.Char)); t += 0.07; }
        double enterAt = t; events.Add((t, KeyKind.Enter)); t += 0.3;
        for (int i = 0; i < 30; i++) { events.Add((t, KeyKind.Char)); t += 0.07; }
        double boomAt = t - 0.07;
        t += 0.6;
        double fireAt = t;
        for (int i = 0; i < 8; i++) { events.Add((t, KeyKind.Delete)); t += 0.06; }
        double end = t + 1.5;

        var shots = new (double t, string name)[]
        {
            (0.2 + 0.07 * 5 + 0.12, "1_gather"),
            (enterAt + 0.05, "2_lightning"),
            (boomAt + 0.08, "3_explosion_a"),
            (boomAt + 0.45, "3_explosion_b"),
            (fireAt + 0.06 * 4 + 0.05, "4_fire"),
        };

        uint* buf = (uint*)NativeMemory.AllocZeroed((nuint)(W * H * 4));
        var times = new List<(double t, double ms, int particles, string phase)>();
        int caretX = 300, caretY = 480, line = 0, chars = 0;
        int ev = 0, shot = 0;
        const double dt = 1.0 / 60;

        for (double now = 0; now < end; now += dt)
        {
            while (ev < events.Count && events[ev].t <= now)
            {
                var kind = events[ev].kind;
                if (kind == KeyKind.Enter) { line++; chars = 0; }
                else if (kind == KeyKind.Delete) chars = Math.Max(0, chars - 1);
                else chars++;
                var caret = new RectangleF(caretX + chars * 13, caretY + line * 30, 2, 22);
                engine.Trigger(kind, caret, now);
                ev++;
            }

            if (!engine.Update((float)dt, now)) continue;
            var area = Rectangle.Intersect(engine.Bounds(now), new Rectangle(0, 0, W, H));
            if (area.Width <= 0 || area.Height <= 0) continue;

            long t0 = Stopwatch.GetTimestamp();
            for (int y = 0; y < area.Height; y++) new Span<uint>(buf + (long)y * W, area.Width).Clear();
            engine.RenderParticles((IntPtr)buf, W, area, now);
            using (var bmp = new Bitmap(area.Width, area.Height, W * 4, PixelFormat.Format32bppPArgb, (IntPtr)buf))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-area.X, -area.Y);
                engine.Render(g, now);
            }
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            string phase = now < enterAt ? "typing" : now < enterAt + 0.6 ? "lightning" : now < boomAt ? "typing" : now < fireAt ? "explosion" : "fire";
            times.Add((now, ms, engine.ParticleCount, phase));

            if (shot < shots.Length && now >= shots[shot].t)
            {
                SaveShot(Path.Combine(outDir, shots[shot].name + ".png"), buf, area, caretX, caretY, line);
                shot++;
            }
        }
        NativeMemory.Free(buf);

        var sb = new StringBuilder();
        sb.AppendLine($"intensity={intensity} frames={times.Count}");
        foreach (var grp in times.GroupBy(x => x.phase))
        {
            sb.AppendLine($"{grp.Key,-10} avg={grp.Average(x => x.ms):F1}ms max={grp.Max(x => x.ms):F1}ms over33ms={grp.Count(x => x.ms > 33)} maxParticles={grp.Max(x => x.particles)}");
        }
        File.WriteAllText(Path.Combine(outDir, "bench.txt"), sb.ToString());
    }

    private static unsafe void SaveShot(string path, uint* buf, Rectangle area, int caretX, int caretY, int line)
    {
        using var canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(24, 26, 36));
            using var font = new Font("Consolas", 16);
            g.DrawString("const std = @import(\"std\");", font, Brushes.Gainsboro, caretX - 260, caretY - 2);
            using var layer = new Bitmap(area.Width, area.Height, W * 4, PixelFormat.Format32bppPArgb, (IntPtr)buf);
            g.DrawImage(layer, area.X, area.Y);
        }
        canvas.Save(path, ImageFormat.Png);
    }
}
