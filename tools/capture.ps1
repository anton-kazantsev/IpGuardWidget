param(
    [string]$Mode = "",
    [string]$Out = "C:\Users\baksf\AppData\Local\Temp\opencode\shot.png",
    [int]$DelayMs = 300,
    [int]$CropX = 1490,
    [int]$CropY = 600,
    [int]$CropW = 430,
    [int]$CropH = 440,
    [int]$Scale = 2
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\Debug\net10.0-windows\win-x64\IpGuardWidget.exe'
if (-not (Test-Path $exe)) {
    $exe = Join-Path $root 'dist\IpGuardWidget-v1.1.exe'
}
if (-not (Test-Path $exe)) {
    Write-Error "IpGuardWidget.exe not found: build the project (bin\Debug\net10.0-windows\win-x64\IpGuardWidget.exe) or publish to dist\IpGuardWidget-v1.1.exe under $root"
    exit 1
}

if ($Mode -ne "") {
    Get-Process | Where-Object { $_.ProcessName -like 'IpGuardWidget*' } | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $exe -ArgumentList '--simulate', $Mode | Out-Null
    Start-Sleep -Milliseconds $DelayMs
}

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
$g.Dispose()

$rect = New-Object System.Drawing.Rectangle($CropX, $CropY, $CropW, $CropH)
$crop = New-Object System.Drawing.Bitmap(($CropW * $Scale), ($CropH * $Scale))
$g2 = [System.Drawing.Graphics]::FromImage($crop)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g2.DrawImage([System.Drawing.Image]$bmp, (New-Object System.Drawing.Rectangle(0, 0, ($CropW * $Scale), ($CropH * $Scale))), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $crop.Dispose(); $bmp.Dispose()
Write-Host $Out
