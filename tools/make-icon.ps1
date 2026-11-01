# Builds Assets/app.ico: rounded terracotta square with a ">_" terminal glyph.
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$outFile = Join-Path $assets 'app.ico'

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngData = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(2.0, $size * 0.22)
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.ColorTranslator]::FromHtml('#E58B68'),
        [System.Drawing.ColorTranslator]::FromHtml('#C05E3C'),
        45.0)
    $g.FillPath($brush, $path)

    $fontSize = [Math]::Max(6.0, $size * 0.50)
    $font = New-Object System.Drawing.Font('Consolas', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('>_', $font, $white, $rect, $format)

    $g.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $white.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$count = $sizes.Count
$headerSize = 6 + 16 * $count
$totalSize = $headerSize
foreach ($data in $pngData) { $totalSize += $data.Length }

$buffer = New-Object byte[] $totalSize
$offset = 0
$buffer[$offset++] = 0
$buffer[$offset++] = 0
$buffer[$offset++] = 1
$buffer[$offset++] = 0
$buffer[$offset++] = [byte]($count -band 0xFF)
$buffer[$offset++] = [byte](($count -shr 8) -band 0xFF)

$imageOffset = $headerSize
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $data = $pngData[$i]
    $buffer[$offset++] = if ($size -ge 256) { 0 } else { [byte]$size }
    $buffer[$offset++] = if ($size -ge 256) { 0 } else { [byte]$size }
    $buffer[$offset++] = 0
    $buffer[$offset++] = 0
    $buffer[$offset++] = 1
    $buffer[$offset++] = 0
    $buffer[$offset++] = 32
    $buffer[$offset++] = 0
    $len = [BitConverter]::GetBytes([uint32]$data.Length)
    [Array]::Copy($len, 0, $buffer, $offset, 4); $offset += 4
    $off = [BitConverter]::GetBytes([uint32]$imageOffset)
    [Array]::Copy($off, 0, $buffer, $offset, 4); $offset += 4
    [Array]::Copy($data, 0, $buffer, $imageOffset, $data.Length)
    $imageOffset += $data.Length
}

[System.IO.File]::WriteAllBytes($outFile, $buffer)
Write-Host "Wrote $outFile ($totalSize bytes, $count sizes)"
