# Builds NeoDenSoftware/Assets/PnpFile.ico from PNP_Icon.png, with real alpha transparency at
# several standard Windows icon sizes. Uses the Vista+ "PNG-in-ICO" format (each ICONDIRENTRY
# points at a raw PNG blob) instead of System.Drawing's Bitmap.GetHicon(), which only produces a
# 1-bit transparency mask and loses the source image's alpha gradient/anti-aliasing.
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot "PNP_Icon.png"
$outputPath = Join-Path $PSScriptRoot "PnpFile.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$source = [System.Drawing.Image]::FromFile($sourcePath)

function New-SquarePng {
    param([System.Drawing.Image]$Source, [int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fit the (non-square) source inside the square canvas, preserving aspect ratio, centered.
    $scale = [Math]::Min($Size / $Source.Width, $Size / $Source.Height)
    $w = [int]([Math]::Round($Source.Width * $scale))
    $h = [int]([Math]::Round($Source.Height * $scale))
    $x = [int](($Size - $w) / 2)
    $y = [int](($Size - $h) / 2)
    $g.DrawImage($Source, $x, $y, $w, $h)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $ms.ToArray()
}

# A plain "$sizes | ForEach-Object { ... }" pipeline would FLATTEN each returned byte[] into
# individual bytes (PowerShell unrolls arrays written to the pipeline), silently turning
# $pngBlobs into one giant array of single bytes instead of 7 separate PNG blobs - every
# .Length/[$i] below would then be operating on a lone byte. Use an explicit List[byte[]] with
# .Add() instead, which stores each array as one element, no unrolling.
$pngBlobs = [System.Collections.Generic.List[byte[]]]::new()
foreach ($s in $sizes) {
    $pngBlobs.Add((New-SquarePng -Source $source -Size $s))
}
$source.Dispose()

$fs = New-Object System.IO.FileStream $outputPath, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

# ICONDIR: reserved(2)=0, type(2)=1 (icon), count(2)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $blob = $pngBlobs[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }  # 0 means 256 in ICO format
    $bw.Write([Byte]$dim)      # width
    $bw.Write([Byte]$dim)      # height
    $bw.Write([Byte]0)         # color count (0 = no palette, true color)
    $bw.Write([Byte]0)         # reserved
    $bw.Write([UInt16]1)       # color planes
    $bw.Write([UInt16]32)      # bits per pixel
    # BinaryWriter.Write(UInt32) doesn't resolve reliably from PowerShell (silently writes the
    # wrong bytes) - go through BitConverter.GetBytes() instead, which unambiguously returns a
    # byte[] and resolves to the Write(Byte[]) overload every time.
    $bw.Write([BitConverter]::GetBytes([UInt32]$blob.Length))
    $bw.Write([BitConverter]::GetBytes([UInt32]$offset))
    $offset += $blob.Length
}

foreach ($blob in $pngBlobs) {
    $bw.Write($blob)
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output "Wrote $outputPath ($([System.IO.FileInfo]::new($outputPath).Length) bytes, sizes: $($sizes -join ', '))"
