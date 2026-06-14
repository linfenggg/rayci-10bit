[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = 'HKCU:\Software\CINOGY\Calibrations\Camera'
$calibrationName = 'CinCam CMOS 1201 EL'
$legacyName = $calibrationName
$registryModel = 'uEye UI-1542LE-M'
$registryShortModel = 'UI-1542LE-M'
$ueyeApiFullModel = 'uEye UI-154xLE Series'
$ueyeApiShortModel = 'UI-1545LE-M'
$displaySerial = '1201EL-U2-1022-0034'
$displaySerialShort = '10220034'
$serialTemplate = '1201EL-U2-{KW:2}{Year:2}-{Number:4}'
$exposureTimes = '300,450,700,1000,1500,2000,2500,3000,3500,4000,4500,5000,6000,7000,8000,9000,10000,12000,14000,16000,18000,20000,22500,25000,27500,30000,32500,35000,37500,40000,42500,45000,47500,50000,55000,60000,65000,70000,75000,80000,85000,90000,95000,100000,110000,120000,130000,140000,150000,160000,170000,180000,190000,200000,225000,250000,275000,300000'
$gain = '1,1.584893192,2.511886432,3.981071706,6.30957344480193'
$sensorKeyName = '00000028'
$listedSerialKeyName = '{0:X8}' -f [uint32]$displaySerialShort
$listedFullCameraKeyName = '{0}{1}' -f $sensorKeyName, $listedSerialKeyName
$baseCalibrationName = 'CinCam CMOS 1201'

function Ensure-Key {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }
}

function Reset-Key {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ($Path -notlike "$root*") {
        throw "Refusing to reset unexpected registry path: $Path"
    }

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Set-StringValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
}

function Set-CommonCameraBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$EquipmentName,
        [Parameter(Mandatory = $true)][bool]$IncludeIdentity,
        [string]$CameraSerialValue = $displaySerialShort,
        [string]$GuidLowValue = $listedSerialKeyName
    )

    Set-StringValue -Path $Path -Name 'Camera Group' -Value 'CinCam CMOS'
    Set-StringValue -Path $Path -Name 'Technology' -Value 'CMOS'
    Set-StringValue -Path $Path -Name 'Triggering' -Value '0'
    Set-StringValue -Path $Path -Name 'BufferCnt' -Value '4'
    Set-StringValue -Path $Path -Name 'BitDepth' -Value '10'
    Set-StringValue -Path $Path -Name 'ColorFormat' -Value 'Y16'
    Set-StringValue -Path $Path -Name 'CameraMode' -Value '0'
    Set-StringValue -Path $Path -Name 'Low Noise Binning' -Value '0'
    Set-StringValue -Path $Path -Name 'Dual-Tap' -Value '0'
    Set-StringValue -Path $Path -Name 'Four-Tap' -Value '0'
    Set-StringValue -Path $Path -Name 'PixelSizeX' -Value '5.2'
    Set-StringValue -Path $Path -Name 'PixelSizeY' -Value '5.2'
    Set-StringValue -Path $Path -Name 'DeInterlace' -Value '0'
    Set-StringValue -Path $Path -Name 'AOI X0' -Value '0'
    Set-StringValue -Path $Path -Name 'AOI Y0' -Value '0'
    Set-StringValue -Path $Path -Name 'AOI Width' -Value '1280'
    Set-StringValue -Path $Path -Name 'AOI Height' -Value '1024'
    Set-StringValue -Path $Path -Name 'Crop Left' -Value '0'
    Set-StringValue -Path $Path -Name 'Crop Right' -Value '0'
    Set-StringValue -Path $Path -Name 'Crop Top' -Value '0'
    Set-StringValue -Path $Path -Name 'Crop Bottom' -Value '0'
    Set-StringValue -Path $Path -Name 'FrameRate' -Value '30'
    Set-StringValue -Path $Path -Name 'FrameRate_2x2' -Value '30'
    Set-StringValue -Path $Path -Name 'FrameRate_4x4' -Value '30'
    Set-StringValue -Path $Path -Name 'FrameRate_8x8' -Value '30'
    Set-StringValue -Path $Path -Name 'Bandwidth' -Value '480'
    Set-StringValue -Path $Path -Name 'PixelClock' -Value '34'
    Set-StringValue -Path $Path -Name 'PixelClock_2x2' -Value '34'
    Set-StringValue -Path $Path -Name 'PixelClock_4x4' -Value '34'
    Set-StringValue -Path $Path -Name 'PixelClock_8x8' -Value '34'
    Set-StringValue -Path $Path -Name 'AutoExposure Min' -Value '0.5'
    Set-StringValue -Path $Path -Name 'AutoExposure Max' -Value '0.9'
    Set-StringValue -Path $Path -Name 'Gain Fak' -Value '1'
    Set-StringValue -Path $Path -Name 'Gain Factor' -Value '100'
    Set-StringValue -Path $Path -Name 'Gain Val' -Value '0'
    Set-StringValue -Path $Path -Name 'Brightness Fak' -Value '0.003408715 * 1.25'
    Set-StringValue -Path $Path -Name 'Brightness Factor' -Value '0.003408715 * 1.25'
    Set-StringValue -Path $Path -Name 'Brightness Offset' -Value '13'
    Set-StringValue -Path $Path -Name 'Brightness Val' -Value '20'
    Set-StringValue -Path $Path -Name 'Exposure Time' -Value '30000'
    Set-StringValue -Path $Path -Name 'Time Offset' -Value '0'
    Set-StringValue -Path $Path -Name 'Gamma' -Value '1'
    Set-StringValue -Path $Path -Name 'LUT Exp' -Value '0'
    Set-StringValue -Path $Path -Name 'HDR Enable' -Value '0'
    Set-StringValue -Path $Path -Name 'List Index' -Value '0'
    Set-StringValue -Path $Path -Name 'Equipment' -Value $EquipmentName
    Set-StringValue -Path $Path -Name 'Name' -Value $registryModel

    if (-not $IncludeIdentity) {
        return
    }

    Set-StringValue -Path $Path -Name 'Device Serial' -Value $displaySerial
    Set-StringValue -Path $Path -Name 'Device Serial Short' -Value $displaySerialShort
    Set-StringValue -Path $Path -Name 'Camera Serial' -Value $CameraSerialValue
    Set-StringValue -Path $Path -Name 'Camera-Name' -Value $EquipmentName
    Set-StringValue -Path $Path -Name 'Camera Name' -Value $EquipmentName
    Set-StringValue -Path $Path -Name 'GUID High' -Value $sensorKeyName
    Set-StringValue -Path $Path -Name 'GUID Low' -Value $GuidLowValue
}

function Seed-Equipment {
    param(
        [Parameter(Mandatory = $true)][string]$CameraKeyPath,
        [Parameter(Mandatory = $true)][string]$EquipmentName,
        [Parameter(Mandatory = $true)][bool]$IncludePerCameraMetadata,
        [string]$CameraSerialValue = $displaySerialShort,
        [string]$GuidLowValue = $listedSerialKeyName
    )

    $equipmentPath = Join-Path $CameraKeyPath $EquipmentName
    Ensure-Key -Path $equipmentPath

    Set-StringValue -Path $equipmentPath -Name 'Icon' -Value ($(if ($EquipmentName -eq $calibrationName) { 'CinCam CMOS' } else { $registryModel }))
    Set-StringValue -Path $equipmentPath -Name 'Serial Number Template' -Value $serialTemplate
    Set-StringValue -Path $equipmentPath -Name 'Device Serial' -Value $displaySerial
    Set-StringValue -Path $equipmentPath -Name 'Device Serial Short' -Value $displaySerialShort
    Set-StringValue -Path $equipmentPath -Name 'Calibration ID' -Value 'plain'

    $plainPath = Join-Path $equipmentPath 'plain'
    Ensure-Key -Path $plainPath
    Set-StringValue -Path $plainPath -Name 'AOI CenterX' -Value '0'
    Set-StringValue -Path $plainPath -Name 'AOI CenterY' -Value '0'
    Set-StringValue -Path $plainPath -Name 'AOI RadiusX' -Value '2.6'
    Set-StringValue -Path $plainPath -Name 'AOI RadiusY' -Value '2.6'
    Set-StringValue -Path $plainPath -Name 'Exposure Times' -Value $exposureTimes
    Set-StringValue -Path $plainPath -Name 'Gain' -Value $gain
    Set-StringValue -Path $plainPath -Name 'MirrorY' -Value '1'
    Set-StringValue -Path $plainPath -Name 'ScaleX' -Value '1'
    Set-StringValue -Path $plainPath -Name 'ScaleY' -Value '1'
    Set-StringValue -Path $plainPath -Name 'Wavelength Max' -Value '1150'
    Set-StringValue -Path $plainPath -Name 'Wavelength Min' -Value '350'

    if (-not $IncludePerCameraMetadata) {
        return
    }

    $cameraInfoPath = Join-Path $equipmentPath '~Camera'
    Ensure-Key -Path $cameraInfoPath
    Set-CommonCameraBlock `
        -Path $cameraInfoPath `
        -EquipmentName $EquipmentName `
        -IncludeIdentity $true `
        -CameraSerialValue $CameraSerialValue `
        -GuidLowValue $GuidLowValue
    Set-StringValue -Path $cameraInfoPath -Name 'Calibration ID' -Value 'plain'
}

function Seed-CameraKey {
    param(
        [Parameter(Mandatory = $true)][string]$KeyName,
        [Parameter(Mandatory = $true)][bool]$IncludePerCameraMetadata,
        [string]$CameraSerialValue = $displaySerialShort,
        [string]$GuidLowValue = $listedSerialKeyName
    )

    $cameraPath = Join-Path $root $KeyName
    Ensure-Key -Path $cameraPath

    Set-StringValue -Path $cameraPath -Name 'AutoExposure Max' -Value '0.9'
    Set-StringValue -Path $cameraPath -Name 'AutoExposure Min' -Value '0.5'
    Set-StringValue -Path $cameraPath -Name 'BitDepth' -Value '10'
    Set-StringValue -Path $cameraPath -Name 'Brightness Factor' -Value '0.003408715 * 1.25'
    Set-StringValue -Path $cameraPath -Name 'Brightness Offset' -Value '13'
    Set-StringValue -Path $cameraPath -Name 'Brightness Val' -Value '20'
    Set-StringValue -Path $cameraPath -Name 'BufferCnt' -Value '4'
    Set-StringValue -Path $cameraPath -Name 'Camera Group' -Value 'CinCam CMOS'
    Set-StringValue -Path $cameraPath -Name 'CameraMode' -Value '0'
    Set-StringValue -Path $cameraPath -Name 'Crop Bottom' -Value '0'
    Set-StringValue -Path $cameraPath -Name 'Crop Top' -Value '0'
    Set-StringValue -Path $cameraPath -Name 'Equipment' -Value $calibrationName
    Set-StringValue -Path $cameraPath -Name 'FrameRate' -Value '30'
    Set-StringValue -Path $cameraPath -Name 'Name' -Value $registryModel
    Set-StringValue -Path $cameraPath -Name 'PixelClock' -Value '34'
    Set-StringValue -Path $cameraPath -Name 'Technology' -Value 'CMOS'
    Set-StringValue -Path $cameraPath -Name 'Triggering' -Value '0'
    Set-CommonCameraBlock `
        -Path $cameraPath `
        -EquipmentName $calibrationName `
        -IncludeIdentity $IncludePerCameraMetadata `
        -CameraSerialValue $CameraSerialValue `
        -GuidLowValue $GuidLowValue

    if ($IncludePerCameraMetadata) {
        Set-StringValue -Path $cameraPath -Name 'Device Serial' -Value $displaySerial
        Set-StringValue -Path $cameraPath -Name 'Device Serial Short' -Value $displaySerialShort
        Set-StringValue -Path $cameraPath -Name 'Calibration ID' -Value 'plain'
    }

    if ($KeyName -eq $sensorKeyName) {
        $basePath = Join-Path $cameraPath $baseCalibrationName
        Ensure-Key -Path $basePath
        $basePlainPath = Join-Path $basePath 'plain'
        Ensure-Key -Path $basePlainPath
        Set-StringValue -Path $basePlainPath -Name 'AOI CenterX' -Value '0'
        Set-StringValue -Path $basePlainPath -Name 'AOI CenterY' -Value '0'
        Set-StringValue -Path $basePlainPath -Name 'AOI RadiusX' -Value '2.6'
        Set-StringValue -Path $basePlainPath -Name 'AOI RadiusY' -Value '2.6'
        Set-StringValue -Path $basePlainPath -Name 'Exposure Times' -Value $exposureTimes
        Set-StringValue -Path $basePlainPath -Name 'Gain' -Value '1'
        Set-StringValue -Path $basePlainPath -Name 'MirrorY' -Value '1'
        Set-StringValue -Path $basePlainPath -Name 'ScaleX' -Value '1'
        Set-StringValue -Path $basePlainPath -Name 'ScaleY' -Value '1'
        Set-StringValue -Path $basePlainPath -Name 'Wavelength Max' -Value '1150'
        Set-StringValue -Path $basePlainPath -Name 'Wavelength Min' -Value '350'
    }

    Seed-Equipment `
        -CameraKeyPath $cameraPath `
        -EquipmentName $calibrationName `
        -IncludePerCameraMetadata:$IncludePerCameraMetadata `
        -CameraSerialValue $CameraSerialValue `
        -GuidLowValue $GuidLowValue
    if ($legacyName -ne $calibrationName) {
        Seed-Equipment `
            -CameraKeyPath $cameraPath `
            -EquipmentName $legacyName `
            -IncludePerCameraMetadata:$IncludePerCameraMetadata `
            -CameraSerialValue $CameraSerialValue `
            -GuidLowValue $GuidLowValue
    }
    if ($registryModel -ne $calibrationName -and $registryModel -ne $legacyName) {
        Seed-Equipment `
            -CameraKeyPath $cameraPath `
            -EquipmentName $registryModel `
            -IncludePerCameraMetadata:$IncludePerCameraMetadata `
            -CameraSerialValue $CameraSerialValue `
            -GuidLowValue $GuidLowValue
    }
    if ($registryShortModel -ne $calibrationName -and $registryShortModel -ne $legacyName -and $registryShortModel -ne $registryModel) {
        Seed-Equipment `
            -CameraKeyPath $cameraPath `
            -EquipmentName $registryShortModel `
            -IncludePerCameraMetadata:$IncludePerCameraMetadata `
            -CameraSerialValue $CameraSerialValue `
            -GuidLowValue $GuidLowValue
    }
    if ($ueyeApiFullModel -ne $calibrationName -and $ueyeApiFullModel -ne $legacyName -and $ueyeApiFullModel -ne $registryModel) {
        Seed-Equipment `
            -CameraKeyPath $cameraPath `
            -EquipmentName $ueyeApiFullModel `
            -IncludePerCameraMetadata:$IncludePerCameraMetadata `
            -CameraSerialValue $CameraSerialValue `
            -GuidLowValue $GuidLowValue
    }
    if ($ueyeApiShortModel -ne $calibrationName -and $ueyeApiShortModel -ne $legacyName -and $ueyeApiShortModel -ne $registryModel -and $ueyeApiShortModel -ne $registryShortModel -and $ueyeApiShortModel -ne $ueyeApiFullModel) {
        Seed-Equipment `
            -CameraKeyPath $cameraPath `
            -EquipmentName $ueyeApiShortModel `
            -IncludePerCameraMetadata:$IncludePerCameraMetadata `
            -CameraSerialValue $CameraSerialValue `
            -GuidLowValue $GuidLowValue
    }
}

Ensure-Key -Path $root

$aliases = @(
    @{ Name = $sensorKeyName; Include = $false; CameraSerial = $displaySerialShort; GuidLow = $listedSerialKeyName },
    @{ Name = $displaySerialShort; Include = $true; CameraSerial = $displaySerialShort; GuidLow = $listedSerialKeyName },
    @{ Name = $listedSerialKeyName; Include = $true; CameraSerial = $displaySerialShort; GuidLow = $listedSerialKeyName },
    @{ Name = $listedFullCameraKeyName; Include = $true; CameraSerial = $displaySerialShort; GuidLow = $listedSerialKeyName }
)

foreach ($alias in $aliases) {
    Reset-Key -Path (Join-Path $root $alias.Name)
    Seed-CameraKey `
        -KeyName $alias.Name `
        -IncludePerCameraMetadata:$alias.Include `
        -CameraSerialValue $alias.CameraSerial `
        -GuidLowValue $alias.GuidLow
}

Write-Host "Seeded single-camera RayCi calibration registry under $root"
