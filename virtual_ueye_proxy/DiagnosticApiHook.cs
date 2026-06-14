using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VirtualUEyeProxy;

internal static unsafe class DiagnosticApiHook
{
    private const uint PageReadWrite = 0x04;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;
    private const ushort ImageDirectoryEntryImport = 1;
    private const ulong ImageOrdinalFlag64 = 0x8000_0000_0000_0000;
    private const uint InvalidFileAttributes = 0xFFFF_FFFF;
    private const uint WmSetText = 0x000C;
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmSetItemA = LvmFirst + 6;
    private const uint LvmInsertItemA = LvmFirst + 7;
    private const uint LvmSetItemTextA = LvmFirst + 46;
    private const uint LvmSetItemW = LvmFirst + 76;
    private const uint LvmInsertItemW = LvmFirst + 77;
    private const uint LvmSetItemTextW = LvmFirst + 116;
    private const int MaxClassNameChars = 128;
    private const int MaxWindowTextChars = 512;
    private const int IdentificationSingletonSlotRva = 0x761340;
    private const int IdentificationMethodSlotOffset = 0x08;
    private const int IdentificationDescriptorSize = 0x40;
    private const ulong MaxCanonicalUserPointer = 0x0000_7FFF_FFFF_FFFF;
    private const int FrameRateMinTextControlId = 2055;
    private const int FrameRateMaxTextControlId = 2056;
    private const int FrameRateCurrentEditControlId = 2058;

    private static readonly object Gate = new();
    private static readonly object IdentificationMutationGate = new();
    private static readonly HashSet<nint> PatchedModules = new();
    private static readonly Dictionary<nint, ulong> AcceptedIdentificationKeys = new();
    private static readonly HashSet<ulong> AcceptedVisibleIdentificationKeys = new();
    private static readonly Dictionary<nint, int> PrimaryCameraRowsByListView = new();
    private static readonly Dictionary<nint, int> SuppressedCameraRowsByListView = new();
    private static readonly bool AllowDuplicateCameraRows =
        string.Equals(Environment.GetEnvironmentVariable("ULTRON_RAYCI_ALLOW_DUPLICATE_CAMERA_ROWS"), "1", StringComparison.OrdinalIgnoreCase);
    private static readonly HashSet<uint> KnownIdentificationSerialComponents = BuildKnownIdentificationSerialComponents();
    private static readonly bool ForceAllowIdentification =
        string.Equals(Environment.GetEnvironmentVariable("ULTRON_RAYCI_IDENTIFICATION_FORCE_ALLOW"), "1", StringComparison.OrdinalIgnoreCase);
    private static readonly bool ExtendIdentificationAllowList = DetermineExtendIdentificationAllowList();

    private static bool _installAttempted;
    private static nint _selfModule;
    private static int _loadTraceCount;
    private static int _fileTraceCount;
    private static int _profileTraceCount;
    private static int _cryptoTraceCount;
    private static int _uiTraceCount;
    private static int _identificationTraceCount;
    private static int _identificationForceTraceCount;
    private static int _identificationExtendTraceCount;
    private static int _identificationCanonicalTraceCount;
    private static nint _originalIdentificationMethod;
    private static bool _identificationHookInstalled;
    private static long _cameraInsertSequence;
    private static ulong _sharedActiveIdentificationKey;
    private static nint _sharedActiveIdentificationSelf;
    [ThreadStatic] private static ulong _activeIdentificationKey;
    [ThreadStatic] private static nint _activeIdentificationSelf;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint moduleHandle, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQuery(nint address, MemoryBasicInformation* buffer, nuint length);

    [DllImport("kernel32.dll")]
    private static extern ushort RtlCaptureStackBackTrace(
        uint framesToSkip,
        uint framesToCapture,
        nint* backTrace,
        uint* backTraceHash);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(char* fileName);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(char* fileName, nint fileHandle, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(
        char* fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint CreateFileA(
        byte* fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(char* fileName);

    [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetPrivateProfileStringW(
        char* appName,
        char* keyName,
        char* defaultValue,
        char* returnedString,
        uint size,
        char* fileName);

    [DllImport("advapi32.dll", EntryPoint = "CryptAcquireContextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptAcquireContextW(
        nint* provider,
        char* container,
        char* providerName,
        uint providerType,
        uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CryptImportKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptImportKey(
        nint provider,
        byte* data,
        uint dataLength,
        nint publicKey,
        uint flags,
        nint* keyHandle);

    [DllImport("advapi32.dll", EntryPoint = "CryptDecrypt", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptDecrypt(
        nint keyHandle,
        nint hashHandle,
        [MarshalAs(UnmanagedType.Bool)] bool final,
        uint flags,
        byte* data,
        uint* dataLength);

    [DllImport("advapi32.dll", EntryPoint = "CryptDestroyKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptDestroyKey(nint keyHandle);

    [DllImport("advapi32.dll", EntryPoint = "CryptReleaseContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptReleaseContext(nint provider, uint flags);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageW(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint SendMessageA(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint windowHandle, char* text);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextA", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextA(nint windowHandle, byte* text);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint windowHandle, char* className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint windowHandle, char* text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetParent", SetLastError = true)]
    private static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetDlgCtrlID", SetLastError = true)]
    private static extern int GetDlgCtrlID(nint windowHandle);

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
            _selfModule = GetModuleHandleW("ueye_api_64.dll");
            if (_selfModule == nint.Zero)
            {
                VirtualCameraState.Log("DiagnosticApiHook: self module handle not found.");
                return;
            }

            PatchLoadedModules("initial");
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool DetermineExtendIdentificationAllowList()
    {
        var explicitSetting = Environment.GetEnvironmentVariable("ULTRON_RAYCI_IDENTIFICATION_EXTEND");
        if (string.Equals(explicitSetting, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(explicitSetting, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("ULTRON_RAYCI_SIMULATE"), "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var mainModule = process.MainModule;
            return mainModule is not null &&
                   string.Equals(mainModule.ModuleName, "RayCi.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void TryInstallIdentificationHook()
    {
        if (_identificationHookInstalled)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var mainModule = process.MainModule;
            if (mainModule is null || !string.Equals(mainModule.ModuleName, "RayCi.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var singletonSlot = mainModule.BaseAddress + IdentificationSingletonSlotRva;
            if (singletonSlot == nint.Zero)
            {
                return;
            }

            var singleton = *(nint*)singletonSlot;
            if (singleton == nint.Zero)
            {
                return;
            }

            var vtable = *(nint*)singleton;
            if (vtable == nint.Zero)
            {
                return;
            }

            var methodSlot = (nint*)((byte*)vtable + IdentificationMethodSlotOffset);
            var hookAddress = (nint)(delegate* unmanaged<nint, nint, int>)&IdentificationCheckHook;
            var current = *methodSlot;
            if (current == nint.Zero)
            {
                return;
            }

            if (current == hookAddress)
            {
                _identificationHookInstalled = true;
                return;
            }

            if (!VirtualProtect((nint)methodSlot, (nuint)IntPtr.Size, PageReadWrite, out var oldProtect))
            {
                return;
            }

            _originalIdentificationMethod = current;
            *methodSlot = hookAddress;
            _ = VirtualProtect((nint)methodSlot, (nuint)IntPtr.Size, oldProtect, out _);
            _identificationHookInstalled = true;

            VirtualCameraState.Log(
                $"DiagnosticApiHook: CIdentification slot patched at 0x{(nint)methodSlot:X}, singleton=0x{singleton:X}, vtable=0x{vtable:X}, original=0x{current:X}, forceAllow={ForceAllowIdentification}");
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: TryInstallIdentificationHook failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_LoadLibraryW_Hook")]
    private static nint LoadLibraryWHook(char* fileName)
    {
        var path = PtrToStringUni(fileName);
        var shouldTrace = ShouldTraceModule(path);

        if (shouldTrace && ShouldLog(ref _loadTraceCount, 96))
        {
            VirtualCameraState.Log($"DiagnosticApiHook: LoadLibraryW(path={path ?? "<null>"})");
        }

        var handle = LoadLibraryW(fileName);

        if (shouldTrace && ShouldLog(ref _loadTraceCount, 96))
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: LoadLibraryW -> handle=0x{handle:X}, lastError={lastError}");
        }

        PatchLoadedModules("LoadLibraryW");
        return handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_LoadLibraryExW_Hook")]
    private static nint LoadLibraryExWHook(char* fileName, nint fileHandle, uint flags)
    {
        var path = PtrToStringUni(fileName);
        var shouldTrace = ShouldTraceModule(path);

        if (shouldTrace && ShouldLog(ref _loadTraceCount, 96))
        {
            VirtualCameraState.Log($"DiagnosticApiHook: LoadLibraryExW(path={path ?? "<null>"}, flags=0x{flags:X})");
        }

        var handle = LoadLibraryExW(fileName, fileHandle, flags);

        if (shouldTrace && ShouldLog(ref _loadTraceCount, 96))
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: LoadLibraryExW -> handle=0x{handle:X}, lastError={lastError}");
        }

        PatchLoadedModules("LoadLibraryExW");
        return handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CreateFileW_Hook")]
    private static nint CreateFileWHook(
        char* fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile)
    {
        var path = PtrToStringUni(fileName);
        var trace = IsInterestingPath(path);
        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CreateFileW(path={path}, access=0x{desiredAccess:X}, share=0x{shareMode:X}, create=0x{creationDisposition:X}, flags=0x{flagsAndAttributes:X})");
        }

        var handle = CreateFileW(fileName, desiredAccess, shareMode, securityAttributes, creationDisposition, flagsAndAttributes, templateFile);

        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: CreateFileW -> handle=0x{handle:X}, lastError={lastError}");
        }

        return handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CreateFileA_Hook")]
    private static nint CreateFileAHook(
        byte* fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile)
    {
        var path = PtrToStringAnsi(fileName);
        var trace = IsInterestingPath(path);
        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CreateFileA(path={path}, access=0x{desiredAccess:X}, share=0x{shareMode:X}, create=0x{creationDisposition:X}, flags=0x{flagsAndAttributes:X})");
        }

        var handle = CreateFileA(fileName, desiredAccess, shareMode, securityAttributes, creationDisposition, flagsAndAttributes, templateFile);

        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: CreateFileA -> handle=0x{handle:X}, lastError={lastError}");
        }

        return handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_GetFileAttributesW_Hook")]
    private static uint GetFileAttributesWHook(char* fileName)
    {
        var path = PtrToStringUni(fileName);
        var trace = IsInterestingPath(path);
        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            VirtualCameraState.Log($"DiagnosticApiHook: GetFileAttributesW(path={path})");
        }

        var result = GetFileAttributesW(fileName);

        if (trace && ShouldLog(ref _fileTraceCount, 160))
        {
            var lastError = Marshal.GetLastWin32Error();
            var status = result == InvalidFileAttributes ? "INVALID" : $"0x{result:X}";
            VirtualCameraState.Log($"DiagnosticApiHook: GetFileAttributesW -> {status}, lastError={lastError}");
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_GetPrivateProfileStringW_Hook")]
    private static uint GetPrivateProfileStringWHook(
        char* appName,
        char* keyName,
        char* defaultValue,
        char* returnedString,
        uint size,
        char* fileName)
    {
        var filePath = PtrToStringUni(fileName);
        var trace = IsInterestingPath(filePath);
        if (trace && ShouldLog(ref _profileTraceCount, 96))
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: GetPrivateProfileStringW(file={filePath}, section={PtrToStringUni(appName) ?? "<null>"}, key={PtrToStringUni(keyName) ?? "<null>"})");
        }

        var result = GetPrivateProfileStringW(appName, keyName, defaultValue, returnedString, size, fileName);

        if (trace && ShouldLog(ref _profileTraceCount, 96))
        {
            VirtualCameraState.Log($"DiagnosticApiHook: GetPrivateProfileStringW -> {result}");
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CryptAcquireContextW_Hook")]
    private static int CryptAcquireContextWHook(
        nint* provider,
        char* container,
        char* providerName,
        uint providerType,
        uint flags)
    {
        var shouldTrace = ShouldLog(ref _cryptoTraceCount, 96);
        if (shouldTrace)
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptAcquireContextW(container={PtrToStringUni(container) ?? "<null>"}, provider={PtrToStringUni(providerName) ?? "<null>"}, type={providerType}, flags=0x{flags:X})");
        }

        var result = CryptAcquireContextW(provider, container, providerName, providerType, flags);

        if (shouldTrace)
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptAcquireContextW -> {(result ? 1 : 0)}, provider=0x{(provider is null ? 0 : *provider):X}, lastError={lastError}");
        }

        return result ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CryptImportKey_Hook")]
    private static int CryptImportKeyHook(
        nint provider,
        byte* data,
        uint dataLength,
        nint publicKey,
        uint flags,
        nint* keyHandle)
    {
        var shouldTrace = ShouldLog(ref _cryptoTraceCount, 96);
        if (shouldTrace)
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptImportKey(provider=0x{provider:X}, dataLen={dataLength}, flags=0x{flags:X}, preview={HexPreview(data, dataLength)})");
        }

        var result = CryptImportKey(provider, data, dataLength, publicKey, flags, keyHandle);

        if (shouldTrace)
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptImportKey -> {(result ? 1 : 0)}, key=0x{(keyHandle is null ? 0 : *keyHandle):X}, lastError={lastError}");
        }

        return result ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CryptDecrypt_Hook")]
    private static int CryptDecryptHook(
        nint keyHandle,
        nint hashHandle,
        int final,
        uint flags,
        byte* data,
        uint* dataLength)
    {
        var beforeLength = dataLength is null ? 0u : *dataLength;
        var shouldTrace = ShouldLog(ref _cryptoTraceCount, 96);
        if (shouldTrace)
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptDecrypt(key=0x{keyHandle:X}, final={final}, flags=0x{flags:X}, dataLen={beforeLength}, before={HexPreview(data, beforeLength)})");
        }

        var result = CryptDecrypt(keyHandle, hashHandle, final != 0, flags, data, dataLength);

        if (shouldTrace)
        {
            var afterLength = dataLength is null ? 0u : *dataLength;
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log(
                $"DiagnosticApiHook: CryptDecrypt -> {(result ? 1 : 0)}, dataLen={afterLength}, after={HexPreview(data, afterLength)}, lastError={lastError}");
        }

        return result ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CryptDestroyKey_Hook")]
    private static int CryptDestroyKeyHook(nint keyHandle)
    {
        var shouldTrace = ShouldLog(ref _cryptoTraceCount, 96);
        if (shouldTrace)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: CryptDestroyKey(key=0x{keyHandle:X})");
        }

        var result = CryptDestroyKey(keyHandle);

        if (shouldTrace)
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: CryptDestroyKey -> {(result ? 1 : 0)}, lastError={lastError}");
        }

        return result ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CryptReleaseContext_Hook")]
    private static int CryptReleaseContextHook(nint provider, uint flags)
    {
        var shouldTrace = ShouldLog(ref _cryptoTraceCount, 96);
        if (shouldTrace)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: CryptReleaseContext(provider=0x{provider:X}, flags=0x{flags:X})");
        }

        var result = CryptReleaseContext(provider, flags);

        if (shouldTrace)
        {
            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log($"DiagnosticApiHook: CryptReleaseContext -> {(result ? 1 : 0)}, lastError={lastError}");
        }

        return result ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_SendMessageW_Hook")]
    private static nint SendMessageWHook(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        var adjustedWParam = wParam;
        if (TrySuppressDuplicateCameraListMessage(windowHandle, message, ref adjustedWParam, lParam, unicode: true, out var suppressedResult))
        {
            TraceUser32Message("SendMessageW", windowHandle, message, adjustedWParam, lParam, unicode: true);
            return suppressedResult;
        }

        if (message == WmSetText &&
            TryGetSyntheticFrameRateControlText(windowHandle, out var replacementText))
        {
            TraceUser32Message("SendMessageW", windowHandle, message, adjustedWParam, lParam, unicode: true);
            fixed (char* replacementPtr = replacementText)
            {
                return SendMessageW(windowHandle, message, adjustedWParam, (nint)replacementPtr);
            }
        }

        var trackCameraInsert = IsCameraListInsert(windowHandle, message, lParam, unicode: true);
        TraceUser32Message("SendMessageW", windowHandle, message, adjustedWParam, lParam, unicode: true);
        var result = SendMessageW(windowHandle, message, adjustedWParam, lParam);
        if (trackCameraInsert && result.ToInt64() >= 0)
        {
            NoteCameraListInsert(windowHandle, message, result);
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_SendMessageA_Hook")]
    private static nint SendMessageAHook(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        var adjustedWParam = wParam;
        if (TrySuppressDuplicateCameraListMessage(windowHandle, message, ref adjustedWParam, lParam, unicode: false, out var suppressedResult))
        {
            TraceUser32Message("SendMessageA", windowHandle, message, adjustedWParam, lParam, unicode: false);
            return suppressedResult;
        }

        if (message == WmSetText &&
            TryGetSyntheticFrameRateControlText(windowHandle, out var replacementText))
        {
            TraceUser32Message("SendMessageA", windowHandle, message, adjustedWParam, lParam, unicode: false);
            var replacementBytes = Encoding.ASCII.GetBytes(replacementText + '\0');
            fixed (byte* replacementPtr = replacementBytes)
            {
                return SendMessageA(windowHandle, message, adjustedWParam, (nint)replacementPtr);
            }
        }

        var trackCameraInsert = IsCameraListInsert(windowHandle, message, lParam, unicode: false);
        TraceUser32Message("SendMessageA", windowHandle, message, adjustedWParam, lParam, unicode: false);
        var result = SendMessageA(windowHandle, message, adjustedWParam, lParam);
        if (trackCameraInsert && result.ToInt64() >= 0)
        {
            NoteCameraListInsert(windowHandle, message, result);
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_SetWindowTextW_Hook")]
    private static int SetWindowTextWHook(nint windowHandle, char* text)
    {
        TraceSetWindowText("SetWindowTextW", windowHandle, SafePtrToStringUni(text));
        if (TryGetSyntheticFrameRateControlText(windowHandle, out var replacementText))
        {
            fixed (char* replacementPtr = replacementText)
            {
                return SetWindowTextW(windowHandle, replacementPtr) ? 1 : 0;
            }
        }

        return SetWindowTextW(windowHandle, text) ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_SetWindowTextA_Hook")]
    private static int SetWindowTextAHook(nint windowHandle, byte* text)
    {
        TraceSetWindowText("SetWindowTextA", windowHandle, SafePtrToStringAnsi(text));
        if (TryGetSyntheticFrameRateControlText(windowHandle, out var replacementText))
        {
            var replacementBytes = Encoding.ASCII.GetBytes(replacementText + '\0');
            fixed (byte* replacementPtr = replacementBytes)
            {
                return SetWindowTextA(windowHandle, replacementPtr) ? 1 : 0;
            }
        }

        return SetWindowTextA(windowHandle, text) ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_CIdentification_Check_Hook")]
    private static int IdentificationCheckHook(nint self, nint descriptor)
    {
        var keyBeforeNormalization = ReadIdentificationDescriptorKey(descriptor);
        var canonicalKey = CanonicalizeIdentificationKey(keyBeforeNormalization);
        if (keyBeforeNormalization != 0 &&
            canonicalKey != 0 &&
            keyBeforeNormalization != canonicalKey)
        {
            WriteIdentificationDescriptorKey(descriptor, canonicalKey);

            if (ShouldLog(ref _identificationCanonicalTraceCount, 16))
            {
                VirtualCameraState.Log(
                    $"DiagnosticApiHook: canonicalized CIdentification descriptor key 0x{keyBeforeNormalization:X16} -> 0x{canonicalKey:X16}.");
            }
        }

        if (TryReuseAcceptedIdentification(self, canonicalKey))
        {
            if (ShouldLog(ref _identificationCanonicalTraceCount, 16))
            {
                VirtualCameraState.Log(
                    $"DiagnosticApiHook: short-circuiting duplicate CIdentification for key 0x{canonicalKey:X16} on self=0x{self:X}.");
            }

            return 1;
        }

        var cameraInsertSequenceBefore = Interlocked.Read(ref _cameraInsertSequence);
        var previousActiveIdentificationKey = _activeIdentificationKey;
        var previousActiveIdentificationSelf = _activeIdentificationSelf;
        _activeIdentificationKey = canonicalKey;
        _activeIdentificationSelf = self;
        lock (IdentificationMutationGate)
        {
            _sharedActiveIdentificationKey = canonicalKey;
            _sharedActiveIdentificationSelf = self;
        }

        try
        {
            var original = _originalIdentificationMethod;
            var result = 0;
            if (original != nint.Zero)
            {
                result = ((delegate* unmanaged<nint, nint, int>)original)(self, descriptor);
            }

            if (result == 0 &&
                !ForceAllowIdentification &&
                ExtendIdentificationAllowList &&
                TryRetryIdentificationWithTemporaryKey(self, descriptor, original, out var retryKey, out var retryResult))
            {
                if (ShouldLog(ref _identificationExtendTraceCount, 8))
                {
                    VirtualCameraState.Log($"DiagnosticApiHook: CIdentification retried with temporary key 0x{retryKey:X16}, retryResult={retryResult}.");
                }

                result = retryResult;
            }

            if (result != 0 &&
                canonicalKey != 0 &&
                Interlocked.Read(ref _cameraInsertSequence) != cameraInsertSequenceBefore)
            {
                RememberAcceptedIdentification(self, canonicalKey);
            }

            if (ShouldLog(ref _identificationTraceCount, 64))
            {
                VirtualCameraState.Log(
                    $"DiagnosticApiHook: CIdentification::Check(self=0x{self:X}, desc=0x{descriptor:X}, result={result}, state={DescribeIdentificationState(self)}, payload={DescribeIdentificationDescriptor(descriptor)})");
            }

            if (ForceAllowIdentification && result == 0)
            {
                if (ShouldLog(ref _identificationForceTraceCount, 8))
                {
                    VirtualCameraState.Log("DiagnosticApiHook: CIdentification::Check forcing allow for diagnostics.");
                }

                return 1;
            }

            return result;
        }
        finally
        {
            _activeIdentificationKey = previousActiveIdentificationKey;
            _activeIdentificationSelf = previousActiveIdentificationSelf;
            lock (IdentificationMutationGate)
            {
                if (_sharedActiveIdentificationSelf == self && _sharedActiveIdentificationKey == canonicalKey)
                {
                    _sharedActiveIdentificationKey = 0;
                    _sharedActiveIdentificationSelf = nint.Zero;
                }
            }
        }
    }

    private static ulong ReadIdentificationDescriptorKey(nint descriptor)
    {
        if (descriptor == nint.Zero)
        {
            return 0;
        }

        try
        {
            return *(ulong*)descriptor;
        }
        catch
        {
            return 0;
        }
    }

    private static void WriteIdentificationDescriptorKey(nint descriptor, ulong key)
    {
        if (descriptor == nint.Zero || key == 0)
        {
            return;
        }

        try
        {
            var protectionChanged = VirtualProtect(descriptor, (nuint)sizeof(ulong), PageReadWrite, out var oldProtect);
            try
            {
                *(ulong*)descriptor = key;
            }
            finally
            {
                if (protectionChanged)
                {
                    _ = VirtualProtect(descriptor, (nuint)sizeof(ulong), oldProtect, out _);
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLog(ref _identificationCanonicalTraceCount, 16))
            {
                VirtualCameraState.Log(
                    $"DiagnosticApiHook: descriptor key write failed for 0x{descriptor:X}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static ulong CanonicalizeIdentificationKey(ulong key)
    {
        if (key == 0)
        {
            return 0;
        }

        var high = (uint)(key >> 32);
        var low = (uint)key;
        var sensorKey = (uint)UeyeNative.IS_SENSOR_UI1545_M;

        if (high == sensorKey && KnownIdentificationSerialComponents.Contains(low))
        {
            return key;
        }

        if (low == sensorKey && KnownIdentificationSerialComponents.Contains(high))
        {
            return ((ulong)sensorKey << 32) | high;
        }

        return key;
    }

    private static HashSet<uint> BuildKnownIdentificationSerialComponents()
    {
        var values = new HashSet<uint>();
        TryAddDecimalSerial(values, VirtualCameraState.ReportedListSerial);
        TryAddDecimalSerial(values, VirtualCameraState.UeyeSerial);
        TryAddDecimalSerial(values, VirtualCameraState.CapturedRawSerial);
        return values;
    }

    private static void TryAddDecimalSerial(HashSet<uint> values, string serialText)
    {
        if (values is null || string.IsNullOrWhiteSpace(serialText))
        {
            return;
        }

        if (uint.TryParse(serialText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            values.Add(parsed);
        }
    }

    private static bool TryReuseAcceptedIdentification(nint self, ulong canonicalKey)
    {
        if (self == nint.Zero || canonicalKey == 0)
        {
            return false;
        }

        lock (IdentificationMutationGate)
        {
            return AcceptedVisibleIdentificationKeys.Contains(canonicalKey) ||
                   (AcceptedIdentificationKeys.TryGetValue(self, out var acceptedKey) && acceptedKey == canonicalKey);
        }
    }

    private static void RememberAcceptedIdentification(nint self, ulong canonicalKey)
    {
        if (self == nint.Zero || canonicalKey == 0)
        {
            return;
        }

        lock (IdentificationMutationGate)
        {
            AcceptedIdentificationKeys[self] = canonicalKey;
            AcceptedVisibleIdentificationKeys.Add(canonicalKey);
        }
    }

    private static bool TryRetryIdentificationWithTemporaryKey(nint self, nint descriptor, nint originalMethod, out ulong retryKey, out int retryResult)
    {
        retryKey = 0;
        retryResult = 0;
        if (self == nint.Zero || descriptor == nint.Zero || originalMethod == nint.Zero)
        {
            return false;
        }

        try
        {
            var selfPtr = (byte*)self;
            var mask = *(ulong*)(selfPtr + 0x30);
            var descriptorKey = *(ulong*)descriptor;
            var maskedKey = CanonicalizeIdentificationKey(descriptorKey & mask);
            var entries = *(ulong**)(selfPtr + 0x10);
            var count = *(long*)(selfPtr + 0x18);

            if (count < 0 || count > 1024)
            {
                return false;
            }

            if (entries == null)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if ((entries[i] & mask) == maskedKey)
                {
                    return false;
                }
            }

            if (count == 0)
            {
                return false;
            }

            lock (IdentificationMutationGate)
            {
                var oldProtect = 0u;
                var protectionChanged = VirtualProtect((nint)entries, (nuint)sizeof(ulong), PageReadWrite, out oldProtect);
                var originalFirstKey = entries[0];

                try
                {
                    entries[0] = maskedKey;
                    retryResult = ((delegate* unmanaged<nint, nint, int>)originalMethod)(self, descriptor);
                    retryKey = maskedKey;
                    return true;
                }
                finally
                {
                    entries[0] = originalFirstKey;
                    if (protectionChanged)
                    {
                        _ = VirtualProtect((nint)entries, (nuint)sizeof(ulong), oldProtect, out _);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLog(ref _identificationExtendTraceCount, 8))
            {
                VirtualCameraState.Log($"DiagnosticApiHook: identification retry failed: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }

    private static string DescribeIdentificationState(nint self)
    {
        if (self == nint.Zero)
        {
            return "<null>";
        }

        try
        {
            var basePtr = (byte*)self;
            var entries = *(ulong**)(basePtr + 0x10);
            var count = *(long*)(basePtr + 0x18);
            var mask = *(ulong*)(basePtr + 0x30);

            if (entries == null || count <= 0)
            {
                return $"mask=0x{mask:X16}, count={count}, keys=[]";
            }

            var sampleCount = (int)Math.Min(count, 16);
            var keys = new string[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                keys[i] = $"0x{entries[i]:X16}";
            }

            var suffix = count > sampleCount ? ",..." : string.Empty;
            return $"mask=0x{mask:X16}, count={count}, keys=[{string.Join(",", keys)}{suffix}]";
        }
        catch (Exception ex)
        {
            return $"<state-failed:{ex.GetType().Name}>";
        }
    }

    private static void PatchLoadedModules(string reason)
    {
        try
        {
            var patchedImports = 0;
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                var moduleBase = module.BaseAddress;
                if (moduleBase == nint.Zero || moduleBase == _selfModule)
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

                patchedImports += PatchModuleImports(moduleBase, module.ModuleName);
            }

            if (patchedImports > 0)
            {
                VirtualCameraState.Log($"DiagnosticApiHook: patched {patchedImports} imports ({reason}).");
            }

            TryInstallIdentificationHook();
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: PatchLoadedModules({reason}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int PatchModuleImports(nint moduleBase, string? moduleName)
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

        var optionalHeader = ntHeader + 0x18;
        if (*(ushort*)optionalHeader != 0x20B)
        {
            return 0;
        }

        var dataDirectory = optionalHeader + 0x70;
        var importDirectoryRva = *(uint*)(dataDirectory + ImageDirectoryEntryImport * 8);
        if (importDirectoryRva == 0)
        {
            return 0;
        }

        var descriptor = (ImageImportDescriptor*)(basePtr + importDirectoryRva);
        var patchedEntries = 0;

        while (descriptor->Name != 0)
        {
            var importModuleName = Marshal.PtrToStringAnsi((nint)(basePtr + descriptor->Name));
            if (string.Equals(importModuleName, "KERNEL32.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(importModuleName, "ADVAPI32.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(importModuleName, "USER32.dll", StringComparison.OrdinalIgnoreCase))
            {
                var lookup = descriptor->OriginalFirstThunk != 0
                    ? (ulong*)(basePtr + descriptor->OriginalFirstThunk)
                    : (ulong*)(basePtr + descriptor->FirstThunk);
                var firstThunk = (nint*)(basePtr + descriptor->FirstThunk);

                while (*lookup != 0)
                {
                    var thunkData = *lookup;
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

                    lookup++;
                    firstThunk++;
                }
            }

            descriptor++;
        }

        if (patchedEntries > 0)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: {moduleName ?? "<unknown>"} patched imports={patchedEntries}");
        }

        return patchedEntries;
    }

    private static bool TryGetReplacement(string? importName, out nint replacement)
    {
        switch (importName)
        {
            case "LoadLibraryW":
                replacement = GetExportedHookAddress("Ultron_LoadLibraryW_Hook");
                return replacement != nint.Zero;
            case "LoadLibraryExW":
                replacement = GetExportedHookAddress("Ultron_LoadLibraryExW_Hook");
                return replacement != nint.Zero;
            case "CreateFileW":
                replacement = GetExportedHookAddress("Ultron_CreateFileW_Hook");
                return replacement != nint.Zero;
            case "CreateFileA":
                replacement = GetExportedHookAddress("Ultron_CreateFileA_Hook");
                return replacement != nint.Zero;
            case "GetFileAttributesW":
                replacement = GetExportedHookAddress("Ultron_GetFileAttributesW_Hook");
                return replacement != nint.Zero;
            case "GetPrivateProfileStringW":
                replacement = GetExportedHookAddress("Ultron_GetPrivateProfileStringW_Hook");
                return replacement != nint.Zero;
            case "CryptAcquireContextW":
                replacement = GetExportedHookAddress("Ultron_CryptAcquireContextW_Hook");
                return replacement != nint.Zero;
            case "CryptImportKey":
                replacement = GetExportedHookAddress("Ultron_CryptImportKey_Hook");
                return replacement != nint.Zero;
            case "CryptDecrypt":
                replacement = GetExportedHookAddress("Ultron_CryptDecrypt_Hook");
                return replacement != nint.Zero;
            case "CryptDestroyKey":
                replacement = GetExportedHookAddress("Ultron_CryptDestroyKey_Hook");
                return replacement != nint.Zero;
            case "CryptReleaseContext":
                replacement = GetExportedHookAddress("Ultron_CryptReleaseContext_Hook");
                return replacement != nint.Zero;
            case "SendMessageW":
                replacement = GetExportedHookAddress("Ultron_SendMessageW_Hook");
                return replacement != nint.Zero;
            case "SendMessageA":
                replacement = GetExportedHookAddress("Ultron_SendMessageA_Hook");
                return replacement != nint.Zero;
            case "SetWindowTextW":
                replacement = GetExportedHookAddress("Ultron_SetWindowTextW_Hook");
                return replacement != nint.Zero;
            case "SetWindowTextA":
                replacement = GetExportedHookAddress("Ultron_SetWindowTextA_Hook");
                return replacement != nint.Zero;
            default:
                replacement = nint.Zero;
                return false;
        }
    }

    private static nint GetExportedHookAddress(string exportName)
    {
        if (_selfModule == nint.Zero)
        {
            return nint.Zero;
        }

        var address = GetProcAddress(_selfModule, exportName);
        if (address == nint.Zero)
        {
            VirtualCameraState.Log($"DiagnosticApiHook: missing export {exportName}");
        }

        return address;
    }

    private static string HexPreview(byte* data, uint length)
    {
        if (data == null || length == 0)
        {
            return "<empty>";
        }

        var previewLength = (int)Math.Min(length, 32);
        var bytes = new byte[previewLength];
        Marshal.Copy((nint)data, bytes, 0, previewLength);
        var suffix = length > (uint)previewLength ? "..." : string.Empty;
        return Convert.ToHexString(bytes) + suffix;
    }

    private static void TraceSetWindowText(string apiName, nint windowHandle, string? text)
    {
        if (!IsInterestingUiText(text))
        {
            return;
        }

        if (!ShouldLog(ref _uiTraceCount, 200))
        {
            return;
        }

        var className = GetWindowClassName(windowHandle);
        var stack = DescribeNativeCallers();
        VirtualCameraState.Log(
            $"DiagnosticApiHook: {apiName}(hwnd=0x{windowHandle:X}, class={className ?? "<unknown>"}, text={QuoteForLog(text)}) stack={stack}");
    }

    private static void TraceUser32Message(string apiName, nint windowHandle, uint message, nuint wParam, nint lParam, bool unicode)
    {
        if (!ShouldTraceUser32Message(message))
        {
            return;
        }

        var className = GetWindowClassName(windowHandle);
        string? text = null;
        int itemIndex = -1;
        int subItemIndex = -1;
        string? details = null;

        if (message == WmSetText)
        {
            text = unicode ? SafePtrToStringUni((char*)lParam) : SafePtrToStringAnsi((byte*)lParam);
        }
        else if (TryDecodeListViewMessage(message, lParam, unicode, out text, out itemIndex, out subItemIndex, out var mask))
        {
            details = $"item={itemIndex}, subItem={subItemIndex}, mask=0x{mask:X}";
        }

        var listViewMessage = message >= LvmFirst && message < LvmFirst + 0x200;
        var interestingText = IsInterestingUiText(text);
        var interestingClass = string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase);

        if (!interestingText && !(interestingClass && listViewMessage))
        {
            return;
        }

        if (!ShouldLog(ref _uiTraceCount, 200))
        {
            return;
        }

        var stack = interestingText ? DescribeNativeCallers() : "<omitted>";
        VirtualCameraState.Log(
            $"DiagnosticApiHook: {apiName}(hwnd=0x{windowHandle:X}, class={className ?? "<unknown>"}, msg={DescribeUser32Message(message)}, wParam=0x{wParam:X}, lParam=0x{lParam:X}{(details is null ? string.Empty : $", {details}")}, text={QuoteForLog(text)}) stack={stack}");
    }

    private static bool ShouldTraceUser32Message(uint message)
    {
        return message == WmSetText ||
               message == LvmSetItemA ||
               message == LvmInsertItemA ||
               message == LvmSetItemTextA ||
               message == LvmSetItemW ||
               message == LvmInsertItemW ||
               message == LvmSetItemTextW;
    }

    private static bool TrySuppressDuplicateCameraListMessage(
        nint windowHandle,
        uint message,
        ref nuint wParam,
        nint lParam,
        bool unicode,
        out nint handledResult)
    {
        handledResult = nint.Zero;

        if (AllowDuplicateCameraRows)
        {
            return false;
        }

        if (windowHandle == nint.Zero || lParam == nint.Zero)
        {
            return false;
        }

        var className = GetWindowClassName(windowHandle);
        if (!string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message == LvmInsertItemW || message == LvmInsertItemA)
        {
            if (!TryDecodeListViewMessage(message, lParam, unicode, out var text, out var itemIndex, out _, out _) ||
                !string.Equals(text, VirtualCameraState.CalibrationName, StringComparison.Ordinal))
            {
                return false;
            }

            lock (IdentificationMutationGate)
            {
                if (!PrimaryCameraRowsByListView.ContainsKey(windowHandle))
                {
                    return false;
                }

                var currentItemCount = SendMessageW(windowHandle, LvmGetItemCount, 0, nint.Zero).ToInt64();
                if (currentItemCount <= 0)
                {
                    PrimaryCameraRowsByListView.Remove(windowHandle);
                    SuppressedCameraRowsByListView.Remove(windowHandle);

                    if (ShouldLog(ref _identificationCanonicalTraceCount, 24))
                    {
                        VirtualCameraState.Log(
                            $"DiagnosticApiHook: cleared stale camera-row suppression state for hwnd=0x{windowHandle:X} because the list is empty.");
                    }

                    return false;
                }

                SuppressedCameraRowsByListView[windowHandle] = itemIndex;
            }

            if (ShouldLog(ref _identificationCanonicalTraceCount, 24))
            {
                VirtualCameraState.Log(
                    $"DiagnosticApiHook: suppressing duplicate camera row insert on hwnd=0x{windowHandle:X}, requestedItem={itemIndex}.");
            }

            handledResult = nint.Zero;
            return true;
        }

        int suppressedItemIndex;
        lock (IdentificationMutationGate)
        {
            if (!SuppressedCameraRowsByListView.TryGetValue(windowHandle, out suppressedItemIndex))
            {
                return false;
            }
        }

        if (message == LvmSetItemTextW || message == LvmSetItemTextA)
        {
            if ((int)wParam == suppressedItemIndex)
            {
                wParam = 0;
            }

            return false;
        }

        if (message == LvmSetItemW || message == LvmSetItemA)
        {
            try
            {
                if (unicode)
                {
                    var item = (LVITEMW*)lParam;
                    if (item->iItem == suppressedItemIndex)
                    {
                        item->iItem = 0;
                    }
                }
                else
                {
                    var item = (LVITEMA*)lParam;
                    if (item->iItem == suppressedItemIndex)
                    {
                        item->iItem = 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsCameraListInsert(nint windowHandle, uint message, nint lParam, bool unicode)
    {
        if ((message != LvmInsertItemA && message != LvmInsertItemW) ||
            windowHandle == nint.Zero ||
            lParam == nint.Zero)
        {
            return false;
        }

        var className = GetWindowClassName(windowHandle);
        if (!string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryDecodeListViewMessage(message, lParam, unicode, out var text, out _, out _, out _) &&
               string.Equals(text, VirtualCameraState.CalibrationName, StringComparison.Ordinal);
    }

    private static void NoteCameraListInsert(nint windowHandle, uint message, nint result)
    {
        Interlocked.Increment(ref _cameraInsertSequence);

        lock (IdentificationMutationGate)
        {
            if (!PrimaryCameraRowsByListView.ContainsKey(windowHandle))
            {
                PrimaryCameraRowsByListView[windowHandle] = (int)result;
            }
        }

        var activeSelf = _activeIdentificationSelf;
        var activeKey = _activeIdentificationKey;
        if (activeSelf == nint.Zero || activeKey == 0)
        {
            lock (IdentificationMutationGate)
            {
                activeSelf = _sharedActiveIdentificationSelf;
                activeKey = _sharedActiveIdentificationKey;
            }
        }

        if (activeSelf != nint.Zero && activeKey != 0)
        {
            RememberAcceptedIdentification(activeSelf, activeKey);
        }

        if (ShouldLog(ref _identificationCanonicalTraceCount, 16))
        {
            VirtualCameraState.Log(
                $"DiagnosticApiHook: observed camera list insert via {DescribeUser32Message(message)} hwnd=0x{windowHandle:X} -> index={result.ToInt64()}, activeSelf=0x{activeSelf:X}, activeKey=0x{activeKey:X16}.");
        }
    }

    private static bool TryDecodeListViewMessage(
        uint message,
        nint lParam,
        bool unicode,
        out string? text,
        out int itemIndex,
        out int subItemIndex,
        out uint mask)
    {
        text = null;
        itemIndex = -1;
        subItemIndex = -1;
        mask = 0;

        if (lParam == nint.Zero)
        {
            return false;
        }

        try
        {
            if (unicode)
            {
                var item = (LVITEMW*)lParam;
                itemIndex = item->iItem;
                subItemIndex = item->iSubItem;
                mask = item->mask;
                text = SafePtrToStringUni(item->pszText);
            }
            else
            {
                var item = (LVITEMA*)lParam;
                itemIndex = item->iItem;
                subItemIndex = item->iSubItem;
                mask = item->mask;
                text = SafePtrToStringAnsi(item->pszText);
            }
        }
        catch (Exception ex)
        {
            text = $"<decode-failed:{ex.GetType().Name}>";
        }

        return true;
    }

    private static string DescribeUser32Message(uint message)
    {
        return message switch
        {
            WmSetText => "WM_SETTEXT",
            LvmSetItemA => "LVM_SETITEMA",
            LvmInsertItemA => "LVM_INSERTITEMA",
            LvmSetItemTextA => "LVM_SETITEMTEXTA",
            LvmSetItemW => "LVM_SETITEMW",
            LvmInsertItemW => "LVM_INSERTITEMW",
            LvmSetItemTextW => "LVM_SETITEMTEXTW",
            _ => $"0x{message:X}",
        };
    }

    private static string? GetWindowClassName(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        var buffer = stackalloc char[MaxClassNameChars];
        var length = GetClassNameW(windowHandle, buffer, MaxClassNameChars);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static bool TryGetSyntheticFrameRateControlText(nint windowHandle, out string replacementText)
    {
        replacementText = string.Empty;
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        int controlId;
        try
        {
            controlId = GetDlgCtrlID(windowHandle);
        }
        catch
        {
            return false;
        }

        if (controlId != FrameRateMinTextControlId &&
            controlId != FrameRateMaxTextControlId &&
            controlId != FrameRateCurrentEditControlId)
        {
            return false;
        }

        if (!IsRayCiOptionsDialogChild(windowHandle))
        {
            return false;
        }

        ResolveFrameRateDisplayValues(out var minFrameRateHz, out var maxFrameRateHz, out var currentFrameRateHz);
        replacementText = controlId switch
        {
            FrameRateMinTextControlId => $"{minFrameRateHz.ToString("0.0", CultureInfo.InvariantCulture)} fps",
            FrameRateMaxTextControlId => $"{maxFrameRateHz.ToString("0.0", CultureInfo.InvariantCulture)} fps",
            FrameRateCurrentEditControlId => currentFrameRateHz.ToString("0.0", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

        return replacementText.Length > 0;
    }

    private static bool IsRayCiOptionsDialogChild(nint windowHandle)
    {
        for (var current = windowHandle; current != nint.Zero; current = GetParent(current))
        {
            var className = GetWindowClassName(current);
            if (!string.Equals(className, "#32770", StringComparison.Ordinal))
            {
                continue;
            }

            var title = GetWindowText(current);
            if (title.StartsWith("LiveMode: Options - ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetWindowText(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return string.Empty;
        }

        var buffer = stackalloc char[MaxWindowTextChars];
        var length = GetWindowTextW(windowHandle, buffer, MaxWindowTextChars);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static void ResolveFrameRateDisplayValues(
        out double minFrameRateHz,
        out double maxFrameRateHz,
        out double currentFrameRateHz)
    {
        const double compatibleMinFrameRateHz = 1.0;
        const double compatibleMaxFrameRateHz = 30.0;

        minFrameRateHz = compatibleMinFrameRateHz;
        maxFrameRateHz = compatibleMaxFrameRateHz;
        currentFrameRateHz = compatibleMaxFrameRateHz;

        if (!DahengFrameBridgeClient.TryGetControlState(out var state))
        {
            return;
        }

        minFrameRateHz = state.FrameRateMinHz > 0.0 ? state.FrameRateMinHz : compatibleMinFrameRateHz;
        maxFrameRateHz = state.FrameRateMaxHz >= minFrameRateHz ? state.FrameRateMaxHz : compatibleMaxFrameRateHz;

        if (maxFrameRateHz <= 0.0 || maxFrameRateHz < minFrameRateHz)
        {
            minFrameRateHz = compatibleMinFrameRateHz;
            maxFrameRateHz = compatibleMaxFrameRateHz;
        }

        minFrameRateHz = Math.Clamp(minFrameRateHz, compatibleMinFrameRateHz, compatibleMaxFrameRateHz);
        maxFrameRateHz = Math.Clamp(maxFrameRateHz, minFrameRateHz, compatibleMaxFrameRateHz);

        var bridgeCurrentFrameRateHz = state.FrameRateHz > 0.0 ? state.FrameRateHz : maxFrameRateHz;
        currentFrameRateHz = Math.Clamp(bridgeCurrentFrameRateHz, minFrameRateHz, maxFrameRateHz);
    }

    private static string DescribeNativeCallers()
    {
        try
        {
            const int maxFrames = 6;
            nint* frames = stackalloc nint[maxFrames];
            var captured = RtlCaptureStackBackTrace(2, maxFrames, frames, null);
            if (captured == 0)
            {
                return "<empty>";
            }

            var parts = new List<string>(captured);
            using var process = Process.GetCurrentProcess();
            var modules = process.Modules.Cast<ProcessModule>().ToArray();

            for (var i = 0; i < captured; i++)
            {
                var address = frames[i];
                string? match = null;
                foreach (var module in modules)
                {
                    var start = module.BaseAddress;
                    var end = start + module.ModuleMemorySize;
                    if (address >= start && address < end)
                    {
                        match = $"{module.ModuleName}+0x{address - start:X}";
                        break;
                    }
                }

                parts.Add(match ?? $"0x{address:X}");
            }

            return string.Join(" | ", parts);
        }
        catch (Exception ex)
        {
            return $"<stack-failed:{ex.GetType().Name}>";
        }
    }

    private static bool IsInterestingUiText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("license", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("CinCam", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("1201", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("plain", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteForLog(string? value)
    {
        return value is null ? "<null>" : "\"" + value.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    private static string DescribeIdentificationDescriptor(nint descriptor)
    {
        if (descriptor == nint.Zero)
        {
            return "<null>";
        }

        try
        {
            var bytes = new byte[IdentificationDescriptorSize];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            var q0 = BitConverter.ToUInt64(bytes, 0x00);
            var q8 = BitConverter.ToUInt64(bytes, 0x08);
            var q10 = BitConverter.ToUInt64(bytes, 0x10);
            var q18 = BitConverter.ToUInt64(bytes, 0x18);
            var q20 = BitConverter.ToUInt64(bytes, 0x20);
            var q28 = BitConverter.ToUInt64(bytes, 0x28);
            var d30 = BitConverter.ToInt32(bytes, 0x30);
            var d34 = BitConverter.ToInt32(bytes, 0x34);
            var q38 = BitConverter.ToUInt64(bytes, 0x38);
            var pointerDetails = new List<string>(4);
            AppendPointerPreview(pointerDetails, "q10", q10);
            AppendPointerPreview(pointerDetails, "q18", q18);
            AppendPointerPreview(pointerDetails, "q20", q20);
            AppendPointerPreview(pointerDetails, "q38", q38);

            return
                $"hex={Convert.ToHexString(bytes)}, " +
                $"q00=0x{q0:X16}, q08=0x{q8:X16}, q10=0x{q10:X16}, q18=0x{q18:X16}, " +
                $"q20=0x{q20:X16}, q28=0x{q28:X16}, d30={d30}, d34={d34}, q38=0x{q38:X16}" +
                (pointerDetails.Count == 0 ? string.Empty : ", " + string.Join(", ", pointerDetails));
        }
        catch (Exception ex)
        {
            return $"<descriptor-failed:{ex.GetType().Name}>";
        }
    }

    private static void AppendPointerPreview(List<string> details, string name, ulong address)
    {
        if (address == 0)
        {
            return;
        }

        details.Add($"{name}*={DescribePointerPreview(address)}");
    }

    private static string DescribePointerPreview(ulong address)
    {
        if (address > MaxCanonicalUserPointer || address < 0x10000)
        {
            return $"<noncanonical:0x{address:X16}>";
        }

        try
        {
            var mbi = default(MemoryBasicInformation);
            if (VirtualQuery((nint)address, &mbi, (nuint)sizeof(MemoryBasicInformation)) == 0)
            {
                return "<unmapped>";
            }

            if (mbi.State != MemCommit || (mbi.Protect & (PageNoAccess | PageGuard)) != 0 || mbi.RegionSize == 0)
            {
                return $"<state=0x{mbi.State:X},protect=0x{mbi.Protect:X}>";
            }

            var regionStart = (ulong)mbi.BaseAddress;
            var regionSize = (ulong)mbi.RegionSize;
            var regionEnd = regionStart + regionSize;
            if (address < regionStart || address >= regionEnd)
            {
                return $"<out-of-range:0x{regionStart:X16}+0x{regionSize:X}>";
            }

            var available = regionEnd - address;
            var sampleLength = (int)Math.Min(available, 96UL);
            if (sampleLength <= 0)
            {
                return "<empty>";
            }

            var bytes = new byte[sampleLength];
            Marshal.Copy((nint)address, bytes, 0, sampleLength);

            var parts = new List<string>(3)
            {
                $"hex={Convert.ToHexString(bytes)}"
            };

            var ascii = ExtractStringPreview(bytes, unicode: false);
            if (!string.IsNullOrEmpty(ascii))
            {
                parts.Insert(0, $"ansi=\"{ascii}\"");
            }

            var utf16 = ExtractStringPreview(bytes, unicode: true);
            if (!string.IsNullOrEmpty(utf16) &&
                !string.Equals(utf16, ascii, StringComparison.Ordinal))
            {
                parts.Insert(ascii is null ? 0 : 1, $"utf16=\"{utf16}\"");
            }

            return string.Join(";", parts);
        }
        catch (Exception ex)
        {
            return $"<preview-failed:{ex.GetType().Name}>";
        }
    }

    private static string? ExtractStringPreview(byte[] bytes, bool unicode)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        if (!unicode)
        {
            var length = 0;
            while (length < bytes.Length)
            {
                var value = bytes[length];
                if (value == 0)
                {
                    break;
                }

                if (value < 0x20 || value > 0x7E)
                {
                    return null;
                }

                length++;
            }

            return length == 0 ? null : Encoding.ASCII.GetString(bytes, 0, Math.Min(length, 48));
        }

        var charCount = 0;
        while ((charCount * 2) + 1 < bytes.Length)
        {
            var value = BitConverter.ToUInt16(bytes, charCount * 2);
            if (value == 0)
            {
                break;
            }

            if (value < 0x20 || value > 0x7E)
            {
                return null;
            }

            charCount++;
        }

        return charCount == 0 ? null : Encoding.Unicode.GetString(bytes, 0, Math.Min(charCount * 2, 96));
    }

    private static bool IsInterestingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Contains("license", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("rayci", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("cinogy", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("update", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("fgcamera", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".lic", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTraceModule(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Contains("cinogy", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("rayci", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("fgcamera", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("xmlrpc", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("gx", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("bgapi", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("license", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".cti", StringComparison.OrdinalIgnoreCase);
    }

    private static string? PtrToStringAnsi(byte* value)
    {
        return value == null ? null : Marshal.PtrToStringAnsi((nint)value);
    }

    private static string? PtrToStringUni(char* value)
    {
        return value == null ? null : Marshal.PtrToStringUni((nint)value);
    }

    private static string? SafePtrToStringAnsi(byte* value)
    {
        if (value == null)
        {
            return null;
        }

        if ((nint)value == -1)
        {
            return "<callback>";
        }

        try
        {
            return Marshal.PtrToStringAnsi((nint)value);
        }
        catch (Exception ex)
        {
            return $"<ansi-failed:{ex.GetType().Name}>";
        }
    }

    private static string? SafePtrToStringUni(char* value)
    {
        if (value == null)
        {
            return null;
        }

        if ((nint)value == -1)
        {
            return "<callback>";
        }

        try
        {
            return Marshal.PtrToStringUni((nint)value);
        }
        catch (Exception ex)
        {
            return $"<wide-failed:{ex.GetType().Name}>";
        }
    }

    private static bool ShouldLog(ref int counter, int limit)
    {
        if (!VirtualCameraState.IsVerboseDebugLoggingEnabled())
        {
            return false;
        }

        var next = Interlocked.Increment(ref counter);
        return next <= limit;
    }

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
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEMA
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public byte* pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public uint* puColumns;
        public int* piColFmt;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public char* pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public uint* puColumns;
        public int* piColFmt;
        public int iGroup;
    }
}
