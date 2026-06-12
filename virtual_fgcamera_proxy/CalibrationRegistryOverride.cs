using System.Globalization;
using System.Runtime.InteropServices;

namespace VirtualFGCameraProxy;

internal static unsafe class CalibrationRegistryOverride
{
    private const int ErrorSuccess = 0;
    private const uint KeyReadWrite = 0x2001F;
    private const uint RegSz = 1;
    private const string SensorKeyName = "00000028";
    private const string BaseCalibrationName = "CinCam CMOS 1201";
    private const string CalibrationName = "CinCam CMOS 1201 EL";
    private const string LegacyCalibrationName = CalibrationName;
    private const string RegistryModel = "uEye UI-1542LE-M";
    private const string DisplaySerial = "1201EL-U2-1022-0034";
    private const string DisplaySerialShort = "10220034";
    private const string RegistrySerial = "4103791906";
    private const string CapturedRawSerial = "1145655880";
    private const string CapturedBoardSerial = "RH1015005021";
    private const string SerialTemplate = "1201EL-U2-{KW:2}{Year:2}-{Number:4}";

    private static readonly object Sync = new();
    private static readonly string LicensedCameraKeyName = BuildSerialKeyName(RegistrySerial);
    private static readonly string LicensedFullCameraKeyName = SensorKeyName + LicensedCameraKeyName;
    private static readonly string LicensedReverseFullCameraKeyName = LicensedCameraKeyName + SensorKeyName;
    private static readonly string ListedSerialKeyName = BuildSerialKeyName(DisplaySerialShort);
    private static readonly string ListedFullCameraKeyName = SensorKeyName + ListedSerialKeyName;
    private static readonly string ListedReverseFullCameraKeyName = ListedSerialKeyName + SensorKeyName;
    private static readonly string CapturedSerialKeyName = BuildSerialKeyName(CapturedRawSerial);
    private static readonly string CapturedFullCameraKeyName = SensorKeyName + CapturedSerialKeyName;
    private static readonly string CapturedReverseFullCameraKeyName = CapturedSerialKeyName + SensorKeyName;

    private static bool _installAttempted;
    private static bool _installed;
    private static nint _overrideRootKey;

    [DllImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegCreateKeyExW(
        nint hKey,
        char* subKey,
        uint reserved,
        char* className,
        uint options,
        uint samDesired,
        void* securityAttributes,
        nint* resultKey,
        uint* disposition);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegSetValueExW(
        nint hKey,
        char* valueName,
        uint reserved,
        uint type,
        char* data,
        uint dataLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(nint hKey);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegOverridePredefKey(nint hKey, nint hNewHKey);

    public static void TryInstall()
    {
        lock (Sync)
        {
            if (_installAttempted)
            {
                return;
            }

            _installAttempted = true;
        }

        try
        {
            var overridePath = $@"Software\Ultron\RayCiFGVirtualHKLM\{Environment.ProcessId}";
            var createStatus = CreateOrOpenKey(HkeyCurrentUser, overridePath, out var rootKey);
            if (createStatus != ErrorSuccess || rootKey == nint.Zero)
            {
                ProxyLog.Write($"Registry override root create failed: {FormatStatus(createStatus)}");
                return;
            }

            foreach (var subKey in BuildVirtualHklm().SubKeys.Values)
            {
                var materializeStatus = WriteNodeRecursive(rootKey, subKey);
                if (materializeStatus != ErrorSuccess)
                {
                    ProxyLog.Write($"Registry override materialize failed for {subKey.Name}: {FormatStatus(materializeStatus)}");
                    _ = RegCloseKey(rootKey);
                    return;
                }
            }

            var overrideStatus = RegOverridePredefKey(HkeyLocalMachine, rootKey);
            if (overrideStatus != ErrorSuccess)
            {
                ProxyLog.Write($"RegOverridePredefKey failed: {FormatStatus(overrideStatus)}");
                _ = RegCloseKey(rootKey);
                return;
            }

            lock (Sync)
            {
                _installed = true;
                _overrideRootKey = rootKey;
            }

            ProxyLog.Write($"Process HKLM override installed via HKCU\\{overridePath}");
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"Registry override install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RegistryNode BuildVirtualHklm()
    {
        var root = new RegistryNode("HKLM");
        var cinogyRoot = root
            .GetOrCreate("SOFTWARE")
            .GetOrCreate("CINOGY");

        AddRayCiRegistryDefaults(cinogyRoot.GetOrCreate("RayCi64"));

        var cameraRoot = cinogyRoot
            .GetOrCreate("Calibrations")
            .GetOrCreate("Camera");

        var baseCameraNode = cameraRoot.GetOrCreate(SensorKeyName);
        AddBase1201Calibration(baseCameraNode);
        AddLicensed1201ElCalibration(baseCameraNode, SensorKeyName, includePerCameraMetadata: false);

        foreach (var alias in EnumerateLicensedKeyAliases())
        {
            AddLicensed1201ElCalibration(cameraRoot.GetOrCreate(alias), alias, includePerCameraMetadata: true);
        }

        return root;
    }

    private static void AddRayCiRegistryDefaults(RegistryNode rayCiNode)
    {
        rayCiNode.SetValue("Date", DateTime.Now.ToString("yyyy/M/d", CultureInfo.InvariantCulture));
        rayCiNode.SetValue("Path Lite", @"C:\Program Files\CINOGY\RayCi64 Lite\");
        rayCiNode.SetValue("Time", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        rayCiNode.SetValue("Path", @"C:\Program Files\CINOGY\RayCi64 Lite\");

        var globalSettings = rayCiNode.GetOrCreate("GlobalSettings");
        globalSettings.SetValue("PathLUT", @".\LUT");
        globalSettings.SetValue("PathHS", @"%MYDOCUMENTS%\RayCi\Single Measurement");
        globalSettings.SetValue("PathSettings", @"%MYDOCUMENTS%\RayCi\Settings");
        globalSettings.SetValue("PathImage", @"%MYDOCUMENTS%\RayCi\Image");
        globalSettings.SetValue("PathExport", @"%MYDOCUMENTS%\RayCi\Export");

        var headerAddress = rayCiNode.GetOrCreate("Header Address");
        headerAddress.SetValue("Company", string.Empty);
        headerAddress.SetValue("WebSite", string.Empty);
        headerAddress.SetValue("City", string.Empty);
        headerAddress.SetValue("Address2", string.Empty);
        headerAddress.SetValue("Phone", string.Empty);
        headerAddress.SetValue("EMail", string.Empty);
        headerAddress.SetValue("Country", string.Empty);
        headerAddress.SetValue("ZIP Code", string.Empty);
        headerAddress.SetValue("Address1", string.Empty);
    }

    private static void AddBase1201Calibration(RegistryNode cameraNode)
    {
        cameraNode.SetValue("AutoExposure Max", "0.9");
        cameraNode.SetValue("AutoExposure Min", "0.5");
        cameraNode.SetValue("BitDepth", "8");
        cameraNode.SetValue("Brightness Factor", "0.000679347826");
        cameraNode.SetValue("Brightness Val", "64");
        cameraNode.SetValue("BufferCnt", "8");
        cameraNode.SetValue("CameraMode", "0");
        cameraNode.SetValue("Crop Bottom", "1");
        cameraNode.SetValue("Crop Top", "1");
        cameraNode.SetValue("FrameRate", "20");
        cameraNode.SetValue("Name", "uEye UI-1542LE-M");
        cameraNode.SetValue("PixelClock", "34");
        cameraNode.SetValue("Technology", "CMOS");
        cameraNode.SetValue("Triggering", "0");

        var equipmentNode = cameraNode.GetOrCreate(BaseCalibrationName);
        var plainNode = equipmentNode.GetOrCreate("plain");
        plainNode.SetValue("AOI CenterX", "0");
        plainNode.SetValue("AOI CenterY", "0");
        plainNode.SetValue("AOI RadiusX", "2.6");
        plainNode.SetValue("AOI RadiusY", "2.6");
        plainNode.SetValue("Exposure Times", "100,200,300,450,700,1000,1500,2000,3000,4500,7000,10000,15000,20000,30000,45000,70000,100000,140000");
        plainNode.SetValue("Gain", "1");
        plainNode.SetValue("MirrorY", "1");
        plainNode.SetValue("ScaleX", "1");
        plainNode.SetValue("ScaleY", "1");
        plainNode.SetValue("Wavelength Max", "1150");
        plainNode.SetValue("Wavelength Min", "350");
    }

    private static void AddLicensed1201ElCalibration(RegistryNode cameraNode, string keyName, bool includePerCameraMetadata)
    {
        var identity = ResolveIdentityMetadata(keyName);
        PopulateCommonCameraBlock(cameraNode, CalibrationName, identity, includePerCameraMetadata);

        foreach (var equipmentName in EnumerateCalibrationAliases())
        {
            AddLicensedEquipmentCalibration(cameraNode, equipmentName, identity, includePerCameraMetadata);
        }
    }

    private static void AddLicensedEquipmentCalibration(
        RegistryNode cameraNode,
        string equipmentName,
        CameraIdentityMetadata identity,
        bool includePerCameraMetadata)
    {
        var equipmentNode = cameraNode.GetOrCreate(equipmentName);
        equipmentNode.SetValue("Icon", string.Equals(equipmentName, CalibrationName, StringComparison.Ordinal) ? "CinCam CMOS" : RegistryModel);
        equipmentNode.SetValue("Serial Number Template", SerialTemplate);
        equipmentNode.SetValue("Device Serial", DisplaySerial);
        equipmentNode.SetValue("Device Serial Short", DisplaySerialShort);
        equipmentNode.SetValue("Calibration ID", "plain");

        var plainNode = equipmentNode.GetOrCreate("plain");
        plainNode.SetValue("AOI CenterX", "0");
        plainNode.SetValue("AOI CenterY", "0");
        plainNode.SetValue("AOI RadiusX", "2.6");
        plainNode.SetValue("AOI RadiusY", "2.6");
        plainNode.SetValue("Exposure Times", "300,450,700,1000,1500,2000,3000,4500,7000,10000,15000,20000,30000,45000,70000,100000,150000,200000,300000");
        plainNode.SetValue("Gain", "1,1.584893192,2.511886432,3.981071706,6.30957344480193");
        plainNode.SetValue("MirrorY", "1");
        plainNode.SetValue("ScaleX", "1");
        plainNode.SetValue("ScaleY", "1");
        plainNode.SetValue("Wavelength Max", "1150");
        plainNode.SetValue("Wavelength Min", "350");

        if (!includePerCameraMetadata)
        {
            return;
        }

        var cameraInfoNode = equipmentNode.GetOrCreate("~Camera");
        PopulateCommonCameraBlock(cameraInfoNode, equipmentName, identity, includeIdentityMetadata: true);
    }

    private static void PopulateCommonCameraBlock(
        RegistryNode cameraNode,
        string equipmentName,
        CameraIdentityMetadata identity,
        bool includeIdentityMetadata)
    {
        cameraNode.SetValue("Camera Group", "CinCam CMOS");
        cameraNode.SetValue("Technology", "CMOS");
        cameraNode.SetValue("Triggering", "0");
        cameraNode.SetValue("BufferCnt", "4");
        cameraNode.SetValue("BitDepth", "max");
        cameraNode.SetValue("ColorFormat", "Y16");
        cameraNode.SetValue("CameraMode", "0");
        cameraNode.SetValue("Low Noise Binning", "0");
        cameraNode.SetValue("Dual-Tap", "0");
        cameraNode.SetValue("Four-Tap", "0");
        cameraNode.SetValue("PixelSizeX", "5.2");
        cameraNode.SetValue("PixelSizeY", "5.2");
        cameraNode.SetValue("DeInterlace", "0");
        cameraNode.SetValue("AOI X0", "0");
        cameraNode.SetValue("AOI Y0", "0");
        cameraNode.SetValue("AOI Width", "1280");
        cameraNode.SetValue("AOI Height", "1024");
        cameraNode.SetValue("Crop Left", "0");
        cameraNode.SetValue("Crop Right", "0");
        cameraNode.SetValue("Crop Top", "0");
        cameraNode.SetValue("Crop Bottom", "0");
        cameraNode.SetValue("FrameRate", "15");
        cameraNode.SetValue("FrameRate_2x2", "15");
        cameraNode.SetValue("FrameRate_4x4", "15");
        cameraNode.SetValue("FrameRate_8x8", "15");
        cameraNode.SetValue("Bandwidth", "480");
        cameraNode.SetValue("PixelClock", "34");
        cameraNode.SetValue("PixelClock_2x2", "34");
        cameraNode.SetValue("PixelClock_4x4", "34");
        cameraNode.SetValue("PixelClock_8x8", "34");
        cameraNode.SetValue("AutoExposure Min", "0.5");
        cameraNode.SetValue("AutoExposure Max", "0.9");
        cameraNode.SetValue("Gain Fak", "1");
        cameraNode.SetValue("Gain Factor", "100");
        cameraNode.SetValue("Gain Val", "0");
        cameraNode.SetValue("Brightness Fak", "0.003408715 * 1.25");
        cameraNode.SetValue("Brightness Factor", "0.003408715 * 1.25");
        cameraNode.SetValue("Brightness Offset", "13");
        cameraNode.SetValue("Brightness Val", "20");
        cameraNode.SetValue("Exposure Time", "30000");
        cameraNode.SetValue("Time Offset", "0");
        cameraNode.SetValue("Gamma", "1");
        cameraNode.SetValue("LUT Exp", "0");
        cameraNode.SetValue("HDR Enable", "0");
        cameraNode.SetValue("List Index", "0");
        cameraNode.SetValue("Equipment", equipmentName);
        cameraNode.SetValue("Name", RegistryModel);

        if (!includeIdentityMetadata)
        {
            return;
        }

        cameraNode.SetValue("Device Serial", DisplaySerial);
        cameraNode.SetValue("Device Serial Short", DisplaySerialShort);
        cameraNode.SetValue("Camera Serial", identity.CameraSerial);
        cameraNode.SetValue("Camera-Name", equipmentName);
        cameraNode.SetValue("Camera Name", equipmentName);
        cameraNode.SetValue("GUID High", SensorKeyName);
        cameraNode.SetValue("GUID Low", identity.GuidLow);
        cameraNode.SetValue("Calibration ID", "plain");
    }

    private static CameraIdentityMetadata ResolveIdentityMetadata(string keyName)
    {
        if (MatchesAny(keyName, DisplaySerialShort, ListedSerialKeyName, ListedFullCameraKeyName, ListedReverseFullCameraKeyName))
        {
            return new CameraIdentityMetadata(DisplaySerialShort, ListedSerialKeyName);
        }

        if (MatchesAny(keyName, CapturedRawSerial, CapturedBoardSerial, CapturedSerialKeyName, CapturedFullCameraKeyName, CapturedReverseFullCameraKeyName))
        {
            return new CameraIdentityMetadata(CapturedRawSerial, CapturedSerialKeyName);
        }

        return new CameraIdentityMetadata(RegistrySerial, LicensedCameraKeyName);
    }

    private static bool MatchesAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCalibrationAliases()
    {
        yield return CalibrationName;

        if (!string.Equals(LegacyCalibrationName, CalibrationName, StringComparison.Ordinal))
        {
            yield return LegacyCalibrationName;
        }
    }

    private static int WriteNodeRecursive(nint parentKey, RegistryNode node)
    {
        var createStatus = CreateOrOpenKey(parentKey, node.Name, out var nodeKey);
        if (createStatus != ErrorSuccess || nodeKey == nint.Zero)
        {
            return createStatus;
        }

        try
        {
            foreach (var value in node.Values.Values)
            {
                var setStatus = SetStringValue(nodeKey, value.Name, value.Data, value.Type);
                if (setStatus != ErrorSuccess)
                {
                    return setStatus;
                }
            }

            foreach (var child in node.SubKeys.Values)
            {
                var childStatus = WriteNodeRecursive(nodeKey, child);
                if (childStatus != ErrorSuccess)
                {
                    return childStatus;
                }
            }
        }
        finally
        {
            _ = RegCloseKey(nodeKey);
        }

        return ErrorSuccess;
    }

    private static int CreateOrOpenKey(nint parentKey, string subKey, out nint resultKey)
    {
        resultKey = nint.Zero;

        fixed (char* subKeyPtr = subKey)
        {
            nint createdKey = nint.Zero;
            var status = RegCreateKeyExW(
                parentKey,
                subKeyPtr,
                0,
                null,
                0,
                KeyReadWrite,
                null,
                &createdKey,
                null);

            if (status == ErrorSuccess)
            {
                resultKey = createdKey;
            }

            return status;
        }
    }

    private static int SetStringValue(nint key, string name, string value, uint type)
    {
        fixed (char* namePtr = name)
        fixed (char* valuePtr = value)
        {
            return RegSetValueExW(key, namePtr, 0, type, valuePtr, (uint)((value.Length + 1) * sizeof(char)));
        }
    }

    private static IEnumerable<string> EnumerateLicensedKeyAliases()
    {
        // RayCi 2022 / 10bpp combines the sensor id with the listed short serial
        // when resolving the per-camera calibration block.
        yield return LicensedCameraKeyName;
        yield return LicensedFullCameraKeyName;
        yield return LicensedReverseFullCameraKeyName;
        yield return RegistrySerial;
        yield return DisplaySerialShort;
        yield return ListedSerialKeyName;
        yield return ListedFullCameraKeyName;
        yield return ListedReverseFullCameraKeyName;

        // Keep older raw-page-derived names only as compatibility aliases.
        yield return CapturedSerialKeyName;
        yield return CapturedReverseFullCameraKeyName;
        yield return CapturedFullCameraKeyName;
        yield return CapturedRawSerial;
        yield return CapturedBoardSerial;
    }

    private static string FormatStatus(int status) => status == ErrorSuccess ? "OK" : $"ERR({status})";

    private static string BuildSerialKeyName(string decimalSerial)
    {
        var serialValue = uint.Parse(decimalSerial, CultureInfo.InvariantCulture);
        return serialValue.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static nint HkeyCurrentUser => unchecked((nint)(int)0x80000001);
    private static nint HkeyLocalMachine => unchecked((nint)(int)0x80000002);

    private readonly record struct CameraIdentityMetadata(string CameraSerial, string GuidLow);

    private sealed class RegistryNode
    {
        public RegistryNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public Dictionary<string, RegistryNode> SubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RegistryValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public RegistryNode GetOrCreate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return this;
            }

            if (!SubKeys.TryGetValue(name, out var child))
            {
                child = new RegistryNode(name);
                SubKeys[name] = child;
            }

            return child;
        }

        public void SetValue(string name, string data, uint type = RegSz)
        {
            Values[name] = new RegistryValue(name, data, type);
        }
    }

    private readonly record struct RegistryValue(string Name, string Data, uint Type);
}
