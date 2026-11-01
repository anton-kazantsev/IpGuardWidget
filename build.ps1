param(
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($FrameworkDependent) {
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
} else {
    dotnet publish -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:ReadyToRun=true `
        -p:EnableCompressionInSingleFile=true
}

$out = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64\publish\IpGuardWidget.exe'
if (Test-Path $out) {
    $mb = [Math]::Round((Get-Item $out).Length / 1MB, 1)
    Write-Host "Ready: $out ($mb MB)"
}
