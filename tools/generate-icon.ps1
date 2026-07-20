# Generates HostsBlocker/appicon.ico from code, so the icon is reproducible
# and reviewable as a diff rather than an opaque binary blob.
#
#   powershell -ExecutionPolicy Bypass -File tools\generate-icon.ps1
#
# Design: the app's accent-blue rounded tile with a white "no entry" sign -
# a blocked site, in one glyph that still reads at 16x16.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$sizes  = @(16, 20, 24, 32, 48, 64, 128, 256)
$outDir = Join-Path $PSScriptRoot '..\HostsBlocker'
$out    = Join-Path $outDir 'appicon.ico'

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- rounded tile, accent gradient (matches App.xaml's Accent/AccentDim) ---
    # Small sizes get a proportionally tighter radius or the tile turns into a blob.
    $pad    = [Math]::Max(1, [int][Math]::Round($s * 0.055))
    $side   = $s - (2 * $pad)
    $radius = [Math]::Max(2, [int][Math]::Round($s * 0.21))
    $d      = $radius * 2

    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($pad, $pad, $d, $d, 180, 90)
    $tile.AddArc($pad + $side - $d, $pad, $d, $d, 270, 90)
    $tile.AddArc($pad + $side - $d, $pad + $side - $d, $d, $d, 0, 90)
    $tile.AddArc($pad, $pad + $side - $d, $d, $d, 90, 90)
    $tile.CloseFigure()

    $rect  = New-Object System.Drawing.Rectangle($pad, $pad, $side, $side)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.ColorTranslator]::FromHtml('#6E9BFF'),
        [System.Drawing.ColorTranslator]::FromHtml('#3A5BB0'),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $tile)

    # --- white "no entry" sign ---
    $cx     = $s / 2.0
    $stroke = [Math]::Max(1.5, $s * 0.098)
    $r      = ($s * 0.29) - ($stroke / 2.0)

    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $white.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $g.DrawEllipse($white, [float]($cx - $r), [float]($cx - $r), [float]($r * 2), [float]($r * 2))

    # Diagonal bar, inset so its round caps sit just inside the ring.
    $k = $r * 0.707
    $g.DrawLine($white, [float]($cx - $k), [float]($cx + $k), [float]($cx + $k), [float]($cx - $k))

    $white.Dispose(); $brush.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

# Encodes a frame the classic way: BITMAPINFOHEADER with a doubled height,
# bottom-up 32bpp BGRA pixels, then a (fully opaque) 1bpp AND mask. Needed
# because GDI+ - unlike Explorer and WPF - cannot read PNG-compressed frames,
# so PNG is reserved for the 256x256 one where the size saving is worth it.
function ConvertTo-Dib([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle(0, 0, $w, $h)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $maskStride = [int](([Math]::Floor(($w + 31) / 32)) * 4)   # 1bpp rows, 4-byte aligned

    $bw.Write([uint32]40)                    # biSize
    $bw.Write([int32]$w)                     # biWidth
    $bw.Write([int32]($h * 2))               # biHeight: colour data + mask
    $bw.Write([uint16]1)                     # biPlanes
    $bw.Write([uint16]32)                    # biBitCount
    $bw.Write([uint32]0)                     # biCompression: BI_RGB
    $bw.Write([uint32]($w * $h * 4 + $maskStride * $h))
    $bw.Write([int32]0); $bw.Write([int32]0) # pixels-per-metre
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    for ($y = $h - 1; $y -ge 0; $y--) {      # DIBs are stored bottom-up
        $bw.Write($pixels, $y * $data.Stride, $w * 4)
    }
    $bw.Write((New-Object byte[] ($maskStride * $h)))  # all-zero AND mask = opaque

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    # Comma prefix stops PowerShell unrolling the array into the pipeline,
    # which would hand the caller loose bytes instead of a byte[].
    return ,$bytes
}

# --- pack the frames into a multi-resolution .ico -------------------------
$frames = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose()
    }
    else {
        [byte[]]$bytes = ConvertTo-Dib $bmp
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Data = $bytes }
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)                 # reserved
    $bw.Write([uint16]1)                 # type: icon
    $bw.Write([uint16]$frames.Count)

    # Image data starts after the 6-byte header and one 16-byte entry per frame.
    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        # 256 is encoded as 0 in the single-byte width/height fields.
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $bw.Write([byte]$dim)            # width
        $bw.Write([byte]$dim)            # height
        $bw.Write([byte]0)               # palette size (0 = truecolour)
        $bw.Write([byte]0)               # reserved
        $bw.Write([uint16]1)             # colour planes
        $bw.Write([uint16]32)            # bits per pixel
        $bw.Write([uint32]$f.Data.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Data.Length
    }
    foreach ($f in $frames) { $bw.Write($f.Data) }
}
finally {
    $bw.Dispose(); $fs.Dispose()
}

# Sanity check: the directory's offsets must actually land inside the file, and
# every frame must load. A silently truncated .ico still "writes fine" otherwise.
$dataBytes = 0
foreach ($f in $frames) { $dataBytes += $f.Data.Length }
$expected = 6 + (16 * $frames.Count) + $dataBytes
$actual   = (Get-Item $out).Length
if ($actual -ne $expected) {
    throw "Icon is $actual bytes but the directory describes $expected - frame data was not written correctly."
}
foreach ($s in $sizes | Where-Object { $_ -lt 256 }) {
    $probe = New-Object System.Drawing.Icon($out, $s, $s)
    $bmp   = $probe.ToBitmap()
    if ($bmp.Width -ne $s) { throw "Frame $s did not round-trip (got $($bmp.Width))." }
    $bmp.Dispose(); $probe.Dispose()
}

# GDI+ can't decode the PNG-compressed 256 frame (Explorer and WPF can), so
# check it structurally: PNG signature, and an IHDR declaring 256x256.
$png = $frames | Where-Object { $_.Size -eq 256 } | Select-Object -First 1
$sig = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
for ($i = 0; $i -lt 8; $i++) {
    if ($png.Data[$i] -ne $sig[$i]) { throw "256 frame is not a valid PNG." }
}
$pw = [int]$png.Data[16] * 16777216 + [int]$png.Data[17] * 65536 + [int]$png.Data[18] * 256 + [int]$png.Data[19]
if ($pw -ne 256) { throw "256 frame's PNG header declares width $pw." }

Write-Host "Wrote $out ($([Math]::Round($actual / 1KB, 1)) KB, $($frames.Count) sizes, all verified)"
