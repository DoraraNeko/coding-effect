using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodingEffect;

/// <summary>タスクトレイ常駐アプリ本体。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly Settings _settings;
    private readonly OverlayWindow _overlay;
    private readonly EffectEngine _engine;
    private readonly CaretWorker _worker;
    private readonly KeyboardHook _hook;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick;

    /// <summary>環境変数 CODINGEFFECT_PERF にファイルパスを入れると、1 フレームごとの描画時間を記録する。</summary>
    private static readonly string? PerfLog = Environment.GetEnvironmentVariable("CODINGEFFECT_PERF");

    private ToolStripMenuItem _enabledItem = null!;

    public TrayContext()
    {
        _settings = Settings.Load();

        _overlay = new OverlayWindow();
        _overlay.EnsureHandle();

        _engine = new EffectEngine(_settings);

        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += (_, _) => Tick();

        _worker = new CaretWorker(_settings, OnCaretResolved);

        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;

        BuildTray();
    }

    // ------------------------------------------------------------------ input → effect

    private void OnKeyDown(int vk)
    {
        if (!_settings.Enabled) return;
        var kind = KeyClassifier.Classify(vk);
        if (kind == KeyKind.None) return;
        if (KeyClassifier.ShortcutModifierHeld()) return;
        _worker.Enqueue(new KeyEvent(kind, Native.GetForegroundWindow()));
    }

    private void OnCaretResolved(IReadOnlyList<KeyEvent> events, RectangleF caret)
    {
        try
        {
            _overlay.BeginInvoke((Action)(() =>
            {
                double now = _clock.Elapsed.TotalSeconds;
                foreach (var e in events) _engine.Trigger(e.Kind, caret, now);
                if (!_timer.Enabled)
                {
                    _lastTick = now;
                    _timer.Start();
                    Tick();
                }
            }));
        }
        catch (InvalidOperationException) { /* 終了処理中 */ }
    }

    private void Tick()
    {
        try
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Clamp(now - _lastTick, 0, 0.05);
            _lastTick = now;

            if (!_engine.Update(dt, now))
            {
                _overlay.HideOverlay();
                _timer.Stop();
                return;
            }

            var area = Rectangle.Intersect(_engine.Bounds(now), SystemInformation.VirtualScreen);
            long t0 = Stopwatch.GetTimestamp();
            double drawMs = 0;
            long d0 = Stopwatch.GetTimestamp();
            _overlay.Present(area,
                (bits, stride) => _engine.RenderParticles(bits, stride, area, now),
                g =>
                {
                    _engine.Render(g, now);
                    drawMs = Stopwatch.GetElapsedTime(d0).TotalMilliseconds;
                });
            if (PerfLog != null)
            {
                double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                System.IO.File.AppendAllText(PerfLog, $"{now:F3}\t{ms:F1}ms\tdraw={drawMs:F1}ms\tparticles={_engine.ParticleCount}\tarea={area.Width}x{area.Height}\n");
            }
        }
        catch
        {
            _overlay.HideOverlay();
            _timer.Stop();
        }
    }

    // ------------------------------------------------------------------ tray

    private NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip();

        _enabledItem = new ToolStripMenuItem("エフェクト有効") { Checked = _settings.Enabled };
        _enabledItem.Click += (_, _) => SetEnabled(!_settings.Enabled);
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());

        var themeMenu = new ToolStripMenuItem("テーマ");
        foreach (var (id, label) in Settings.Themes)
        {
            var item = new ToolStripMenuItem(label) { Checked = _settings.Theme == id, Tag = id };
            item.Click += (_, _) =>
            {
                _settings.Theme = id;
                foreach (ToolStripMenuItem i in themeMenu.DropDownItems) i.Checked = (string)i.Tag! == id;
                _settings.Save();
            };
            themeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(themeMenu);

        var intensityMenu = new ToolStripMenuItem("派手さ");
        foreach (var (val, label) in new[] { (0.5, "控えめ"), (1.0, "標準"), (1.7, "派手"), (2.6, "爆発"), (4.0, "限界突破") })
        {
            var item = new ToolStripMenuItem(label) { Checked = Math.Abs(_settings.Intensity - val) < 0.01, Tag = val };
            item.Click += (_, _) =>
            {
                _settings.Intensity = val;
                foreach (ToolStripMenuItem i in intensityMenu.DropDownItems) i.Checked = Math.Abs((double)i.Tag! - val) < 0.01;
                _settings.Save();
            };
            intensityMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(intensityMenu);

        menu.Items.Add(Toggle("コンボ表示", () => _settings.Combo, v => _settings.Combo = v));
        menu.Items.Add(Toggle("削除キーで炎", () => _settings.FireOnDelete, v => _settings.FireOnDelete = v));
        menu.Items.Add(Toggle("位置不明時はマウス位置に表示", () => _settings.MouseFallback, v => _settings.MouseFallback = v));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定ファイルを開く", null, (_, _) =>
        {
            _settings.Save();
            try { Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true }); } catch { }
        });
        menu.Items.Add("設定を再読み込み", null, (_, _) =>
        {
            _settings.CopyFrom(Settings.Load());
            // メニューのチェック状態を作り直す
            var old = _trayRef;
            _overlay.BeginInvoke((Action)(() =>
            {
                if (old != null) { old.Visible = false; old.Dispose(); }
                BuildTray();
            }));
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());

        var tray = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Coding Effect",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => SetEnabled(!_settings.Enabled);
        _trayRef = tray;
        return tray;
    }

    private NotifyIcon? _trayRef;

    private ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Checked = get() };
        item.Click += (_, _) =>
        {
            set(!get());
            item.Checked = get();
            _settings.Save();
        };
        return item;
    }

    private void SetEnabled(bool on)
    {
        _settings.Enabled = on;
        _enabledItem.Checked = on;
        _settings.Save();
        _trayRef?.ShowBalloonTip(1000, "Coding Effect", on ? "エフェクト ON" : "エフェクト OFF", ToolTipIcon.None);
    }

    /// <summary>exe に埋め込んだ app.ico から、トレイ用の小さいサイズ（DPI に合わせる）を取り出す。</summary>
    private static Icon CreateIcon()
    {
        var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("CodingEffect.app.ico");
        if (stream != null)
        {
            using (stream) return new Icon(stream, SystemInformation.SmallIconSize);
        }
        return SystemIcons.Application;
    }

    protected override void ExitThreadCore()
    {
        _hook.Dispose();
        _worker.Dispose();
        _timer.Stop();
        if (_trayRef != null) _trayRef.Visible = false;
        _trayRef?.Dispose();
        _overlay.Close();
        _overlay.Dispose();
        base.ExitThreadCore();
    }
}
