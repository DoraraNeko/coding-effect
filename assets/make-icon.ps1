# Coding Effect のアイコン（assets/app.ico と assets/icon-256.png）を生成する。
# 使い方: powershell -ExecutionPolicy Bypass -File assets\make-icon.ps1
param([string]$OutDir = $PSScriptRoot)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $n / 256.0
    $g.ScaleTransform($u, $u)
    $small = $n -le 24

    # 背景：角丸の夜空色
    $pad = if ($small) { 6 } else { 10 }
    $bg = New-RoundRect $pad $pad (256 - 2 * $pad) (256 - 2 * $pad) 54
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, 256), ([System.Drawing.Color]::FromArgb(255, 30, 36, 72)), ([System.Drawing.Color]::FromArgb(255, 8, 10, 22))
    $g.FillPath($lg, $bg)

    # 中央のシアンの光
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse(28, 28, 200, 200)
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
    $pgb.CenterColor = [System.Drawing.Color]::FromArgb(150, 0, 220, 255)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 0, 220, 255))
    $g.SetClip($bg)
    $g.FillPath($pgb, $glowPath)

    if (-not $small) {
        # 炎の粒（左下）
        $fire = @(@(58, 196, 13, 255, 120, 20), @(80, 214, 9, 255, 180, 50), @(44, 172, 7, 255, 90, 20), @(70, 178, 6, 255, 220, 120))
        foreach ($f in $fire) {
            $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, $f[3], $f[4], $f[5]))
            $g.FillEllipse($b, $f[0] - $f[2] * 1.8, $f[1] - $f[2] * 1.8, $f[2] * 3.6, $f[2] * 3.6)
            $b.Color = [System.Drawing.Color]::FromArgb(255, $f[3], $f[4], $f[5])
            $g.FillEllipse($b, $f[0] - $f[2], $f[1] - $f[2], $f[2] * 2, $f[2] * 2)
        }
        # シアンの粒子（右側から集まる）
        $dots = @(@(204, 70, 7), @(214, 128, 5), @(196, 186, 8), @(226, 160, 4), @(60, 64, 5), @(182, 44, 4))
        foreach ($d in $dots) {
            $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 225, 255))
            $g.FillEllipse($b, $d[0] - $d[2] * 2, $d[1] - $d[2] * 2, $d[2] * 4, $d[2] * 4)
            $b.Color = [System.Drawing.Color]::FromArgb(255, 220, 255, 255)
            $g.FillEllipse($b, $d[0] - $d[2], $d[1] - $d[2], $d[2] * 2, $d[2] * 2)
        }
    }
    $g.ResetClip()

    # 稲妻
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 152, 26),
        (New-Object System.Drawing.PointF 74, 142),
        (New-Object System.Drawing.PointF 122, 142),
        (New-Object System.Drawing.PointF 94, 232),
        (New-Object System.Drawing.PointF 186, 104),
        (New-Object System.Drawing.PointF 136, 104),
        (New-Object System.Drawing.PointF 172, 26))
    $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bolt.AddPolygon($pts)

    foreach ($w in @(34, 22, 12)) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int](360 / $w), 0, 210, 255)), $w
        $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $bolt)
    }
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 26), (New-Object System.Drawing.PointF 0, 232), ([System.Drawing.Color]::White), ([System.Drawing.Color]::FromArgb(255, 120, 235, 255))
    $g.FillPath($fill, $bolt)
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 200, 255)), $(if ($small) { 8 } else { 4 })
    $edge.LineJoin = 'Round'
    $g.DrawPath($edge, $bolt)

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Get-DibBytes($bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int][Math]::Ceiling($n / 32.0) * 4
    $bw.Write([int]40); $bw.Write([int]$n); $bw.Write([int]($n * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($n * $n * 4 + $maskStride * $n))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskStride * $n)))
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 128) { $bytes = Get-PngBytes $bmp } else { $bytes = Get-DibBytes $bmp }
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $images += , @($s, $bytes)
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $len = $img[1].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$len); $w.Write([int]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())
"wrote $(Join-Path $OutDir 'app.ico')"
