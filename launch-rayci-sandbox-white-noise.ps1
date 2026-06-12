[CmdletBinding()]
param(
    [ValidateSet('white-noise', 'beam-target')]
    [string]$Pattern = 'white-noise',
    [switch]$CloseExisting,
    [switch]$Verify,
    [switch]$RebuildHelper
)

$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$prepareScript = Join-Path $workspaceRoot 'prepare-rayci-sandbox-compatible-portable.ps1'
$launchScript = Join-Path $workspaceRoot 'launch-rayci-ueye-white-noise.ps1'
$portableRoot = Join-Path $workspaceRoot 'dist\RayCi64Lite-SandboxCompatBridge'

& $prepareScript -RebuildHelper:$RebuildHelper

& $launchScript `
    -Pattern $Pattern `
    -PortableRoot $portableRoot `
    -CloseExisting:$CloseExisting `
    -Verify:$Verify `
    -DumpPrefix (Join-Path $workspaceRoot 'tmp_sandbox_compat')
