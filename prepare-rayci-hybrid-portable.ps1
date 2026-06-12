param(
    [string]$RayCiSource = 'C:\Program Files\CINOGY\RayCi64 Lite',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist\RayCi64Lite-HybridBridge-final'),
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $candidateFull = (Get-FullPath -PathValue $CandidatePath).TrimEnd('\')
    $rootFull = (Get-FullPath -PathValue $RootPath).TrimEnd('\')

    return $candidateFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Copy-Tree {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    & robocopy $SourcePath $DestinationPath /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed copying '$SourcePath' to '$DestinationPath' with exit code $LASTEXITCODE"
    }
}

function Copy-OptionalFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    if (Test-Path -LiteralPath $SourcePath) {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
        return $true
    }

    return $false
}

$repoRoot = Get-FullPath -PathValue $PSScriptRoot
$ueyeBuildScript = Join-Path $repoRoot 'build-rayci-ueye-bridge.ps1'
$fgcameraBuildScript = Join-Path $repoRoot 'build-rayci-fgcamera-bridge.ps1'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$ueyeOutput = Join-Path $artifactsRoot 'ueye_proxy'
$fgcameraOutput = Join-Path $artifactsRoot 'fgcamera_proxy'
$helperOutput = Join-Path $artifactsRoot 'DahengBridgeHelper'
$ueyeDll = Join-Path $ueyeOutput 'ueye_api_64.dll'
$fgcameraDll = Join-Path $fgcameraOutput 'FGCamera.dll'
$helperExe = Join-Path $helperOutput 'DahengFrameServer.exe'

if ($Rebuild -or -not (Test-Path -LiteralPath $ueyeDll) -or -not (Test-Path -LiteralPath $helperExe)) {
    & $ueyeBuildScript
}

if ($Rebuild -or -not (Test-Path -LiteralPath $fgcameraDll)) {
    & $fgcameraBuildScript
}

$rayciSourceFull = Get-FullPath -PathValue $RayCiSource
$outputFull = Get-FullPath -PathValue $OutputRoot
$rayciExe = Join-Path $rayciSourceFull 'RayCi.exe'

if (-not (Test-Path -LiteralPath $rayciExe)) {
    throw "RayCi.exe was not found under: $rayciSourceFull"
}

if (Test-Path -LiteralPath $outputFull) {
    if (-not (Test-PathWithinRoot -CandidatePath $outputFull -RootPath $repoRoot)) {
        throw "Refusing to replace output outside workspace: $outputFull"
    }

    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

Write-Host "Copying RayCi portable tree..."
Copy-Tree -SourcePath $rayciSourceFull -DestinationPath $outputFull

Write-Host "Installing virtual uEye proxy..."
Copy-Item -LiteralPath $ueyeDll -Destination (Join-Path $outputFull 'ueye_api_64.dll') -Force

$ueyePdb = Join-Path $ueyeOutput 'ueye_api_64.pdb'
if (Test-Path -LiteralPath $ueyePdb) {
    Copy-Item -LiteralPath $ueyePdb -Destination (Join-Path $outputFull 'ueye_api_64.pdb') -Force
}

$originalCandidates = @(
    'C:\Windows\System32\FGCamera.dll',
    (Join-Path $rayciSourceFull 'FGCamera.dll')
)

$originalSource = $originalCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $originalSource) {
    throw "No original FGCamera.dll source was found."
}

Copy-Item -LiteralPath $originalSource -Destination (Join-Path $outputFull 'FGCamera.original.dll') -Force

Write-Host "Installing FGCamera proxy..."
Copy-Item -LiteralPath $fgcameraDll -Destination (Join-Path $outputFull 'FGCamera.dll') -Force

$fgcameraPdb = Join-Path $fgcameraOutput 'FGCamera.pdb'
if (Test-Path -LiteralPath $fgcameraPdb) {
    Copy-Item -LiteralPath $fgcameraPdb -Destination (Join-Path $outputFull 'FGCamera.pdb') -Force
}

$helperTarget = Join-Path $outputFull 'DahengBridgeHelper'
Write-Host "Copying Daheng helper runtime..."
Copy-Tree -SourcePath $helperOutput -DestinationPath $helperTarget

$eniTarget = Join-Path $outputFull 'RayCi.eni'
if (-not (Test-Path -LiteralPath $eniTarget)) {
    $eniCandidates = @(
        (Join-Path $rayciSourceFull 'RayCi.eni'),
        'C:\Program Files\CINOGY\RayCi64\RayCi.eni',
        (Join-Path $repoRoot 'dist\RayCi64Lite-UeyeBridge\RayCi.eni')
    )

    foreach ($eniCandidate in $eniCandidates) {
        if (Copy-OptionalFile -SourcePath $eniCandidate -DestinationPath $eniTarget) {
            Write-Host "Copied fallback RayCi.eni from $eniCandidate"
            break
        }
    }
}

Write-Host ""
Write-Host "Portable RayCi hybrid bridge is ready:"
Write-Host "  Folder : $outputFull"
Write-Host "  Launch : $(Join-Path $outputFull 'RayCi.exe')"
Write-Host "  uEye   : $(Join-Path $outputFull 'ueye_api_64.dll')"
Write-Host "  FGCam  : $(Join-Path $outputFull 'FGCamera.dll')"
Write-Host "  Helper : $(Join-Path $helperTarget 'DahengFrameServer.exe')"
Write-Host "  FG logs: $env:LOCALAPPDATA\\Ultron\\RayCiFGCameraBridge\\logs"
Write-Host "  UE logs: $env:LOCALAPPDATA\\Ultron\\RayCiUeyeBridge\\logs"
