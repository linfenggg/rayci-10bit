param(
    [string]$SandboxSource = 'D:\work\ultron\usb ccd\rayci_sandbox',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist\RayCi64Lite-SandboxCompatBridge'),
    [switch]$RebuildHelper
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

$repoRoot = Get-FullPath -PathValue $PSScriptRoot
$sandboxSourceFull = Get-FullPath -PathValue $SandboxSource
$outputFull = Get-FullPath -PathValue $OutputRoot
$buildScript = Join-Path $repoRoot 'build-rayci-ueye-bridge.ps1'
$helperOutput = Join-Path $repoRoot 'artifacts\DahengBridgeHelper'
$helperExe = Join-Path $helperOutput 'DahengFrameServer.exe'
$sandboxExe = Join-Path $sandboxSourceFull 'RayCi.exe'

if (-not (Test-Path -LiteralPath $sandboxExe)) {
    throw "Sandbox RayCi.exe was not found under: $sandboxSourceFull"
}

if ($RebuildHelper -or -not (Test-Path -LiteralPath $helperExe)) {
    & $buildScript
    if ($LASTEXITCODE -ne 0) {
        throw "build-rayci-ueye-bridge.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $helperExe)) {
    throw "Sandbox-compatible helper runtime is missing: $helperExe"
}

if (Test-Path -LiteralPath $outputFull) {
    if (-not (Test-PathWithinRoot -CandidatePath $outputFull -RootPath $repoRoot)) {
        throw "Refusing to replace output outside workspace: $outputFull"
    }

    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

Write-Host "Copying sandbox RayCi runtime..."
Copy-Tree -SourcePath $sandboxSourceFull -DestinationPath $outputFull

Write-Host "Overlaying current Daheng helper runtime for simulation support..."
Copy-Tree -SourcePath $helperOutput -DestinationPath $outputFull

Write-Host ""
Write-Host "Sandbox-compatible RayCi bridge is ready:"
Write-Host "  Folder : $outputFull"
Write-Host "  Launch : $(Join-Path $outputFull 'RayCi.exe')"
Write-Host "  Proxy  : $(Join-Path $outputFull 'ueye_api_64.dll')"
Write-Host "  Helper : $(Join-Path $outputFull 'DahengFrameServer.exe')"
