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
$proxyProject = Join-Path $repoRoot 'virtual_ueye_proxy\VirtualUEyeProxy.csproj'
$helperProject = Join-Path $repoRoot 'daheng_frame_server\DahengFrameServer.csproj'
$proxyOutput = Join-Path $artifactsRootFull 'ueye_proxy'
$helperOutput = Join-Path $artifactsRootFull 'DahengBridgeHelper'

New-Item -ItemType Directory -Path $artifactsRootFull -Force | Out-Null
Reset-Directory -TargetPath $proxyOutput -SafeRoot $repoRoot
Reset-Directory -TargetPath $helperOutput -SafeRoot $repoRoot

Write-Host "Publishing virtual uEye proxy..."
dotnet publish $proxyProject `
    -c $Configuration `
    -o $proxyOutput

Write-Host "Publishing Daheng frame helper..."
dotnet publish $helperProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $helperOutput

$proxyDll = Join-Path $proxyOutput 'ueye_api_64.dll'
$helperExe = Join-Path $helperOutput 'DahengFrameServer.exe'

if (-not (Test-Path -LiteralPath $proxyDll)) {
    throw "Virtual uEye proxy was not produced: $proxyDll"
}

if (-not (Test-Path -LiteralPath $helperExe)) {
    throw "Daheng helper was not produced: $helperExe"
}

Write-Host ""
Write-Host "Bridge artifacts ready:"
Write-Host "  Proxy : $proxyDll"
Write-Host "  Helper: $helperExe"
