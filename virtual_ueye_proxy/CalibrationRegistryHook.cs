using System.Globalization;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualUEyeProxy;

internal static unsafe class CalibrationRegistryHook
{
    private const bool EnableImportHook = true;
    private const bool EnableProcessRegistryOverride = true;
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorMoreData = 234;
    private const int ErrorNoMoreItems = 259;

    private const uint RegSz = 1;
    private const uint RegExpandSz = 2;
    private const uint RegBinary = 3;
    private const uint RegDword = 4;
    private const uint RegMultiSz = 7;
    private const uint RegQword = 11;
    private const uint PageReadWrite = 0x04;
    private const uint KeyReadWrite = 0x2001F;
    private const ushort ImageDirectoryEntryImport = 1;
    private const ulong ImageOrdinalFlag64 = 0x8000_0000_0000_0000;

    private static readonly object Gate = new();
    private static readonly HashSet<nint> PatchedModules = new();
    private static readonly Dictionary<nint, OpenVirtualKey> VirtualHandles = new();
    private static readonly Dictionary<nint, string> ObservedRealHandles = new();

    private static readonly string CalibrationRootPath = @"SOFTWARE\CINOGY\Calibrations";
    private static readonly string InterestingObservedRoot = @"SOFTWARE";
    private static readonly string SensorKeyName = ((uint)UeyeNative.IS_SENSOR_UI1545_M).ToString("X8", CultureInfo.InvariantCulture);
    private static readonly string LicensedCameraKeyName = BuildSerialKeyName(VirtualCameraState.UeyeSerial);
    private static readonly string FullCameraKeyName = BuildFullCameraKeyName(VirtualCameraState.UeyeSerial);
    private static readonly string ReverseFullCameraKeyName = LicensedCameraKeyName + SensorKeyName;
    private static readonly string CapturedSerialKeyName = BuildSerialKeyName(VirtualCameraState.CapturedRawSerial);
    private static readonly string CapturedFullCameraKeyName = BuildFullCameraKeyName(VirtualCameraState.CapturedRawSerial);
    private static readonly string CapturedReverseFullCameraKeyName = CapturedSerialKeyName + SensorKeyName;
    private static readonly string ListedSerialKeyName = BuildSerialKeyName(VirtualCameraState.ReportedListSerial);
    private static readonly string ListedFullCameraKeyName = BuildFullCameraKeyName(VirtualCameraState.ReportedListSerial);
    private static readonly string ListedReverseFullCameraKeyName = ListedSerialKeyName + SensorKeyName;
    private static readonly RegistryNode VirtualHklm = BuildVirtualRegistryTree();
    private const bool EnableVirtualRegistry = false;
    private const string BaseCalibrationName = "CinCam CMOS 1201";
    private const string Base1201ExposureTimesCsv = "100,200,300,450,700,1000,1500,2000,3000,4500,7000,10000,15000,20000,30000,45000,70000,100000,140000";
    private const string RayCiElExposureTimesCsv = "30000,45000,70000,100000,150000,200000,300000";
    private const string RayCiGainCsv = "1,1.584893192,2.511886432,3.981071706,6.30957344480193";

    private static bool _installAttempted;
    private static bool _overrideInstalled;
    private static int _traceCount;
    private static nint _overrideRootKey;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint moduleHandle, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyExW(nint hKey, char* subKey, uint options, uint samDesired, nint* resultKey);

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int RegOpenKeyExA(nint hKey, byte* subKey, uint options, uint samDesired, nint* resultKey);

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyTransactedW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyTransactedW(nint hKey, char* subKey, uint options, uint samDesired, nint* resultKey, nint transaction, void* extendedParameter);

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

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryInfoKeyW(
        nint hKey,
        char* className,
        uint* classNameLength,
        uint* reserved,
        uint* subKeyCount,
        uint* maxSubKeyLength,
        uint* maxClassLength,
        uint* valueCount,
        uint* maxValueNameLength,
        uint* maxValueDataLength,
        uint* securityDescriptorLength,
        FileTime* lastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegEnumKeyExW(
        nint hKey,
        uint index,
        char* name,
        uint* nameLength,
        uint* reserved,
        char* className,
        uint* classNameLength,
        FileTime* lastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegEnumValueW(
        nint hKey,
        uint index,
        char* valueName,
        uint* valueNameLength,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumValueA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int RegEnumValueA(
        nint hKey,
        uint index,
        byte* valueName,
        uint* valueNameLength,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryValueExW(
        nint hKey,
        char* valueName,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int RegQueryValueExA(
        nint hKey,
        byte* valueName,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength);

    [DllImport("advapi32.dll", EntryPoint = "RegCloseKey", SetLastError = true)]
    private static extern int RegCloseKey(nint hKey);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegSetValueExW(nint hKey, string? valueName, uint reserved, uint type, char* data, uint dataLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegOverridePredefKey(nint hKey, nint hNewHKey);

    public static void TryInstall()
    {
        lock (Gate)
        {
            if (_installAttempted)
            {
                return;
            }

            _installAttempted = true;
        }

        try
        {
            if (EnableProcessRegistryOverride)
            {
                var overrideInstalled = TryInstallProcessRegistryOverride();
                VirtualCameraState.Log($"CalibrationRegistryHook: process HKLM override {(overrideInstalled ? "enabled" : "not enabled")}.");
            }

            if (!EnableImportHook)
            {
                VirtualCameraState.Log("CalibrationRegistryHook: import patch disabled.");
                return;
            }

            PatchLoadedModules("initial");
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"CalibrationRegistryHook: install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void AppendIdentityReport(List<string> lines)
    {
        if (lines is null)
        {
            return;
        }

        lines.Add($"SensorKeyName={SensorKeyName}");
        lines.Add($"LicensedCameraKeyName={LicensedCameraKeyName}");
        lines.Add($"FullCameraKeyName={FullCameraKeyName}");
        lines.Add($"ReverseFullCameraKeyName={ReverseFullCameraKeyName}");
        lines.Add($"CapturedSerialKeyName={CapturedSerialKeyName}");
        lines.Add($"CapturedFullCameraKeyName={CapturedFullCameraKeyName}");
        lines.Add($"CapturedReverseFullCameraKeyName={CapturedReverseFullCameraKeyName}");
        lines.Add($"ListedSerialKeyName={ListedSerialKeyName}");
        lines.Add($"ListedFullCameraKeyName={ListedFullCameraKeyName}");
        lines.Add($"ListedReverseFullCameraKeyName={ListedReverseFullCameraKeyName}");
        lines.Add(string.Empty);
        AppendRegistryNode(lines, VirtualHklm, depth: 0, maxDepth: 8);
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegOpenKeyExW_Hook")]
    private static int RegOpenKeyExWHook(nint hKey, char* subKey, uint options, uint samDesired, nint* resultKey)
    {
        if (EnableVirtualRegistry &&
            TryOpenVirtualKey(hKey, PtrToStringUni(subKey), createIfMissing: false, resultKey, null, out var resolvedPath, out var status))
        {
            Trace($"RegOpenKeyExW virtual {FormatStatus(status)}: HKLM\\{resolvedPath}");
            return status;
        }

        var nativeStatus = RegOpenKeyExW(hKey, subKey, options, samDesired, resultKey);
        RememberObservedRealHandle(hKey, PtrToStringUni(subKey), resultKey, nativeStatus, "RegOpenKeyExW");
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegOpenKeyExA_Hook")]
    private static int RegOpenKeyExAHook(nint hKey, byte* subKey, uint options, uint samDesired, nint* resultKey)
    {
        if (EnableVirtualRegistry &&
            TryOpenVirtualKey(hKey, PtrToStringAnsi(subKey), createIfMissing: false, resultKey, null, out var resolvedPath, out var status))
        {
            Trace($"RegOpenKeyExA virtual {FormatStatus(status)}: HKLM\\{resolvedPath}");
            return status;
        }

        var nativeStatus = RegOpenKeyExA(hKey, subKey, options, samDesired, resultKey);
        RememberObservedRealHandle(hKey, PtrToStringAnsi(subKey), resultKey, nativeStatus, "RegOpenKeyExA");
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegOpenKeyTransactedW_Hook")]
    private static int RegOpenKeyTransactedWHook(nint hKey, char* subKey, uint options, uint samDesired, nint* resultKey, nint transaction, void* extendedParameter)
    {
        if (EnableVirtualRegistry &&
            TryOpenVirtualKey(hKey, PtrToStringUni(subKey), createIfMissing: false, resultKey, null, out var resolvedPath, out var status))
        {
            Trace($"RegOpenKeyTransactedW virtual {FormatStatus(status)}: HKLM\\{resolvedPath}");
            return status;
        }

        var nativeStatus = RegOpenKeyTransactedW(hKey, subKey, options, samDesired, resultKey, transaction, extendedParameter);
        RememberObservedRealHandle(hKey, PtrToStringUni(subKey), resultKey, nativeStatus, "RegOpenKeyTransactedW");
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegCreateKeyExW_Hook")]
    private static int RegCreateKeyExWHook(
        nint hKey,
        char* subKey,
        uint reserved,
        char* className,
        uint options,
        uint samDesired,
        void* securityAttributes,
        nint* resultKey,
        uint* disposition)
    {
        if (TryOpenVirtualKey(hKey, PtrToStringUni(subKey), createIfMissing: true, resultKey, disposition, out var resolvedPath, out var status))
        {
            Trace($"RegCreateKeyExW virtual {FormatStatus(status)}: HKLM\\{resolvedPath}");
            return status;
        }

        var nativeStatus = RegCreateKeyExW(hKey, subKey, reserved, className, options, samDesired, securityAttributes, resultKey, disposition);
        RememberObservedRealHandle(hKey, PtrToStringUni(subKey), resultKey, nativeStatus, "RegCreateKeyExW");
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegQueryInfoKeyW_Hook")]
    private static int RegQueryInfoKeyWHook(
        nint hKey,
        char* className,
        uint* classNameLength,
        uint* reserved,
        uint* subKeyCount,
        uint* maxSubKeyLength,
        uint* maxClassLength,
        uint* valueCount,
        uint* maxValueNameLength,
        uint* maxValueDataLength,
        uint* securityDescriptorLength,
        FileTime* lastWriteTime)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            if (classNameLength != null)
            {
                *classNameLength = 0;
            }

            if (className != null)
            {
                *className = '\0';
            }

            if (subKeyCount != null)
            {
                *subKeyCount = (uint)openKey.Node.SubKeys.Count;
            }

            if (maxSubKeyLength != null)
            {
                *maxSubKeyLength = GetMaxLength(openKey.Node.SubKeys.Keys);
            }

            if (maxClassLength != null)
            {
                *maxClassLength = 0;
            }

            if (valueCount != null)
            {
                *valueCount = (uint)openKey.Node.Values.Count;
            }

            if (maxValueNameLength != null)
            {
                *maxValueNameLength = GetMaxLength(openKey.Node.Values.Keys);
            }

            if (maxValueDataLength != null)
            {
                *maxValueDataLength = GetMaxValueDataLength(openKey.Node.Values.Values);
            }

            if (securityDescriptorLength != null)
            {
                *securityDescriptorLength = 0;
            }

            if (lastWriteTime != null)
            {
                *lastWriteTime = default;
            }

            Trace($"RegQueryInfoKeyW virtual OK: HKLM\\{openKey.Path}");
            return ErrorSuccess;
        }

        var nativeStatus = RegQueryInfoKeyW(
            hKey,
            className,
            classNameLength,
            reserved,
            subKeyCount,
            maxSubKeyLength,
            maxClassLength,
            valueCount,
            maxValueNameLength,
            maxValueDataLength,
            securityDescriptorLength,
            lastWriteTime);

        TraceObservedRealQueryInfo(hKey, nativeStatus, subKeyCount, valueCount);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegEnumKeyExW_Hook")]
    private static int RegEnumKeyExWHook(
        nint hKey,
        uint index,
        char* name,
        uint* nameLength,
        uint* reserved,
        char* className,
        uint* classNameLength,
        FileTime* lastWriteTime)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            if (index >= openKey.Node.SubKeys.Count)
            {
                return ErrorNoMoreItems;
            }

            var subKeyName = openKey.Node.SubKeys.Keys.ElementAt((int)index);
            var status = CopyUnicodeName(subKeyName, name, nameLength);
            if (status != ErrorSuccess)
            {
                return status;
            }

            if (classNameLength != null)
            {
                *classNameLength = 0;
            }

            if (className != null)
            {
                *className = '\0';
            }

            if (lastWriteTime != null)
            {
                *lastWriteTime = default;
            }

            Trace($"RegEnumKeyExW virtual OK: HKLM\\{openKey.Path} -> {subKeyName}");
            return ErrorSuccess;
        }

        var nativeStatus = RegEnumKeyExW(hKey, index, name, nameLength, reserved, className, classNameLength, lastWriteTime);
        TraceObservedRealEnumKey(hKey, nativeStatus, name, nameLength);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegEnumValueW_Hook")]
    private static int RegEnumValueWHook(
        nint hKey,
        uint index,
        char* valueName,
        uint* valueNameLength,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            if (index >= openKey.Node.Values.Count)
            {
                return ErrorNoMoreItems;
            }

            var value = openKey.Node.Values.Values.ElementAt((int)index);
            var status = CopyUnicodeName(value.Name, valueName, valueNameLength);
            if (status != ErrorSuccess)
            {
                return status;
            }

            if (type != null)
            {
                *type = value.Type;
            }

            status = CopyUnicodeData(value.Data, data, dataLength);
            if (status != ErrorSuccess)
            {
                return status;
            }

            Trace($"RegEnumValueW virtual OK: HKLM\\{openKey.Path} -> {value.Name}");
            return ErrorSuccess;
        }

        var nativeStatus = RegEnumValueW(hKey, index, valueName, valueNameLength, reserved, type, data, dataLength);
        TraceObservedRealValue("RegEnumValueW", hKey, nativeStatus, ReadUnicodeBuffer(valueName, valueNameLength), type, data, dataLength, ansiStrings: false);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegEnumValueA_Hook")]
    private static int RegEnumValueAHook(
        nint hKey,
        uint index,
        byte* valueName,
        uint* valueNameLength,
        uint* reserved,
        uint* type,
        byte* data,
        uint* dataLength)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            if (index >= openKey.Node.Values.Count)
            {
                return ErrorNoMoreItems;
            }

            var value = openKey.Node.Values.Values.ElementAt((int)index);
            var status = CopyAnsiName(value.Name, valueName, valueNameLength);
            if (status != ErrorSuccess)
            {
                return status;
            }

            if (type != null)
            {
                *type = value.Type;
            }

            status = CopyAnsiData(value.Data, data, dataLength);
            if (status != ErrorSuccess)
            {
                return status;
            }

            Trace($"RegEnumValueA virtual OK: HKLM\\{openKey.Path} -> {value.Name}");
            return ErrorSuccess;
        }

        var nativeStatus = RegEnumValueA(hKey, index, valueName, valueNameLength, reserved, type, data, dataLength);
        TraceObservedRealValue("RegEnumValueA", hKey, nativeStatus, ReadAnsiBuffer(valueName, valueNameLength), type, data, dataLength, ansiStrings: true);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegQueryValueExW_Hook")]
    private static int RegQueryValueExWHook(nint hKey, char* valueName, uint* reserved, uint* type, byte* data, uint* dataLength)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            var lookupName = PtrToStringUni(valueName) ?? string.Empty;
            if (!openKey.Node.Values.TryGetValue(lookupName, out var value))
            {
                Trace($"RegQueryValueExW virtual miss: HKLM\\{openKey.Path} [{lookupName}]");
                return ErrorFileNotFound;
            }

            if (type != null)
            {
                *type = value.Type;
            }

            var status = CopyUnicodeData(value.Data, data, dataLength);
            if (status == ErrorSuccess)
            {
                Trace($"RegQueryValueExW virtual OK: HKLM\\{openKey.Path} [{lookupName}] -> {value.Data}");
            }

            return status;
        }

        var nativeStatus = RegQueryValueExW(hKey, valueName, reserved, type, data, dataLength);
        TraceObservedRealValue("RegQueryValueExW", hKey, nativeStatus, PtrToStringUni(valueName), type, data, dataLength, ansiStrings: false);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegQueryValueExA_Hook")]
    private static int RegQueryValueExAHook(nint hKey, byte* valueName, uint* reserved, uint* type, byte* data, uint* dataLength)
    {
        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            var lookupName = PtrToStringAnsi(valueName) ?? string.Empty;
            if (!openKey.Node.Values.TryGetValue(lookupName, out var value))
            {
                Trace($"RegQueryValueExA virtual miss: HKLM\\{openKey.Path} [{lookupName}]");
                return ErrorFileNotFound;
            }

            if (type != null)
            {
                *type = value.Type;
            }

            var status = CopyAnsiData(value.Data, data, dataLength);
            if (status == ErrorSuccess)
            {
                Trace($"RegQueryValueExA virtual OK: HKLM\\{openKey.Path} [{lookupName}] -> {value.Data}");
            }

            return status;
        }

        var nativeStatus = RegQueryValueExA(hKey, valueName, reserved, type, data, dataLength);
        TraceObservedRealValue("RegQueryValueExA", hKey, nativeStatus, PtrToStringAnsi(valueName), type, data, dataLength, ansiStrings: true);
        return nativeStatus;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_RegCloseKey_Hook")]
    private static int RegCloseKeyHook(nint hKey)
    {
        OpenVirtualKey? virtualHandle;
        lock (Gate)
        {
            if (VirtualHandles.TryGetValue(hKey, out virtualHandle))
            {
                VirtualHandles.Remove(hKey);
            }
            else
            {
                virtualHandle = null;
            }
        }

        if (virtualHandle is not null)
        {
            Marshal.FreeHGlobal(hKey);
            Trace($"RegCloseKey virtual OK: HKLM\\{virtualHandle.Path}");
            return ErrorSuccess;
        }

        var nativeStatus = RegCloseKey(hKey);
        lock (Gate)
        {
            ObservedRealHandles.Remove(hKey);
        }

        return nativeStatus;
    }

    private static void PatchLoadedModules(string reason)
    {
        try
        {
            var patchedEntries = 0;
            var selfModule = GetModuleHandleW("ueye_api_64.dll");

            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                var moduleBase = module.BaseAddress;
                if (moduleBase == nint.Zero || moduleBase == selfModule)
                {
                    continue;
                }

                lock (Gate)
                {
                    if (!PatchedModules.Add(moduleBase))
                    {
                        continue;
                    }
                }

                patchedEntries += PatchAdvapiImports(moduleBase, module.ModuleName);
            }

            if (patchedEntries > 0)
            {
                VirtualCameraState.Log($"CalibrationRegistryHook: patched {patchedEntries} ADVAPI32 imports ({reason}).");
            }
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"CalibrationRegistryHook: PatchLoadedModules({reason}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int PatchAdvapiImports(nint moduleBase, string? moduleName)
    {
        var basePtr = (byte*)moduleBase;
        var lfanew = *(int*)(basePtr + 0x3C);
        if (lfanew <= 0)
        {
            return 0;
        }

        var ntHeader = basePtr + lfanew;
        if (*(uint*)ntHeader != 0x00004550)
        {
            return 0;
        }

        var fileHeader = ntHeader + 4;
        var optionalHeader = fileHeader + 20;
        var optionalMagic = *(ushort*)optionalHeader;
        if (optionalMagic != 0x20B)
        {
            return 0;
        }

        var importDirectoryOffset = 112 + (ImageDirectoryEntryImport * 8);
        var importRva = *(uint*)(optionalHeader + importDirectoryOffset);
        if (importRva == 0)
        {
            return 0;
        }

        var descriptor = (ImageImportDescriptor*)(basePtr + importRva);
        var patchedEntries = 0;

        while (descriptor->Name != 0)
        {
            var importModuleName = Marshal.PtrToStringAnsi((nint)(basePtr + descriptor->Name));
            if (string.Equals(importModuleName, "ADVAPI32.dll", StringComparison.OrdinalIgnoreCase))
            {
                var originalThunkRva = descriptor->OriginalFirstThunk != 0 ? descriptor->OriginalFirstThunk : descriptor->FirstThunk;
                var originalThunk = (ulong*)(basePtr + originalThunkRva);
                var firstThunk = (nint*)(basePtr + descriptor->FirstThunk);

                while (*originalThunk != 0)
                {
                    var thunkData = *originalThunk;
                    if ((thunkData & ImageOrdinalFlag64) == 0)
                    {
                        var importByName = basePtr + (int)thunkData;
                        var importName = Marshal.PtrToStringAnsi((nint)(importByName + 2));
                        if (TryGetReplacement(importName, out var replacement) && *firstThunk != replacement)
                        {
                            if (VirtualProtect((nint)firstThunk, (nuint)IntPtr.Size, PageReadWrite, out var oldProtect))
                            {
                                *firstThunk = replacement;
                                _ = VirtualProtect((nint)firstThunk, (nuint)IntPtr.Size, oldProtect, out _);
                                patchedEntries++;
                            }
                        }
                    }

                    originalThunk++;
                    firstThunk++;
                }
            }

            descriptor++;
        }

        if (patchedEntries > 0)
        {
            VirtualCameraState.Log($"CalibrationRegistryHook: {moduleName ?? "<unknown>"} patched imports={patchedEntries}");
        }

        return patchedEntries;
    }

    private static bool TryGetReplacement(string? importName, out nint replacement)
    {
        switch (importName)
        {
            case "RegOpenKeyExA":
                replacement = GetExportedHookAddress("Ultron_RegOpenKeyExA_Hook");
                return replacement != nint.Zero;
            case "RegOpenKeyExW":
                replacement = GetExportedHookAddress("Ultron_RegOpenKeyExW_Hook");
                return replacement != nint.Zero;
            case "RegOpenKeyTransactedW":
                replacement = GetExportedHookAddress("Ultron_RegOpenKeyTransactedW_Hook");
                return replacement != nint.Zero;
            case "RegCreateKeyExW":
                replacement = GetExportedHookAddress("Ultron_RegCreateKeyExW_Hook");
                return replacement != nint.Zero;
            case "RegQueryInfoKeyW":
                replacement = GetExportedHookAddress("Ultron_RegQueryInfoKeyW_Hook");
                return replacement != nint.Zero;
            case "RegEnumKeyExW":
                replacement = GetExportedHookAddress("Ultron_RegEnumKeyExW_Hook");
                return replacement != nint.Zero;
            case "RegEnumValueW":
                replacement = GetExportedHookAddress("Ultron_RegEnumValueW_Hook");
                return replacement != nint.Zero;
            case "RegEnumValueA":
                replacement = GetExportedHookAddress("Ultron_RegEnumValueA_Hook");
                return replacement != nint.Zero;
            case "RegQueryValueExA":
                replacement = GetExportedHookAddress("Ultron_RegQueryValueExA_Hook");
                return replacement != nint.Zero;
            case "RegQueryValueExW":
                replacement = GetExportedHookAddress("Ultron_RegQueryValueExW_Hook");
                return replacement != nint.Zero;
            case "RegCloseKey":
                replacement = GetExportedHookAddress("Ultron_RegCloseKey_Hook");
                return replacement != nint.Zero;
            default:
                replacement = nint.Zero;
                return false;
        }
    }

    private static nint GetExportedHookAddress(string exportName)
    {
        var selfModule = GetModuleHandleW("ueye_api_64.dll");
        if (selfModule == nint.Zero)
        {
            Trace($"GetExportedHookAddress failed: self module not found for {exportName}");
            return nint.Zero;
        }

        var address = GetProcAddress(selfModule, exportName);
        if (address == nint.Zero)
        {
            Trace($"GetExportedHookAddress failed: export {exportName} missing");
        }

        return address;
    }

    private static bool TryInstallProcessRegistryOverride()
    {
        lock (Gate)
        {
            if (_overrideInstalled)
            {
                return true;
            }
        }

        var overridePath = $@"Software\Ultron\RayCiVirtualHKLM\{Environment.ProcessId}";
        var createStatus = CreateOrOpenKey(HkeyCurrentUser, overridePath, out var rootKey);
        if (createStatus != ErrorSuccess || rootKey == nint.Zero)
        {
            Trace($"CreateOrOpenKey failed for override root '{overridePath}': {FormatStatus(createStatus)}");
            return false;
        }

        foreach (var subKey in VirtualHklm.SubKeys.Values)
        {
            var materializeStatus = WriteNodeRecursive(rootKey, subKey);
            if (materializeStatus != ErrorSuccess)
            {
                Trace($"WriteNodeRecursive failed for '{subKey.Name}': {FormatStatus(materializeStatus)}");
                _ = RegCloseKey(rootKey);
                return false;
            }
        }

        var overrideStatus = RegOverridePredefKey(HkeyLocalMachine, rootKey);
        if (overrideStatus != ErrorSuccess)
        {
            Trace($"RegOverridePredefKey failed: {FormatStatus(overrideStatus)}");
            _ = RegCloseKey(rootKey);
            return false;
        }

        lock (Gate)
        {
            _overrideInstalled = true;
            _overrideRootKey = rootKey;
        }

        Trace($"Process HKLM override -> HKCU\\{overridePath}");
        return true;
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
            resultKey = createdKey;
            return status;
        }
    }

    private static int SetStringValue(nint key, string name, string data, uint type)
    {
        var text = data + '\0';
        fixed (char* dataPtr = text)
        {
            return RegSetValueExW(
                key,
                name,
                0,
                type,
                dataPtr,
                (uint)(text.Length * sizeof(char)));
        }
    }

    private static bool TryOpenVirtualKey(
        nint hKey,
        string? subKey,
        bool createIfMissing,
        nint* resultKey,
        uint* disposition,
        out string resolvedPath,
        out int status)
    {
        resolvedPath = string.Empty;
        status = ErrorPathNotFound;

        if (resultKey != null)
        {
            *resultKey = nint.Zero;
        }

        if (disposition != null)
        {
            *disposition = 0;
        }

        var normalizedSubKey = NormalizePath(subKey);

        if (TryGetVirtualHandle(hKey, out var openKey))
        {
            resolvedPath = string.IsNullOrEmpty(normalizedSubKey)
                ? openKey.Path
                : CombinePath(openKey.Path, normalizedSubKey);

            if (TryResolveVirtualPath(resolvedPath, createIfMissing, out var targetNode, out var created))
            {
                if (resultKey != null)
                {
                    *resultKey = RegisterVirtualHandle(resolvedPath, targetNode);
                }

                if (disposition != null)
                {
                    *disposition = created ? 1u : 2u;
                }

                status = ErrorSuccess;
                return true;
            }

            status = ErrorFileNotFound;
            return true;
        }

        if (hKey == HkeyLocalMachine)
        {
            resolvedPath = normalizedSubKey;
            if (!IsCalibrationPath(resolvedPath))
            {
                return false;
            }

            if (TryResolveVirtualPath(resolvedPath, createIfMissing, out var targetNode, out var created))
            {
                if (resultKey != null)
                {
                    *resultKey = RegisterVirtualHandle(resolvedPath, targetNode);
                }

                if (disposition != null)
                {
                    *disposition = created ? 1u : 2u;
                }

                status = ErrorSuccess;
                return true;
            }

            status = ErrorFileNotFound;
            return true;
        }

        if (TryGetObservedRealPath(hKey, out var realBasePath))
        {
            resolvedPath = string.IsNullOrEmpty(normalizedSubKey)
                ? realBasePath
                : CombinePath(realBasePath, normalizedSubKey);

            if (!IsCalibrationPath(resolvedPath))
            {
                return false;
            }

            if (TryResolveVirtualPath(resolvedPath, createIfMissing, out var targetNode, out var created))
            {
                if (resultKey != null)
                {
                    *resultKey = RegisterVirtualHandle(resolvedPath, targetNode);
                }

                if (disposition != null)
                {
                    *disposition = created ? 1u : 2u;
                }

                status = ErrorSuccess;
                return true;
            }

            status = ErrorFileNotFound;
            return true;
        }

        return false;
    }

    private static bool TryResolveVirtualPath(string path, bool createIfMissing, out RegistryNode node, out bool created)
    {
        node = VirtualHklm;
        created = false;

        var normalizedPath = NormalizePath(path);
        if (!IsCalibrationPath(normalizedPath))
        {
            return false;
        }

        foreach (var segment in normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.SubKeys.TryGetValue(segment, out var nextNode))
            {
                if (!createIfMissing)
                {
                    return false;
                }

                nextNode = node.GetOrCreate(segment);
                created = true;
            }

            node = nextNode;
        }

        return true;
    }

    private static nint RegisterVirtualHandle(string path, RegistryNode node)
    {
        var handle = Marshal.AllocHGlobal(1);
        lock (Gate)
        {
            VirtualHandles[handle] = new OpenVirtualKey(path, node);
        }

        return handle;
    }

    private static void RememberObservedRealHandle(nint parentKey, string? subKey, nint* resultKey, int status, string apiName)
    {
        if (status != ErrorSuccess || resultKey == null || *resultKey == nint.Zero)
        {
            if (status != ErrorSuccess && TryComposeObservedPath(parentKey, subKey, out var failedPath) && IsInterestingObservedPath(failedPath))
            {
                Trace($"{apiName} real {FormatStatus(status)}: HKLM\\{failedPath}");
            }

            return;
        }

        if (!TryComposeObservedPath(parentKey, subKey, out var resolvedPath))
        {
            return;
        }

        lock (Gate)
        {
            ObservedRealHandles[*resultKey] = resolvedPath;
        }

        if (IsInterestingObservedPath(resolvedPath))
        {
            Trace($"{apiName} real OK: HKLM\\{resolvedPath}");
        }
    }

    private static bool TryComposeObservedPath(nint parentKey, string? subKey, out string resolvedPath)
    {
        var normalizedSubKey = NormalizePath(subKey);
        if (parentKey == HkeyLocalMachine)
        {
            resolvedPath = normalizedSubKey;
            return resolvedPath.StartsWith("SOFTWARE", StringComparison.OrdinalIgnoreCase);
        }

        if (TryGetObservedRealPath(parentKey, out var parentPath))
        {
            resolvedPath = string.IsNullOrEmpty(normalizedSubKey)
                ? parentPath
                : CombinePath(parentPath, normalizedSubKey);
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }

    private static void TraceObservedRealHandle(string apiName, nint hKey, int status)
    {
        if (TryGetObservedRealPath(hKey, out var path) && IsInterestingObservedPath(path))
        {
            Trace($"{apiName} real {FormatStatus(status)}: HKLM\\{path}");
        }
    }

    private static void TraceObservedRealQueryInfo(nint hKey, int status, uint* subKeyCount, uint* valueCount)
    {
        if (!TryGetObservedRealPath(hKey, out var path) || !IsInterestingObservedPath(path))
        {
            return;
        }

        if (status == ErrorSuccess)
        {
            var subKeys = subKeyCount == null ? "?" : subKeyCount->ToString(CultureInfo.InvariantCulture);
            var values = valueCount == null ? "?" : valueCount->ToString(CultureInfo.InvariantCulture);
            Trace($"RegQueryInfoKeyW real OK: HKLM\\{path} -> subKeys={subKeys}, values={values}");
            return;
        }

        Trace($"RegQueryInfoKeyW real {FormatStatus(status)}: HKLM\\{path}");
    }

    private static void TraceObservedRealEnumKey(nint hKey, int status, char* name, uint* nameLength)
    {
        if (!TryGetObservedRealPath(hKey, out var path) || !IsInterestingObservedPath(path))
        {
            return;
        }

        if (status == ErrorSuccess)
        {
            var subKeyName = ReadUnicodeBuffer(name, nameLength);
            Trace($"RegEnumKeyExW real OK: HKLM\\{path} -> {subKeyName}");
            return;
        }

        Trace($"RegEnumKeyExW real {FormatStatus(status)}: HKLM\\{path}");
    }

    private static void TraceObservedRealValue(
        string apiName,
        nint hKey,
        int status,
        string? valueName,
        uint* type,
        byte* data,
        uint* dataLength,
        bool ansiStrings)
    {
        if (!TryGetObservedRealPath(hKey, out var path) || !IsInterestingObservedPath(path))
        {
            return;
        }

        var displayName = string.IsNullOrEmpty(valueName) ? "(Default)" : valueName;
        if (status == ErrorSuccess)
        {
            var preview = DescribeRegistryData(type, data, dataLength, ansiStrings);
            Trace($"{apiName} real OK: HKLM\\{path} [{displayName}] -> {preview}");
            return;
        }

        Trace($"{apiName} real {FormatStatus(status)}: HKLM\\{path} [{displayName}]");
    }

    private static bool TryGetVirtualHandle(nint hKey, out OpenVirtualKey openKey)
    {
        lock (Gate)
        {
            return VirtualHandles.TryGetValue(hKey, out openKey!);
        }
    }

    private static bool TryGetObservedRealPath(nint hKey, out string path)
    {
        lock (Gate)
        {
            return ObservedRealHandles.TryGetValue(hKey, out path!);
        }
    }

    private static bool IsCalibrationPath(string path)
    {
        return path.Equals(CalibrationRootPath, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(CalibrationRootPath + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterestingObservedPath(string path)
    {
        return path.Equals(InterestingObservedRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(InterestingObservedRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('/', '\\').Trim('\\');
    }

    private static string CombinePath(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left + "\\" + right;
    }

    private static string? PtrToStringUni(char* value)
    {
        return value == null ? null : new string(value);
    }

    private static string? PtrToStringAnsi(byte* value)
    {
        return value == null ? null : Marshal.PtrToStringAnsi((nint)value);
    }

    private static string ReadUnicodeBuffer(char* value, uint* length)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (length == null)
        {
            return new string(value);
        }

        return new string(value, 0, (int)(*length));
    }

    private static string ReadAnsiBuffer(byte* value, uint* length)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (length == null)
        {
            return Marshal.PtrToStringAnsi((nint)value) ?? string.Empty;
        }

        return Encoding.ASCII.GetString(new ReadOnlySpan<byte>(value, checked((int)(*length))));
    }

    private static string DescribeRegistryData(uint* type, byte* data, uint* dataLength, bool ansiStrings)
    {
        var dataType = type == null ? 0u : *type;
        var byteCount = dataLength == null ? 0u : *dataLength;

        if (data == null || byteCount == 0)
        {
            return $"type={dataType}, data=<empty>";
        }

        return dataType switch
        {
            RegSz or RegExpandSz => $"type={dataType}, data=\"{ReadRegistryString(data, byteCount, ansiStrings)}\"",
            RegMultiSz => $"type={dataType}, data=\"{ReadRegistryMultiString(data, byteCount, ansiStrings)}\"",
            RegDword when byteCount >= sizeof(uint) => $"type={dataType}, data=0x{(*(uint*)data):X8}",
            RegQword when byteCount >= sizeof(ulong) => $"type={dataType}, data=0x{(*(ulong*)data):X16}",
            RegBinary => $"type={dataType}, data={FormatHex(data, byteCount)}",
            _ => $"type={dataType}, bytes={byteCount}, data={FormatHex(data, byteCount)}"
        };
    }

    private static string ReadRegistryString(byte* data, uint byteCount, bool ansiStrings)
    {
        if (ansiStrings)
        {
            var length = byteCount > 0 ? (int)(byteCount - 1) : 0;
            return Encoding.ASCII.GetString(new ReadOnlySpan<byte>(data, length)).TrimEnd('\0');
        }

        var charCount = (int)(byteCount / sizeof(char));
        if (charCount == 0)
        {
            return string.Empty;
        }

        return new string((char*)data, 0, charCount).TrimEnd('\0');
    }

    private static string ReadRegistryMultiString(byte* data, uint byteCount, bool ansiStrings)
    {
        var raw = ReadRegistryString(data, byteCount, ansiStrings);
        return raw.Replace("\0", "|", StringComparison.Ordinal);
    }

    private static string FormatHex(byte* data, uint byteCount)
    {
        var length = (int)Math.Min(byteCount, 32);
        if (length <= 0)
        {
            return "<empty>";
        }

        var bytes = new ReadOnlySpan<byte>(data, length);
        var builder = new StringBuilder(length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        if (byteCount > (uint)length)
        {
            builder.Append("...");
        }

        return builder.ToString();
    }

    private static int CopyUnicodeName(string value, char* destination, uint* destinationLength)
    {
        if (destinationLength == null)
        {
            return ErrorAccessDenied;
        }

        var requiredLength = (uint)value.Length;
        var availableLength = *destinationLength;
        if (destination == null || availableLength <= requiredLength)
        {
            *destinationLength = requiredLength + 1;
            return ErrorMoreData;
        }

        value.AsSpan().CopyTo(new Span<char>(destination, (int)availableLength));
        destination[requiredLength] = '\0';
        *destinationLength = requiredLength;
        return ErrorSuccess;
    }

    private static int CopyAnsiName(string value, byte* destination, uint* destinationLength)
    {
        if (destinationLength == null)
        {
            return ErrorAccessDenied;
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        var requiredLength = (uint)bytes.Length;
        var availableLength = *destinationLength;
        if (destination == null || availableLength <= requiredLength)
        {
            *destinationLength = requiredLength + 1;
            return ErrorMoreData;
        }

        bytes.CopyTo(new Span<byte>(destination, (int)availableLength));
        destination[requiredLength] = 0;
        *destinationLength = requiredLength;
        return ErrorSuccess;
    }

    private static int CopyUnicodeData(string value, byte* destination, uint* destinationLength)
    {
        if (destinationLength == null)
        {
            return ErrorAccessDenied;
        }

        var requiredBytes = (uint)((value.Length + 1) * sizeof(char));
        var availableBytes = *destinationLength;
        if (destination == null || availableBytes < requiredBytes)
        {
            *destinationLength = requiredBytes;
            return ErrorMoreData;
        }

        var destinationChars = new Span<char>((char*)destination, (int)(availableBytes / sizeof(char)));
        destinationChars.Clear();
        value.AsSpan().CopyTo(destinationChars);
        *destinationLength = requiredBytes;
        return ErrorSuccess;
    }

    private static int CopyAnsiData(string value, byte* destination, uint* destinationLength)
    {
        if (destinationLength == null)
        {
            return ErrorAccessDenied;
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        var requiredBytes = (uint)(bytes.Length + 1);
        var availableBytes = *destinationLength;
        if (destination == null || availableBytes < requiredBytes)
        {
            *destinationLength = requiredBytes;
            return ErrorMoreData;
        }

        var span = new Span<byte>(destination, (int)availableBytes);
        span.Clear();
        bytes.CopyTo(span);
        *destinationLength = requiredBytes;
        return ErrorSuccess;
    }

    private static uint GetMaxLength(IEnumerable<string> values)
    {
        var maxLength = 0;
        foreach (var value in values)
        {
            maxLength = Math.Max(maxLength, value.Length);
        }

        return (uint)maxLength;
    }

    private static uint GetMaxValueDataLength(IEnumerable<RegistryValue> values)
    {
        var maxBytes = 0;
        foreach (var value in values)
        {
            maxBytes = Math.Max(maxBytes, (value.Data.Length + 1) * sizeof(char));
        }

        return (uint)maxBytes;
    }

    private static string FormatStatus(int status) => status == ErrorSuccess ? "OK" : $"ERR({status})";

    private static void Trace(string message)
    {
        lock (Gate)
        {
            if (_traceCount >= 4096)
            {
                return;
            }

            _traceCount++;
        }

        VirtualCameraState.Log("CalibrationRegistryHook: " + message);
    }

    private static RegistryNode BuildVirtualRegistryTree()
    {
        var root = new RegistryNode("HKLM");
        var cinogyRoot = root
            .GetOrCreate("SOFTWARE")
            .GetOrCreate("CINOGY");

        AddRayCiRegistryDefaults(cinogyRoot.GetOrCreate("RayCi64"));

        var calibrationsNode = cinogyRoot.GetOrCreate("Calibrations");
        var mirroredCurrentUserTree = TryCloneCurrentUserCalibrationTree(calibrationsNode);
        var preferCurrentUserCameraTree =
            mirroredCurrentUserTree &&
            !string.Equals(
                Environment.GetEnvironmentVariable("ULTRON_RAYCI_FORCE_SYNTHETIC_CAMERA_TREE"),
                "1",
                StringComparison.OrdinalIgnoreCase);

        RegistryNode cameraRoot;
        if (!preferCurrentUserCameraTree)
        {
            calibrationsNode.SubKeys.Remove("Camera");
            cameraRoot = calibrationsNode.GetOrCreate("Camera");
            SeedFallbackCalibrationTree(cameraRoot);
        }
        else
        {
            cameraRoot = calibrationsNode.GetOrCreate("Camera");
            Trace("Normalizing mirrored current-user Camera tree for RayCi 2022 compatibility.");
        }

        NormalizeRayCi2022CameraKeys(cameraRoot);
        PruneRayCi2022CompatibilityKeys(cameraRoot);
        EnsureSensorCompatibilityMetadata(cameraRoot);
        EnsureMinimalCameraMetadataNodes(cameraRoot);

        return root;
    }

    private static bool TryCloneCurrentUserCalibrationTree(RegistryNode calibrationsNode)
    {
        try
        {
            using var sourceKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\CINOGY\Calibrations");
            if (sourceKey == null)
            {
                Trace("Current-user calibration tree missing; using synthetic fallback.");
                return false;
            }

            CopyRegistryKeyRecursive(sourceKey, calibrationsNode);
            Trace("Mirrored HKCU\\SOFTWARE\\CINOGY\\Calibrations into virtual HKLM.");
            return true;
        }
        catch (Exception ex)
        {
            Trace($"Calibration mirror failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void CopyRegistryKeyRecursive(RegistryKey sourceKey, RegistryNode targetNode)
    {
        foreach (var valueName in sourceKey.GetValueNames())
        {
            var value = sourceKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                continue;
            }

            switch (sourceKey.GetValueKind(valueName))
            {
                case RegistryValueKind.String:
                    targetNode.SetValue(valueName, value.ToString() ?? string.Empty, RegSz);
                    break;
                case RegistryValueKind.ExpandString:
                    targetNode.SetValue(valueName, value.ToString() ?? string.Empty, RegExpandSz);
                    break;
                case RegistryValueKind.MultiString:
                    targetNode.SetValue(valueName, string.Join("\0", (string[])value), RegMultiSz);
                    break;
            }
        }

        foreach (var subKeyName in sourceKey.GetSubKeyNames())
        {
            using var childKey = sourceKey.OpenSubKey(subKeyName);
            if (childKey == null)
            {
                continue;
            }

            CopyRegistryKeyRecursive(childKey, targetNode.GetOrCreate(subKeyName));
        }
    }

    private static void SeedFallbackCalibrationTree(RegistryNode cameraRoot)
    {
        foreach (var alias in EnumerateCameraKeyAliases())
        {
            var cameraNode = cameraRoot.GetOrCreate(alias.KeyName);
            if (string.Equals(alias.KeyName, SensorKeyName, StringComparison.OrdinalIgnoreCase))
            {
                AddBase1201Calibration(cameraNode);
            }

            AddLicensedCameraCalibration(cameraNode, alias);
        }
    }

    private static void NormalizeRayCi2022CameraKeys(RegistryNode cameraRoot)
    {
        var sensorTemplate = FindFirstExistingCameraNode(cameraRoot, SensorKeyName);
        EnsureCameraAliasClone(cameraRoot, SensorKeyName, sensorTemplate);

        var rawTemplate = FindFirstExistingCameraNode(
            cameraRoot,
            VirtualCameraState.DisplaySerialShort,
            ListedSerialKeyName,
            ListedFullCameraKeyName,
            ShouldExposeReverseCompatibilityAliases() ? ListedReverseFullCameraKeyName : string.Empty);

        var officialTemplate = FindFirstExistingCameraNode(
            cameraRoot,
            FullCameraKeyName,
            ShouldExposeReverseCompatibilityAliases() ? ReverseFullCameraKeyName : string.Empty,
            LicensedCameraKeyName,
            VirtualCameraState.UeyeSerial) ?? rawTemplate;

        var canonicalPerCameraAlias = new CameraKeyAlias(
            KeyName: LicensedCameraKeyName,
            IncludePerCameraMetadata: true,
            RegistryName: VirtualCameraState.ReportedCalibrationRegistryName,
            DeviceSerial: VirtualCameraState.DisplaySerial,
            DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
            CameraSerial: VirtualCameraState.UeyeSerial,
            GuidLow: LicensedCameraKeyName,
            UseMinimalMetadataShape: true);

        var listedPerCameraAlias = canonicalPerCameraAlias with
        {
            // RayCi 2022 probes the display-serial-derived key name first, but it
            // still keeps the registry-facing model name on the camera entry itself
            // while exposing the CinCam equipment branch underneath it.
            KeyName = ListedFullCameraKeyName,
            RegistryName = VirtualCameraState.ReportedCalibrationRegistryName,
            CameraSerial = VirtualCameraState.ReportedListSerial,
            GuidLow = ListedSerialKeyName,
            UseMinimalMetadataShape = false
        };

        if (cameraRoot.SubKeys.TryGetValue(SensorKeyName, out var sensorNode))
        {
            ResetNode(sensorNode);
            AddBase1201Calibration(sensorNode);
            AddLicensedCameraCalibration(
                sensorNode,
                canonicalPerCameraAlias with
                {
                    KeyName = SensorKeyName,
                    IncludePerCameraMetadata = false,
                    UseMinimalMetadataShape = false
                });
        }

        foreach (var keyName in new[]
                 {
                     FullCameraKeyName,
                     LicensedCameraKeyName,
                     VirtualCameraState.UeyeSerial
                 })
        {
            ReplaceCameraAliasClone(cameraRoot, keyName, officialTemplate);

            if (cameraRoot.SubKeys.TryGetValue(keyName, out var officialNode))
            {
                NormalizeElCameraCalibration(
                    officialNode,
                    canonicalPerCameraAlias with
                    {
                        KeyName = keyName
                    });
            }
        }

        var listedTemplate = FindFirstExistingCameraNode(
                                 cameraRoot,
                                 ListedFullCameraKeyName,
                                 ShouldExposeReverseCompatibilityAliases() ? ListedReverseFullCameraKeyName : string.Empty,
                                 ListedSerialKeyName,
                                 VirtualCameraState.DisplaySerialShort)
                             ?? rawTemplate
                             ?? officialTemplate;

        foreach (var keyName in new[]
                 {
                     ListedFullCameraKeyName,
                     ListedSerialKeyName,
                     VirtualCameraState.DisplaySerialShort
                 })
        {
            ReplaceCameraAliasClone(cameraRoot, keyName, listedTemplate);

            if (cameraRoot.SubKeys.TryGetValue(keyName, out var listedNode))
            {
                NormalizeElCameraCalibration(
                    listedNode,
                    listedPerCameraAlias with
                    {
                        KeyName = keyName
                    });
            }
        }

    }

    private static void PruneRayCi2022CompatibilityKeys(RegistryNode cameraRoot)
    {
        if (ShouldExposeExtendedCompatibilityKeys())
        {
            return;
        }

        var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SensorKeyName,
            ListedSerialKeyName,
            ListedFullCameraKeyName
        };

        foreach (var keyName in cameraRoot.SubKeys.Keys.ToArray())
        {
            if (!allowedKeys.Contains(keyName))
            {
                cameraRoot.SubKeys.Remove(keyName);
            }
        }
    }

    private static void NormalizeBase1201SensorCalibration(RegistryNode cameraNode)
    {
        ResetNode(cameraNode);

        cameraNode.SetValue("AutoExposure Max", "0.9");
        cameraNode.SetValue("AutoExposure Min", "0.5");
        cameraNode.SetValue("BitDepth", "max");
        cameraNode.SetValue("ColorFormat", "Y16");
        cameraNode.SetValue("Brightness Factor", "0.000679347826");
        cameraNode.SetValue("Brightness Val", "64");
        cameraNode.SetValue("BufferCnt", "8");
        cameraNode.SetValue("CameraMode", "0");
        cameraNode.SetValue("Crop Bottom", "1");
        cameraNode.SetValue("Crop Top", "1");
        cameraNode.SetValue("Equipment", BaseCalibrationName);
        cameraNode.SetValue("FrameRate", "20");
        cameraNode.SetValue("Name", VirtualCameraState.RegistryModel);
        cameraNode.SetValue("PixelClock", "34");
        cameraNode.SetValue("Technology", "CMOS");
        cameraNode.SetValue("Triggering", "0");

        var equipmentNode = cameraNode.GetOrCreate(BaseCalibrationName);
        equipmentNode.SetValue("Icon", "CinCam CMOS");
        equipmentNode.SetValue("Calibration ID", "plain");

        var plainNode = equipmentNode.GetOrCreate("plain");
        plainNode.SetValue("AOI CenterX", "0");
        plainNode.SetValue("AOI CenterY", "0");
        plainNode.SetValue("AOI RadiusX", "2.6");
        plainNode.SetValue("AOI RadiusY", "2.6");
        plainNode.SetValue("Exposure Times", Base1201ExposureTimesCsv);
        plainNode.SetValue("Gain", RayCiGainCsv);
        plainNode.SetValue("MirrorY", "1");
        plainNode.SetValue("ScaleX", "1");
        plainNode.SetValue("ScaleY", "1");
        plainNode.SetValue("Wavelength Max", "1150");
        plainNode.SetValue("Wavelength Min", "350");

        var cameraInfoNode = equipmentNode.GetOrCreate("~Camera");
        var alias = new CameraKeyAlias(
            SensorKeyName,
            IncludePerCameraMetadata: true,
            RegistryName: VirtualCameraState.RegistryModel,
            DeviceSerial: VirtualCameraState.DisplaySerial,
            DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
            CameraSerial: VirtualCameraState.DisplaySerialShort,
            GuidLow: ListedSerialKeyName,
            UseMinimalMetadataShape: false);
        PopulateFlattenedCameraParameters(cameraInfoNode, BaseCalibrationName, alias, includeIdentityMetadata: true);
        cameraInfoNode.SetValue("Calibration ID", "plain");
        cameraInfoNode.SetValue("Equipment", BaseCalibrationName);
        cameraInfoNode.SetValue("Name", VirtualCameraState.RegistryModel);
    }

    private static void NormalizeElCameraCalibration(RegistryNode cameraNode, CameraKeyAlias alias)
    {
        ResetNode(cameraNode);

        cameraNode.SetValue("AutoExposure Max", "0.9");
        cameraNode.SetValue("AutoExposure Min", "0.5");
        cameraNode.SetValue("BitDepth", "max");
        cameraNode.SetValue("Brightness Factor", "0.003408715 * 1.25");
        cameraNode.SetValue("Brightness Offset", "13");
        cameraNode.SetValue("Brightness Val", "20");
        cameraNode.SetValue("BufferCnt", "8");
        cameraNode.SetValue("Camera Group", "CinCam CMOS");
        cameraNode.SetValue("CameraMode", "0");
        cameraNode.SetValue("Crop Bottom", "0");
        cameraNode.SetValue("Crop Top", "0");
        cameraNode.SetValue("Equipment", VirtualCameraState.CalibrationName);
        cameraNode.SetValue("FrameRate", "15");
        cameraNode.SetValue("Name", GetCameraDisplayName(alias));
        cameraNode.SetValue("PixelClock", "34");
        cameraNode.SetValue("Technology", "CMOS");
        cameraNode.SetValue("Triggering", "0");
        cameraNode.SetValue("Device Serial", alias.DeviceSerial);
        cameraNode.SetValue("Device Serial Short", alias.DeviceSerialShort);
        cameraNode.SetValue("Calibration ID", "plain");

        foreach (var equipmentName in EnumerateCalibrationAliases(alias))
        {
            AddEquipmentCalibration(cameraNode, equipmentName, alias);
        }
    }

    private static IEnumerable<CameraKeyAlias> EnumerateRayCi2022CameraAliases()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Add(string keyName)
        {
            return seen.Add(keyName);
        }

        foreach (var keyName in new[]
                 {
                     FullCameraKeyName,
                     LicensedCameraKeyName,
                     VirtualCameraState.UeyeSerial
                 })
        {
            if (!Add(keyName))
            {
                continue;
            }

            yield return new CameraKeyAlias(
                keyName,
                IncludePerCameraMetadata: true,
                RegistryName: VirtualCameraState.ReportedCalibrationRegistryName,
                DeviceSerial: VirtualCameraState.DisplaySerial,
                DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                CameraSerial: VirtualCameraState.UeyeSerial,
                GuidLow: LicensedCameraKeyName,
                UseMinimalMetadataShape: true);
        }

        foreach (var keyName in new[]
                 {
                     VirtualCameraState.DisplaySerialShort,
                     ListedSerialKeyName,
                     ListedFullCameraKeyName
                 })
        {
            if (!Add(keyName))
            {
                continue;
            }

            yield return new CameraKeyAlias(
                keyName,
                IncludePerCameraMetadata: true,
                RegistryName: VirtualCameraState.ReportedCalibrationRegistryName,
                DeviceSerial: VirtualCameraState.DisplaySerial,
                DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                CameraSerial: VirtualCameraState.ReportedListSerial,
                GuidLow: ListedSerialKeyName,
                UseMinimalMetadataShape: false);
        }
    }

    private static void ResetNode(RegistryNode node)
    {
        node.SubKeys.Clear();
        node.Values.Clear();
    }

    private static void EnsureMinimalCameraMetadataNodes(RegistryNode cameraRoot)
    {
        foreach (var cameraNode in cameraRoot.SubKeys.Values)
        {
            if (!cameraNode.Values.ContainsKey("Calibration ID") &&
                cameraNode.SubKeys.Values.Any(subKey => subKey.SubKeys.ContainsKey("plain")))
            {
                cameraNode.SetValue("Calibration ID", "plain");
            }

            if (!cameraNode.Values.ContainsKey("Device Serial"))
            {
                continue;
            }

            foreach (var equipmentNode in cameraNode.SubKeys.Values)
            {
                if (string.Equals(equipmentNode.Name, "plain", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(equipmentNode.Name, "~Camera", StringComparison.OrdinalIgnoreCase) ||
                    !equipmentNode.SubKeys.ContainsKey("plain"))
                {
                    continue;
                }

                var metadataNode = equipmentNode.GetOrCreate("~Camera");
                SetValueIfMissing(metadataNode, "Calibration ID", GetPreferredValue(equipmentNode, cameraNode, "Calibration ID") ?? "plain");
                SetValueIfMissing(metadataNode, "Equipment", GetPreferredValue(equipmentNode, cameraNode, "Equipment") ?? equipmentNode.Name);
                SetValueIfMissing(metadataNode, "Name", GetPreferredValue(equipmentNode, cameraNode, "Name") ?? equipmentNode.Name);
                CopyValueIfPresent(metadataNode, equipmentNode, cameraNode, "Device Serial");
                CopyValueIfPresent(metadataNode, equipmentNode, cameraNode, "Device Serial Short");
            }
        }
    }

    private static RegistryNode? FindFirstExistingCameraNode(RegistryNode cameraRoot, params string[] keyNames)
    {
        foreach (var keyName in keyNames)
        {
            if (!string.IsNullOrWhiteSpace(keyName) &&
                cameraRoot.SubKeys.TryGetValue(keyName, out var node))
            {
                return node;
            }
        }

        return null;
    }

    private static void EnsureCameraAliasClone(RegistryNode cameraRoot, string keyName, RegistryNode? templateNode)
    {
        if (templateNode is null || cameraRoot.SubKeys.ContainsKey(keyName))
        {
            return;
        }

        var targetNode = cameraRoot.GetOrCreate(keyName);
        CopyNodeContents(templateNode, targetNode);
    }

    private static void ReplaceCameraAliasClone(RegistryNode cameraRoot, string keyName, RegistryNode? templateNode)
    {
        if (templateNode is null)
        {
            return;
        }

        var targetNode = cameraRoot.GetOrCreate(keyName);
        CopyNodeContents(templateNode, targetNode);
    }

    private static void CopyNodeContents(RegistryNode source, RegistryNode target)
    {
        ResetNode(target);

        foreach (var value in source.Values.Values)
        {
            target.SetValue(value.Name, value.Data, value.Type);
        }

        foreach (var child in source.SubKeys.Values)
        {
            var targetChild = target.GetOrCreate(child.Name);
            CopyNodeContents(child, targetChild);
        }
    }

    private static void EnsureSensorCompatibilityMetadata(RegistryNode cameraRoot)
    {
        if (!cameraRoot.SubKeys.TryGetValue(SensorKeyName, out var sensorNode) ||
            !sensorNode.SubKeys.TryGetValue(VirtualCameraState.CalibrationName, out var equipmentNode) ||
            !equipmentNode.SubKeys.ContainsKey("plain"))
        {
            return;
        }

        var metadataNode = equipmentNode.GetOrCreate("~Camera");
        // RayCi reopens the sensor-key compatibility path after it has already
        // consumed the richer 2022 listed-key metadata. Keep the sensor-key
        // block deliberately lean so it does not duplicate the full per-camera
        // shape during the second pass.
        ResetNode(metadataNode);
        PopulateMinimalCameraMetadata(
            metadataNode,
            VirtualCameraState.CalibrationName,
            new CameraKeyAlias(
                KeyName: SensorKeyName,
                IncludePerCameraMetadata: true,
                RegistryName: VirtualCameraState.CalibrationName,
                DeviceSerial: VirtualCameraState.DisplaySerial,
                DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                CameraSerial: VirtualCameraState.DisplaySerialShort,
                GuidLow: ListedSerialKeyName,
                UseMinimalMetadataShape: true));
        metadataNode.SetValue("Calibration ID", "plain");
        metadataNode.SetValue("Equipment", VirtualCameraState.CalibrationName);
        metadataNode.SetValue("Name", VirtualCameraState.CalibrationName);
        metadataNode.SetValue("Device Serial", VirtualCameraState.DisplaySerial);
        metadataNode.SetValue("Device Serial Short", VirtualCameraState.DisplaySerialShort);
        metadataNode.SetValue("Camera Serial", VirtualCameraState.DisplaySerialShort);
        metadataNode.SetValue("GUID High", SensorKeyName);
        metadataNode.SetValue("GUID Low", ListedSerialKeyName);
    }

    private static string? GetPreferredValue(RegistryNode primary, RegistryNode fallback, string valueName)
    {
        if (primary.Values.TryGetValue(valueName, out var primaryValue) &&
            !string.IsNullOrWhiteSpace(primaryValue.Data))
        {
            return primaryValue.Data;
        }

        if (fallback.Values.TryGetValue(valueName, out var fallbackValue) &&
            !string.IsNullOrWhiteSpace(fallbackValue.Data))
        {
            return fallbackValue.Data;
        }

        return null;
    }

    private static void CopyValueIfPresent(RegistryNode destination, RegistryNode primary, RegistryNode fallback, string valueName)
    {
        var value = GetPreferredValue(primary, fallback, valueName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            SetValueIfMissing(destination, valueName, value);
        }
    }

    private static void SetValueIfMissing(RegistryNode node, string valueName, string valueData, uint type = RegSz)
    {
        if (!node.Values.ContainsKey(valueName))
        {
            node.SetValue(valueName, valueData, type);
        }
    }

    private static void AppendRegistryNode(List<string> lines, RegistryNode node, int depth, int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var indent = new string(' ', depth * 2);
        lines.Add($"{indent}[{node.Name}]");

        foreach (var value in node.Values.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{indent}{value.Name}={value.Data}");
        }

        foreach (var child in node.SubKeys.Values.OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendRegistryNode(lines, child, depth + 1, maxDepth);
        }
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
        var equipmentNode = cameraNode.GetOrCreate(BaseCalibrationName);
        var plainNode = equipmentNode.GetOrCreate("plain");
        plainNode.SetValue("AOI CenterX", "0");
        plainNode.SetValue("AOI CenterY", "0");
        plainNode.SetValue("AOI RadiusX", "2.6");
        plainNode.SetValue("AOI RadiusY", "2.6");
        plainNode.SetValue("Exposure Times", Base1201ExposureTimesCsv);
        plainNode.SetValue("Gain", RayCiGainCsv);
        plainNode.SetValue("MirrorY", "1");
        plainNode.SetValue("ScaleX", "1");
        plainNode.SetValue("ScaleY", "1");
        plainNode.SetValue("Wavelength Max", "1150");
        plainNode.SetValue("Wavelength Min", "350");
    }

    private static void AddLicensedCameraCalibration(RegistryNode cameraNode, CameraKeyAlias alias)
    {
        cameraNode.SetValue("AutoExposure Max", "0.9");
        cameraNode.SetValue("AutoExposure Min", "0.5");
        cameraNode.SetValue("BitDepth", "max");
        cameraNode.SetValue("Brightness Factor", "0.003408715 * 1.25");
        cameraNode.SetValue("Brightness Offset", "13");
        cameraNode.SetValue("Brightness Val", "20");
        cameraNode.SetValue("BufferCnt", "8");
        cameraNode.SetValue("CameraMode", "0");
        cameraNode.SetValue("Crop Bottom", "0");
        cameraNode.SetValue("Crop Top", "0");
        cameraNode.SetValue("Equipment", VirtualCameraState.CalibrationName);
        cameraNode.SetValue("FrameRate", "15");
        cameraNode.SetValue("Name", GetCameraDisplayName(alias));
        cameraNode.SetValue("PixelClock", "34");
        cameraNode.SetValue("Technology", "CMOS");
        cameraNode.SetValue("Triggering", "0");
        cameraNode.SetValue("Camera Group", "CinCam CMOS");

        if (!alias.UseMinimalMetadataShape)
        {
            PopulateFlattenedCameraParameters(cameraNode, VirtualCameraState.CalibrationName, alias, includeIdentityMetadata: alias.IncludePerCameraMetadata);
        }

        if (alias.IncludePerCameraMetadata)
        {
            cameraNode.SetValue("Device Serial", alias.DeviceSerial);
            cameraNode.SetValue("Device Serial Short", alias.DeviceSerialShort);
            cameraNode.SetValue("Calibration ID", "plain");
        }

        foreach (var equipmentName in EnumerateCalibrationAliases(alias))
        {
            AddEquipmentCalibration(cameraNode, equipmentName, alias);
        }
    }

    private static void AddEquipmentCalibration(RegistryNode cameraNode, string equipmentName, CameraKeyAlias alias)
    {
        var equipmentNode = cameraNode.GetOrCreate(equipmentName);
        equipmentNode.SetValue(
            "Icon",
            string.Equals(equipmentName, VirtualCameraState.CalibrationName, StringComparison.Ordinal)
                ? "CinCam CMOS"
                : alias.RegistryName);
        equipmentNode.SetValue("Serial Number Template", VirtualCameraState.SerialTemplate);
        equipmentNode.SetValue("Device Serial", alias.DeviceSerial);
        equipmentNode.SetValue("Device Serial Short", alias.DeviceSerialShort);
        equipmentNode.SetValue("Calibration ID", "plain");

        var plainNode = equipmentNode.GetOrCreate("plain");
        plainNode.SetValue("AOI CenterX", "0");
        plainNode.SetValue("AOI CenterY", "0");
        plainNode.SetValue("AOI RadiusX", "2.6");
        plainNode.SetValue("AOI RadiusY", "2.6");
        plainNode.SetValue("Exposure Times", RayCiElExposureTimesCsv);
        plainNode.SetValue("Gain", RayCiGainCsv);
        plainNode.SetValue("MirrorY", "1");
        plainNode.SetValue("ScaleX", "1");
        plainNode.SetValue("ScaleY", "1");
        plainNode.SetValue("Wavelength Max", "1150");
        plainNode.SetValue("Wavelength Min", "350");

        if (!alias.IncludePerCameraMetadata)
        {
            return;
        }

        var cameraInfoNode = equipmentNode.GetOrCreate("~Camera");
        ResetNode(cameraInfoNode);

        if (alias.UseMinimalMetadataShape || !ShouldExposeVerboseEquipmentMetadata())
        {
            PopulateMinimalCameraMetadata(cameraInfoNode, equipmentName, alias);
            cameraInfoNode.SetValue("Camera Serial", alias.CameraSerial);
            cameraInfoNode.SetValue("GUID High", SensorKeyName);
            cameraInfoNode.SetValue("GUID Low", alias.GuidLow);
            return;
        }

        PopulateFlattenedCameraParameters(cameraInfoNode, equipmentName, alias, includeIdentityMetadata: true);
        cameraInfoNode.SetValue("Calibration ID", "plain");
        cameraInfoNode.SetValue("Device Serial", alias.DeviceSerial);
        cameraInfoNode.SetValue("Device Serial Short", alias.DeviceSerialShort);
        cameraInfoNode.SetValue("Equipment", equipmentName);
        cameraInfoNode.SetValue("Name", GetCameraDisplayName(alias));
    }

    private static void PopulateFlattenedCameraParameters(RegistryNode node, string equipmentName, CameraKeyAlias alias, bool includeIdentityMetadata)
    {
        node.SetValue("Camera Group", "CinCam CMOS");
        node.SetValue("Technology", "CMOS");
        node.SetValue("Triggering", "0");
        node.SetValue("BufferCnt", "8");
        node.SetValue("BitDepth", "max");
        node.SetValue("ColorFormat", "Y16");
        node.SetValue("CameraMode", "0");
        node.SetValue("Low Noise Binning", "0");
        node.SetValue("Dual-Tap", "0");
        node.SetValue("Four-Tap", "0");
        node.SetValue("PixelSizeX", "5.2");
        node.SetValue("PixelSizeY", "5.2");
        node.SetValue("DeInterlace", "0");
        node.SetValue("AOI X0", "0");
        node.SetValue("AOI Y0", "0");
        node.SetValue("AOI Width", UeyeNative.DefaultWidth.ToString(CultureInfo.InvariantCulture));
        node.SetValue("AOI Height", UeyeNative.DefaultHeight.ToString(CultureInfo.InvariantCulture));
        node.SetValue("Crop Left", "0");
        node.SetValue("Crop Right", "0");
        node.SetValue("Crop Top", "0");
        node.SetValue("Crop Bottom", "0");
        node.SetValue("FrameRate", "15");
        node.SetValue("FrameRate_2x2", "15");
        node.SetValue("FrameRate_4x4", "15");
        node.SetValue("FrameRate_8x8", "15");
        node.SetValue("Bandwidth", "480");
        node.SetValue("PixelClock", "34");
        node.SetValue("PixelClock_2x2", "34");
        node.SetValue("PixelClock_4x4", "34");
        node.SetValue("PixelClock_8x8", "34");
        node.SetValue("AutoExposure Min", "0.5");
        node.SetValue("AutoExposure Max", "0.9");
        node.SetValue("Gain Fak", "1");
        node.SetValue("Gain Factor", "100");
        node.SetValue("Gain Val", "0");
        node.SetValue("Brightness Fak", "0.003408715 * 1.25");
        node.SetValue("Brightness Factor", "0.003408715 * 1.25");
        node.SetValue("Brightness Offset", "13");
        node.SetValue("Brightness Val", "20");
        node.SetValue("Exposure Time", "30000");
        node.SetValue("Time Offset", "0");
        node.SetValue("Gamma", "1");
        node.SetValue("LUT Exp", "0");
        node.SetValue("HDR Enable", "0");
        node.SetValue("List Index", "0");
        node.SetValue("Equipment", equipmentName);
        node.SetValue("Name", GetCameraDisplayName(alias));

        if (!includeIdentityMetadata)
        {
            return;
        }

        node.SetValue("Device Serial", alias.DeviceSerial);
        node.SetValue("Device Serial Short", alias.DeviceSerialShort);
        node.SetValue("Camera Serial", alias.CameraSerial);
        node.SetValue("Camera-Name", equipmentName);
        node.SetValue("Camera Name", equipmentName);
        node.SetValue("GUID High", SensorKeyName);
        node.SetValue("GUID Low", alias.GuidLow);
    }

    private static void PopulateMinimalCameraMetadata(RegistryNode node, string equipmentName, CameraKeyAlias alias)
    {
        ResetNode(node);
        node.SetValue("Device Serial", alias.DeviceSerial);
        node.SetValue("Device Serial Short", alias.DeviceSerialShort);
        node.SetValue("Calibration ID", "plain");
        node.SetValue("Equipment", equipmentName);
        node.SetValue("Name", GetCameraDisplayName(alias));
    }

    private static string GetCameraDisplayName(CameraKeyAlias alias)
    {
        return alias.UseMinimalMetadataShape
            ? VirtualCameraState.CalibrationName
            : alias.RegistryName;
    }

    private static IEnumerable<string> EnumerateCalibrationAliases(CameraKeyAlias alias)
    {
        yield return VirtualCameraState.CalibrationName;

        if (alias.UseMinimalMetadataShape || !ShouldExposeModelCompatibilityAliases())
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            VirtualCameraState.CalibrationName
        };

        foreach (var calibrationAlias in new[]
                 {
                     VirtualCameraState.RegistryModel,
                     VirtualCameraState.RegistryShortModel,
                     VirtualCameraState.FullModel,
                     VirtualCameraState.ShortModel
                 })
        {
            if (!string.IsNullOrWhiteSpace(calibrationAlias) && seen.Add(calibrationAlias))
            {
                yield return calibrationAlias;
            }
        }
    }

    private static IEnumerable<CameraKeyAlias> EnumerateCameraKeyAliases()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Add(string keyName)
        {
            return seen.Add(keyName);
        }

        yield return new CameraKeyAlias(
            SensorKeyName,
            IncludePerCameraMetadata: false,
            RegistryName: VirtualCameraState.RegistryModel,
            DeviceSerial: VirtualCameraState.DisplaySerial,
            DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
            CameraSerial: VirtualCameraState.DisplaySerialShort,
            GuidLow: ListedSerialKeyName,
            UseMinimalMetadataShape: false);

        foreach (var keyName in new[]
                 {
                     FullCameraKeyName,
                     LicensedCameraKeyName,
                     VirtualCameraState.UeyeSerial
                 })
        {
            if (Add(keyName))
            {
                yield return new CameraKeyAlias(
                    keyName,
                    IncludePerCameraMetadata: true,
                    RegistryName: VirtualCameraState.RegistryModel,
                    DeviceSerial: VirtualCameraState.DisplaySerial,
                    DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                    CameraSerial: VirtualCameraState.UeyeSerial,
                    GuidLow: LicensedCameraKeyName,
                    UseMinimalMetadataShape: true);
            }
        }

        foreach (var keyName in new[]
                 {
                     VirtualCameraState.DisplaySerialShort,
                     ListedSerialKeyName,
                     ListedFullCameraKeyName
                 })
        {
            if (Add(keyName))
            {
                yield return new CameraKeyAlias(
                    keyName,
                    IncludePerCameraMetadata: true,
                    RegistryName: VirtualCameraState.RegistryModel,
                    DeviceSerial: VirtualCameraState.DisplaySerial,
                    DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                    CameraSerial: VirtualCameraState.ReportedListSerial,
                    GuidLow: ListedSerialKeyName,
                    UseMinimalMetadataShape: false);
            }
        }

        if (ShouldExposeCapturedCompatibilityAliases())
        {
            foreach (var keyName in new[]
                     {
                         CapturedFullCameraKeyName,
                         ShouldExposeReverseCompatibilityAliases() ? CapturedReverseFullCameraKeyName : string.Empty,
                         CapturedSerialKeyName,
                         VirtualCameraState.CapturedRawSerial,
                         VirtualCameraState.BoardSerial
                     })
            {
                if (!string.IsNullOrWhiteSpace(keyName) && Add(keyName))
                {
                    yield return new CameraKeyAlias(
                        keyName,
                        IncludePerCameraMetadata: true,
                        RegistryName: VirtualCameraState.CalibrationName,
                        DeviceSerial: VirtualCameraState.DisplaySerial,
                        DeviceSerialShort: VirtualCameraState.DisplaySerialShort,
                        CameraSerial: VirtualCameraState.CapturedRawSerial,
                        GuidLow: CapturedSerialKeyName,
                        UseMinimalMetadataShape: true);
                }
            }
        }
    }

    private static bool ShouldExposeCapturedCompatibilityAliases()
    {
        return string.Equals(
                   Environment.GetEnvironmentVariable("ULTRON_RAYCI_LIST_SERIAL_STYLE"),
                   "captured",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   Environment.GetEnvironmentVariable("ULTRON_RAYCI_EXPOSE_CAPTURED_ALIASES"),
                   "1",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExposeModelCompatibilityAliases()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ULTRON_RAYCI_EXPOSE_MODEL_ALIASES"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExposeVerboseEquipmentMetadata()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ULTRON_RAYCI_EXPOSE_VERBOSE_CAMERA_METADATA"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExposeReverseCompatibilityAliases()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ULTRON_RAYCI_EXPOSE_REVERSE_ALIASES"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExposeExtendedCompatibilityKeys()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ULTRON_RAYCI_EXPOSE_EXTENDED_CAMERA_KEYS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFullCameraKeyName(string decimalSerial)
    {
        var serialValue = uint.Parse(decimalSerial, CultureInfo.InvariantCulture);
        var cameraId = ((ulong)UeyeNative.IS_SENSOR_UI1545_M << 32) | serialValue;
        return cameraId.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static string BuildSerialKeyName(string decimalSerial)
    {
        var serialValue = uint.Parse(decimalSerial, CultureInfo.InvariantCulture);
        return serialValue.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static nint HkeyCurrentUser => unchecked((nint)(int)0x80000001);
    private static nint HkeyLocalMachine => unchecked((nint)(int)0x80000002);

    private readonly record struct CameraKeyAlias(
        string KeyName,
        bool IncludePerCameraMetadata,
        string RegistryName,
        string DeviceSerial,
        string DeviceSerialShort,
        string CameraSerial,
        string GuidLow,
        bool UseMinimalMetadataShape);

    [StructLayout(LayoutKind.Sequential)]
    private struct ImageImportDescriptor
    {
        public uint OriginalFirstThunk;
        public uint TimeDateStamp;
        public uint ForwarderChain;
        public uint Name;
        public uint FirstThunk;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    private sealed class OpenVirtualKey
    {
        public OpenVirtualKey(string path, RegistryNode node)
        {
            Path = path;
            Node = node;
        }

        public string Path { get; }
        public RegistryNode Node { get; }
    }

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
