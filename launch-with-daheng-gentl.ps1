param(
    [Parameter(Mandatory = $true)]
    [string]$Program,

    [string]$Arguments = "",

    [switch]$IncludeCinogy
)

$ErrorActionPreference = "Stop"

$dahengPath = "C:\Program Files\Daheng Imaging\GalaxySDK\GenTL\Win64"
$cinogyPath = "C:\Program Files\CINOGY\Driver\CMOS_EL_USB\GenICam\TL"

$paths = @($dahengPath)
if ($IncludeCinogy) {
    $paths += $cinogyPath
}

foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "GenTL path not found: $path"
    }
}

$env:GENICAM_GENTL64_PATH = ($paths -join ";")

Write-Host "GENICAM_GENTL64_PATH=$env:GENICAM_GENTL64_PATH"
Write-Host "Launching: $Program $Arguments"

Start-Process -FilePath $Program -ArgumentList $Arguments -WorkingDirectory (Split-Path -Parent $Program)
