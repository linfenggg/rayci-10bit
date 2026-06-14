param(
    [string]$RayCiSource = 'C:\Program Files\CINOGY\RayCi64 Lite',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'dist\RayCi64Lite-HybridBridge-final'),
    [switch]$Rebuild,
    [switch]$CreateZip
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

function Remove-DirectoryRobust {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$SafeRoot
    )

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return
    }

    if (-not (Test-PathWithinRoot -CandidatePath $TargetPath -RootPath $SafeRoot)) {
        throw "Refusing to replace output outside workspace: $TargetPath"
    }

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Remove-Item -LiteralPath $TargetPath -Recurse -Force -ErrorAction Stop
            break
        } catch {
            if (-not (Test-Path -LiteralPath $TargetPath)) {
                break
            }

            if ($attempt -eq 3) {
                throw
            }

            Start-Sleep -Milliseconds (300 * $attempt)
        }
    }
}

$repoRoot = Get-FullPath -PathValue $PSScriptRoot
$ueyeBuildScript = Join-Path $repoRoot 'build-rayci-ueye-bridge.ps1'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$runtimeSubdirectoryName = 'RayCiRuntime'
$ueyeOutput = Join-Path $artifactsRoot 'ueye_proxy'
$helperOutput = Join-Path $artifactsRoot 'DahengBridgeHelper'
$launcherOutput = Join-Path $artifactsRoot 'RayCiPortableLauncher'
$ueyeDll = Join-Path $ueyeOutput 'ueye_api_64.dll'
$helperExe = Join-Path $helperOutput 'DahengFrameServer.exe'
$launcherExe = Join-Path $launcherOutput 'RayCi.exe'

if ($Rebuild -or -not (Test-Path -LiteralPath $ueyeDll) -or -not (Test-Path -LiteralPath $helperExe) -or -not (Test-Path -LiteralPath $launcherExe)) {
    & $ueyeBuildScript
}

$rayciSourceFull = Get-FullPath -PathValue $RayCiSource
$outputFull = Get-FullPath -PathValue $OutputRoot
$rayciExe = Join-Path $rayciSourceFull 'RayCi.exe'

if (-not (Test-Path -LiteralPath $rayciExe)) {
    throw "RayCi.exe was not found under: $rayciSourceFull"
}

if (Test-Path -LiteralPath $outputFull) {
    Remove-DirectoryRobust -TargetPath $outputFull -SafeRoot $repoRoot
}

Write-Host "Copying RayCi portable tree..."
New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
$runtimeRoot = Join-Path $outputFull $runtimeSubdirectoryName
Copy-Tree -SourcePath $rayciSourceFull -DestinationPath $runtimeRoot

Write-Host "Installing green launcher entry..."
Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $outputFull 'RayCi.exe') -Force

Write-Host "Installing virtual uEye proxy..."
Copy-Item -LiteralPath $ueyeDll -Destination (Join-Path $runtimeRoot 'ueye_api_64.dll') -Force

$ueyePdb = Join-Path $ueyeOutput 'ueye_api_64.pdb'
if (Test-Path -LiteralPath $ueyePdb) {
    Copy-Item -LiteralPath $ueyePdb -Destination (Join-Path $runtimeRoot 'ueye_api_64.pdb') -Force
}

$helperTarget = Join-Path $runtimeRoot 'DahengBridgeHelper'
Write-Host "Copying Daheng helper runtime..."
Copy-Tree -SourcePath $helperOutput -DestinationPath $helperTarget

$eniTarget = Join-Path $runtimeRoot 'RayCi.eni'
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
Write-Host "  Runtime: $runtimeRoot"
Write-Host "  Real   : $(Join-Path $runtimeRoot 'RayCi.exe')"
Write-Host "  uEye   : $(Join-Path $runtimeRoot 'ueye_api_64.dll')"
Write-Host "  ENI    : $eniTarget"
Write-Host "  FGCam  : original file preserved from RayCi install"
Write-Host "  Helper : $(Join-Path $helperTarget 'DahengFrameServer.exe')"
Write-Host "  UE logs: $env:LOCALAPPDATA\\Ultron\\RayCiUeyeBridge\\logs"

if ($CreateZip) {
    $zipPath = "$outputFull.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -LiteralPath $outputFull -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "  Zip    : $zipPath"
}
