[CmdletBinding()]
param(
    [ValidateSet('white-noise', 'beam-target')]
    [string]$Pattern = 'white-noise',
    [ValidateSet('captured', 'registry')]
    [string]$IdentityStyle = 'registry',
    [ValidateSet('captured', 'licensed')]
    [string]$ListSerialStyle = 'licensed',
    [switch]$CloseExisting,
    [switch]$AllowDuplicateCameraRows,
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

    $statusAccepted =
        ($null -ne $liveModeLine) -and
        ($liveModeLine -notlike '*not connected*') -and
        ($null -ne $connectionLine) -and
        ($connectionLine -notlike '*not connected*') -and
        ($null -ne $statusLine) -and
        ($statusLine -like '*bpp*') -and
        ($statusLine -notlike '*0bpp*')

    $liveViewAccepted =
        ($null -ne $liveModeLine) -and
        ($liveModeLine -notlike '*not connected*') -and
        ($null -ne $liveViewLine)

    $accepted =
        ($statusAccepted -or $liveViewAccepted) -and
        ($null -eq $licenseLine)

    return [pscustomobject]@{
        Accepted      = $accepted
        LiveModeTitle = $liveModeLine
        LiveViewTitle = $liveViewLine
        CameraRow     = $cameraLine
        Connection    = $connectionLine
        StreamStatus  = $statusLine
        LicenseLine   = $licenseLine
    }
}

$workspaceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$portableRoot = if ([string]::IsNullOrWhiteSpace($PortableRoot)) {
    Join-Path $workspaceRoot 'dist\RayCi64Lite-HybridBridge-final'
} else {
    [System.IO.Path]::GetFullPath($PortableRoot)
}
$rayciExe = Join-Path $portableRoot 'RayCi.exe'
$prepareScript = Join-Path $workspaceRoot 'prepare-rayci-bridge-portable.ps1'
$dumpPrefixResolved = if ([string]::IsNullOrWhiteSpace($DumpPrefix)) {
    Join-Path $workspaceRoot 'result_live_verify'
} else {
    $DumpPrefix
}

if (-not (Test-Path -LiteralPath $rayciExe)) {
    if (-not (Test-Path -LiteralPath $prepareScript)) {
        throw "Portable RayCi bridge is missing and prepare-rayci-bridge-portable.ps1 was not found."
    }

    & $prepareScript
}

if ($CloseExisting) {
    Stop-ExistingRayCi
}

$env:ULTRON_RAYCI_SIMULATE = '1'
$env:ULTRON_RAYCI_AUTO_SIMULATE = '1'
$env:ULTRON_RAYCI_SIM_PATTERN = $Pattern
$env:ULTRON_RAYCI_SIM_CAPTURE_PIXEL_FORMAT = '2'
$env:BEAMMIC_DAHENG_START_WIDTH = '1280'
$env:BEAMMIC_DAHENG_START_HEIGHT = '1024'
$env:ULTRON_RAYCI_IDENTITY_STYLE = $IdentityStyle
$env:ULTRON_RAYCI_LIST_SERIAL_STYLE = $ListSerialStyle
if ($AllowDuplicateCameraRows) {
    $env:ULTRON_RAYCI_ALLOW_DUPLICATE_CAMERA_ROWS = '1'
} else {
    Remove-Item Env:ULTRON_RAYCI_ALLOW_DUPLICATE_CAMERA_ROWS -ErrorAction SilentlyContinue
}

Write-Host "Launching RayCi bridge from: $rayciExe"
Write-Host "Simulation pattern: $Pattern"
Write-Host "Simulation capture format: Mono10 in Y16 container"
Write-Host "Simulation size: 1280x1024"
Write-Host "Identity style: $IdentityStyle"
Write-Host "List serial style: $ListSerialStyle"
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

        $openLiveModeButton = $selectionReady.OpenLiveModeButton
        $openButton = $selectionReady.OpenButton

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

    & $dumpScript `
        -ProcessName 'RayCi' `
        -View 'Control' `
        -MaxDepth 6 `
        -IncludePatterns `
        -TreeOut $controlDumpPath `
        -JsonOut $controlJsonPath | Out-Null

    $summary = Get-VerificationSummary -ControlDumpPath $controlDumpPath

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
