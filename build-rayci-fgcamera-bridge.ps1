param(
    [string]$Configuration = 'Release',
    [string]$ArtifactsRoot = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$Clean
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

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$SafeRoot
    )

    if (Test-Path -LiteralPath $TargetPath) {
        if (-not (Test-PathWithinRoot -CandidatePath $TargetPath -RootPath $SafeRoot)) {
            throw "Refusing to delete path outside workspace: $TargetPath"
        }

        Remove-Item -LiteralPath $TargetPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
}

$repoRoot = Get-FullPath -PathValue $PSScriptRoot
$artifactsRootFull = Get-FullPath -PathValue $ArtifactsRoot
$proxyProject = Join-Path $repoRoot 'virtual_fgcamera_proxy\VirtualFGCameraProxy.csproj'
$proxyOutput = Join-Path $artifactsRootFull 'fgcamera_proxy'

New-Item -ItemType Directory -Path $artifactsRootFull -Force | Out-Null
Reset-Directory -TargetPath $proxyOutput -SafeRoot $repoRoot

Write-Host "Publishing FGCamera proxy..."
dotnet publish $proxyProject `
    -c $Configuration `
    -o $proxyOutput

$proxyDll = Join-Path $proxyOutput 'FGCamera.dll'
if (-not (Test-Path -LiteralPath $proxyDll)) {
    throw "FGCamera proxy was not produced: $proxyDll"
}

Write-Host ""
Write-Host "FGCamera proxy artifacts ready:"
Write-Host "  Proxy : $proxyDll"
