param(
    [ValidateSet(
        'status',
        'exp_auto_off',
        'exp_auto_on',
        'exp_150ms',
        'exp_300ms',
        'gain_auto_off',
        'gain_auto_on',
        'gain_0db',
        'gain_4db',
        'gain_8db',
        'gain_12db',
        'gain_16db',
        'frame_rate_auto_off',
        'frame_rate_auto_on',
        'reduce_pixel_clock_off',
        'reduce_pixel_clock_on',
        'baseline_off',
        'baseline_on',
        'live_auto_off',
        'live_auto_on',
        'noop'
    )]
    [string]$Action = 'status',
    [string]$ProcessName = 'RayCi',
    [int]$DelayMs = 500
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

function Get-RayCiWindows {
    param([string]$TargetProcessName)

    $procIds = @(Get-Process -Name $TargetProcessName -ErrorAction Stop | Select-Object -ExpandProperty Id)
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

function Select-TabSequence {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [Parameter(Mandatory = $true)]
        [string[]]$TabNames,
        [Parameter(Mandatory = $true)]
        [int]$DelayMs
    )

    foreach ($tabName in $TabNames) {
        $element = Find-RayCiElement `
            -Windows $Windows `
            -Name $tabName `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::TabItem.ProgrammaticName)

        if ($null -eq $element) {
            throw "Tab '$tabName' not found."
        }

        $pattern = Get-SafeValue {
            $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        } $null

        if ($null -eq $pattern) {
            throw "Tab '$tabName' does not support SelectionItemPattern."
        }

        $pattern.Select()
        Start-Sleep -Milliseconds $DelayMs
        $Windows = @(Get-RayCiWindows -TargetProcessName $ProcessName)
    }
}

function Get-ToggleStateText {
    param($Element)

    $pattern = Get-SafeValue {
        $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    } $null

    if ($null -eq $pattern) {
        return '<no-toggle-pattern>'
    }

    return [string]$pattern.Current.ToggleState
}

function Set-CheckboxState {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId,
        [bool]$DesiredOn,
        [int]$DelayMs
    )

    $element = Find-RayCiElement `
        -Windows $Windows `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::CheckBox.ProgrammaticName)

    if ($null -eq $element) {
        throw "Checkbox '$Name' (AutomationId=$AutomationId) not found."
    }

    if (-not (Get-SafeValue { $element.Current.IsEnabled } $false)) {
        throw "Checkbox '$Name' (AutomationId=$AutomationId) is disabled."
    }

    $pattern = Get-SafeValue {
        $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    } $null

    if ($null -eq $pattern) {
        throw "Checkbox '$Name' (AutomationId=$AutomationId) does not support TogglePattern."
    }

    $before = [string]$pattern.Current.ToggleState
    $beforeOn = $before -eq 'On'

    if ($beforeOn -ne $DesiredOn) {
        $pattern.Toggle()
        Start-Sleep -Milliseconds $DelayMs

        $element = Find-RayCiElement `
            -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) `
            -Name $Name `
            -AutomationId $AutomationId `
            -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::CheckBox.ProgrammaticName)

        $pattern = Get-SafeValue {
            $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        } $null
    }

    $after = if ($null -ne $pattern) { [string]$pattern.Current.ToggleState } else { '<missing-after>' }
    [pscustomobject]@{
        Name         = $Name
        AutomationId = $AutomationId
        Before       = $before
        After        = $after
    }
}

function Get-CheckboxState {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    $element = Find-RayCiElement `
        -Windows $Windows `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::CheckBox.ProgrammaticName)

    if ($null -eq $element) {
        return [pscustomobject]@{
            Name         = $Name
            AutomationId = $AutomationId
            Enabled      = $false
            ToggleState  = '<not-found>'
        }
    }

    [pscustomobject]@{
        Name         = $Name
        AutomationId = $AutomationId
        Enabled      = Get-SafeValue { $element.Current.IsEnabled } $false
        ToggleState  = Get-ToggleStateText -Element $element
    }
}

function Find-RayCiElementByProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetProcessName,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.ControlType]$ControlType
    )

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $procIds = @(Get-Process -Name $TargetProcessName -ErrorAction Stop | Select-Object -ExpandProperty Id)
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name
    )
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType
    )

    foreach ($procId in $procIds) {
        $procCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $procId
        )
        $condition = New-Object System.Windows.Automation.AndCondition(
            @($procCondition, $nameCondition, $typeCondition)
        )
        $match = Get-SafeValue {
            $root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
        } $null

        if ($null -ne $match) {
            return $match
        }
    }

    return $null
}

function Get-ComboSelectionState {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    $element = Find-RayCiElement `
        -Windows $Windows `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName)

    if ($null -eq $element) {
        return [pscustomobject]@{
            Name         = $Name
            AutomationId = $AutomationId
            Enabled      = $false
            ToggleState  = '<not-found>'
        }
    }

    $selectionPattern = Get-SafeValue {
        $element.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    } $null

    $selected = '<no-selection-pattern>'
    if ($null -ne $selectionPattern) {
        $selectedItems = @($selectionPattern.Current.GetSelection())
        $selected = if ($selectedItems.Count -gt 0) {
            ($selectedItems | ForEach-Object {
                Get-SafeValue { $_.Current.Name } '<unknown>'
            }) -join ', '
        } else {
            '<none>'
        }
    }

    [pscustomobject]@{
        Name         = $Name
        AutomationId = $AutomationId
        Enabled      = Get-SafeValue { $element.Current.IsEnabled } $false
        ToggleState  = $selected
    }
}

function Set-ComboSelectionState {
    param(
        [Parameter(Mandatory = $true)]
        [Object[]]$Windows,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId,
        [Parameter(Mandatory = $true)]
        [string]$DesiredItemName,
        [int]$DelayMs
    )

    $element = Find-RayCiElement `
        -Windows $Windows `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName)

    if ($null -eq $element) {
        throw "Combo '$Name' (AutomationId=$AutomationId) not found."
    }

    if (-not (Get-SafeValue { $element.Current.IsEnabled } $false)) {
        throw "Combo '$Name' (AutomationId=$AutomationId) is disabled."
    }

    $selectionPattern = Get-SafeValue {
        $element.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    } $null
    $expandPattern = Get-SafeValue {
        $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    } $null

    if ($null -eq $selectionPattern) {
        throw "Combo '$Name' (AutomationId=$AutomationId) does not support SelectionPattern."
    }

    if ($null -eq $expandPattern) {
        throw "Combo '$Name' (AutomationId=$AutomationId) does not support ExpandCollapsePattern."
    }

    $beforeItems = @($selectionPattern.Current.GetSelection())
    $before = if ($beforeItems.Count -gt 0) {
        ($beforeItems | ForEach-Object {
            Get-SafeValue { $_.Current.Name } '<unknown>'
        }) -join ', '
    } else {
        '<none>'
    }

    if ($before -ne $DesiredItemName) {
        $expandPattern.Expand()
        Start-Sleep -Milliseconds $DelayMs

        $item = Find-RayCiElementByProcess `
            -TargetProcessName $ProcessName `
            -Name $DesiredItemName `
            -ControlType ([System.Windows.Automation.ControlType]::ListItem)

        if ($null -eq $item) {
            throw "Combo item '$DesiredItemName' for '$Name' was not found."
        }

        $itemPattern = Get-SafeValue {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        } $null

        if ($null -eq $itemPattern) {
            throw "Combo item '$DesiredItemName' for '$Name' does not support SelectionItemPattern."
        }

        $itemPattern.Select()
        Start-Sleep -Milliseconds $DelayMs
    }

    $element = Find-RayCiElement `
        -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) `
        -Name $Name `
        -AutomationId $AutomationId `
        -ControlTypeProgrammaticName ([System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName)

    $selectionPattern = Get-SafeValue {
        $element.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    } $null

    $afterItems = if ($null -ne $selectionPattern) {
        @($selectionPattern.Current.GetSelection())
    } else {
        @()
    }
    $after = if ($afterItems.Count -gt 0) {
        ($afterItems | ForEach-Object {
            Get-SafeValue { $_.Current.Name } '<unknown>'
        }) -join ', '
    } else {
        '<none>'
    }

    [pscustomobject]@{
        Name         = $Name
        AutomationId = $AutomationId
        Before       = $before
        After        = $after
    }
}

$windows = @(Get-RayCiWindows -TargetProcessName $ProcessName)
if ($windows.Count -eq 0) {
    throw "RayCi process '$ProcessName' is not running."
}

switch ($Action) {
    'noop' {
        Start-Sleep -Milliseconds $DelayMs
        Write-Output 'noop'
        break
    }
    'status' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        $windows = @(Get-RayCiWindows -TargetProcessName $ProcessName)
        @(
            Get-CheckboxState -Windows $windows -Name 'Auto' -AutomationId '1103'
            Get-ComboSelectionState -Windows $windows -Name 'Exposure Time:' -AutomationId '2057'
            Get-CheckboxState -Windows $windows -Name 'Auto' -AutomationId '1922'
            Get-CheckboxState -Windows $windows -Name 'Auto' -AutomationId '1865'
            Get-CheckboxState -Windows $windows -Name 'Reduce Pixel Clock' -AutomationId '1903'
            Get-CheckboxState -Windows $windows -Name 'Baseline' -AutomationId '32783'
            Get-CheckboxState -Windows $windows -Name 'Auto' -AutomationId '32905'
        ) | Format-Table -AutoSize
        break
    }
    'exp_auto_off' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1103' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'exp_auto_on' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1103' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
    'exp_150ms' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1103' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Exposure Time:' -AutomationId '2057' -DesiredItemName '150 ms' -DelayMs $DelayMs
        break
    }
    'exp_300ms' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1103' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Exposure Time:' -AutomationId '2057' -DesiredItemName '300 ms' -DelayMs $DelayMs
        break
    }
    'gain_auto_off' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'gain_auto_on' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
    'gain_0db' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Gain:' -AutomationId '1229' -DesiredItemName '0 dB ( 1.0x )' -DelayMs $DelayMs
        break
    }
    'gain_4db' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Gain:' -AutomationId '1229' -DesiredItemName '4 dB ( 1.6x )' -DelayMs $DelayMs
        break
    }
    'gain_8db' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Gain:' -AutomationId '1229' -DesiredItemName '8 dB ( 2.5x )' -DelayMs $DelayMs
        break
    }
    'gain_12db' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Gain:' -AutomationId '1229' -DesiredItemName '12 dB ( 4.0x )' -DelayMs $DelayMs
        break
    }
    'gain_16db' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1922' -DesiredOn:$false -DelayMs $DelayMs | Out-Null
        Set-ComboSelectionState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Gain:' -AutomationId '1229' -DesiredItemName '16 dB ( 6.3x )' -DelayMs $DelayMs
        break
    }
    'frame_rate_auto_off' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1865' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'frame_rate_auto_on' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '1865' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
    'reduce_pixel_clock_off' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Reduce Pixel Clock' -AutomationId '1903' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'reduce_pixel_clock_on' {
        Select-TabSequence -Windows $windows -TabNames @('Control') -DelayMs $DelayMs
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Reduce Pixel Clock' -AutomationId '1903' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
    'baseline_off' {
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Baseline' -AutomationId '32783' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'baseline_on' {
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Baseline' -AutomationId '32783' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
    'live_auto_off' {
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '32905' -DesiredOn:$false -DelayMs $DelayMs
        break
    }
    'live_auto_on' {
        Set-CheckboxState -Windows @(Get-RayCiWindows -TargetProcessName $ProcessName) -Name 'Auto' -AutomationId '32905' -DesiredOn:$true -DelayMs $DelayMs
        break
    }
}
