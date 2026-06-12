using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VirtualUEyeProxy;

namespace VirtualFGCameraProxy;

internal static unsafe class Forwarder
{
    private sealed class SyntheticVtableState
    {
        public required nint OriginalVtable { get; init; }
        public required nint SyntheticVtable { get; init; }
        public required int PointerCount { get; init; }
    }

    private const string IdentityStyleCaptured = "captured";
    private const string IdentityStyleRegistry = "registry";
    private const string ListSerialStyleCaptured = "captured";
    private const string ListSerialStyleLicensed = "licensed";
    private static readonly object LoadSync = new();
    private static readonly object RegisterSync = new();
    private static readonly ConcurrentDictionary<string, nint> ExportCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<nuint, byte> SyntheticConnectedCameras = new();
    private static readonly ConcurrentDictionary<nuint, SyntheticVtableState> SyntheticVtables = new();
    private static readonly byte[] RegisterSpace = BuildRegisterSpace();
    private static readonly string IdentityStyle = ReadIdentityStyle();
    private static readonly string ListSerialStyle = ReadListSerialStyle();
    private static readonly bool ForceInitSuccess = ReadBoolEnvironmentVariable(
        "ULTRON_RAYCI_FGCAMERA_FORCE_INIT_SUCCESS",
        defaultValue: true);
    private static readonly bool ForceNodeListSuccess = ReadBoolEnvironmentVariable(
        "ULTRON_RAYCI_FGCAMERA_FORCE_NODELIST_SUCCESS",
        defaultValue: true);
    private static readonly bool ForceConnectSuccess = ReadBoolEnvironmentVariable(
        "ULTRON_RAYCI_FGCAMERA_FORCE_CONNECT_SUCCESS",
        defaultValue: true);
    private static readonly bool ForceDeviceNameSuccess = ReadBoolEnvironmentVariable(
        "ULTRON_RAYCI_FGCAMERA_FORCE_DEVICE_NAME_SUCCESS",
        defaultValue: true);
    private static bool _captureOpen;
    private static bool _streamPrepared;
    private static bool _streamRunning;
    private static int _pendingTransientStatusReads;

    private static nint _moduleHandle;
    private static nint _selfModuleHandle;
    private static string? _loadedPath;
    private static IReadOnlyDictionary<nint, (nint ProxyAddress, string ExportName)>? _virtualDispatchMap;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static nuint Call(
        string exportName,
        nuint a1 = 0,
        nuint a2 = 0,
        nuint a3 = 0,
        nuint a4 = 0,
        nuint a5 = 0,
        nuint a6 = 0,
        nuint a7 = 0,
        nuint a8 = 0)
    {
        try
        {
            CalibrationRegistryOverride.TryInstall();
            TraceSpecialCall(exportName, "PRE", a1, a2, a3, a4, a5, a6, a7, a8);
            if (TryHandleSyntheticCall(exportName, a1, a2, a3, a4, a5, a6, a7, a8, out var syntheticResult))
            {
                ProxyLog.Write($"RET  {exportName} -> {ProxyLog.Hex(syntheticResult)}");
                return syntheticResult;
            }

            var proc = ResolveExport(exportName);
            if (proc == 0)
            {
                ProxyLog.Write($"ResolveExport failed: {exportName}");
                return 0;
            }

            if (string.Equals(exportName, "??1CFGCamera@@UEAA@XZ", StringComparison.Ordinal))
            {
                TryRestoreOriginalVtable((nint)a1);
            }

            ProxyLog.Write(
                $"CALL {exportName}(" +
                $"{ProxyLog.Hex(a1)}, {ProxyLog.Hex(a2)}, {ProxyLog.Hex(a3)}, {ProxyLog.Hex(a4)}, " +
                $"{ProxyLog.Hex(a5)}, {ProxyLog.Hex(a6)}, {ProxyLog.Hex(a7)}, {ProxyLog.Hex(a8)})");

            var function = (delegate* unmanaged<nuint, nuint, nuint, nuint, nuint, nuint, nuint, nuint, nuint>)proc;
            var result = function(a1, a2, a3, a4, a5, a6, a7, a8);
            TraceSpecialCall(exportName, "POST", a1, a2, a3, a4, a5, a6, a7, a8);
            if (string.Equals(exportName, "??0CFGCamera@@QEAA@XZ", StringComparison.Ordinal))
            {
                TryInstallSyntheticVtable((nint)a1);
                TraceSpecialCall(exportName, "OVR", a1, a2, a3, a4, a5, a6, a7, a8);
            }

            result = ApplySyntheticOverrides(exportName, result, a1, a2, a3, a4, a5, a6, a7, a8);
            if (string.Equals(exportName, "??1CFGCamera@@UEAA@XZ", StringComparison.Ordinal))
            {
                ReleaseSyntheticVtable((nint)a1);
            }

            ProxyLog.Write($"RET  {exportName} -> {ProxyLog.Hex(result)}");
            return result;
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"EXC  {exportName}: {ex}");
            return 0;
        }
    }

    private static bool TryHandleSyntheticCall(
        string exportName,
        nuint a1,
        nuint a2,
        nuint a3,
        nuint a4,
        nuint a5,
        nuint a6,
        nuint a7,
        nuint a8,
        out nuint result)
    {
        if (ForceInitSuccess &&
            string.Equals(exportName, "FGInitModule", StringComparison.Ordinal))
        {
            ProxyLog.Write("SYN  FGInitModule -> synthetic success");
            result = 0;
            return true;
        }

        if (ForceNodeListSuccess &&
            string.Equals(exportName, "FGGetNodeList", StringComparison.Ordinal))
        {
            result = TryPopulateSyntheticNodeList(a1, a2, a3) ? 0u : 0x3EAu;
            ProxyLog.Write($"SYN  FGGetNodeList -> {ProxyLog.Hex(result)}");
            TraceSpecialCall(exportName, "SYN", a1, a2, a3, a4, a5, a6, a7, a8);
            return true;
        }

        if (ForceConnectSuccess &&
            string.Equals(exportName, "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z", StringComparison.Ordinal))
        {
            SyntheticConnectedCameras[a1] = 0;
            lock (RegisterSync)
            {
                ResetRuntimeStateLocked();
            }
            ProxyLog.Write($"SYN  Connect -> synthetic success for {ProxyLog.Hex(a1)}");
            TraceSpecialCall(exportName, "SYN", a1, a2, a3, a4, a5, a6, a7, a8);
            result = 0;
            return true;
        }

        if (ForceDeviceNameSuccess &&
            SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z", StringComparison.Ordinal))
        {
            TryWriteAnsiString(a2, (int)a3, ReportedDeviceSerial);
            TryWriteAnsiString(a4, 64, ReportedDeviceModel);
            ProxyLog.Write(
                $"SYN  GetDeviceName -> synthetic identity for {ProxyLog.Hex(a1)} " +
                $"(model='{ReportedDeviceModel}', serial='{ReportedDeviceSerial}', identityStyle={IdentityStyle}, listSerialStyle={ListSerialStyle})");
            TraceSpecialCall(exportName, "SYN", a1, a2, a3, a4, a5, a6, a7, a8);
            result = 0;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?ReadRegister@CFGCamera@@UEAAKKPEAK@Z", StringComparison.Ordinal))
        {
            result = TryReadRegister(a2, a3) ? 0u : 0x3EAu;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?WriteRegister@CFGCamera@@UEAAKKK@Z", StringComparison.Ordinal))
        {
            result = TryWriteRegister(a2, a3) ? 0u : 0x3EAu;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z", StringComparison.Ordinal))
        {
            result = TryReadBlock(a2, a3, a4) ? 0u : 0x3EAu;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?WriteBlock@CFGCamera@@UEAAKKPEAEK@Z", StringComparison.Ordinal))
        {
            result = TryWriteBlock(a2, a3, a4) ? 0u : 0x3EAu;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?OpenCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            lock (RegisterSync)
            {
                _captureOpen = true;
                ProxyLog.Write($"SYN  OpenCapture -> capture open (prepared={_streamPrepared}, running={_streamRunning})");
            }

            result = 0;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?CloseCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            lock (RegisterSync)
            {
                _captureOpen = false;
                _streamRunning = false;
                _pendingTransientStatusReads = 0;
                ProxyLog.Write("SYN  CloseCapture -> capture closed");
            }

            result = 0;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?StartDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            lock (RegisterSync)
            {
                _streamPrepared = true;
                _streamRunning = true;
                ProxyLog.Write("SYN  StartDevice -> live state entered");
            }

            result = 0;
            return true;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?StopDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            lock (RegisterSync)
            {
                _streamRunning = false;
                _pendingTransientStatusReads = 0;
                ProxyLog.Write("SYN  StopDevice -> live state left");
            }

            result = 0;
            return true;
        }

        if (string.Equals(exportName, "?Disconnect@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            SyntheticConnectedCameras.TryRemove(a1, out _);
            lock (RegisterSync)
            {
                ResetRuntimeStateLocked();
            }
            ProxyLog.Write($"SYN  Disconnect -> synthetic success for {ProxyLog.Hex(a1)}");
            result = 0;
            return true;
        }

        result = 0;
        return false;
    }

    private static nint ResolveExport(string exportName)
    {
        EnsureLoaded();
        if (_moduleHandle == 0)
        {
            return 0;
        }

        return ExportCache.GetOrAdd(exportName, static (name, module) =>
        {
            var address = GetProcAddress(module, name);
            if (address == 0)
            {
                var error = Marshal.GetLastWin32Error();
                ProxyLog.Write($"GetProcAddress failed: {name} (Win32={error})");
            }

            return address;
        }, _moduleHandle);
    }

    private static nint ResolveSelfExport(string exportName)
    {
        if (_selfModuleHandle == 0)
        {
            _selfModuleHandle = GetModuleHandleW("FGCamera.dll");
            if (_selfModuleHandle == 0)
            {
                var fullPath = Path.Combine(AppContext.BaseDirectory, "FGCamera.dll");
                _selfModuleHandle = GetModuleHandleW(fullPath);
            }

            if (_selfModuleHandle == 0)
            {
                ProxyLog.Write($"GetModuleHandleW failed for FGCamera.dll (Win32={Marshal.GetLastWin32Error()})");
                return 0;
            }
        }

        var address = GetProcAddress(_selfModuleHandle, exportName);
        if (address == 0)
        {
            ProxyLog.Write($"GetProcAddress self failed: {exportName} (Win32={Marshal.GetLastWin32Error()})");
        }

        return address;
    }

    private static void EnsureLoaded()
    {
        if (_moduleHandle != 0)
        {
            return;
        }

        lock (LoadSync)
        {
            if (_moduleHandle != 0)
            {
                return;
            }

            foreach (var candidate in GetCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                var handle = LoadLibraryW(candidate);
                if (handle != 0)
                {
                    _moduleHandle = handle;
                    _loadedPath = candidate;
                    ProxyLog.Write($"Loaded original FGCamera from: {_loadedPath}");
                    return;
                }

                var error = new Win32Exception(Marshal.GetLastWin32Error());
                ProxyLog.Write($"LoadLibrary failed: {candidate} ({error.Message})");
            }

            ProxyLog.Write("Unable to load any original FGCamera target.");
        }
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var overridePath = Environment.GetEnvironmentVariable("ULTRON_RAYCI_FGCAMERA_ORIGINAL");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return overridePath;
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield return Path.Combine(baseDirectory, "FGCamera.original.dll");
        }

        var systemDirectory = Environment.SystemDirectory;
        if (!string.IsNullOrWhiteSpace(systemDirectory))
        {
            yield return Path.Combine(systemDirectory, "FGCamera.dll");
        }
    }

    private static void TryInstallSyntheticVtable(nint objectAddress)
    {
        if (objectAddress == 0 || _moduleHandle == 0)
        {
            return;
        }

        try
        {
            var originalVtable = Marshal.ReadIntPtr(objectAddress);
            if (originalVtable == 0)
            {
                ProxyLog.Write("OVR  CFGCamera vtable install skipped: null original vptr");
                return;
            }

            var dispatchMap = GetVirtualDispatchMap();
            if (dispatchMap.Count == 0)
            {
                ProxyLog.Write("OVR  CFGCamera vtable install skipped: no dispatch map entries");
                return;
            }

            var pointerSize = IntPtr.Size;
            const int maxPointerCount = 32;
            var buffer = new byte[maxPointerCount * pointerSize];
            if (!ReadProcessMemory(GetCurrentProcess(), originalVtable, buffer, (nuint)buffer.Length, out var bytesRead))
            {
                ProxyLog.Write($"OVR  CFGCamera vtable copy failed (Win32={Marshal.GetLastWin32Error()})");
                return;
            }

            var pointerCount = (int)(bytesRead / (nuint)pointerSize);
            if (pointerCount <= 0)
            {
                ProxyLog.Write("OVR  CFGCamera vtable install skipped: empty table");
                return;
            }

            var matchedSlots = new List<string>();
            for (var index = 0; index < pointerCount; index++)
            {
                var slotOffset = index * pointerSize;
                nint originalSlot = pointerSize == 8
                    ? unchecked((nint)BitConverter.ToInt64(buffer, slotOffset))
                    : new nint(BitConverter.ToInt32(buffer, slotOffset));
                if (!dispatchMap.TryGetValue(originalSlot, out var replacement))
                {
                    continue;
                }

                if (pointerSize == 8)
                {
                    BitConverter.GetBytes(replacement.ProxyAddress.ToInt64()).CopyTo(buffer, slotOffset);
                }
                else
                {
                    BitConverter.GetBytes(replacement.ProxyAddress.ToInt32()).CopyTo(buffer, slotOffset);
                }

                matchedSlots.Add($"slot{index}:{replacement.ExportName}");
            }

            if (matchedSlots.Count == 0)
            {
                ProxyLog.Write("OVR  CFGCamera vtable install found no matching virtual exports");
                return;
            }

            var syntheticVtable = Marshal.AllocHGlobal(pointerCount * pointerSize);
            Marshal.Copy(buffer, 0, syntheticVtable, pointerCount * pointerSize);
            Marshal.WriteIntPtr(objectAddress, syntheticVtable);

            SyntheticVtables[(nuint)objectAddress] = new SyntheticVtableState
            {
                OriginalVtable = originalVtable,
                SyntheticVtable = syntheticVtable,
                PointerCount = pointerCount,
            };

            ProxyLog.Write(
                $"OVR  CFGCamera vtable redirected for {ProxyLog.Hex((nuint)objectAddress)} " +
                $"original={ProxyLog.Hex((nuint)originalVtable)} synthetic={ProxyLog.Hex((nuint)syntheticVtable)} " +
                $"matches={string.Join(", ", matchedSlots)}");
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"OVR  CFGCamera vtable install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryRestoreOriginalVtable(nint objectAddress)
    {
        if (objectAddress == 0)
        {
            return;
        }

        if (!SyntheticVtables.TryGetValue((nuint)objectAddress, out var state))
        {
            return;
        }

        try
        {
            Marshal.WriteIntPtr(objectAddress, state.OriginalVtable);
            ProxyLog.Write(
                $"OVR  CFGCamera vtable restored for {ProxyLog.Hex((nuint)objectAddress)} -> " +
                $"{ProxyLog.Hex((nuint)state.OriginalVtable)}");
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"OVR  CFGCamera vtable restore failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReleaseSyntheticVtable(nint objectAddress)
    {
        if (objectAddress == 0)
        {
            return;
        }

        if (!SyntheticVtables.TryRemove((nuint)objectAddress, out var state))
        {
            return;
        }

        try
        {
            Marshal.FreeHGlobal(state.SyntheticVtable);
            ProxyLog.Write(
                $"OVR  CFGCamera synthetic vtable freed for {ProxyLog.Hex((nuint)objectAddress)} " +
                $"({ProxyLog.Hex((nuint)state.SyntheticVtable)})");
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"OVR  CFGCamera synthetic vtable free failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<nint, (nint ProxyAddress, string ExportName)> GetVirtualDispatchMap()
    {
        if (_virtualDispatchMap is not null)
        {
            return _virtualDispatchMap;
        }

        EnsureLoaded();
        if (_moduleHandle == 0)
        {
            _virtualDispatchMap = new Dictionary<nint, (nint ProxyAddress, string ExportName)>();
            return _virtualDispatchMap;
        }

        var map = new Dictionary<nint, (nint ProxyAddress, string ExportName)>();
        foreach (var exportName in VirtualDispatchExportNames)
        {
            var originalAddress = GetProcAddress(_moduleHandle, exportName);
            var proxyAddress = ResolveSelfExport(exportName);
            if (originalAddress == 0 || proxyAddress == 0)
            {
                continue;
            }

            map[originalAddress] = (proxyAddress, exportName);
            ProxyLog.Write(
                $"OVR  Virtual map {exportName}: " +
                $"original={ProxyLog.Hex((nuint)originalAddress)} proxy={ProxyLog.Hex((nuint)proxyAddress)}");
        }

        _virtualDispatchMap = map;
        return _virtualDispatchMap;
    }

    private static nuint ApplySyntheticOverrides(
        string exportName,
        nuint result,
        nuint a1,
        nuint a2,
        nuint a3,
        nuint a4,
        nuint a5,
        nuint a6,
        nuint a7,
        nuint a8)
    {
        if (ForceInitSuccess &&
            string.Equals(exportName, "FGInitModule", StringComparison.Ordinal) &&
            result != 0)
        {
            ProxyLog.Write($"OVR  FGInitModule {ProxyLog.Hex(result)} -> 0x0 (synthetic success)");
            return 0;
        }

        if (ForceNodeListSuccess &&
            string.Equals(exportName, "FGGetNodeList", StringComparison.Ordinal) &&
            result != 0)
        {
            if (TryPopulateSyntheticNodeList(a1, a2, a3))
            {
                ProxyLog.Write($"OVR  FGGetNodeList {ProxyLog.Hex(result)} -> 0x0 (synthetic single node)");
                TraceSpecialCall(exportName, "OVR", a1, a2, a3, a4, a5, a6, a7, a8);
                return 0;
            }
        }

        if (string.Equals(exportName, "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z", StringComparison.Ordinal))
        {
            SyntheticConnectedCameras[a1] = 0;
            lock (RegisterSync)
            {
                ResetRuntimeStateLocked();
            }
            if (ForceConnectSuccess && result != 0)
            {
                ProxyLog.Write($"OVR  Connect {ProxyLog.Hex(result)} -> 0x0 (synthetic connect for {ProxyLog.Hex(a1)})");
                return 0;
            }

            return result;
        }

        if (string.Equals(exportName, "?Disconnect@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            SyntheticConnectedCameras.TryRemove(a1, out _);
            lock (RegisterSync)
            {
                ResetRuntimeStateLocked();
            }
            if (result != 0)
            {
                ProxyLog.Write($"OVR  Disconnect {ProxyLog.Hex(result)} -> 0x0 (synthetic disconnect for {ProxyLog.Hex(a1)})");
                return 0;
            }

            return result;
        }

        if (ForceDeviceNameSuccess &&
            SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z", StringComparison.Ordinal) &&
            result != 0)
        {
            TryWriteAnsiString(a2, (int)a3, ReportedDeviceSerial);
            TryWriteAnsiString(a4, 64, ReportedDeviceModel);
            ProxyLog.Write(
                $"OVR  GetDeviceName {ProxyLog.Hex(result)} -> 0x0 " +
                $"(synthetic identity for {ProxyLog.Hex(a1)}, model='{ReportedDeviceModel}', serial='{ReportedDeviceSerial}', " +
                $"identityStyle={IdentityStyle}, listSerialStyle={ListSerialStyle})");
            TraceSpecialCall(exportName, "OVR", a1, a2, a3, a4, a5, a6, a7, a8);
            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?OpenCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) &&
            result != 0)
        {
            lock (RegisterSync)
            {
                _captureOpen = true;
                ProxyLog.Write($"OVR  OpenCapture {ProxyLog.Hex(result)} -> 0x0");
            }

            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?CloseCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) &&
            result != 0)
        {
            lock (RegisterSync)
            {
                _captureOpen = false;
                _streamRunning = false;
                _pendingTransientStatusReads = 0;
                ProxyLog.Write($"OVR  CloseCapture {ProxyLog.Hex(result)} -> 0x0");
            }

            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?StartDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) &&
            result != 0)
        {
            lock (RegisterSync)
            {
                _streamPrepared = true;
                _streamRunning = true;
                ProxyLog.Write($"OVR  StartDevice {ProxyLog.Hex(result)} -> 0x0");
            }

            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?StopDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) &&
            result != 0)
        {
            lock (RegisterSync)
            {
                _streamRunning = false;
                _pendingTransientStatusReads = 0;
                ProxyLog.Write($"OVR  StopDevice {ProxyLog.Hex(result)} -> 0x0");
            }

            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?ReadRegister@CFGCamera@@UEAAKKPEAK@Z", StringComparison.Ordinal) &&
            result != 0 &&
            TryReadRegister(a2, a3))
        {
            ProxyLog.Write($"OVR  ReadRegister {ProxyLog.Hex(result)} -> 0x0");
            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?WriteRegister@CFGCamera@@UEAAKKK@Z", StringComparison.Ordinal) &&
            result != 0 &&
            TryWriteRegister(a2, a3))
        {
            ProxyLog.Write($"OVR  WriteRegister {ProxyLog.Hex(result)} -> 0x0");
            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z", StringComparison.Ordinal) &&
            result != 0 &&
            TryReadBlock(a2, a3, a4))
        {
            ProxyLog.Write($"OVR  ReadBlock {ProxyLog.Hex(result)} -> 0x0");
            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "?WriteBlock@CFGCamera@@UEAAKKPEAEK@Z", StringComparison.Ordinal) &&
            result != 0 &&
            TryWriteBlock(a2, a3, a4))
        {
            ProxyLog.Write($"OVR  WriteBlock {ProxyLog.Hex(result)} -> 0x0");
            return 0;
        }

        if (SyntheticConnectedCameras.ContainsKey(a1) &&
            string.Equals(exportName, "??1CFGCamera@@UEAA@XZ", StringComparison.Ordinal))
        {
            SyntheticConnectedCameras.TryRemove(a1, out _);
        }

        return result;
    }

    private static bool TryPopulateSyntheticNodeList(nuint nodeBuffer, nuint nodeBufferSize, nuint realCountPointer)
    {
        try
        {
            if (nodeBuffer == 0 || realCountPointer == 0 || nodeBufferSize == 0)
            {
                ProxyLog.Write("OVR  FGGetNodeList skipped: missing node buffer or count pointer");
                return false;
            }

            // RayCi enumerates the node list in 12-byte steps:
            //   uint32 low_camera_id
            //   uint32 high_camera_id
            //   uint32 flags_or_type
            // Names and serials are fetched later via GetDeviceName.
            if (nodeBufferSize < 1)
            {
                ProxyLog.Write("OVR  FGGetNodeList skipped: node capacity is zero");
                return false;
            }

            Marshal.WriteInt32((nint)nodeBuffer, 0, 0x00000028);
            Marshal.WriteInt32((nint)nodeBuffer, sizeof(int), ReportedNodeHighId);
            Marshal.WriteInt32((nint)nodeBuffer, 8, SyntheticNodeFlags);
            Marshal.WriteInt32((nint)realCountPointer, 0, 1);
            ProxyLog.Write(
                $"SYN  FGGetNodeList populated low=0x00000028 high=0x{unchecked((uint)ReportedNodeHighId):X8} " +
                $"(identityStyle={IdentityStyle}, listSerialStyle={ListSerialStyle})");
            return true;
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"OVR  FGGetNodeList population failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool TryReadRegister(nuint registerIndex, nuint destination)
    {
        if (destination == 0)
        {
            ProxyLog.Write($"SYN  ReadRegister skipped: null destination for 0x{registerIndex:X}");
            return false;
        }

        lock (RegisterSync)
        {
            if (!TryReadUInt32Locked(registerIndex, out var value))
            {
                value = 0;
            }

            Marshal.WriteInt32((nint)destination, unchecked((int)value));
            ProxyLog.Write($"SYN  ReadRegister[0x{registerIndex:X}] -> 0x{value:X8}");
            return true;
        }
    }

    private static bool TryWriteRegister(nuint registerIndex, nuint value)
    {
        lock (RegisterSync)
        {
            EnsureRegisterSpaceCapacity((int)registerIndex + sizeof(uint));
            WriteUInt32Locked(registerIndex, unchecked((uint)value));
            ApplyObservedWriteEffectsLocked((int)registerIndex, BitConverter.GetBytes(unchecked((uint)value)));
            ProxyLog.Write($"SYN  WriteRegister[0x{registerIndex:X}] = 0x{value:X8}");
            return true;
        }
    }

    private static bool TryReadBlock(nuint startIndex, nuint destination, nuint requestedLength)
    {
        if (destination == 0 || requestedLength == 0)
        {
            ProxyLog.Write($"SYN  ReadBlock skipped: dst={ProxyLog.Hex(destination)} len={requestedLength}");
            return false;
        }

        var length = checked((int)requestedLength);
        var buffer = new byte[length];

        lock (RegisterSync)
        {
            CopyRegisterBytesLocked(startIndex, buffer);
        }

        Marshal.Copy(buffer, 0, (nint)destination, buffer.Length);
        ProxyLog.Write($"SYN  ReadBlock[0x{startIndex:X}, {requestedLength}] -> {Convert.ToHexString(buffer.AsSpan(0, Math.Min(buffer.Length, 64)))}");
        return true;
    }

    private static bool TryWriteBlock(nuint startIndex, nuint source, nuint requestedLength)
    {
        if (source == 0 || requestedLength == 0)
        {
            ProxyLog.Write($"SYN  WriteBlock skipped: src={ProxyLog.Hex(source)} len={requestedLength}");
            return false;
        }

        var length = checked((int)requestedLength);
        var buffer = new byte[length];
        Marshal.Copy((nint)source, buffer, 0, buffer.Length);

        lock (RegisterSync)
        {
            EnsureRegisterSpaceCapacity((int)startIndex + buffer.Length);
            buffer.CopyTo(RegisterSpace.AsSpan((int)startIndex, buffer.Length));
            ApplyObservedWriteEffectsLocked((int)startIndex, buffer);
        }

        ProxyLog.Write($"SYN  WriteBlock[0x{startIndex:X}, {requestedLength}] = {Convert.ToHexString(buffer.AsSpan(0, Math.Min(buffer.Length, 64)))}");
        return true;
    }

    private static bool TryReadUInt32Locked(nuint registerIndex, out uint value)
    {
        value = 0;
        var offset = checked((int)registerIndex);
        if (offset < 0)
        {
            return false;
        }

        if (TryGetDynamicRegisterValueLocked(offset, out value))
        {
            return true;
        }

        EnsureRegisterSpaceCapacity(offset + sizeof(uint));
        value = BitConverter.ToUInt32(RegisterSpace, offset);
        return true;
    }

    private static void CopyRegisterBytesLocked(nuint startIndex, byte[] destination)
    {
        var offset = checked((int)startIndex);
        EnsureRegisterSpaceCapacity(offset + destination.Length);
        RegisterSpace.AsSpan(offset, destination.Length).CopyTo(destination);

        if (RangeContainsRegister(offset, destination.Length, StatusRegisterIndex))
        {
            WriteUInt32ToBuffer(destination, offset, StatusRegisterIndex, GetCurrentStatusValueLocked());
        }

        if (RangeContainsRegister(offset, destination.Length, StreamInfoRegisterIndex))
        {
            WriteUInt32ToBuffer(destination, offset, StreamInfoRegisterIndex, StreamInfoRegisterValue);
        }
    }

    private static void WriteUInt32Locked(nuint registerIndex, uint value)
    {
        var offset = checked((int)registerIndex);
        EnsureRegisterSpaceCapacity(offset + sizeof(uint));
        var bytes = BitConverter.GetBytes(value);
        bytes.CopyTo(RegisterSpace.AsSpan(offset, sizeof(uint)));
    }

    private static void EnsureRegisterSpaceCapacity(int requiredLength)
    {
        if (requiredLength <= RegisterSpace.Length)
        {
            return;
        }

        throw new InvalidOperationException($"Register space overflow: requested 0x{requiredLength:X}, max 0x{RegisterSpace.Length:X}");
    }

    private static byte[] BuildRegisterSpace()
    {
        var length = 0;
        foreach (var page in CapturedCameraProfile.RegisterPages)
        {
            length = Math.Max(length, page.Key + page.Value.Length);
        }

        foreach (var pair in CapturedCameraProfile.StartupRegisterValues)
        {
            length = Math.Max(length, pair.Key + sizeof(uint));
        }

        var registerSpace = new byte[length];
        foreach (var page in CapturedCameraProfile.RegisterPages)
        {
            page.Value.CopyTo(registerSpace.AsSpan(page.Key, page.Value.Length));
        }

        foreach (var pair in CapturedCameraProfile.StartupRegisterValues)
        {
            BitConverter.GetBytes(pair.Value).CopyTo(registerSpace.AsSpan(pair.Key, sizeof(uint)));
        }

        return registerSpace;
    }

    private static bool TryGetDynamicRegisterValueLocked(int registerIndex, out uint value)
    {
        if (registerIndex == StatusRegisterIndex)
        {
            value = GetCurrentStatusValueLocked();
            return true;
        }

        if (registerIndex == StreamInfoRegisterIndex)
        {
            value = StreamInfoRegisterValue;
            return true;
        }

        value = 0;
        return false;
    }

    private static uint GetCurrentStatusValueLocked()
    {
        if (_pendingTransientStatusReads > 0)
        {
            _pendingTransientStatusReads--;
            return StatusValueTransient;
        }

        return _streamRunning || _streamPrepared
            ? StatusValueLive
            : StatusValueIdle;
    }

    private static void ResetRuntimeStateLocked()
    {
        Array.Clear(RegisterSpace);
        BuildRegisterSpace().CopyTo(RegisterSpace, 0);
        _captureOpen = false;
        _streamPrepared = false;
        _streamRunning = false;
        _pendingTransientStatusReads = 0;
        ProxyLog.Write("SYN  Runtime state reset");
    }

    private static void ApplyObservedWriteEffectsLocked(int startIndex, byte[] buffer)
    {
        if (startIndex == PrepareStreamRegisterIndex && buffer.Length >= CapturedCameraProfile.PrepareStreamPayload.Length)
        {
            foreach (var variant in CapturedCameraProfile.ObservedPrepareStreamPayloadVariants)
            {
                if (buffer.AsSpan(0, variant.Length).SequenceEqual(variant))
                {
                    _streamPrepared = true;
                    ProxyLog.Write($"SYN  PrepareStream payload accepted: {Convert.ToHexString(variant)}");
                    break;
                }
            }
        }

        if (startIndex == StreamEnableRegisterIndex && buffer.Length >= 1)
        {
            _streamPrepared = buffer[0] != 0;
            _streamRunning = buffer[0] != 0;
            ProxyLog.Write($"SYN  StreamEnable[{startIndex:X}] = {buffer[0]} (running={_streamRunning})");
        }

        if (RangeContainsRegister(startIndex, buffer.Length, ExposureRegisterIndex) ||
            RangeContainsRegister(startIndex, buffer.Length, GainRegisterIndex))
        {
            _pendingTransientStatusReads = Math.Max(_pendingTransientStatusReads, 1);
            ProxyLog.Write("SYN  Parameter write observed -> next status poll returns 0x54");
        }
    }

    private static bool RangeContainsRegister(int startIndex, int length, int registerIndex)
    {
        return registerIndex >= startIndex && registerIndex + sizeof(uint) <= startIndex + length;
    }

    private static void WriteUInt32ToBuffer(byte[] buffer, int bufferStartIndex, int registerIndex, uint value)
    {
        var localOffset = registerIndex - bufferStartIndex;
        if (localOffset < 0 || localOffset + sizeof(uint) > buffer.Length)
        {
            return;
        }

        BitConverter.GetBytes(value).CopyTo(buffer, localOffset);
    }

    private static void TraceSpecialCall(
        string exportName,
        string stage,
        nuint a1,
        nuint a2,
        nuint a3,
        nuint a4,
        nuint a5,
        nuint a6,
        nuint a7,
        nuint a8)
    {
        if (string.Equals(exportName, "??0CFGCamera@@QEAA@XZ", StringComparison.Ordinal) ||
            string.Equals(exportName, "??1CFGCamera@@UEAA@XZ", StringComparison.Ordinal))
        {
            DumpObjectWithVtable(stage, "CFGCamera.this", (nint)a1, 0x100, 12);
            return;
        }

        if (string.Equals(exportName, "FGGetNodeList", StringComparison.Ordinal))
        {
            DumpMemory(stage, "FGGetNodeList.a1", (nint)a1, ClampDumpLength((int)a2, 0x40));
            DumpMemory(stage, "FGGetNodeList.a3", (nint)a3, 0x20);
            DumpMemory(stage, "FGGetNodeList.a4", (nint)a4, 0x80);
            DumpMemory(stage, "FGGetNodeList.a7", (nint)a7, 0x20);
            DumpMemory(stage, "FGGetNodeList.a8", (nint)a8, 0x20);
            return;
        }

        if (string.Equals(exportName, "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z", StringComparison.Ordinal))
        {
            DumpMemory(stage, "GetDeviceName.a2", (nint)a2, ClampDumpLength((int)a3, 0x40));
            DumpMemory(stage, "GetDeviceName.a4", (nint)a4, 0x40);
            return;
        }

        if (string.Equals(exportName, "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z", StringComparison.Ordinal))
        {
            DumpObjectWithVtable(stage, "Connect.this", (nint)a1, 0x100, 12);
            DumpMemory(stage, "Connect.a2", (nint)a2, SyntheticNodeStride);
            DumpMemory(stage, "Connect.a5", (nint)a5, 0x40);
            return;
        }

        if (string.Equals(exportName, "?GetLicenseRequest@CFGCamera@@UEAAKPEADK@Z", StringComparison.Ordinal))
        {
            DumpMemory(stage, "GetLicenseRequest.a2", (nint)a2, ClampDumpLength((int)a3, 0x100));
            return;
        }

        if (string.Equals(exportName, "?GetParameterInfo@CFGCamera@@UEAAKGPEAUFGPINFO@@@Z", StringComparison.Ordinal))
        {
            DumpMemory(stage, "GetParameterInfo.a3", (nint)a3, 0x80);
            return;
        }

        if (string.Equals(exportName, "?OpenCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) ||
            string.Equals(exportName, "?CloseCapture@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) ||
            string.Equals(exportName, "?StartDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal) ||
            string.Equals(exportName, "?StopDevice@CFGCamera@@UEAAKXZ", StringComparison.Ordinal))
        {
            ProxyLog.Write($"{stage} {exportName} state: captureOpen={_captureOpen}, prepared={_streamPrepared}, running={_streamRunning}, pending54={_pendingTransientStatusReads}");
            return;
        }

        if (string.Equals(exportName, "?ReadRegister@CFGCamera@@UEAAKKPEAK@Z", StringComparison.Ordinal))
        {
            DumpMemory(stage, "ReadRegister.a3", (nint)a3, 0x10);
            return;
        }

        if (string.Equals(exportName, "?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z", StringComparison.Ordinal))
        {
            DumpMemory(stage, "ReadBlock.a3", (nint)a3, ClampDumpLength((int)a4, 0x80));
        }
    }

    private static int ClampDumpLength(int requestedLength, int fallbackLength)
    {
        if (requestedLength <= 0)
        {
            return fallbackLength;
        }

        return Math.Clamp(requestedLength, 1, 0x200);
    }

    private static void DumpObjectWithVtable(string stage, string label, nint objectAddress, int objectLength, int pointerCount)
    {
        DumpMemory(stage, label, objectAddress, objectLength);

        if (objectAddress == 0)
        {
            return;
        }

        try
        {
            var pointerSize = IntPtr.Size;
            var pointerBuffer = new byte[pointerSize];
            if (!ReadProcessMemory(GetCurrentProcess(), objectAddress, pointerBuffer, (nuint)pointerBuffer.Length, out var bytesRead) ||
                bytesRead < (nuint)pointerSize)
            {
                ProxyLog.Write($"{stage} {label}.vptr read failed (Win32={Marshal.GetLastWin32Error()})");
                return;
            }

            nuint vtableAddress = pointerSize == 8
                ? unchecked((nuint)BitConverter.ToUInt64(pointerBuffer, 0))
                : BitConverter.ToUInt32(pointerBuffer, 0);
            ProxyLog.Write($"{stage} {label}.vptr = {ProxyLog.Hex(vtableAddress)}");
            DumpPointerTable(stage, $"{label}.vtable", (nint)vtableAddress, pointerCount);
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"{stage} {label}.vtable dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpPointerTable(string stage, string label, nint address, int pointerCount)
    {
        if (address == 0 || pointerCount <= 0)
        {
            ProxyLog.Write($"{stage} {label} = <null>");
            return;
        }

        try
        {
            var pointerSize = IntPtr.Size;
            var buffer = new byte[pointerCount * pointerSize];
            if (!ReadProcessMemory(GetCurrentProcess(), address, buffer, (nuint)buffer.Length, out var bytesRead))
            {
                ProxyLog.Write($"{stage} {label} {ProxyLog.Hex((nuint)address)} read failed (Win32={Marshal.GetLastWin32Error()})");
                return;
            }

            var availablePointers = Math.Min(pointerCount, (int)(bytesRead / (nuint)pointerSize));
            var formatted = new string[availablePointers];
            for (var index = 0; index < availablePointers; index++)
            {
                nuint value = pointerSize == 8
                    ? unchecked((nuint)BitConverter.ToUInt64(buffer, index * pointerSize))
                    : BitConverter.ToUInt32(buffer, index * pointerSize);
                formatted[index] = ProxyLog.Hex(value);
            }

            ProxyLog.Write(
                $"{stage} {label} {ProxyLog.Hex((nuint)address)} [{availablePointers}] = " +
                string.Join(", ", formatted));
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"{stage} {label} {ProxyLog.Hex((nuint)address)} dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpMemory(string stage, string label, nint address, int length)
    {
        if (address == 0 || length <= 0)
        {
            ProxyLog.Write($"{stage} {label} = <null>");
            return;
        }

        try
        {
            var buffer = new byte[length];
            if (!ReadProcessMemory(GetCurrentProcess(), address, buffer, (nuint)buffer.Length, out var bytesRead))
            {
                ProxyLog.Write($"{stage} {label} {ProxyLog.Hex((nuint)address)} read failed (Win32={Marshal.GetLastWin32Error()})");
                return;
            }

            var actualLength = (int)Math.Min((nuint)buffer.Length, bytesRead);
            ProxyLog.Write(
                $"{stage} {label} {ProxyLog.Hex((nuint)address)} [{actualLength}] = " +
                Convert.ToHexString(buffer.AsSpan(0, actualLength)));
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"{stage} {label} {ProxyLog.Hex((nuint)address)} dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryWriteAnsiString(nuint destination, int destinationSize, string value)
    {
        if (destination == 0 || destinationSize <= 0)
        {
            return;
        }

        try
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            var maxCopy = Math.Max(0, destinationSize - 1);
            var copyLength = Math.Min(bytes.Length, maxCopy);
            Marshal.Copy(bytes, 0, (nint)destination, copyLength);
            Marshal.WriteByte((nint)destination, copyLength, 0);
        }
        catch (Exception ex)
        {
            ProxyLog.Write($"OVR  string write failed at {ProxyLog.Hex(destination)}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool ReadBoolEnvironmentVariable(string variableName, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        value = value.Trim();
        if (string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return defaultValue;
    }

    private static string ReportedDeviceModel =>
        string.Equals(IdentityStyle, IdentityStyleCaptured, StringComparison.OrdinalIgnoreCase)
            ? CapturedCameraProfile.ModelName
            : LicensedDeviceModel;

    private static string ReportedDeviceSerial =>
        string.Equals(ListSerialStyle, ListSerialStyleLicensed, StringComparison.OrdinalIgnoreCase)
            ? LicensedDeviceSerial
            : CapturedCameraProfile.BoardSerial;

    private static int ReportedNodeHighId =>
        string.Equals(ListSerialStyle, ListSerialStyleLicensed, StringComparison.OrdinalIgnoreCase)
            ? LicensedSyntheticNodeHighId
            : CapturedSyntheticNodeHighId;

    private static string ReadIdentityStyle()
    {
        var value = Environment.GetEnvironmentVariable("ULTRON_RAYCI_IDENTITY_STYLE");
        if (string.Equals(value, IdentityStyleCaptured, StringComparison.OrdinalIgnoreCase))
        {
            return IdentityStyleCaptured;
        }

        if (string.Equals(value, IdentityStyleRegistry, StringComparison.OrdinalIgnoreCase))
        {
            return IdentityStyleRegistry;
        }

        return IdentityStyleRegistry;
    }

    private static string ReadListSerialStyle()
    {
        var value = Environment.GetEnvironmentVariable("ULTRON_RAYCI_LIST_SERIAL_STYLE");
        if (string.Equals(value, ListSerialStyleCaptured, StringComparison.OrdinalIgnoreCase))
        {
            return ListSerialStyleCaptured;
        }

        if (string.Equals(value, ListSerialStyleLicensed, StringComparison.OrdinalIgnoreCase))
        {
            return ListSerialStyleLicensed;
        }

        return ListSerialStyleLicensed;
    }

    private const string LicensedDeviceModel = "CinCam CMOS 1201 EL";
    private const string LicensedDeviceSerial = "1201EL-U2-1022-0034";
    private const int SyntheticNodeStride = 12;
    private const int StatusRegisterIndex = 0x00b4;
    private const int PrepareStreamRegisterIndex = 0x00b2;
    private const int StreamInfoRegisterIndex = 0x00ca;
    private const int StreamEnableRegisterIndex = 0x00bc;
    private const int ExposureRegisterIndex = 0x0458;
    private const int GainRegisterIndex = 0x0704;
    private const uint StatusValueIdle = 0x70;
    private const uint StatusValueLive = 0x74;
    private const uint StatusValueTransient = 0x54;
    private const uint StreamInfoRegisterValue = 0x1a;
    // RayCi 2022 / 10bpp binds the FG node identity to the display short serial
    // (10220034 -> 0x009BF202), not the older licensed uEye board serial.
    private const int LicensedSyntheticNodeHighId = unchecked((int)0x009BF202u);
    private const int CapturedSyntheticNodeHighId = unchecked((int)0x44495248u);
    private const int SyntheticNodeFlags = 0x00010000;
    private static readonly string[] VirtualDispatchExportNames =
    [
        "?GetPtrDCam@CFGCamera@@UEAAPEAVCCamera@@XZ",
        "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z",
        "?Disconnect@CFGCamera@@UEAAKXZ",
        "?SetParameter@CFGCamera@@UEAAKGK@Z",
        "?GetParameter@CFGCamera@@UEAAKGPEAK@Z",
        "?GetParameterInfo@CFGCamera@@UEAAKGPEAUFGPINFO@@@Z",
        "?OpenCapture@CFGCamera@@UEAAKXZ",
        "?CloseCapture@CFGCamera@@UEAAKXZ",
        "?AssignUserBuffers@CFGCamera@@UEAAKKKPEAPEAX@Z",
        "?StartDevice@CFGCamera@@UEAAKXZ",
        "?StopDevice@CFGCamera@@UEAAKXZ",
        "?GetFrame@CFGCamera@@UEAAKPEAUFGFRAME@@K@Z",
        "?PutFrame@CFGCamera@@UEAAKPEAUFGFRAME@@@Z",
        "?DiscardFrames@CFGCamera@@UEAAKXZ",
        "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z",
        "?GetContext@CFGCamera@@UEAAPEAXXZ",
        "?GetLicenseRequest@CFGCamera@@UEAAKPEADK@Z",
        "?WriteRegister@CFGCamera@@UEAAKKK@Z",
        "?ReadRegister@CFGCamera@@UEAAKKPEAK@Z",
        "?WriteBlock@CFGCamera@@UEAAKKPEAEK@Z",
        "?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z",
    ];
}
