[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('native', 'compatible')]
    [string]$Mode,

    [string]$PortableRoot,

    [string]$RayCiSource = 'C:\Program Files\CINOGY\RayCi64 Lite',

    [switch]$CloseExisting,

    [switch]$RebuildArtifacts,

    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Resolve-PortableRootPath {
    param(
        [string]$RequestedPath
    )

    if ($RequestedPath) {
        return Get-FullPath -PathValue $RequestedPath
    }

    $bundlePortable = Join-Path $PSScriptRoot 'RayCi64Lite'
    if (Test-Path -LiteralPath (Join-Path $bundlePortable 'RayCi.exe')) {
        return Get-FullPath -PathValue $bundlePortable
    }

    $preferred = Join-Path $PSScriptRoot 'dist\RayCi64Lite-HybridBridge-final'
    if (Test-Path -LiteralPath $preferred) {
        return Get-FullPath -PathValue $preferred
    }

    $distRoot = Join-Path $PSScriptRoot 'dist'
    $candidates = @(Get-ChildItem -Path $distRoot -Directory -Filter 'RayCi64Lite-HybridBridge-final*' -ErrorAction SilentlyContinue |
        Sort-Object `
            @{ Expression = 'LastWriteTimeUtc'; Descending = $true }, `
            @{ Expression = 'Name'; Descending = $true })

    if ($candidates.Count -gt 0) {
        return $candidates[0].FullName
    }

    return $preferred
}

function Copy-Tree {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    & robocopy $SourcePath $DestinationPath /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed copying '$SourcePath' to '$DestinationPath' with exit code $LASTEXITCODE"
    }
}

function Remove-FileIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if (Test-Path -LiteralPath $PathValue) {
        Remove-Item -LiteralPath $PathValue -Force
    }
}

function Stop-ProcessesByName {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) {
            continue
        }

        foreach ($proc in $procs) {
            try {
                if ($proc.MainWindowHandle -ne 0) {
                    $null = $proc.CloseMainWindow()
                }
            } catch {
            }
        }

        Start-Sleep -Milliseconds 800

        foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            } catch {
                Write-Warning "Unable to stop process $($proc.ProcessName) [$($proc.Id)]: $($_.Exception.Message)"
            }
        }
    }
}

function Ensure-RayCiPortable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PortableRootPath,
        [Parameter(Mandatory = $true)]
        [string]$RayCiSourcePath
    )

    $rayciExe = Join-Path $PortableRootPath 'RayCi.exe'
    if (Test-Path -LiteralPath $rayciExe) {
        return
    }

    $prepareScript = Join-Path $PSScriptRoot 'prepare-rayci-hybrid-portable.ps1'
    if (-not (Test-Path -LiteralPath $prepareScript)) {
        throw "Portable RayCi was not found at '$PortableRootPath' and '$prepareScript' is missing."
    }

    $rayciSourceExe = Join-Path $RayCiSourcePath 'RayCi.exe'
    if (-not (Test-Path -LiteralPath $rayciSourceExe)) {
        throw "Portable RayCi was not found at '$PortableRootPath', and RayCi source is missing at '$RayCiSourcePath'."
    }

    Write-Host "Portable RayCi not found, preparing a fresh hybrid package..."
    & $prepareScript -RayCiSource $RayCiSourcePath -OutputRoot $PortableRootPath
}

function Ensure-CompatibleArtifacts {
    param(
        [switch]$ForceRebuild
    )

    $ueyeBuildScript = Join-Path $PSScriptRoot 'build-rayci-ueye-bridge.ps1'
    $fgcameraBuildScript = Join-Path $PSScriptRoot 'build-rayci-fgcamera-bridge.ps1'
    $artifactsRoot = Join-Path $PSScriptRoot 'artifacts'

    $ueyeDll = Join-Path $artifactsRoot 'ueye_proxy\ueye_api_64.dll'
    $fgcameraDll = Join-Path $artifactsRoot 'fgcamera_proxy\FGCamera.dll'
    $helperExe = Join-Path $artifactsRoot 'DahengBridgeHelper\DahengFrameServer.exe'

    $missingArtifacts = @(
        $ueyeDll,
        $fgcameraDll,
        $helperExe
    ) | Where-Object { -not (Test-Path -LiteralPath $_) }

    if ($missingArtifacts.Count -eq 0 -and -not $ForceRebuild) {
        return
    }

    if ($ForceRebuild -or -not (Test-Path -LiteralPath $ueyeDll) -or -not (Test-Path -LiteralPath $helperExe)) {
        if (-not (Test-Path -LiteralPath $ueyeBuildScript)) {
            $missingText = ($missingArtifacts | ForEach-Object { "'$_'" }) -join ', '
            if (-not $missingText) {
                $missingText = "'$ueyeDll', '$helperExe'"
            }

            throw "Compatible bundle artifacts are missing ($missingText) and '$ueyeBuildScript' is unavailable. Copy the 'artifacts' folder into this bundle or rebuild from the full repo."
        }

        & $ueyeBuildScript
    }

    if ($ForceRebuild -or -not (Test-Path -LiteralPath $fgcameraDll)) {
        if (-not (Test-Path -LiteralPath $fgcameraBuildScript)) {
            throw "Compatible bundle artifact '$fgcameraDll' is missing and '$fgcameraBuildScript' is unavailable. Copy the 'artifacts' folder into this bundle or rebuild from the full repo."
        }

        & $fgcameraBuildScript
    }

    $missingAfterBuild = @(
        $ueyeDll,
        $fgcameraDll,
        $helperExe
    ) | Where-Object { -not (Test-Path -LiteralPath $_) }

    if ($missingAfterBuild.Count -gt 0) {
        $missingText = ($missingAfterBuild | ForEach-Object { "'$_'" }) -join ', '
        throw "Compatible artifacts are still missing after preparation: $missingText"
    }
}

function Save-NativeDllBackup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PortableRootPath,
        [Parameter(Mandatory = $true)]
        [string]$BackupFileName,
        [Parameter(Mandatory = $true)]
        [string[]]$SourceCandidates
    )

    $backupPath = Join-Path $PortableRootPath $BackupFileName
    if (Test-Path -LiteralPath $backupPath) {
        return $backupPath
    }

    $sourcePath = $SourceCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $sourcePath) {
        return $null
    }

    Copy-Item -LiteralPath $sourcePath -Destination $backupPath -Force
    return $backupPath
}

function Resolve-NativeSource {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Candidates,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $resolved = $Candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $resolved) {
        throw "No native source was found for $Label."
    }

    return $resolved
}

$portableRootFull = Resolve-PortableRootPath -RequestedPath $PortableRoot
$rayciSourceFull = Get-FullPath -PathValue $RayCiSource

if ($CloseExisting -or @(Get-Process -Name 'RayCi', 'DahengFrameServer' -ErrorAction SilentlyContinue).Count -gt 0) {
    Write-Host 'Stopping running RayCi bridge processes...'
    Stop-ProcessesByName -Names @('RayCi', 'DahengFrameServer')
}

Ensure-RayCiPortable -PortableRootPath $portableRootFull -RayCiSourcePath $rayciSourceFull

$portableRayciExe = Join-Path $portableRootFull 'RayCi.exe'
$portableFgcameraDll = Join-Path $portableRootFull 'FGCamera.dll'
$portableFgcameraNativeDll = Join-Path $portableRootFull 'FGCamera.native.dll'
$portableFgcameraOriginalDll = Join-Path $portableRootFull 'FGCamera.original.dll'
$portableFgcameraPdb = Join-Path $portableRootFull 'FGCamera.pdb'
$portableUeyeDll = Join-Path $portableRootFull 'ueye_api_64.dll'
$portableUeyeNativeDll = Join-Path $portableRootFull 'ueye_api_64.native.dll'
$portableUeyePdb = Join-Path $portableRootFull 'ueye_api_64.pdb'
$portableHelperRoot = Join-Path $portableRootFull 'DahengBridgeHelper'
$portableModeMarker = Join-Path $portableRootFull '.ultron-camera-mode.txt'

$rayciSourceFgcamera = Join-Path $rayciSourceFull 'FGCamera.dll'
$rayciSourceUeye = Join-Path $rayciSourceFull 'ueye_api_64.dll'
$systemFgcamera = 'C:\Windows\System32\FGCamera.dll'

$fgcameraBackup = Save-NativeDllBackup -PortableRootPath $portableRootFull -BackupFileName 'FGCamera.native.dll' -SourceCandidates @(
    $rayciSourceFgcamera,
    $portableFgcameraOriginalDll,
    $systemFgcamera
)

$null = Save-NativeDllBackup -PortableRootPath $portableRootFull -BackupFileName 'ueye_api_64.native.dll' -SourceCandidates @(
    $rayciSourceUeye
)

if ($Mode -eq 'compatible') {
    Ensure-CompatibleArtifacts -ForceRebuild:$RebuildArtifacts

    $artifactsRoot = Join-Path $PSScriptRoot 'artifacts'
    $proxyUeyeDll = Join-Path $artifactsRoot 'ueye_proxy\ueye_api_64.dll'
    $proxyUeyePdb = Join-Path $artifactsRoot 'ueye_proxy\ueye_api_64.pdb'
    $proxyFgcameraDll = Join-Path $artifactsRoot 'fgcamera_proxy\FGCamera.dll'
    $proxyFgcameraPdb = Join-Path $artifactsRoot 'fgcamera_proxy\FGCamera.pdb'
    $helperSourceRoot = Join-Path $artifactsRoot 'DahengBridgeHelper'

    if (-not (Test-Path -LiteralPath $portableFgcameraOriginalDll)) {
        $originalSource = Resolve-NativeSource -Label 'FGCamera.dll backup' -Candidates @(
            $fgcameraBackup,
            $rayciSourceFgcamera,
            $systemFgcamera
        )
        Copy-Item -LiteralPath $originalSource -Destination $portableFgcameraOriginalDll -Force
    }

    Write-Host 'Switching portable RayCi to compatible camera mode...'
    Copy-Item -LiteralPath $proxyUeyeDll -Destination $portableUeyeDll -Force
    Copy-Item -LiteralPath $proxyFgcameraDll -Destination $portableFgcameraDll -Force
    if (Test-Path -LiteralPath $proxyUeyePdb) {
        Copy-Item -LiteralPath $proxyUeyePdb -Destination $portableUeyePdb -Force
    }
    if (Test-Path -LiteralPath $proxyFgcameraPdb) {
        Copy-Item -LiteralPath $proxyFgcameraPdb -Destination $portableFgcameraPdb -Force
    }
    Copy-Tree -SourcePath $helperSourceRoot -DestinationPath $portableHelperRoot

    Set-Content -LiteralPath $portableModeMarker -Value 'compatible' -Encoding ASCII
} else {
    Write-Host 'Switching portable RayCi to native camera mode...'

    $nativeFgcameraSource = Resolve-NativeSource -Label 'FGCamera.dll' -Candidates @(
        $portableFgcameraNativeDll,
        $portableFgcameraOriginalDll,
        $rayciSourceFgcamera,
        $systemFgcamera
    )
    Copy-Item -LiteralPath $nativeFgcameraSource -Destination $portableFgcameraDll -Force

    $nativeUeyeSource = @(
        $portableUeyeNativeDll,
        $rayciSourceUeye
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

    if ($nativeUeyeSource) {
        Copy-Item -LiteralPath $nativeUeyeSource -Destination $portableUeyeDll -Force
    } else {
        Remove-FileIfExists -PathValue $portableUeyeDll
        Remove-FileIfExists -PathValue $portableUeyePdb
    }

    Remove-FileIfExists -PathValue $portableFgcameraPdb
    Set-Content -LiteralPath $portableModeMarker -Value 'native' -Encoding ASCII
}

Write-Host ''
Write-Host 'Camera mode switch completed:'
Write-Host "  Mode        : $Mode"
Write-Host "  Portable    : $portableRootFull"
Write-Host "  Launch      : $portableRayciExe"
Write-Host "  FGCamera    : $portableFgcameraDll"
Write-Host "  uEye        : $(if (Test-Path -LiteralPath $portableUeyeDll) { $portableUeyeDll } else { '<removed>' })"
Write-Host "  Helper      : $(if (Test-Path -LiteralPath $portableHelperRoot) { $portableHelperRoot } else { '<missing>' })"

if ($Launch) {
    Write-Host ''
    Write-Host 'Launching RayCi...'
    Start-Process -FilePath $portableRayciExe -WorkingDirectory $portableRootFull
}
