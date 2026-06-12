param(
    [string]$RayCiSource = 'C:\Program Files\CINOGY\RayCi64 Lite',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist\RayCi64Lite-UeyeBridge'),
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
$buildScript = Join-Path $repoRoot 'build-rayci-ueye-bridge.ps1'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$proxyOutput = Join-Path $artifactsRoot 'ueye_proxy'
$helperOutput = Join-Path $artifactsRoot 'DahengBridgeHelper'
$proxyDll = Join-Path $proxyOutput 'ueye_api_64.dll'
$helperExe = Join-Path $helperOutput 'DahengFrameServer.exe'

if ($Rebuild -or -not (Test-Path -LiteralPath $proxyDll) -or -not (Test-Path -LiteralPath $helperExe)) {
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

Write-Host "Installing virtual uEye proxy..."
Copy-Item -LiteralPath $proxyDll -Destination (Join-Path $outputFull 'ueye_api_64.dll') -Force

$proxyPdb = Join-Path $proxyOutput 'ueye_api_64.pdb'
if (Test-Path -LiteralPath $proxyPdb) {
    Copy-Item -LiteralPath $proxyPdb -Destination (Join-Path $outputFull 'ueye_api_64.pdb') -Force
}

$helperTarget = Join-Path $outputFull 'DahengBridgeHelper'
Write-Host "Copying Daheng helper runtime..."
Copy-Tree -SourcePath $helperOutput -DestinationPath $helperTarget

$eniTarget = Join-Path $outputFull 'RayCi.eni'
if (-not (Test-Path -LiteralPath $eniTarget)) {
    $eniCandidates = @(
        (Join-Path $rayciSourceFull 'RayCi.eni'),
        'C:\Program Files\CINOGY\RayCi64\RayCi.eni'
    )

    foreach ($eniCandidate in $eniCandidates) {
        if (Copy-OptionalFile -SourcePath $eniCandidate -DestinationPath $eniTarget) {
            Write-Host "Copied fallback RayCi.eni from $eniCandidate"
            break
        }
    }
}

Write-Host ""
Write-Host "Portable RayCi bridge is ready:"
Write-Host "  Folder : $outputFull"
Write-Host "  Launch : $(Join-Path $outputFull 'RayCi.exe')"
Write-Host "  Helper : $(Join-Path $helperTarget 'DahengFrameServer.exe')"
Write-Host "  Logs   : $env:LOCALAPPDATA\\Ultron\\RayCiUeyeBridge\\logs"
Write-Host ""
Write-Host "The proxy auto-discovers the helper from DahengBridgeHelper."
