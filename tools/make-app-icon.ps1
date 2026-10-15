<#
  Draws the application icon of the client (a mint post of a roadside speed camera with its head on top, on a dark rounded square) and writes it as a multi-size .ico (PNG entries) plus a 256 px preview PNG.
  Needs only Windows PowerShell or PowerShell 7 on Windows (System.Drawing). Run from the repository root:
    pwsh tools/make-app-icon.ps1
#>
param(
    [string] $IcoPath = "src/Client/HighwaySpeedEnforcement.Client.Wpf/Resources/AppIcon.ico",
    [string] $PreviewPath = ""
)

Add-Type -AssemblyName System.Drawing

$mint = [System.Drawing.Color]::FromArgb(255, 0, 227, 140)
$dark = [System.Drawing.Color]::FromArgb(255, 25, 26, 30)

function New-IconBitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0

    # Rounded dark square.
    $radius = 52 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $radius
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), $path)

    # The post of a roadside speed camera that carries the camera head.
    $penY = New-Object System.Drawing.Pen $mint, ([Math]::Max(2.4, 28 * $k))
    $penY.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penY.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penY.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLine($penY, 128 * $k, 110 * $k, 128 * $k, 222 * $k)

    # The camera head: a light housing with a dark lens ringed in mint, and an amber flash.
    $headX = 46 * $k; $headY = 34 * $k; $headW = 164 * $k; $headH = 82 * $k; $headR = 20 * $k
    $head = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hd = 2 * $headR
    $head.AddArc($headX, $headY, $hd, $hd, 180, 90)
    $head.AddArc($headX + $headW - $hd, $headY, $hd, $hd, 270, 90)
    $head.AddArc($headX + $headW - $hd, $headY + $headH - $hd, $hd, $hd, 0, 90)
    $head.AddArc($headX, $headY + $headH - $hd, $hd, $hd, 90, 90)
    $head.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 246, 247, 250))), $head)

    $lensR = 30 * $k
    $lensX = 112 * $k; $lensY = 75 * $k
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $dark), $lensX - $lensR, $lensY - $lensR, 2 * $lensR, 2 * $lensR)
    $ring = New-Object System.Drawing.Pen $mint, ([Math]::Max(1.5, 7 * $k))
    $g.DrawEllipse($ring, $lensX - $lensR, $lensY - $lensR, 2 * $lensR, 2 * $lensR)
    if ($size -ge 32) {
        $glint = 7 * $k
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $lensX - 12 * $k, $lensY - 14 * $k, 2 * $glint, 2 * $glint)
        $flash = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 193, 7))
        $g.FillEllipse($flash, 168 * $k, 52 * $k, 26 * $k, 26 * $k)
    }

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @{ Size = $size; Bytes = $ms.ToArray() }
    if ($PreviewPath -and $size -eq 256) {
        $bmp.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}

$dir = Split-Path -Parent $IcoPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16] 0); $w.Write([uint16] 1); $w.Write([uint16] $images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte] $dim); $w.Write([byte] $dim); $w.Write([byte] 0); $w.Write([byte] 0)
    $w.Write([uint16] 1); $w.Write([uint16] 32)
    $w.Write([uint32] $img.Bytes.Length); $w.Write([uint32] $offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path -Parent $IcoPath)).Path + "/" + (Split-Path -Leaf $IcoPath), $out.ToArray())
Write-Host "Wrote $IcoPath ($($images.Count) sizes)"
