using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace CodingEffect;

/// <summary>
/// フォアグラウンドアプリのキャレット（文字入力位置）をスクリーン座標（物理ピクセル）で求める。
/// 1. Win32 のシステムキャレット（メモ帳・多くの Win32 アプリ・IME 対応アプリ）
/// 2. UI Automation の TextPattern（Chrome / Edge / Word / Windows Terminal など）
/// </summary>
internal static class CaretLocator
{
    public static RectangleF? Locate(IntPtr foreground)
    {
        var r = FromGuiThreadInfo(foreground);
        if (r != null) return r;
        try { return FromUiAutomation(); }
        catch { return null; }
    }

    private static RectangleF? FromGuiThreadInfo(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return null;
        uint tid = Native.GetWindowThreadProcessId(foreground, out _);
        if (tid == 0) return null;

        var gi = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref gi) || gi.hwndCaret == IntPtr.Zero) return null;

        var rc = gi.rcCaret;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (h <= 0) return null;

        var origin = new Native.POINT();
        if (!Native.ClientToScreen(gi.hwndCaret, ref origin)) return null;

        double scale = DpiScaleFor(gi.hwndCaret);
        return new RectangleF(
            (float)(origin.X + rc.Left * scale),
            (float)(origin.Y + rc.Top * scale),
            (float)Math.Max(1, w * scale),
            (float)(h * scale));
    }

    /// <summary>
    /// DPI 非対応 / システム DPI 対応のアプリはキャレット矩形を論理座標で返すので、物理ピクセルに換算する倍率。
    /// </summary>
    private static double DpiScaleFor(IntPtr hwnd)
    {
        try
        {
            int awareness = Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(hwnd));
            if (awareness == 2 || awareness < 0) return 1.0; // Per-monitor aware: そのまま
            var mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
            if (Native.GetDpiForMonitor(mon, 0, out uint dpi, out _) != 0) return 1.0;
            double baseDpi = awareness == 0 ? 96.0 : Native.GetDpiForSystem();
            return dpi / baseDpi;
        }
        catch { return 1.0; }
    }

    private static RectangleF? FromUiAutomation()
    {
        var el = AutomationElement.FocusedElement;
        if (el == null) return null;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj)) return null;

        var tp = (TextPattern)patternObj;
        var sel = tp.GetSelection();
        if (sel.Length == 0) return null;
        var range = sel[0];

        // 範囲選択中なら末尾
        var rects = range.GetBoundingRectangles();
        if (rects.Length > 0 && rects[^1].Height > 0)
        {
            var b = rects[^1];
            return new RectangleF((float)b.Right, (float)b.Top, 2, (float)b.Height);
        }

        // 空の範囲（通常のキャレット）は矩形が返らないので、前後 1 文字に広げて位置を推定する
        var back = range.Clone();
        back.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
        var backRects = back.GetBoundingRectangles();

        var fwd = range.Clone();
        fwd.ExpandToEnclosingUnit(TextUnit.Character);
        var fwdRects = fwd.GetBoundingRectangles();

        bool backOk = backRects.Length > 0 && backRects[0].Height > 0;
        bool fwdOk = fwdRects.Length > 0 && fwdRects[0].Height > 0;

        if (fwdOk)
        {
            var f = fwdRects[0];
            // 改行文字などで幅が異常に広い場合は、直前の文字の右端を優先
            bool fwdLooksOdd = f.Width > f.Height * 3;
            if (!fwdLooksOdd || !backOk)
                return new RectangleF((float)f.Left, (float)f.Top, 2, (float)f.Height);
        }
        if (backOk)
        {
            var b = backRects[^1];
            return new RectangleF((float)b.Right, (float)b.Top, 2, (float)b.Height);
        }
        return null;
    }
}
