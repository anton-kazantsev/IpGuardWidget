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
$exe = "C:\Users\baksf\Documents\IpGuardWidget\bin\Debug\net10.0-windows\win-x64\IpGuardWidget.exe"

if ($Mode -ne "") {
    Get-Process IpGuardWidget -ErrorAction SilentlyContinue | Stop-Process -Force
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
