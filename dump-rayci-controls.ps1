param(
    [string]$ProcessName = "RayCi",
    [string]$TitleContains,
    [ValidateSet("Control", "Content", "Raw")]
    [string]$View = "Control",
    [int]$MaxDepth = 8,
    [string[]]$SelectTab = @(),
    [int]$TabSelectDelayMs = 500,
    [switch]$IncludeOffscreen,
    [switch]$IncludePatterns,
    [switch]$ListTopLevelWindows,
    [string]$TreeOut,
    [string]$JsonOut
)

$ErrorActionPreference = "Stop"

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

function Format-Rect {
    param($Rect)

    if ($null -eq $Rect) {
        return $null
    }

    if ($Rect -is [System.Windows.Rect] -and -not $Rect.IsEmpty) {
        return [ordered]@{
            x      = [math]::Round($Rect.X, 2)
            y      = [math]::Round($Rect.Y, 2)
            width  = [math]::Round($Rect.Width, 2)
            height = [math]::Round($Rect.Height, 2)
        }
    }

    return $null
}

function Format-Handle {
    param([int]$NativeWindowHandle)

    if ($NativeWindowHandle -le 0) {
        return $null
    }

    return ("0x{0:X8}" -f $NativeWindowHandle)
}

function Get-ViewWalker {
    param([string]$RequestedView)

    switch ($RequestedView) {
        "Control" { return [System.Windows.Automation.TreeWalker]::ControlViewWalker }
        "Content" { return [System.Windows.Automation.TreeWalker]::ContentViewWalker }
        "Raw"     { return [System.Windows.Automation.TreeWalker]::RawViewWalker }
        default   { throw "Unsupported view: $RequestedView" }
    }
}

function Get-TopLevelWindows {
    param(
        [string]$TargetProcessName,
        [string]$TargetTitleContains
    )

    $processes = @(Get-Process -Name $TargetProcessName -ErrorAction Stop | Sort-Object Id)
    if ($processes.Count -eq 0) {
        throw "No process found for name '$TargetProcessName'."
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $all = @()

    foreach ($process in $processes) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $process.Id
        )

        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        for ($i = 0; $i -lt $windows.Count; $i++) {
            $window = $windows.Item($i)
            $name = Get-SafeValue { $window.Current.Name } ""

            if ($TargetTitleContains -and $name -notlike "*$TargetTitleContains*") {
                continue
            }

            $all += $window
        }
    }

    return @($all)
}

function Get-SupportedPatternNames {
    param($Element)

    $patternNames = @()
    try {
        foreach ($pattern in $Element.GetSupportedPatterns()) {
            $patternNames += $pattern.ProgrammaticName
        }
    } catch {
    }

    return @($patternNames | Sort-Object -Unique)
}

function Find-FirstElementByNameAndControlType {
    param(
        [Parameter(Mandatory = $true)]
        $RootElement,
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.TreeWalker]$Walker,
        [Parameter(Mandatory = $true)]
        [string]$TargetName,
        [Parameter(Mandatory = $true)]
        [string]$TargetControlTypeProgrammaticName
    )

    $currentName = Get-SafeValue { $RootElement.Current.Name } ""
    $currentControlType = Get-SafeValue { $RootElement.Current.ControlType.ProgrammaticName } ""

    if ($currentName -eq $TargetName -and $currentControlType -eq $TargetControlTypeProgrammaticName) {
        return $RootElement
    }

    $child = Get-SafeValue { $Walker.GetFirstChild($RootElement) } $null
    while ($null -ne $child) {
        $match = Find-FirstElementByNameAndControlType `
            -RootElement $child `
            -Walker $Walker `
            -TargetName $TargetName `
            -TargetControlTypeProgrammaticName $TargetControlTypeProgrammaticName

        if ($null -ne $match) {
            return $match
        }

        $child = Get-SafeValue { $Walker.GetNextSibling($child) } $null
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

    $searchWalker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $tabType = [System.Windows.Automation.ControlType]::TabItem.ProgrammaticName

    foreach ($tabName in $TabNames) {
        $selected = $false

        foreach ($window in $Windows) {
            $tabElement = Find-FirstElementByNameAndControlType `
                -RootElement $window `
                -Walker $searchWalker `
                -TargetName $tabName `
                -TargetControlTypeProgrammaticName $tabType

            if ($null -eq $tabElement) {
                continue
            }

            $pattern = Get-SafeValue {
                $tabElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            } $null

            if ($null -eq $pattern) {
                continue
            }

            $pattern.Select()
            Start-Sleep -Milliseconds $DelayMs
            $selected = $true
            break
        }

        if (-not $selected) {
            throw "Tab '$tabName' was not found or cannot be selected."
        }
    }
}

function Get-ElementNode {
    param(
        [Parameter(Mandatory = $true)]
        $Element,
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.TreeWalker]$Walker,
        [Parameter(Mandatory = $true)]
        [int]$Depth,
        [Parameter(Mandatory = $true)]
        [int]$MaxDepthLimit,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$KeepOffscreen,
        [switch]$AddPatterns
    )

    $name = Get-SafeValue { $Element.Current.Name } ""
    $automationId = Get-SafeValue { $Element.Current.AutomationId } ""
    $className = Get-SafeValue { $Element.Current.ClassName } ""
    $controlTypeProgrammatic = Get-SafeValue { $Element.Current.ControlType.ProgrammaticName } ""
    $localizedControlType = Get-SafeValue { $Element.Current.LocalizedControlType } ""
    $frameworkId = Get-SafeValue { $Element.Current.FrameworkId } ""
    $processId = Get-SafeValue { $Element.Current.ProcessId } 0
    $nativeWindowHandle = Get-SafeValue { $Element.Current.NativeWindowHandle } 0
    $isEnabled = Get-SafeValue { $Element.Current.IsEnabled } $false
    $isOffscreen = Get-SafeValue { $Element.Current.IsOffscreen } $false
    $isKeyboardFocusable = Get-SafeValue { $Element.Current.IsKeyboardFocusable } $false
    $hasKeyboardFocus = Get-SafeValue { $Element.Current.HasKeyboardFocus } $false
    $helpText = Get-SafeValue { $Element.Current.HelpText } ""
    $itemType = Get-SafeValue { $Element.Current.ItemType } ""
    $rect = Format-Rect (Get-SafeValue { $Element.Current.BoundingRectangle } $null)

    $node = [ordered]@{
        path                = $Path
        depth               = $Depth
        name                = $name
        automationId        = $automationId
        className           = $className
        controlType         = $controlTypeProgrammatic
        localizedControlType = $localizedControlType
        frameworkId         = $frameworkId
        processId           = $processId
        nativeWindowHandle  = $nativeWindowHandle
        nativeWindowHandleHex = (Format-Handle $nativeWindowHandle)
        isEnabled           = $isEnabled
        isOffscreen         = $isOffscreen
        isKeyboardFocusable = $isKeyboardFocusable
        hasKeyboardFocus    = $hasKeyboardFocus
        helpText            = $helpText
        itemType            = $itemType
        boundingRectangle   = $rect
        children            = @()
    }

    if ($AddPatterns) {
        $node.patterns = @(Get-SupportedPatternNames -Element $Element)
    }

    if ($Depth -ge $MaxDepthLimit) {
        return [pscustomobject]$node
    }

    $children = @()
    $childIndex = 0
    $child = Get-SafeValue { $Walker.GetFirstChild($Element) } $null
    while ($null -ne $child) {
        $childOffscreen = Get-SafeValue { $child.Current.IsOffscreen } $false
        if ($KeepOffscreen -or -not $childOffscreen) {
            $children += Get-ElementNode `
                -Element $child `
                -Walker $Walker `
                -Depth ($Depth + 1) `
                -MaxDepthLimit $MaxDepthLimit `
                -Path "$Path/$childIndex" `
                -KeepOffscreen:$KeepOffscreen `
                -AddPatterns:$AddPatterns
        }

        $childIndex += 1
        $child = Get-SafeValue { $Walker.GetNextSibling($child) } $null
    }

    $node.children = @($children)
    return [pscustomobject]$node
}

function Get-TreeLines {
    param(
        [Parameter(Mandatory = $true)]
        $Node
    )

    $indent = ("  " * [int]$Node.depth)
    $parts = @(
        "$indent[$($Node.path)]"
        "name=""$($Node.name)"""
        "type=$($Node.controlType)"
    )

    if ($Node.localizedControlType) {
        $parts += "localized=""$($Node.localizedControlType)"""
    }

    if ($Node.automationId) {
        $parts += "id=""$($Node.automationId)"""
    }

    if ($Node.className) {
        $parts += "class=""$($Node.className)"""
    }

    if ($Node.nativeWindowHandleHex) {
        $parts += "hwnd=$($Node.nativeWindowHandleHex)"
    }

    $parts += "enabled=$($Node.isEnabled)"
    $parts += "offscreen=$($Node.isOffscreen)"

    if ($Node.boundingRectangle) {
        $r = $Node.boundingRectangle
        $parts += "rect=($($r.x),$($r.y),$($r.width),$($r.height))"
    }

    if ($Node.patterns -and $Node.patterns.Count -gt 0) {
        $parts += "patterns=$([string]::Join(',', $Node.patterns))"
    }

    $lines = @([string]::Join(" ", $parts))
    foreach ($child in @($Node.children)) {
        $lines += Get-TreeLines -Node $child
    }

    return @($lines)
}

$windows = @(Get-TopLevelWindows -TargetProcessName $ProcessName -TargetTitleContains $TitleContains)
if ($windows.Count -eq 0) {
    $suffix = if ($TitleContains) { " with title containing '$TitleContains'" } else { "" }
    throw "No top-level RayCi windows found for process '$ProcessName'$suffix."
}

$windowSummaries = @()
for ($i = 0; $i -lt $windows.Count; $i++) {
    $window = $windows[$i]
    $windowSummaries += [pscustomobject]@{
        index              = $i
        name               = Get-SafeValue { $window.Current.Name } ""
        className          = Get-SafeValue { $window.Current.ClassName } ""
        automationId       = Get-SafeValue { $window.Current.AutomationId } ""
        nativeWindowHandle = Get-SafeValue { $window.Current.NativeWindowHandle } 0
        nativeWindowHandleHex = (Format-Handle (Get-SafeValue { $window.Current.NativeWindowHandle } 0))
        processId          = Get-SafeValue { $window.Current.ProcessId } 0
    }
}

if ($ListTopLevelWindows) {
    $windowSummaries | Format-Table -AutoSize
    return
}

if ($SelectTab.Count -gt 0) {
    Select-TabSequence -Windows $windows -TabNames $SelectTab -DelayMs $TabSelectDelayMs
    $windows = @(Get-TopLevelWindows -TargetProcessName $ProcessName -TargetTitleContains $TitleContains)
}

$walker = Get-ViewWalker -RequestedView $View
$dump = [ordered]@{
    generatedAt = (Get-Date).ToString("s")
    processName = $ProcessName
    titleContains = $TitleContains
    view        = $View
    maxDepth    = $MaxDepth
    windows     = @()
}

for ($i = 0; $i -lt $windows.Count; $i++) {
    $windowNode = Get-ElementNode `
        -Element $windows[$i] `
        -Walker $walker `
        -Depth 0 `
        -MaxDepthLimit $MaxDepth `
        -Path "$i" `
        -KeepOffscreen:$IncludeOffscreen `
        -AddPatterns:$IncludePatterns

    $dump.windows += $windowNode
}

$treeLines = @(
    "RayCi control dump"
    "generatedAt=$($dump.generatedAt)"
    "processName=$($dump.processName)"
    "titleContains=$($dump.titleContains)"
    "view=$($dump.view)"
    "maxDepth=$($dump.maxDepth)"
    ""
)

foreach ($windowNode in @($dump.windows)) {
    $treeLines += Get-TreeLines -Node $windowNode
}

$treeText = [string]::Join([Environment]::NewLine, $treeLines)
$jsonText = ([pscustomobject]$dump | ConvertTo-Json -Depth 100)

if ($TreeOut) {
    $treeDir = Split-Path -Parent $TreeOut
    if ($treeDir) {
        New-Item -ItemType Directory -Force -Path $treeDir | Out-Null
    }
    Set-Content -LiteralPath $TreeOut -Value $treeText -Encoding UTF8
}

if ($JsonOut) {
    $jsonDir = Split-Path -Parent $JsonOut
    if ($jsonDir) {
        New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
    }
    Set-Content -LiteralPath $JsonOut -Value $jsonText -Encoding UTF8
}

Write-Output $treeText
