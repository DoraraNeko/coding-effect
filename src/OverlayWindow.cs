using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodingEffect;

/// <summary>
/// クリック透過・最前面・フォーカスを奪わない per-pixel alpha のオーバーレイ。
/// エフェクトが存在する範囲だけの大きさに毎フレーム合わせて UpdateLayeredWindow する。
/// </summary>
internal sealed class OverlayWindow : Form
{
    private IntPtr _memDc, _dib, _oldBmp, _bits;
    private int _capW, _capH;
    private bool _shown;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "CodingEffect Overlay";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                        | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void EnsureHandle() => _ = Handle;

    private void EnsureCapacity(int w, int h)
    {
        if (_dib != IntPtr.Zero && w <= _capW && h <= _capH) return;
        ReleaseBuffer();
        _capW = RoundUp(Math.Max(w, _capW));
        _capH = RoundUp(Math.Max(h, _capH));

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(screenDc);
        Native.ReleaseDC(IntPtr.Zero, screenDc);

        var bi = new Native.BITMAPINFOHEADER
        {
            biSize = 40,
            biWidth = _capW,
            biHeight = -_capH, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _dib = Native.CreateDIBSection(_memDc, ref bi, 0, out _bits, IntPtr.Zero, 0);
        _oldBmp = Native.SelectObject(_memDc, _dib);

        static int RoundUp(int v) => (v + 127) / 128 * 128;
    }

    private void ReleaseBuffer()
    {
        if (_memDc != IntPtr.Zero)
        {
            Native.SelectObject(_memDc, _oldBmp);
            Native.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            Native.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
    }

    /// <summary>area（スクリーン座標・物理ピクセル）に draw の内容を表示する。</summary>
    /// <param name="raw">ピクセルを直接書く描画（先に呼ばれる）。引数はバッファ先頭・1 行のピクセル数。</param>
    /// <param name="draw">GDI+ での描画（raw の上に重なる）。</param>
    public unsafe void Present(Rectangle area, Action<IntPtr, int> raw, Action<Graphics> draw)
    {
        if (area.Width <= 0 || area.Height <= 0) { HideOverlay(); return; }
        EnsureCapacity(area.Width, area.Height);

        uint* buf = (uint*)_bits;
        for (int y = 0; y < area.Height; y++)
            new Span<uint>(buf + (long)y * _capW, area.Width).Clear();
        raw(_bits, _capW);

        using (var bmp = new Bitmap(area.Width, area.Height, _capW * 4, PixelFormat.Format32bppPArgb, _bits))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-area.X, -area.Y);
            draw(g);
        }

        var dst = new Native.POINT { X = area.X, Y = area.Y };
        var size = new Native.SIZE { cx = area.Width, cy = area.Height };
        var src = new Native.POINT();
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            SourceConstantAlpha = 255,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);

        if (!_shown)
        {
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _shown = true;
        }
    }

    public void HideOverlay()
    {
        if (!_shown) return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _shown = false;
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseBuffer();
        base.Dispose(disposing);
    }
}
