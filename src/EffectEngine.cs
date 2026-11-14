using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodingEffect;

internal enum ParticleKind { Spark, Ember, Ring, Gather, Flash }

internal sealed class Particle
{
    public ParticleKind Kind;
    public float X, Y, VX, VY;
    public float PX, PY;          // 前フレームの位置（Gather の尾）
    public float SX, SY, TX, TY;  // Gather の出発点と目的地
    public float Swirl;           // Gather の渦巻き量（rad）
    public float Delay;           // 出現までの待ち時間
    public float Life, MaxLife;
    public float Size;
    public float Gravity, Drag;
    public float Radius;          // Ring / Flash の半径
    public float Flicker;         // Ember のゆらぎ位相
    public Color Core, Glow;
}

/// <summary>落雷 1 本。</summary>
internal sealed class Bolt
{
    public PointF Start, End;
    public float S;
    public float W = 1f;   // 太さの倍率
    public float Life, MaxLife;
    public float Regen;
    public readonly List<PointF[]> Main = new();
    public readonly List<PointF[]> Branches = new();
}

/// <summary>パーティクル・落雷・コンボ表示のシミュレーションと描画。</summary>
internal sealed class EffectEngine
{
    private const int MaxParticles = 10000;

    /// <summary>全体の強さ：粒の数の倍率（「派手さ」に掛かる）</summary>
    private const float PowerCount = 3.0f;
    /// <summary>全体の強さ：大きさ・広がりの倍率</summary>
    private const float PowerSize = 1.4f;
    private const float StreakTime = 0.03f;
    private const double ComboTimeout = 1.5;
    private const double ComboFade = 0.5;
    private const int ExplosionEvery = 50;

    private static readonly Color BoltGlow = Color.FromArgb(90, 170, 255);
    private static readonly Color BoltMid = Color.FromArgb(170, 225, 255);

    private readonly Settings _settings;
    private readonly List<Particle> _ps = new();
    private readonly List<Bolt> _bolts = new();
    private readonly Random _rng = new();
    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    private readonly FontFamily _font;
    private readonly FontStyle _fontStyle;

    private float _hue = 180;
    private int _combo;
    private double _lastKeyTime = -100;
    private float _comboPop;
    private float _comboScale = 1;
    private PointF _comboAnchor;
    private Color _comboColor = Color.Cyan;

    public int ParticleCount => _ps.Count;

    public EffectEngine(Settings settings)
    {
        _settings = settings;
        (_font, _fontStyle) = PickFont();
    }

    private static (FontFamily, FontStyle) PickFont()
    {
        foreach (var (name, style) in new[] { ("Segoe UI Black", FontStyle.Regular), ("Segoe UI", FontStyle.Bold), ("Arial", FontStyle.Bold) })
        {
            try
            {
                var f = new FontFamily(name);
                if (f.IsStyleAvailable(style)) return (f, style);
            }
            catch { }
        }
        return (FontFamily.GenericSansSerif, FontStyle.Regular);
    }

    private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ trigger

    public void Trigger(KeyKind kind, RectangleF caret, double now)
    {
        float h = Math.Max(caret.Height, 8f);
        float s0 = Math.Clamp(h / 22f, 0.6f, 3f);         // 文字サイズに合わせてエフェクトの大きさを変える
        float s = s0 * PowerSize;
        float k = (float)Math.Clamp(_settings.Intensity, 0.2, 5.0) * PowerCount;
        float cx = caret.X + caret.Width / 2f;
        float cy = caret.Y + h / 2f;

        switch (kind)
        {
            case KeyKind.Delete:
                if (_settings.FireOnDelete) SpawnFire(cx, caret.Y, h, s, k);
                else SpawnGather(cx, cy, s, k * 0.6f, ThemeColors(true));
                break;
            case KeyKind.Enter:
                SpawnLightning(cx, cy, s, k);
                break;
            case KeyKind.Space:
                SpawnGather(cx, cy, s, k * 0.7f, ThemeColors(true));
                break;
            default:
                SpawnGather(cx, cy, s, k, ThemeColors(true));
                break;
        }

        // コンボ：数えるのは文字（英数字・記号・スペース）だけ。Enter / 削除はコンボを途切れさせない
        bool alive = now - _lastKeyTime < ComboTimeout;
        bool counts = kind is KeyKind.Char or KeyKind.Space;
        if (counts) _combo = alive ? _combo + 1 : 1;
        else if (!alive) _combo = 0;
        _lastKeyTime = now;
        _comboScale = s0 * 1.3f;
        _comboAnchor = new PointF(caret.Right + 18 * s, caret.Y - 4 * s);
        _comboColor = ThemeColors(false).glow;
        if (counts)
        {
            _comboPop = 1f;
            if (_settings.Combo && _combo % ExplosionEvery == 0) SpawnBigExplosion(cx, cy, s, k);
        }

        if (_ps.Count > MaxParticles) _ps.RemoveRange(0, _ps.Count - MaxParticles);
    }

    // ------------------------------------------------------------------ 文字入力：粒子が集まる

    private void SpawnGather(float cx, float cy, float s, float k, (Color core, Color glow) col)
    {
        int n = Math.Max(4, (int)MathF.Round(R(14, 22) * k));
        float maxArrive = 0;
        for (int i = 0; i < n; i++)
        {
            float ang = R(0, MathF.PI * 2);
            float dist = R(35, 110) * s;
            float life = R(0.18f, 0.32f);
            float delay = R(0, 0.06f);
            maxArrive = Math.Max(maxArrive, life + delay);
            float sx = cx + MathF.Cos(ang) * dist, sy = cy + MathF.Sin(ang) * dist;
            _ps.Add(new Particle
            {
                Kind = ParticleKind.Gather,
                SX = sx, SY = sy, TX = cx, TY = cy,
                X = sx, Y = sy, PX = sx, PY = sy,
                Swirl = R(-1.6f, 1.6f),
                Delay = delay,
                Life = life, MaxLife = life,
                Size = R(1.4f, 3.0f) * s,
                Core = col.core, Glow = col.glow,
            });
        }

        // 集まりきった瞬間に光って弾ける
        float arrive = maxArrive * 0.8f;
        AddFlash(cx, cy, 16 * s, 0.22f, arrive, col.glow);
        AddRing(cx, cy, 28 * s, 1.6f * s, 0.3f, arrive, col.glow);
        int pops = Math.Max(2, (int)MathF.Round(R(4, 7) * k));
        for (int i = 0; i < pops; i++)
        {
            float ang = R(0, MathF.PI * 2);
            float sp = R(90, 260) * s;
            float life = R(0.2f, 0.45f);
            _ps.Add(new Particle
            {
                Kind = ParticleKind.Spark,
                X = cx, Y = cy,
                VX = MathF.Cos(ang) * sp, VY = MathF.Sin(ang) * sp,
                Delay = arrive,
                Life = life, MaxLife = life,
                Size = R(0.9f, 1.8f) * s,
                Gravity = 250 * s, Drag = 3f,
                Core = col.core, Glow = col.glow,
            });
        }
    }

    // ------------------------------------------------------------------ Enter：落雷

    private void SpawnLightning(float cx, float cy, float s, float k)
    {
        var mon = Screen.FromPoint(new Point((int)cx, (int)cy)).Bounds;

        // 本命の太い雷 + 細い副雷 1〜2 本
        float startX = cx + R(-120, 120) * s;
        AddBolt(new PointF(startX, mon.Top - 40), new PointF(cx, cy), s, 1f, 0.6f);
        int extra = Math.Min(6, _rng.Next(2, 3 + (int)(k / 2)));
        for (int i = 0; i < extra; i++)
        {
            var end = new PointF(cx + R(-160, 160) * s, cy + R(-60, 30) * s);
            AddBolt(new PointF(startX + R(-250, 250) * s, mon.Top - 40), end, s, 0.5f, R(0.3f, 0.45f));
        }

        // 空の発光
        AddFlash(startX, mon.Top, 220 * s, 0.3f, 0, BoltGlow, core: false);

        // 着弾
        AddFlash(cx, cy, 55 * s, 0.3f, 0, Color.White);
        AddRing(cx, cy, 85 * s, 3.2f * s, 0.5f, 0, BoltMid);
        AddRing(cx, cy, 140 * s, 2.0f * s, 0.6f, 0.06f, BoltGlow);
        int n = Math.Max(12, (int)MathF.Round(45 * k));
        for (int i = 0; i < n; i++)
        {
            float ang = R(MathF.PI * 0.9f, MathF.PI * 2.1f); // 上半分へ跳ねる
            float sp = R(180, 620) * s;
            float life = R(0.35f, 0.9f);
            _ps.Add(new Particle
            {
                Kind = ParticleKind.Spark,
                X = cx, Y = cy,
                VX = MathF.Cos(ang) * sp, VY = MathF.Sin(ang) * sp,
                Life = life, MaxLife = life,
                Size = R(1.0f, 2.2f) * s,
                Gravity = 700 * s, Drag = 2.0f,
                Core = Color.White, Glow = BoltGlow,
            });
        }
    }

    private void AddBolt(PointF start, PointF end, float s, float width, float life)
    {
        var bolt = new Bolt
        {
            Start = start, End = end, S = s, W = width,
            Life = life, MaxLife = life,
            Regen = 0.1f,
        };
        BuildBolt(bolt);
        _bolts.Add(bolt);
    }

    private void BuildBolt(Bolt b)
    {
        b.Main.Clear();
        b.Branches.Clear();
        var main = Jag(b.Start, b.End, 0.18f, 7);
        b.Main.Add(main);

        float total = Dist(b.Start, b.End);
        int branches = b.W >= 1f ? _rng.Next(4, 8) : _rng.Next(1, 3);
        for (int i = 0; i < branches; i++)
        {
            int idx = (int)(main.Length * R(0.15f, 0.75f));
            var from = main[idx];
            float ang = MathF.PI / 2 + R(0.35f, 1.0f) * (_rng.Next(2) == 0 ? -1 : 1); // 下向き ± 20〜57°
            float len = total * R(0.08f, 0.22f);
            var to = new PointF(from.X + MathF.Cos(ang) * len, from.Y + MathF.Sin(ang) * len);
            b.Branches.Add(Jag(from, to, 0.22f, 5));
        }
    }

    private PointF[] Jag(PointF a, PointF b, float rough, int depth)
    {
        var pts = new List<PointF> { a, b };
        float disp = Dist(a, b) * rough;
        for (int d = 0; d < depth; d++)
        {
            var next = new List<PointF>(pts.Count * 2);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p = pts[i];
                var q = pts[i + 1];
                float dx = q.X - p.X, dy = q.Y - p.Y;
                float len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
                float nx = -dy / len, ny = dx / len;
                float off = R(-disp, disp);
                next.Add(p);
                next.Add(new PointF((p.X + q.X) / 2 + nx * off, (p.Y + q.Y) / 2 + ny * off));
            }
            next.Add(pts[^1]);
            pts = next;
            disp *= 0.5f;
        }
        return pts.ToArray();
    }

    private static float Dist(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ------------------------------------------------------------------ 削除：炎

    private void SpawnFire(float cx, float top, float h, float s, float k)
    {
        int n = Math.Max(8, (int)MathF.Round(R(36, 50) * k));
        for (int i = 0; i < n; i++)
            AddEmber(cx + R(-60, 4) * s, top + R(0.0f, 1.0f) * h,
                     -R(60, 420) * s, -R(20, 220) * s,
                     R(0.4f, 1.1f), R(2.2f, 5.0f) * s, -260 * s, 2.4f);

        AddFlash(cx, top + h / 2, 22 * s, 0.2f, 0, Color.FromArgb(255, 140, 30));

        // 火の粉
        int sparks = Math.Max(3, (int)MathF.Round(R(8, 12) * k));
        for (int i = 0; i < sparks; i++)
        {
            float ang = (-90 + R(-85, 30)) * MathF.PI / 180f;
            float sp = R(120, 460) * s;
            float life = R(0.35f, 0.8f);
            _ps.Add(new Particle
            {
                Kind = ParticleKind.Spark,
                X = cx, Y = top + h / 2,
                VX = MathF.Cos(ang) * sp, VY = MathF.Sin(ang) * sp,
                Life = life, MaxLife = life,
                Size = R(0.9f, 1.8f) * s,
                Gravity = 500 * s, Drag = 2.2f,
                Core = Color.FromArgb(255, 250, 210), Glow = Color.FromArgb(255, 150, 20),
            });
        }
    }

    private void AddEmber(float x, float y, float vx, float vy, float life, float size, float gravity, float drag, float delay = 0) =>
        _ps.Add(new Particle
        {
            Kind = ParticleKind.Ember,
            X = x, Y = y, VX = vx, VY = vy,
            Delay = delay,
            Life = life, MaxLife = life,
            Size = size, Gravity = gravity, Drag = drag,
            Flicker = R(0, MathF.PI * 2),
        });

    // ------------------------------------------------------------------ 50 文字コンボ：大爆発

    private void SpawnBigExplosion(float cx, float cy, float s, float k)
    {
        AddFlash(cx, cy, 180 * s, 0.45f, 0, Color.White);
        AddRing(cx, cy, 160 * s, 6f * s, 0.55f, 0.00f, Color.White);
        AddRing(cx, cy, 240 * s, 4.5f * s, 0.70f, 0.07f, Color.FromArgb(255, 170, 40));
        AddRing(cx, cy, 320 * s, 3.5f * s, 0.85f, 0.14f, _comboColor);
        AddRing(cx, cy, 420 * s, 2.5f * s, 1.00f, 0.22f, Color.FromArgb(255, 80, 200));

        Burst(cx, cy, s, k, 1f, 0);

        // 周りで連鎖する小爆発
        int chain = _rng.Next(4, 7);
        for (int i = 0; i < chain; i++)
        {
            float ang = R(0, MathF.PI * 2);
            float dist = R(80, 200) * s;
            float x = cx + MathF.Cos(ang) * dist, y = cy + MathF.Sin(ang) * dist;
            float delay = R(0.12f, 0.5f);
            AddFlash(x, y, 55 * s, 0.25f, delay, Color.FromArgb(255, 200, 80));
            AddRing(x, y, 90 * s, 2.5f * s, 0.45f, delay, _comboColor);
            Burst(x, y, s, k, 0.3f, delay);
        }
    }

    /// <summary>火球と虹色の火花を全方向へ。</summary>
    private void Burst(float cx, float cy, float s, float k, float scale, float delay)
    {
        int embers = (int)MathF.Round(140 * k * scale);
        for (int i = 0; i < embers; i++)
        {
            float ang = R(0, MathF.PI * 2);
            float sp = R(100, 750) * s * MathF.Sqrt(scale);
            AddEmber(cx, cy, MathF.Cos(ang) * sp, MathF.Sin(ang) * sp,
                     R(0.6f, 1.4f), R(3.5f, 8f) * s, -120 * s, 2.0f, delay);
        }

        int sparks = (int)MathF.Round(180 * k * scale);
        for (int i = 0; i < sparks; i++)
        {
            float hue = R(0, 360);
            float ang = R(0, MathF.PI * 2);
            float sp = R(300, 1200) * s * MathF.Sqrt(scale);
            float life = R(0.6f, 1.6f);
            _ps.Add(new Particle
            {
                Kind = ParticleKind.Spark,
                X = cx, Y = cy,
                VX = MathF.Cos(ang) * sp, VY = MathF.Sin(ang) * sp,
                Delay = delay,
                Life = life, MaxLife = life,
                Size = R(1.6f, 3.6f) * s,
                Gravity = 450 * s, Drag = 1.6f,
                Core = Hsv(hue, 0.2f, 1), Glow = Hsv(hue, 1, 1),
            });
        }
    }

    // ------------------------------------------------------------------ helpers

    private void AddRing(float x, float y, float radius, float width, float life, float delay, Color c) =>
        _ps.Add(new Particle
        {
            Kind = ParticleKind.Ring,
            X = x, Y = y, Radius = radius, Size = width,
            Life = life, MaxLife = life, Delay = delay, Glow = c, Core = c,
        });

    private void AddFlash(float x, float y, float radius, float life, float delay, Color c, bool core = true) =>
        _ps.Add(new Particle
        {
            Kind = ParticleKind.Flash,
            X = x, Y = y, Radius = radius,
            Life = life, MaxLife = life, Delay = delay, Glow = c, Core = core ? Color.White : Color.Empty,
        });

    private (Color core, Color glow) ThemeColors(bool advance)
    {
        switch (_settings.Theme)
        {
            case "Rainbow":
                if (advance) _hue = (_hue + 17) % 360;
                return (Hsv(_hue, 0.25f, 1), Hsv(_hue, 1, 1));
            case "Fire":
                return (Color.FromArgb(255, 245, 190), Color.FromArgb(255, 120, 0));
            case "Sakura":
                return (Color.FromArgb(255, 235, 245), Color.FromArgb(255, 110, 185));
            case "Matrix":
                return (Color.FromArgb(210, 255, 220), Color.FromArgb(0, 255, 90));
            default: // Cyan
                return (Color.FromArgb(225, 255, 255), Color.FromArgb(0, 225, 255));
        }
    }

    // ------------------------------------------------------------------ update

    /// <returns>まだ描画するものがあれば true</returns>
    public bool Update(float dt, double now)
    {
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i];
            if (p.Delay > 0)
            {
                p.Delay -= dt;
                continue;
            }
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _ps[i] = _ps[^1];
                _ps.RemoveAt(_ps.Count - 1);
                continue;
            }

            if (p.Kind == ParticleKind.Gather)
            {
                float prog = 1 - p.Life / p.MaxLife;
                float e = prog * prog * prog; // だんだん加速して吸い込まれる
                float dx = p.SX - p.TX, dy = p.SY - p.TY;
                float rot = p.Swirl * e;
                float c = MathF.Cos(rot), sn = MathF.Sin(rot);
                p.PX = p.X;
                p.PY = p.Y;
                p.X = p.TX + (dx * c - dy * sn) * (1 - e);
                p.Y = p.TY + (dx * sn + dy * c) * (1 - e);
                continue;
            }

            float d = MathF.Exp(-p.Drag * dt);
            p.VX *= d;
            p.VY = p.VY * d + p.Gravity * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
        }

        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Life -= dt;
            if (b.Life <= 0) { _bolts.RemoveAt(i); continue; }
            b.Regen -= dt;
            if (b.Regen <= 0)
            {
                BuildBolt(b);  // 形を変えて再放電
                b.Regen = 0.1f;
            }
        }

        _comboPop = Math.Max(0, _comboPop - dt * 5f);
        return _ps.Count > 0 || _bolts.Count > 0 || ComboAlpha(now) > 0;
    }

    private float ComboAlpha(double now)
    {
        if (!_settings.Combo || _combo < _settings.ComboMin) return 0;
        double idle = now - _lastKeyTime;
        if (idle < ComboTimeout) return 1;
        if (idle < ComboTimeout + ComboFade) return (float)(1 - (idle - ComboTimeout) / ComboFade);
        return 0;
    }

    /// <summary>描画が必要な範囲（スクリーン座標）。</summary>
    public Rectangle Bounds(double now)
    {
        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        void Inc(float x0, float y0, float x1, float y1)
        {
            if (x0 < l) l = x0;
            if (y0 < t) t = y0;
            if (x1 > r) r = x1;
            if (y1 > b) b = y1;
        }

        foreach (var p in _ps)
        {
            switch (p.Kind)
            {
                case ParticleKind.Ring:
                case ParticleKind.Flash:
                {
                    float e = p.Radius + p.Size * 3 + 4;
                    Inc(p.X - e, p.Y - e, p.X + e, p.Y + e);
                    break;
                }
                case ParticleKind.Gather:
                {
                    float e = p.Size * 3.5f;
                    float ex = Math.Abs(p.SX - p.TX) + e, ey = Math.Abs(p.SY - p.TY) + e;
                    Inc(p.TX - ex, p.TY - ey, p.TX + ex, p.TY + ey);
                    break;
                }
                case ParticleKind.Spark:
                {
                    float e = p.Size * 3.5f;
                    float tx = p.X - p.VX * StreakTime, ty = p.Y - p.VY * StreakTime;
                    Inc(Math.Min(p.X, tx) - e, Math.Min(p.Y, ty) - e, Math.Max(p.X, tx) + e, Math.Max(p.Y, ty) + e);
                    break;
                }
                default:
                {
                    float e = p.Size * 4.5f;
                    Inc(p.X - e, p.Y - e, p.X + e, p.Y + e);
                    break;
                }
            }
        }

        foreach (var bolt in _bolts)
        {
            float e = 22 * bolt.S * bolt.W;
            foreach (var path in bolt.Main) foreach (var pt in path) Inc(pt.X - e, pt.Y - e, pt.X + e, pt.Y + e);
            foreach (var path in bolt.Branches) foreach (var pt in path) Inc(pt.X - e, pt.Y - e, pt.X + e, pt.Y + e);
        }

        if (ComboAlpha(now) > 0)
        {
            var (big, small) = ComboSizes();
            int digits = _combo.ToString().Length + 1;
            float s = _comboScale;
            Inc(_comboAnchor.X - 12 * s,
                _comboAnchor.Y - big * 1.3f - small * 1.4f - 12 * s,
                _comboAnchor.X + big * 0.8f * digits + 20 * s,
                _comboAnchor.Y + 12 * s);
        }

        if (l > r || t > b) return Rectangle.Empty;
        return Rectangle.FromLTRB((int)MathF.Floor(l) - 4, (int)MathF.Floor(t) - 4, (int)MathF.Ceiling(r) + 4, (int)MathF.Ceiling(b) + 4);
    }

    private (float big, float small) ComboSizes() =>
        (40 * _comboScale * (1 + _comboPop * 0.6f), 15 * _comboScale);

    // ------------------------------------------------------------------ render

    public void Render(Graphics g, double now)
    {
        // 火花・炎・集束の粒は RenderParticles で描画済み。ここでは数の少ないものだけ GDI+ で描く
        foreach (var b in _bolts) DrawBolt(g, b);
        foreach (var p in _ps)
        {
            if (p.Delay > 0) continue;
            switch (p.Kind)
            {
                case ParticleKind.Ring: DrawRing(g, p); break;
                case ParticleKind.Flash: DrawFlash(g, p); break;
            }
        }
        DrawCombo(g, now);
    }

    // ------------------------------------------------------------------ 高速パーティクル描画（加算合成）

    /// <summary>
    /// 火花・炎・集束の粒を、premultiplied BGRA のバッファへ直接「加算合成」で描く。
    /// GDI+ より桁違いに速く、重なった光が明るくなるので発光感も強い。
    /// </summary>
    public void RenderParticles(IntPtr bits, int stridePixels, Rectangle area, double now)
    {
        // 画面を横帯に分けて CPU コアごとに並列で描く（帯ごとに書き込む行が分かれるので競合しない）
        int bands = Math.Clamp(Environment.ProcessorCount, 1, 16);
        int bandH = (area.Height + bands - 1) / bands;
        System.Threading.Tasks.Parallel.For(0, bands, b =>
        {
            int y0 = b * bandH, y1 = Math.Min(area.Height, y0 + bandH);
            if (y0 < y1) RenderBand(bits, stridePixels, area, now, y0, y1);
        });
    }

    private unsafe void RenderBand(IntPtr bits, int stridePixels, Rectangle area, double now, int bandTop, int bandBottom)
    {
        // バッファの先頭を帯の上端にずらし、帯の高さを h として扱う
        uint* buf = (uint*)bits + (long)bandTop * stridePixels;
        int w = area.Width, h = bandBottom - bandTop;
        float ox = area.X, oy = area.Y + bandTop;

        foreach (var p in _ps)
        {
            if (p.Delay > 0) continue;
            switch (p.Kind)
            {
                case ParticleKind.Spark:
                {
                    float t = p.Life / p.MaxLife;
                    float a = MathF.Pow(t, 0.8f);
                    float tx = p.X - p.VX * StreakTime, ty = p.Y - p.VY * StreakTime;
                    float glowR = p.Size * 2.1f * (0.6f + 0.4f * t);
                    float coreR = Math.Max(0.8f, p.Size * 0.75f * (0.5f + 0.5f * t));
                    StampLine(buf, w, h, stridePixels, tx - ox, ty - oy, p.X - ox, p.Y - oy, glowR, p.Glow, a * 0.45f, soft: true);
                    StampLine(buf, w, h, stridePixels, tx - ox, ty - oy, p.X - ox, p.Y - oy, coreR, Lerp(p.Glow, p.Core, t), a, soft: false);
                    break;
                }
                case ParticleKind.Gather:
                {
                    float prog = 1 - p.Life / p.MaxLife;
                    float a = Math.Min(1f, prog * 4f);
                    float size = p.Size * (1.1f - prog * 0.4f);
                    StampLine(buf, w, h, stridePixels, p.PX - ox, p.PY - oy, p.X - ox, p.Y - oy, size * 2.1f, p.Glow, a * 0.45f, soft: true);
                    StampLine(buf, w, h, stridePixels, p.PX - ox, p.PY - oy, p.X - ox, p.Y - oy, Math.Max(0.8f, size * 0.75f), Lerp(p.Glow, p.Core, prog), a, soft: false);
                    break;
                }
                case ParticleKind.Ember:
                {
                    float t = p.Life / p.MaxLife;
                    float age = 1 - t;
                    Color c = FireGradient(age);
                    float flicker = 0.75f + 0.25f * MathF.Sin((float)now * 40f + p.Flicker);
                    float a = t * flicker;
                    float glowR = p.Size * (0.8f + age * 0.9f) * 2.3f;
                    Stamp(buf, w, h, stridePixels, p.X - ox, p.Y - oy, glowR, c, a * 0.5f, soft: true);
                    float coreR = Math.Max(0.8f, p.Size * (1.1f - age) * 1.2f);
                    Stamp(buf, w, h, stridePixels, p.X - ox, p.Y - oy, coreR, Lerp(c, Color.FromArgb(255, 255, 230), t * 0.6f), a, soft: false);
                    break;
                }
            }
        }
    }

    /// <summary>線分に沿って丸い点を並べる（光の尾）。</summary>
    private static unsafe void StampLine(uint* buf, int w, int h, int stride, float x0, float y0, float x1, float y1,
        float r, Color c, float a, bool soft)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        int n = Math.Clamp((int)(len / Math.Max(1f, r * 1.2f)) + 1, 1, soft ? 3 : 6);
        float each = n == 1 ? a : a * MathF.Min(1f, 1.8f / n);
        for (int i = 0; i < n; i++)
        {
            float f = n == 1 ? 1f : i / (float)(n - 1);
            // 先端ほど明るく
            Stamp(buf, w, h, stride, x0 + dx * f, y0 + dy * f, r, c, each * (0.35f + 0.65f * f), soft);
        }
    }

    /// <summary>円を 1 つ加算合成で描く。soft=true は中心から外へなめらかに減衰、false は縁だけぼかした円。</summary>
    private static unsafe void Stamp(uint* buf, int w, int h, int stride, float cx, float cy, float r, Color c, float a, bool soft)
    {
        if (a <= 0.004f) return;
        if (cy + r < 0 || cy - r >= h || cx + r < 0 || cx - r >= w) return; // この帯にかからない
        if (r < 1f) { a *= r * r; r = 1f; }
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(w - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(h - 1, (int)(cy + r) + 1);
        if (x0 > x1 || y0 > y1) return;

        a = Math.Min(a, 1f);
        float sr = c.R * a, sg = c.G * a, sb = c.B * a, sa = 255f * a;
        float inv = 1f / (r * r);
        float edge = soft ? 0 : Math.Min(1f, 2.5f / r); // ハード円の縁の幅（約 1px）

        for (int y = y0; y <= y1; y++)
        {
            float fy = y + 0.5f - cy;
            float dy2 = fy * fy;
            uint* row = buf + (long)y * stride;
            for (int x = x0; x <= x1; x++)
            {
                float fx = x + 0.5f - cx;
                float d2 = (fx * fx + dy2) * inv;
                if (d2 >= 1f) continue;
                float f;
                if (soft)
                {
                    f = 1f - d2;
                    f *= f;
                }
                else
                {
                    f = (1f - d2) / edge;
                    if (f > 1f) f = 1f;
                }

                uint px = row[x];
                int nb = (int)(px & 0xFF) + (int)(sb * f);
                int ng = (int)((px >> 8) & 0xFF) + (int)(sg * f);
                int nr = (int)((px >> 16) & 0xFF) + (int)(sr * f);
                int na = (int)(px >> 24) + (int)(sa * f);
                if (nb > 255) nb = 255;
                if (ng > 255) ng = 255;
                if (nr > 255) nr = 255;
                if (na > 255) na = 255;
                row[x] = (uint)(nb | (ng << 8) | (nr << 16) | (na << 24));
            }
        }
    }

    private void DrawBolt(Graphics g, Bolt b)
    {
        float t = b.Life / b.MaxLife;
        float age = b.MaxLife - b.Life;
        float a = age < 0.06f ? 1f : t * (0.55f + 0.45f * MathF.Abs(MathF.Sin(age * 55f)));
        float s = b.S;

        foreach (var path in b.Main) DrawBoltPath(g, path, s, a, b.W);
        foreach (var path in b.Branches) DrawBoltPath(g, path, s, a * 0.8f, b.W * 0.45f);
    }

    private void DrawBoltPath(Graphics g, PointF[] pts, float s, float a, float w)
    {
        if (pts.Length < 2) return;
        _pen.Color = WithAlpha(BoltGlow, a * 0.12f);
        _pen.Width = 40 * s * w;
        g.DrawLines(_pen, pts);
        _pen.Color = WithAlpha(BoltGlow, a * 0.3f);
        _pen.Width = 18 * s * w;
        g.DrawLines(_pen, pts);
        _pen.Color = WithAlpha(BoltMid, a * 0.7f);
        _pen.Width = 7.5f * s * w;
        g.DrawLines(_pen, pts);
        _pen.Color = WithAlpha(Color.White, a);
        _pen.Width = Math.Max(1f, 3.2f * s * w);
        g.DrawLines(_pen, pts);
    }

    private void DrawGather(Graphics g, Particle p, bool glow)
    {
        float prog = 1 - p.Life / p.MaxLife;
        float a = Math.Min(1f, prog * 4f);                 // フェードイン
        float size = p.Size * (1.1f - prog * 0.4f);
        if (glow)
            DrawSegment(g, p.PX, p.PY, p.X, p.Y, size * 3.2f, WithAlpha(p.Glow, a * 0.3f));
        else
            DrawSegment(g, p.PX, p.PY, p.X, p.Y, size, WithAlpha(Lerp(p.Glow, p.Core, prog), a));
    }

    private void DrawFlash(Graphics g, Particle p)
    {
        float t = p.Life / p.MaxLife;
        float a = t * t;
        float r = p.Radius * (0.6f + 0.4f * (1 - t));
        float outer = r * 1.6f;
        if (outer < 1) return;

        // 中心から外へ透明になる柔らかい光
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(p.X - outer, p.Y - outer, outer * 2, outer * 2);
            using var pgb = new PathGradientBrush(path)
            {
                CenterColor = WithAlpha(Lerp(p.Glow, Color.White, 0.3f), a * 0.55f),
                SurroundColors = new[] { WithAlpha(p.Glow, 0) },
                CenterPoint = new PointF(p.X, p.Y),
            };
            g.FillPath(pgb, path);
        }
        if (p.Core.A == 0) return; // 芯なし（空の発光など）
        float core = Math.Min(r * 0.45f, 26f);
        _brush.Color = WithAlpha(p.Core, a * 0.85f);
        g.FillEllipse(_brush, p.X - core, p.Y - core, core * 2, core * 2);
    }

    private void DrawSpark(Graphics g, Particle p, bool glow)
    {
        float t = p.Life / p.MaxLife;            // 1 → 0
        float a = MathF.Pow(t, 0.8f);
        float tx = p.X - p.VX * StreakTime, ty = p.Y - p.VY * StreakTime;
        if (glow)
            DrawSegment(g, tx, ty, p.X, p.Y, p.Size * 3.2f * (0.6f + 0.4f * t), WithAlpha(p.Glow, a * 0.3f));
        else
            DrawSegment(g, tx, ty, p.X, p.Y, p.Size * (0.5f + 0.5f * t), WithAlpha(Lerp(p.Glow, p.Core, t), a));
    }

    private void DrawEmber(Graphics g, Particle p, double now, bool glow)
    {
        float t = p.Life / p.MaxLife;
        float age = 1 - t;
        Color c = FireGradient(age);
        float flicker = 0.75f + 0.25f * MathF.Sin((float)now * 40f + p.Flicker);
        float a = t * flicker;

        if (glow)
        {
            float glowR = p.Size * (0.8f + age * 0.9f) * 2.4f;
            _brush.Color = WithAlpha(c, a * 0.28f);
            g.FillEllipse(_brush, p.X - glowR, p.Y - glowR, glowR * 2, glowR * 2);
            return;
        }

        float coreR = Math.Max(0.4f, p.Size * (1.1f - age));
        _brush.Color = WithAlpha(Lerp(c, Color.FromArgb(255, 255, 230), t * 0.6f), a);
        g.FillEllipse(_brush, p.X - coreR, p.Y - coreR, coreR * 2, coreR * 2);
    }

    private void DrawRing(Graphics g, Particle p)
    {
        float t = p.Life / p.MaxLife;
        float prog = 1 - t;
        float ease = 1 - MathF.Pow(1 - prog, 3);
        float rad = Math.Max(1, p.Radius * ease);

        _pen.Width = Math.Max(0.5f, p.Size * t * 3f);
        _pen.Color = WithAlpha(p.Glow, t * 0.25f);
        g.DrawEllipse(_pen, p.X - rad, p.Y - rad, rad * 2, rad * 2);

        _pen.Width = Math.Max(0.5f, p.Size * t);
        _pen.Color = WithAlpha(Lerp(p.Glow, Color.White, 0.5f), t * 0.9f);
        g.DrawEllipse(_pen, p.X - rad, p.Y - rad, rad * 2, rad * 2);
    }

    private void DrawCombo(Graphics g, double now)
    {
        float a = ComboAlpha(now);
        if (a <= 0) return;

        float s = _comboScale;
        var (big, small) = ComboSizes();
        float jx = R(-1, 1) * _comboPop * 3 * s, jy = R(-1, 1) * _comboPop * 3 * s;
        float ax = _comboAnchor.X + jx, ay = _comboAnchor.Y + jy;

        using var path = new GraphicsPath();
        var fmt = StringFormat.GenericTypographic;
        path.AddString($"×{_combo}", _font, (int)_fontStyle, big, new PointF(ax, ay - big * 1.25f), fmt);
        path.AddString("COMBO", _font, (int)_fontStyle, small, new PointF(ax + 2 * s, ay - big * 1.25f - small * 1.35f), fmt);

        _pen.Color = WithAlpha(_comboColor, a * 0.25f);
        _pen.Width = 7 * s;
        g.DrawPath(_pen, path);

        _pen.Color = WithAlpha(Color.Black, a * 0.55f);
        _pen.Width = 2.5f * s;
        g.DrawPath(_pen, path);

        _brush.Color = WithAlpha(Lerp(_comboColor, Color.White, 0.45f + _comboPop * 0.55f), a);
        g.FillPath(_brush, path);
    }

    private void DrawSegment(Graphics g, float x0, float y0, float x1, float y1, float width, Color c)
    {
        float dx = x1 - x0, dy = y1 - y0;
        if (dx * dx + dy * dy < 0.5f)
        {
            float r = width / 2;
            _brush.Color = c;
            g.FillEllipse(_brush, x1 - r, y1 - r, r * 2, r * 2);
        }
        else
        {
            _pen.Color = c;
            _pen.Width = width;
            g.DrawLine(_pen, x0, y0, x1, y1);
        }
    }

    // ------------------------------------------------------------------ color utils

    private static readonly (float t, Color c)[] FireStops =
    {
        (0.00f, Color.FromArgb(255, 255, 220)),
        (0.15f, Color.FromArgb(255, 220, 90)),
        (0.40f, Color.FromArgb(255, 140, 30)),
        (0.70f, Color.FromArgb(220, 50, 15)),
        (1.00f, Color.FromArgb(90, 15, 10)),
    };

    private static Color FireGradient(float x)
    {
        x = Math.Clamp(x, 0, 1);
        for (int i = 1; i < FireStops.Length; i++)
        {
            if (x <= FireStops[i].t)
            {
                var (t0, c0) = FireStops[i - 1];
                var (t1, c1) = FireStops[i];
                return Lerp(c0, c1, (x - t0) / (t1 - t0));
            }
        }
        return FireStops[^1].c;
    }

    private static Color WithAlpha(Color c, float a) =>
        Color.FromArgb((int)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static Color Hsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360;
        float c = v * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = v - c;
        (float r, float g, float b) = h switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
