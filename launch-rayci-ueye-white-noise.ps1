[CmdletBinding()]
param(
    [switch]$CloseExisting,
    [switch]$FinalizeOpenLiveMode,
    [switch]$Verify,
    [double]$StartupTimeoutSec = 25.0,
    [double]$SelectionTimeoutSec = 90.0,
    [int]$PostLaunchDelayMs = 1500,
    [int]$PostInvokeDelayMs = 4500,
    [string]$PortableRoot,
    [string]$DumpPrefix
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Get-SafeValue {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script,
        $Default = $null
    )

    try {
        & $Script
    } catch {
        $Default
    }
}

function Stop-ExistingRayCi {
    $procs = @(Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        return
    }

    foreach ($proc in $procs) {
        try {
            if ($proc.MainWindowHandle -ne 0) {
                $null = $proc.CloseMainWindow()
            }
        } catch {
        }
    }

    Start-Sleep -Seconds 2
    $remaining = @(Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        foreach ($proc in $remaining) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            } catch {
                Write-Warning "Unable to stop RayCi process $($proc.Id): $($_.Exception.Message)"
            }
        }
    }
}

function Stop-ExistingDahengHelpers {
    param(
        [string]$AllowedExecutablePath
    )

    $allowedFullPath = $null
    if (-not [string]::IsNullOrWhiteSpace($AllowedExecutablePath)) {
        try {
            $allowedFullPath = [System.IO.Path]::GetFullPath($AllowedExecutablePath)
        } catch {
            $allowedFullPath = $null
        }
    }

    $helperNames = @('DahengFrameServer')
    foreach ($name in $helperNames) {
        $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object {
            if ($null -eq $allowedFullPath) {
                return $true
            }

            try {
                return -not [string]::Equals($_.Path, $allowedFullPath, [System.StringComparison]::OrdinalIgnoreCase)
            } catch {
                return $true
            }
        })
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

        foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object {
            if ($null -eq $allowedFullPath) {
                return $true
            }

            try {
                return -not [string]::Equals($_.Path, $allowedFullPath, [System.StringComparison]::OrdinalIgnoreCase)
            } catch {
                return $true
            }
        })) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            } catch {
                Write-Warning "Unable to stop helper process $($proc.Id): $($_.Exception.Message)"
            }
        }
    }
}

function Wait-ForRayCiWindow {
    param(
        [Parameter(Mandatory = $true)]
        [double]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $proc = Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } |
            Select-Object -First 1
        if ($null -ne $proc) {
            return $proc.MainWindowTitle
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Timed out waiting for the RayCi main window."
}

function Get-RayCiWindows {
    $procIds = @(Get-Process -Name 'RayCi' -ErrorAction Stop | Select-Object -ExpandProperty Id)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windows = @()

    foreach ($procId in $procIds) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $procId
        )
        $found = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        for ($i = 0; $i -lt $found.Count; $i++) {
            $windows += $found.Item($i)
        }
    }

    return @($windows)
}

function Find-ElementRecursive {
    param(
        [Parameter(Mandatory = $true)]
        $RootElement,
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.TreeWalker]$Walker,
        [string]$Name,
        [string]$AutomationId,
        [string]$ControlTypeProgrammaticName
    )

    $currentName = Get-SafeValue { $RootElement.Current.Name } ''
    $currentAutomationId = Get-SafeValue { $RootElement.Current.AutomationId } ''
    $currentControlType = Get-SafeValue { $RootElement.Current.ControlType.ProgrammaticName } ''

    $nameMatch = (-not $Name) -or ($currentName -eq $Name)
    $idMatch = (-not $AutomationId) -or ($currentAutomationId -eq $AutomationId)
    $typeMatch = (-not $ControlTypeProgrammaticName) -or ($currentControlType -eq $ControlTypeProgrammaticName)

    if ($nameMatch -and $idMatch -and $typeMatch) {
        return $RootElement
    }

    $child = Get-SafeValue { $Walker.GetFirstChild($RootElement) } $null
    while ($null -ne $child) {
        $match = Find-ElementRecursive `
            -RootElement $child `
            -Walker $Walker `
            -Name $Name `
            -AutomationId $AutomationId `
            -ControlTypeProgrammaticName $ControlTypeProgrammaticName

        if ($null -ne $match) {
            return $match
        }

        $child = Get-SafeValue { $Walker.GetNextSibling($child) } $null
    }

    return $null
}

function Find-RayCiElement {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [string]$Name,
        [string]$AutomationId,
        [string]$ControlTypeProgrammaticName
    )

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    foreach ($window in $Windows) {
        $match = Find-ElementRecursive `
            -RootElement $window `
            -Walker $walker `
            -Name $Name `
            -AutomationId $AutomationId `
            -ControlTypeProgrammaticName $ControlTypeProgrammaticName
        if ($null -ne $match) {
            return $match
        }
    }

    return $null
}

function Invoke-RayCiButton {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [string]$AutomationId,
        [int]$DelayMs
    )

    $button = Find-RayCiElement `
        -Windows @(Get-RayCiWindows) `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::Button.ProgrammaticName)

    if ($null -eq $button) {
        throw "Button '$Name' was not found."
    }

    if (-not (Get-SafeValue { $button.Current.IsEnabled } $false)) {
        throw "Button '$Name' was found but is disabled."
    }

    $pattern = Get-SafeValue {
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    } $null

    if ($null -eq $pattern) {
        throw "Button '$Name' does not support InvokePattern."
    }

    $pattern.Invoke()
    Start-Sleep -Milliseconds $DelayMs
}

function Select-RayCiItem {
    param(
        [Parameter(Mandatory = $true)]
        $Element,
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [int]$DelayMs = 500
    )

    $pattern = Get-SafeValue {
        $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    } $null

    if ($null -eq $pattern) {
        throw "Selection item '$Label' does not support SelectionItemPattern."
    }

    $pattern.Select()
    Start-Sleep -Milliseconds $DelayMs
}

function Wait-RayCiSelectionReady {
    param(
        [Parameter(Mandatory = $true)]
        [double]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $windows = @(Get-RayCiWindows)
        if ($windows.Count -eq 0) {
            Start-Sleep -Milliseconds 500
            continue
        }

        $cameraRow = Find-RayCiElement `
            -Windows $windows `
            -Name 'CinCam CMOS 1201 EL' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::DataItem.ProgrammaticName)
        $accessoryRow = Find-RayCiElement `
            -Windows $windows `
            -Name 'plain' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::DataItem.ProgrammaticName)
        $openLiveModeButton = Find-RayCiElement `
            -Windows $windows `
            -Name 'Open LiveMode' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::Button.ProgrammaticName)
        $openButton = Find-RayCiElement `
            -Windows $windows `
            -Name 'Open' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::Button.ProgrammaticName)

        if ($null -ne $cameraRow -and $null -ne $accessoryRow) {
            return [pscustomobject]@{
                CameraRow          = $cameraRow
                AccessoryRow       = $accessoryRow
                OpenLiveModeButton = $openLiveModeButton
                OpenButton         = $openButton
            }
        }

        Start-Sleep -Milliseconds 500
    }

    return $null
}

function Get-VerificationSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ControlDumpPath
    )

    $lines = @(Get-Content -LiteralPath $ControlDumpPath)
    $cameraLine = $lines | Where-Object {
        $_ -like '*type=ControlType.DataItem*' -and
        $_ -notlike '*name="plain"*' -and
        $_ -notlike '*name="Header"*'
    } | Select-Object -First 1
    $liveModeLine = $lines | Where-Object { $_ -like '*LiveMode: Options -*' } | Select-Object -First 1
    $liveViewLine = $lines | Where-Object { $_ -like '*LiveMode: 2D-View (*' } | Select-Object -First 1
    $connectionLine = $lines | Where-Object { $_ -like '*StatusBar.Pane1*' } | Select-Object -First 1
    $statusLine = $lines | Where-Object { $_ -like '*StatusBar.Pane2*' } | Select-Object -First 1
    $licenseLine = $lines | Where-Object { $_ -like '*no license*' } | Select-Object -First 1
    $statusLineText = [string]$statusLine
    $reportedBpp = 0
    $reportedWidth = 0
    $reportedHeight = 0

    if ($statusLineText -match 'name="(?<bpp>\d+)bpp .*?: (?<width>\d+) x (?<height>\d+) at ') {
        $reportedBpp = [int]$Matches['bpp']
        $reportedWidth = [int]$Matches['width']
        $reportedHeight = [int]$Matches['height']
    }

    $statusAccepted =
        ($null -ne $liveModeLine) -and
        ($liveModeLine -notlike '*not connected*') -and
        ($null -ne $connectionLine) -and
        ($connectionLine -notlike '*not connected*') -and
        ($null -ne $statusLine) -and
        ($reportedBpp -gt 0) -and
        ($reportedWidth -gt 0) -and
        ($reportedHeight -gt 0)

    $liveViewAccepted =
        ($null -ne $liveModeLine) -and
        ($liveModeLine -notlike '*not connected*') -and
        ($null -ne $liveViewLine)

    $accepted =
        $statusAccepted -and
        ($null -eq $licenseLine)

    return [pscustomobject]@{
        Accepted      = $accepted
        LiveModeTitle = $liveModeLine
        LiveViewTitle = $liveViewLine
        CameraRow     = $cameraLine
        Connection    = $connectionLine
        StreamStatus  = $statusLine
        ReportedBpp   = $reportedBpp
        ReportedWidth = $reportedWidth
        ReportedHeight = $reportedHeight
        LicenseLine   = $licenseLine
    }
}

function Wait-RayCiAcceptedStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DumpScript,
        [Parameter(Mandatory = $true)]
        [string]$ControlDumpPath,
        [Parameter(Mandatory = $true)]
        [string]$ControlJsonPath,
        [double]$TimeoutSec = 30.0
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $summary = $null

    while ((Get-Date) -lt $deadline) {
        & $DumpScript `
            -ProcessName 'RayCi' `
            -View 'Control' `
            -MaxDepth 6 `
            -IncludePatterns `
            -TreeOut $ControlDumpPath `
            -JsonOut $ControlJsonPath | Out-Null

        $summary = Get-VerificationSummary -ControlDumpPath $ControlDumpPath
        if ($summary.Accepted) {
            return $summary
        }

        Start-Sleep -Milliseconds 1000
    }

    return $summary
}

$workspaceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$portableRoot = if ([string]::IsNullOrWhiteSpace($PortableRoot)) {
    Join-Path $workspaceRoot 'dist\RayCi64Lite-HybridBridge-final'
} else {
    [System.IO.Path]::GetFullPath($PortableRoot)
}
$rayciExe = Join-Path $portableRoot 'RayCi.exe'
$prepareScript = Join-Path $workspaceRoot 'prepare-rayci-hybrid-portable.ps1'
$seedRegistryScript = Join-Path $workspaceRoot 'seed-rayci-calibration-registry.ps1'
$dumpPrefixResolved = if ([string]::IsNullOrWhiteSpace($DumpPrefix)) {
    Join-Path $workspaceRoot 'result_live_verify'
} else {
    $DumpPrefix
}

if (-not (Test-Path -LiteralPath $rayciExe)) {
    if (-not (Test-Path -LiteralPath $prepareScript)) {
        throw "Portable RayCi bridge is missing and prepare-rayci-hybrid-portable.ps1 was not found."
    }

    & $prepareScript
}

if ($CloseExisting) {
    Stop-ExistingRayCi
    Stop-ExistingDahengHelpers
    if (Test-Path -LiteralPath $seedRegistryScript) {
        & $seedRegistryScript
    }
}

$env:BEAMMIC_DAHENG_START_WIDTH = '1280'
$env:BEAMMIC_DAHENG_START_HEIGHT = '1024'
$realCameraEnvironmentNames = @(
    'ULTRON_RAYCI_SIMULATE',
    'ULTRON_RAYCI_AUTO_SIMULATE',
    'ULTRON_RAYCI_SIM_PATTERN',
    'ULTRON_RAYCI_SIM_CAPTURE_PIXEL_FORMAT',
    'ULTRON_RAYCI_IDENTITY_STYLE',
    'ULTRON_RAYCI_LIST_SERIAL_STYLE',
    'ULTRON_RAYCI_ALLOW_DUPLICATE_CAMERA_ROWS',
    'ULTRON_RAYCI_EXPOSE_CAPTURED_ALIASES',
    'ULTRON_RAYCI_EXPOSE_REVERSE_ALIASES',
    'ULTRON_RAYCI_EXPOSE_EXTENDED_CAMERA_KEYS',
    'ULTRON_RAYCI_EXPOSE_MODEL_ALIASES',
    'ULTRON_RAYCI_EXPOSE_VERBOSE_CAMERA_METADATA',
    'BEAMMIC_DAHENG_SIMULATE',
    'BEAMMIC_DAHENG_AUTO_SIMULATE',
    'BEAMMIC_DAHENG_SIM_PATTERN',
    'BEAMMIC_DAHENG_SN',
    'VIRTUAL_UEYE_DAHENG_SN',
    'VIRTUAL_UEYE_DAHENG_PIXEL_FORMAT',
    'VIRTUAL_UEYE_DAHENG_FPS',
    'BEAMMIC_DAHENG_FPS'
)

foreach ($name in $realCameraEnvironmentNames) {
    Remove-Item ("Env:{0}" -f $name) -ErrorAction SilentlyContinue
}

$env:BEAMMIC_DAHENG_PIXEL_FORMAT = 'Mono10'
$env:BEAMMIC_DAHENG_REVERSE_X = '0'
$env:BEAMMIC_DAHENG_REVERSE_Y = '0'
$env:BEAMMIC_DAHENG_GAIN_DB = '8'
$env:ULTRON_RAYCI_IDENTIFICATION_EXTEND = '1'
$portableHelperExe = Join-Path $portableRoot 'DahengBridgeHelper\DahengFrameServer.exe'
$env:ULTRON_RAYCI_BRIDGE_HELPER = $portableHelperExe
Stop-ExistingDahengHelpers -AllowedExecutablePath $portableHelperExe

Write-Host "Launching RayCi bridge from: $rayciExe"
Write-Host "Camera mode: single Daheng MER-130-30UM* bridge"
Write-Host "Camera model prefix: MER-130-30UM"
Write-Host "Pixel format: Mono10"
Write-Host "Frame size: 1280x1024"
Write-Host "Helper path: $portableHelperExe"
$null = Start-Process -FilePath $rayciExe -WorkingDirectory $portableRoot -PassThru
$title = Wait-ForRayCiWindow -TimeoutSec $StartupTimeoutSec
Write-Host "RayCi main window: $title"
Start-Sleep -Milliseconds $PostLaunchDelayMs

Invoke-RayCiButton -Name 'Live Mode' -DelayMs $PostInvokeDelayMs
Write-Host 'Invoked Live Mode.'

if ($FinalizeOpenLiveMode) {
    $selectionReady = Wait-RayCiSelectionReady -TimeoutSec $SelectionTimeoutSec
    if ($null -ne $selectionReady) {
        Write-Host 'Selection rows are ready.'

        Select-RayCiItem -Element $selectionReady.CameraRow -Label 'CinCam CMOS 1201 EL'
        Select-RayCiItem -Element $selectionReady.AccessoryRow -Label 'plain'
        Start-Sleep -Milliseconds 500

        $openLiveModeButton = Find-RayCiElement `
            -Windows @(Get-RayCiWindows) `
            -Name 'Open LiveMode' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::Button.ProgrammaticName)
        $openButton = Find-RayCiElement `
            -Windows @(Get-RayCiWindows) `
            -Name 'Open' `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::Button.ProgrammaticName)

        if ($null -ne $openLiveModeButton -and (Get-SafeValue { $openLiveModeButton.Current.IsEnabled } $false)) {
            $invokePattern = Get-SafeValue {
                $openLiveModeButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            } $null

            if ($null -ne $invokePattern) {
                $invokePattern.Invoke()
                Start-Sleep -Milliseconds $PostInvokeDelayMs
                Write-Host 'Invoked Open LiveMode.'
            } else {
                Write-Warning 'Open LiveMode button does not support InvokePattern.'
            }
        } else {
            $openLiveModeReady = $null -ne $openLiveModeButton
            $plainOpenReady = $null -ne $openButton -and (Get-SafeValue { $openButton.Current.IsEnabled } $false)
            $stateLabel = if ($openLiveModeReady) { 'present but disabled' } else { 'not found' }

            if ($plainOpenReady) {
                Write-Warning "Open LiveMode was $stateLabel after selection rows appeared. Refusing to invoke plain Open because it contaminates verification with the file dialog."
            } else {
                Write-Warning "Open LiveMode was $stateLabel after selection rows appeared."
            }
        }
    } else {
        Write-Warning "Timed out waiting for selection rows within $SelectionTimeoutSec seconds."
    }
}

if ($Verify) {
    $controlDumpPath = "$dumpPrefixResolved.control.txt"
    $controlJsonPath = "$dumpPrefixResolved.control.json"
    $dumpScript = Join-Path $workspaceRoot 'dump-rayci-controls.ps1'

    $summary = Wait-RayCiAcceptedStatus `
        -DumpScript $dumpScript `
        -ControlDumpPath $controlDumpPath `
        -ControlJsonPath $controlJsonPath `
        -TimeoutSec 30.0

    Write-Host ("Accepted: {0}" -f $summary.Accepted)
    if ($summary.LiveModeTitle) {
        Write-Host ("LiveMode: {0}" -f $summary.LiveModeTitle.Trim())
    }
    if ($summary.LiveViewTitle) {
        Write-Host ("LiveView: {0}" -f $summary.LiveViewTitle.Trim())
    }
    if ($summary.CameraRow) {
        Write-Host ("Camera  : {0}" -f $summary.CameraRow.Trim())
    }
    if ($summary.Connection) {
        Write-Host ("Connect : {0}" -f $summary.Connection.Trim())
    }
    if ($summary.StreamStatus) {
        Write-Host ("Status  : {0}" -f $summary.StreamStatus.Trim())
    }
    if ($summary.LicenseLine) {
        Write-Host ("License : {0}" -f $summary.LicenseLine.Trim())
    }
    Write-Host ("Dump    : {0}" -f $controlDumpPath)

    if (-not $summary.Accepted) {
        throw "RayCi did not enter the accepted Live Mode state."
    }
}
