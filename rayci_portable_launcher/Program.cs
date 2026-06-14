using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace RayCiPortableLauncher;

internal static class Program
{
    private const string RuntimeSubdirectoryName = "RayCiRuntime";
    private const string RealExeName = "RayCi.exe";
    private const string HelperRelativePath = @"DahengBridgeHelper\DahengFrameServer.exe";
    private const string RegistryRoot = @"Software\CINOGY\Calibrations\Camera";
    private const string BackupRoot = @"Software\Ultron\RayCiPortableLauncher\Backup";
    private const string CalibrationName = "CinCam CMOS 1201 EL";
    private const string LegacyName = CalibrationName;
    private const string RegistryModel = "uEye UI-1542LE-M";
    private const string RegistryShortModel = "UI-1542LE-M";
    private const string UeyeApiFullModel = "uEye UI-154xLE Series";
    private const string UeyeApiShortModel = "UI-1545LE-M";
    private const string DisplaySerial = "1201EL-U2-1022-0034";
    private const string DisplaySerialShort = "10220034";
    private const string SerialTemplate = "1201EL-U2-{KW:2}{Year:2}-{Number:4}";
    private const string ExposureTimes = "300,450,700,1000,1500,2000,2500,3000,3500,4000,4500,5000,6000,7000,8000,9000,10000,12000,14000,16000,18000,20000,22500,25000,27500,30000,32500,35000,37500,40000,42500,45000,47500,50000,55000,60000,65000,70000,75000,80000,85000,90000,95000,100000,110000,120000,130000,140000,150000,160000,170000,180000,190000,200000,225000,250000,275000,300000";
    private const string Gain = "1,1.584893192,2.511886432,3.981071706,6.30957344480193";
    private const string SensorKeyName = "00000028";
    private const string BaseCalibrationName = "CinCam CMOS 1201";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? backupKeyName = null;
        try
        {
            var portableRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var runtimeRoot = Path.Combine(portableRoot, RuntimeSubdirectoryName);
            var realExePath = Path.Combine(runtimeRoot, RealExeName);
            if (!File.Exists(realExePath))
            {
                throw new FileNotFoundException($"Required runtime file was not found: {realExePath}");
            }

            backupKeyName = BackupExistingRegistryState();
            SeedRayCiRegistry();
            StopForeignHelpers(runtimeRoot);

            var startInfo = CreateStartInfo(runtimeRoot, realExePath, args);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("RayCi could not be started.");
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return 1;
        }
        finally
        {
            try
            {
                RestorePreviousRegistryState(backupKeyName);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string runtimeRoot, string realExePath, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = realExePath,
            WorkingDirectory = runtimeRoot,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var variableName in GetVariablesToClear())
        {
            startInfo.Environment.Remove(variableName);
        }

        startInfo.Environment["BEAMMIC_DAHENG_START_WIDTH"] = "1280";
        startInfo.Environment["BEAMMIC_DAHENG_START_HEIGHT"] = "1024";
        startInfo.Environment["BEAMMIC_DAHENG_PIXEL_FORMAT"] = "Mono10";
        startInfo.Environment["BEAMMIC_DAHENG_REVERSE_X"] = "0";
        startInfo.Environment["BEAMMIC_DAHENG_REVERSE_Y"] = "0";
        startInfo.Environment["BEAMMIC_DAHENG_GAIN_DB"] = "8";
        startInfo.Environment["ULTRON_RAYCI_IDENTIFICATION_EXTEND"] = "1";
        startInfo.Environment["ULTRON_RAYCI_BRIDGE_HELPER"] = Path.Combine(runtimeRoot, HelperRelativePath);
        return startInfo;
    }

    private static IEnumerable<string> GetVariablesToClear()
    {
        return
        [
            "ULTRON_RAYCI_SIMULATE",
            "ULTRON_RAYCI_AUTO_SIMULATE",
            "ULTRON_RAYCI_SIM_PATTERN",
            "ULTRON_RAYCI_SIM_CAPTURE_PIXEL_FORMAT",
            "ULTRON_RAYCI_IDENTITY_STYLE",
            "ULTRON_RAYCI_LIST_SERIAL_STYLE",
            "ULTRON_RAYCI_ALLOW_DUPLICATE_CAMERA_ROWS",
            "ULTRON_RAYCI_EXPOSE_CAPTURED_ALIASES",
            "ULTRON_RAYCI_EXPOSE_REVERSE_ALIASES",
            "ULTRON_RAYCI_EXPOSE_EXTENDED_CAMERA_KEYS",
            "ULTRON_RAYCI_EXPOSE_MODEL_ALIASES",
            "ULTRON_RAYCI_EXPOSE_VERBOSE_CAMERA_METADATA",
            "ULTRON_RAYCI_VERBOSE_DEBUG_LOG",
            "ULTRON_RAYCI_VERBOSE_REGISTRY_LOG",
            "BEAMMIC_DAHENG_SIMULATE",
            "BEAMMIC_DAHENG_AUTO_SIMULATE",
            "BEAMMIC_DAHENG_SIM_PATTERN",
            "BEAMMIC_DAHENG_SN",
            "VIRTUAL_UEYE_DAHENG_SN",
            "VIRTUAL_UEYE_DAHENG_PIXEL_FORMAT",
            "VIRTUAL_UEYE_DAHENG_FPS",
            "BEAMMIC_DAHENG_FPS"
        ];
    }

    private static void StopForeignHelpers(string runtimeRoot)
    {
        var allowedHelperPath = Path.GetFullPath(Path.Combine(runtimeRoot, HelperRelativePath));
        foreach (var process in Process.GetProcessesByName("DahengFrameServer"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null &&
                    string.Equals(Path.GetFullPath(path), allowedHelperPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch
            {
                // Best-effort cleanup only.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? BackupExistingRegistryState()
    {
        var backupKeyName = Guid.NewGuid().ToString("N");
        using var currentUser = Registry.CurrentUser;
        using var backupRoot = currentUser.CreateSubKey(BackupRoot, writable: true)
                             ?? throw new InvalidOperationException($"Unable to open backup root HKCU\\{BackupRoot}");
        using var backupKey = backupRoot.CreateSubKey(backupKeyName, writable: true)
                            ?? throw new InvalidOperationException("Unable to create backup key.");
        using var existingRoot = currentUser.OpenSubKey(RegistryRoot, writable: false);

        if (existingRoot is not null)
        {
            using var snapshotKey = backupKey.CreateSubKey("Camera", writable: true)
                                   ?? throw new InvalidOperationException("Unable to create backup snapshot key.");
            CopyRegistryTree(existingRoot, snapshotKey);
            backupKey.SetValue("HasSnapshot", 1, RegistryValueKind.DWord);
        }
        else
        {
            backupKey.SetValue("HasSnapshot", 0, RegistryValueKind.DWord);
        }

        return backupKeyName;
    }

    private static void RestorePreviousRegistryState(string? backupKeyName)
    {
        if (string.IsNullOrWhiteSpace(backupKeyName))
        {
            return;
        }

        using var currentUser = Registry.CurrentUser;
        currentUser.DeleteSubKeyTree(RegistryRoot, throwOnMissingSubKey: false);

        using var backupRoot = currentUser.OpenSubKey(BackupRoot, writable: true);
        using var backupKey = backupRoot?.OpenSubKey(backupKeyName, writable: true);
        var hasSnapshot = Convert.ToInt32(backupKey?.GetValue("HasSnapshot", 0) ?? 0) != 0;
        if (hasSnapshot)
        {
            using var snapshotKey = backupKey?.OpenSubKey("Camera", writable: false);
            if (snapshotKey is not null)
            {
                using var restoredRoot = currentUser.CreateSubKey(RegistryRoot, writable: true)
                                       ?? throw new InvalidOperationException($"Unable to restore HKCU\\{RegistryRoot}");
                CopyRegistryTree(snapshotKey, restoredRoot);
            }
        }

        backupRoot?.DeleteSubKeyTree(backupKeyName, throwOnMissingSubKey: false);
    }

    private static void SeedRayCiRegistry()
    {
        using var root = Registry.CurrentUser.CreateSubKey(RegistryRoot, writable: true)
                         ?? throw new InvalidOperationException($"Unable to open registry root HKCU\\{RegistryRoot}");

        var listedSerialKeyName = $"{uint.Parse(DisplaySerialShort):X8}";
        var listedFullCameraKeyName = $"{SensorKeyName}{listedSerialKeyName}";
        var aliases = new[]
        {
            new CameraAlias(SensorKeyName, false, DisplaySerialShort, listedSerialKeyName),
            new CameraAlias(DisplaySerialShort, true, DisplaySerialShort, listedSerialKeyName),
            new CameraAlias(listedSerialKeyName, true, DisplaySerialShort, listedSerialKeyName),
            new CameraAlias(listedFullCameraKeyName, true, DisplaySerialShort, listedSerialKeyName),
        };

        foreach (var alias in aliases)
        {
            root.DeleteSubKeyTree(alias.Name, throwOnMissingSubKey: false);
            using var cameraKey = root.CreateSubKey(alias.Name, writable: true)
                                  ?? throw new InvalidOperationException($"Unable to create camera key {alias.Name}");
            SeedCameraKey(cameraKey, alias.IncludePerCameraMetadata, alias.CameraSerialValue, alias.GuidLowValue);
        }
    }

    private static void SeedCameraKey(RegistryKey cameraKey, bool includePerCameraMetadata, string cameraSerialValue, string guidLowValue)
    {
        SetCommonCameraBlock(cameraKey, CalibrationName, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        SetStringValue(cameraKey, "AutoExposure Max", "0.9");
        SetStringValue(cameraKey, "AutoExposure Min", "0.5");
        SetStringValue(cameraKey, "Brightness Factor", "0.003408715 * 1.25");
        SetStringValue(cameraKey, "Brightness Offset", "13");
        SetStringValue(cameraKey, "Brightness Val", "20");
        SetStringValue(cameraKey, "Equipment", CalibrationName);
        SetStringValue(cameraKey, "FrameRate", "30");
        SetStringValue(cameraKey, "PixelClock", "34");

        if (includePerCameraMetadata)
        {
            SetStringValue(cameraKey, "Device Serial", DisplaySerial);
            SetStringValue(cameraKey, "Device Serial Short", DisplaySerialShort);
            SetStringValue(cameraKey, "Calibration ID", "plain");
        }

        if (string.Equals(cameraKey.Name?.Split('\\').LastOrDefault(), SensorKeyName, StringComparison.OrdinalIgnoreCase))
        {
            using var basePath = cameraKey.CreateSubKey(BaseCalibrationName, writable: true)
                                ?? throw new InvalidOperationException("Unable to create base calibration key.");
            using var basePlainPath = basePath.CreateSubKey("plain", writable: true)
                                     ?? throw new InvalidOperationException("Unable to create base plain calibration key.");
            SeedPlainCalibration(basePlainPath, "1");
        }

        SeedEquipment(cameraKey, CalibrationName, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        if (!string.Equals(LegacyName, CalibrationName, StringComparison.Ordinal))
        {
            SeedEquipment(cameraKey, LegacyName, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        }

        if (!string.Equals(RegistryModel, CalibrationName, StringComparison.Ordinal) &&
            !string.Equals(RegistryModel, LegacyName, StringComparison.Ordinal))
        {
            SeedEquipment(cameraKey, RegistryModel, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        }

        if (!string.Equals(RegistryShortModel, CalibrationName, StringComparison.Ordinal) &&
            !string.Equals(RegistryShortModel, LegacyName, StringComparison.Ordinal) &&
            !string.Equals(RegistryShortModel, RegistryModel, StringComparison.Ordinal))
        {
            SeedEquipment(cameraKey, RegistryShortModel, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        }

        if (!string.Equals(UeyeApiFullModel, CalibrationName, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiFullModel, LegacyName, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiFullModel, RegistryModel, StringComparison.Ordinal))
        {
            SeedEquipment(cameraKey, UeyeApiFullModel, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        }

        if (!string.Equals(UeyeApiShortModel, CalibrationName, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiShortModel, LegacyName, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiShortModel, RegistryModel, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiShortModel, RegistryShortModel, StringComparison.Ordinal) &&
            !string.Equals(UeyeApiShortModel, UeyeApiFullModel, StringComparison.Ordinal))
        {
            SeedEquipment(cameraKey, UeyeApiShortModel, includePerCameraMetadata, cameraSerialValue, guidLowValue);
        }
    }

    private static void SeedEquipment(RegistryKey cameraKey, string equipmentName, bool includePerCameraMetadata, string cameraSerialValue, string guidLowValue)
    {
        using var equipmentPath = cameraKey.CreateSubKey(equipmentName, writable: true)
                                 ?? throw new InvalidOperationException($"Unable to create equipment key {equipmentName}");
        SetStringValue(equipmentPath, "Icon", equipmentName == CalibrationName ? "CinCam CMOS" : RegistryModel);
        SetStringValue(equipmentPath, "Serial Number Template", SerialTemplate);
        SetStringValue(equipmentPath, "Device Serial", DisplaySerial);
        SetStringValue(equipmentPath, "Device Serial Short", DisplaySerialShort);
        SetStringValue(equipmentPath, "Calibration ID", "plain");

        using var plainPath = equipmentPath.CreateSubKey("plain", writable: true)
                             ?? throw new InvalidOperationException($"Unable to create plain key for {equipmentName}");
        SeedPlainCalibration(plainPath, Gain);

        if (!includePerCameraMetadata)
        {
            return;
        }

        using var cameraInfoPath = equipmentPath.CreateSubKey("~Camera", writable: true)
                                  ?? throw new InvalidOperationException($"Unable to create ~Camera key for {equipmentName}");
        SetCommonCameraBlock(cameraInfoPath, equipmentName, includeIdentity: true, cameraSerialValue, guidLowValue);
        SetStringValue(cameraInfoPath, "Calibration ID", "plain");
    }

    private static void SeedPlainCalibration(RegistryKey plainPath, string gainValue)
    {
        SetStringValue(plainPath, "AOI CenterX", "0");
        SetStringValue(plainPath, "AOI CenterY", "0");
        SetStringValue(plainPath, "AOI RadiusX", "2.6");
        SetStringValue(plainPath, "AOI RadiusY", "2.6");
        SetStringValue(plainPath, "Exposure Times", ExposureTimes);
        SetStringValue(plainPath, "Gain", gainValue);
        SetStringValue(plainPath, "MirrorY", "1");
        SetStringValue(plainPath, "ScaleX", "1");
        SetStringValue(plainPath, "ScaleY", "1");
        SetStringValue(plainPath, "Wavelength Max", "1150");
        SetStringValue(plainPath, "Wavelength Min", "350");
    }

    private static void SetCommonCameraBlock(RegistryKey key, string equipmentName, bool includeIdentity, string cameraSerialValue, string guidLowValue)
    {
        var values = new Dictionary<string, string>
        {
            ["Camera Group"] = "CinCam CMOS",
            ["Technology"] = "CMOS",
            ["Triggering"] = "0",
            ["BufferCnt"] = "4",
            ["BitDepth"] = "10",
            ["ColorFormat"] = "Y16",
            ["CameraMode"] = "0",
            ["Low Noise Binning"] = "0",
            ["Dual-Tap"] = "0",
            ["Four-Tap"] = "0",
            ["PixelSizeX"] = "5.2",
            ["PixelSizeY"] = "5.2",
            ["DeInterlace"] = "0",
            ["AOI X0"] = "0",
            ["AOI Y0"] = "0",
            ["AOI Width"] = "1280",
            ["AOI Height"] = "1024",
            ["Crop Left"] = "0",
            ["Crop Right"] = "0",
            ["Crop Top"] = "0",
            ["Crop Bottom"] = "0",
            ["FrameRate"] = "30",
            ["FrameRate_2x2"] = "30",
            ["FrameRate_4x4"] = "30",
            ["FrameRate_8x8"] = "30",
            ["Bandwidth"] = "480",
            ["PixelClock"] = "34",
            ["PixelClock_2x2"] = "34",
            ["PixelClock_4x4"] = "34",
            ["PixelClock_8x8"] = "34",
            ["AutoExposure Min"] = "0.5",
            ["AutoExposure Max"] = "0.9",
            ["Gain Fak"] = "1",
            ["Gain Factor"] = "100",
            ["Gain Val"] = "0",
            ["Brightness Fak"] = "0.003408715 * 1.25",
            ["Brightness Factor"] = "0.003408715 * 1.25",
            ["Brightness Offset"] = "13",
            ["Brightness Val"] = "20",
            ["Exposure Time"] = "30000",
            ["Time Offset"] = "0",
            ["Gamma"] = "1",
            ["LUT Exp"] = "0",
            ["HDR Enable"] = "0",
            ["List Index"] = "0",
            ["Equipment"] = equipmentName,
            ["Name"] = RegistryModel,
        };

        foreach (var pair in values)
        {
            SetStringValue(key, pair.Key, pair.Value);
        }

        if (!includeIdentity)
        {
            return;
        }

        SetStringValue(key, "Device Serial", DisplaySerial);
        SetStringValue(key, "Device Serial Short", DisplaySerialShort);
        SetStringValue(key, "Camera Serial", cameraSerialValue);
        SetStringValue(key, "Camera-Name", equipmentName);
        SetStringValue(key, "Camera Name", equipmentName);
        SetStringValue(key, "GUID High", SensorKeyName);
        SetStringValue(key, "GUID Low", guidLowValue);
    }

    private static void CopyRegistryTree(RegistryKey source, RegistryKey destination)
    {
        foreach (var valueName in source.GetValueNames())
        {
            var value = source.GetValue(valueName);
            var kind = source.GetValueKind(valueName);
            destination.SetValue(valueName, value, kind);
        }

        foreach (var subKeyName in source.GetSubKeyNames())
        {
            using var sourceSubKey = source.OpenSubKey(subKeyName, writable: false);
            using var destinationSubKey = destination.CreateSubKey(subKeyName, writable: true);
            if (sourceSubKey is null || destinationSubKey is null)
            {
                continue;
            }

            CopyRegistryTree(sourceSubKey, destinationSubKey);
        }
    }

    private static void SetStringValue(RegistryKey key, string name, string value)
    {
        key.SetValue(name, value, RegistryValueKind.String);
    }

    private static void ShowError(Exception ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RayCi green package startup failed.");
        builder.AppendLine();
        builder.AppendLine(ex.Message);
        if (ex.InnerException is not null)
        {
            builder.AppendLine();
            builder.AppendLine(ex.InnerException.Message);
        }

        MessageBox.Show(
            builder.ToString(),
            "RayCi Portable Launcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private readonly record struct CameraAlias(string Name, bool IncludePerCameraMetadata, string CameraSerialValue, string GuidLowValue);
}
