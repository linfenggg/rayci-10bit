param(
    [string]$SdkRoot = "C:\Program Files\Daheng Imaging\GalaxySDK"
)

$ErrorActionPreference = "Stop"

Write-Host "== Camera devices =="
Get-PnpDevice |
    Where-Object {
        $_.FriendlyName -match "^MER-|Galaxy|USB3 Vision Digital Camera|USB2\.0 Digital Camera|Machine Vision" -or
        $_.InstanceId -match "VID_2BA2|VID_4448"
    } |
    Sort-Object FriendlyName, InstanceId |
    Format-Table -AutoSize Status, Class, FriendlyName, InstanceId

Write-Host ""
Write-Host "== GxIAPI.dll versions =="
$dllPaths = @(
    (Join-Path $SdkRoot "APIDll\Win64\GxIAPI.dll"),
    "C:\Windows\System32\GxIAPI.dll"
)

@(
    foreach ($dllPath in $dllPaths) {
        if (Test-Path -LiteralPath $dllPath) {
            $item = Get-Item $dllPath
            [PSCustomObject]@{
                Path           = $dllPath
                FileVersion    = $item.VersionInfo.FileVersion
                ProductVersion = $item.VersionInfo.ProductVersion
            }
        } else {
            [PSCustomObject]@{
                Path           = $dllPath
                FileVersion    = "<missing>"
                ProductVersion = "<missing>"
            }
        }
    }
) | Format-Table -AutoSize
