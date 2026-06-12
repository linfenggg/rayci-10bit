param(
    [string]$SdkRoot = "C:\Program Files\Daheng Imaging\GalaxySDK",
    [string]$PortableRoot = (Join-Path $PSScriptRoot "GalaxyViewPortable"),
    [switch]$ForceRefresh
)

$ErrorActionPreference = "Stop"

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

$demoRoot = Join-Path $SdkRoot "Demo\Win64"
$apiRoot = Join-Path $SdkRoot "APIDll\Win64"

Assert-PathExists -Path $SdkRoot -Label "GalaxySDK root"
Assert-PathExists -Path $demoRoot -Label "GalaxyView demo directory"
Assert-PathExists -Path $apiRoot -Label "GalaxySDK API DLL directory"

if ($ForceRefresh -or -not (Test-Path -LiteralPath $PortableRoot) -or -not (Test-Path -LiteralPath (Join-Path $PortableRoot "GxIAPI.dll"))) {
    if (Test-Path -LiteralPath $PortableRoot) {
        Remove-Item -LiteralPath $PortableRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $PortableRoot | Out-Null
    Copy-Item (Join-Path $demoRoot "*") $PortableRoot -Recurse -Force
    Copy-Item (Join-Path $apiRoot "*") $PortableRoot -Recurse -Force
}

$viewerExe = Join-Path $PortableRoot "GalaxyView.exe"
Assert-PathExists -Path $viewerExe -Label "GalaxyView executable"

$sdkDll = Join-Path $PortableRoot "GxIAPI.dll"
$sdkDllVersion = (Get-Item $sdkDll).VersionInfo.FileVersion
$systemDll = "C:\Windows\System32\GxIAPI.dll"
$systemDllVersion = if (Test-Path -LiteralPath $systemDll) {
    (Get-Item $systemDll).VersionInfo.FileVersion
} else {
    "<missing>"
}

Write-Host "Launching GalaxyView from: $viewerExe"
Write-Host "Portable GxIAPI.dll version: $sdkDllVersion"
Write-Host "System32 GxIAPI.dll version: $systemDllVersion"
Write-Host "Using the portable folder avoids loading the older System32 camera DLL."

Start-Process -FilePath $viewerExe -WorkingDirectory $PortableRoot
