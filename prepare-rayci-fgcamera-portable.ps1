param(
    [string]$RayCiSource = 'C:\Program Files\CINOGY\RayCi64 Lite',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist\RayCi64Lite-FGCameraBridge'),
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
$buildScript = Join-Path $repoRoot 'build-rayci-fgcamera-bridge.ps1'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$proxyOutput = Join-Path $artifactsRoot 'fgcamera_proxy'
$proxyDll = Join-Path $proxyOutput 'FGCamera.dll'

if ($Rebuild -or -not (Test-Path -LiteralPath $proxyDll)) {
    & $buildScript
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

$originalCandidates = @(
    'C:\Windows\System32\FGCamera.dll',
    (Join-Path $rayciSourceFull 'FGCamera.dll')
)

$originalSource = $originalCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $originalSource) {
    throw "No original FGCamera.dll source was found."
}

$originalTarget = Join-Path $outputFull 'FGCamera.original.dll'
Copy-Item -LiteralPath $originalSource -Destination $originalTarget -Force

Write-Host "Installing FGCamera proxy..."
Copy-Item -LiteralPath $proxyDll -Destination (Join-Path $outputFull 'FGCamera.dll') -Force

$proxyPdb = Join-Path $proxyOutput 'FGCamera.pdb'
if (Test-Path -LiteralPath $proxyPdb) {
    Copy-Item -LiteralPath $proxyPdb -Destination (Join-Path $outputFull 'FGCamera.pdb') -Force
}

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
Write-Host "Portable RayCi FGCamera bridge is ready:"
Write-Host "  Folder : $outputFull"
Write-Host "  Launch : $(Join-Path $outputFull 'RayCi.exe')"
Write-Host "  Original FGCamera : $originalTarget"
Write-Host "  Logs   : $env:LOCALAPPDATA\\Ultron\\RayCiFGCameraBridge\\logs"
Write-Host ""
Write-Host "Override the forwarded target with ULTRON_RAYCI_FGCAMERA_ORIGINAL if needed."
