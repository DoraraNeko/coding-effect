using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace CodingEffect;

internal readonly record struct KeyEvent(KeyKind Kind, IntPtr Foreground);

/// <summary>
/// キーフックのスレッドを止めないよう、キャレット位置の取得（UI Automation は遅いことがある）を別スレッドで行う。
/// 連打はまとめて 1 回の位置取得で処理する。
/// </summary>
internal sealed class CaretWorker : IDisposable
{
    private readonly BlockingCollection<KeyEvent> _queue = new(boundedCapacity: 256);
    private readonly Settings _settings;
    private readonly Action<IReadOnlyList<KeyEvent>, RectangleF> _emit;
    private readonly Dictionary<uint, string> _procNames = new();

    private IntPtr _lastHwnd;
    private RectangleF _lastRect;
    private DateTime _lastTime;

    public CaretWorker(Settings settings, Action<IReadOnlyList<KeyEvent>, RectangleF> emit)
    {
        _settings = settings;
        _emit = emit;
        var t = new Thread(Run) { IsBackground = true, Name = "CaretWorker" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    public void Enqueue(KeyEvent e) => _queue.TryAdd(e);

    private void Run()
    {
        try
        {
            foreach (var first in _queue.GetConsumingEnumerable())
            {
                var batch = new List<KeyEvent> { first };
                // アプリ側がキー入力を処理してキャレットが動くのを少し待つ
                Thread.Sleep(Math.Clamp(_settings.CaretDelayMs, 0, 200));
                while (_queue.TryTake(out var more)) batch.Add(more);

                try { Process(batch); }
                catch { /* 1 回の失敗で止めない */ }
            }
        }
        catch (InvalidOperationException) { /* CompleteAdding 後 */ }
    }

    private void Process(List<KeyEvent> batch)
    {
        var fg = batch[^1].Foreground;
        if (IsExcluded(fg)) return;

        RectangleF? rect = CaretLocator.Locate(fg);
        if (rect is RectangleF found)
        {
            _lastHwnd = fg;
            _lastRect = found;
            _lastTime = DateTime.UtcNow;
        }
        else if (fg == _lastHwnd && (DateTime.UtcNow - _lastTime).TotalSeconds < 10)
        {
            rect = _lastRect;
        }
        else if (_settings.MouseFallback && Native.GetCursorPos(out var p))
        {
            float h = 20f * Native.GetDpiForSystem() / 96f;
            rect = new RectangleF(p.X + 8, p.Y - h / 2, 2, h);
        }

        if (rect is RectangleF r)
            _emit(batch.TakeLast(8).ToList(), r);
    }

    private bool IsExcluded(IntPtr hwnd)
    {
        var list = _settings.ExcludedProcesses;
        if (list == null || list.Count == 0 || hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        if (!_procNames.TryGetValue(pid, out var name))
        {
            try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
            catch { name = ""; }
            if (_procNames.Count > 256) _procNames.Clear();
            _procNames[pid] = name;
        }

        foreach (var ex in list)
        {
            var n = ex.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ex[..^4] : ex;
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void Dispose() => _queue.CompleteAdding();
}
