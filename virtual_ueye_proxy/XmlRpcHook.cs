using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualUEyeProxy;

internal static unsafe class XmlRpcHook
{
    private const uint PageReadWrite = 0x04;
    private const ushort ImageDirectoryEntryImport = 1;

    private static readonly object Gate = new();

    private static bool _installAttempted;
    private static nint _originalExecute;
    private static nint _originalBindAndListen;
    private static nint _assignBoolExport;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint moduleHandle, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

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
            var mainModule = GetModuleHandleW(null);
            if (mainModule == nint.Zero)
            {
                VirtualCameraState.Log("XmlRpcHook: GetModuleHandleW(NULL) failed.");
                return;
            }

            var patched = 0;
            patched += PatchImport(
                mainModule,
                "XmlRpc.dll",
                "?execute@XmlRpcClient@XmlRpc@@QEAA_NPEBDAEBVXmlRpcValue@2@AEAV32@N@Z",
                (nint)(delegate* unmanaged<nint, byte*, nint, nint, double, byte>)&ExecuteHook,
                ref _originalExecute);
            patched += PatchImport(
                mainModule,
                "XmlRpc.dll",
                "?bindAndListen@XmlRpcServer@XmlRpc@@QEAA_NHH@Z",
                (nint)(delegate* unmanaged<nint, int, int, byte>)&BindAndListenHook,
                ref _originalBindAndListen);

            VirtualCameraState.DebugLog($"XmlRpcHook: patched {patched} XmlRpc imports.");
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"XmlRpcHook: install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_XmlRpcClientExecute_Hook")]
    private static byte ExecuteHook(nint thisPtr, byte* methodName, nint inputValue, nint resultValue, double timeoutSeconds)
    {
        var method = PtrToStringAnsi(methodName) ?? string.Empty;
        VirtualCameraState.DebugLog($"XmlRpcHook: execute(method={method}, timeout={timeoutSeconds:0.###})");

        if (ShouldForceLicense(method) && TryAssignBoolResult(resultValue, true))
        {
            VirtualCameraState.DebugLog($"XmlRpcHook: forced XmlRpc license success for {method}");
            return 1;
        }

        if (_originalExecute == nint.Zero)
        {
            VirtualCameraState.DebugLog($"XmlRpcHook: original execute missing for {method}");
            return 0;
        }

        var original = (delegate* unmanaged<nint, byte*, nint, nint, double, byte>)_originalExecute;
        var result = original(thisPtr, methodName, inputValue, resultValue, timeoutSeconds);
        VirtualCameraState.DebugLog($"XmlRpcHook: execute(method={method}) -> {result}");
        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "Ultron_XmlRpcServerBindAndListen_Hook")]
    private static byte BindAndListenHook(nint thisPtr, int port, int backlog)
    {
        VirtualCameraState.DebugLog($"XmlRpcHook: bindAndListen(port={port}, backlog={backlog})");
        if (_originalBindAndListen == nint.Zero)
        {
            return 0;
        }

        var original = (delegate* unmanaged<nint, int, int, byte>)_originalBindAndListen;
        var result = original(thisPtr, port, backlog);
        VirtualCameraState.DebugLog($"XmlRpcHook: bindAndListen(port={port}) -> {result}");
        return result;
    }

    private static bool TryAssignBoolResult(nint resultValue, bool value)
    {
        if (resultValue == nint.Zero)
        {
            return false;
        }

        var assignExport = ResolveAssignBoolExport();
        if (assignExport == nint.Zero)
        {
            return false;
        }

        var valuePtr = stackalloc byte[1];
        valuePtr[0] = value ? (byte)1 : (byte)0;
        var assign = (delegate* unmanaged<nint, byte*, nint>)assignExport;
        _ = assign(resultValue, valuePtr);

        return true;
    }

    private static nint ResolveAssignBoolExport()
    {
        if (_assignBoolExport != nint.Zero)
        {
            return _assignBoolExport;
        }

        var module = GetModuleHandleW("XmlRpc.dll");
        if (module == nint.Zero)
        {
            return nint.Zero;
        }

        _assignBoolExport = GetProcAddress(module, "??4XmlRpcValue@XmlRpc@@QEAAAEAV01@AEB_N@Z");
        if (_assignBoolExport == nint.Zero)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            VirtualCameraState.DebugLog($"XmlRpcHook: resolve assign(bool) failed: {error.Message}");
        }

        return _assignBoolExport;
    }

    private static bool ShouldForceLicense(string methodName)
    {
        var force = Environment.GetEnvironmentVariable("ULTRON_RAYCI_XMLRPC_FORCE_LICENSE");
        if (!string.Equals(force, "1", StringComparison.Ordinal) &&
            !string.Equals(force, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(force, "yes", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(force, "on", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(methodName, "RayCi.License.isValid", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(methodName, "RayCi.License.isExternalToolLicensed", StringComparison.OrdinalIgnoreCase);
    }

    private static int PatchImport(nint moduleBase, string targetDll, string targetImport, nint replacement, ref nint original)
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

        var importDescriptor = (ImageImportDescriptor*)(basePtr + importDirectoryRva);
        for (; importDescriptor->Name != 0; importDescriptor++)
        {
            var dllName = Marshal.PtrToStringAnsi((nint)(basePtr + importDescriptor->Name));
            if (!string.Equals(dllName, targetDll, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lookup = importDescriptor->OriginalFirstThunk != 0
                ? (ulong*)(basePtr + importDescriptor->OriginalFirstThunk)
                : (ulong*)(basePtr + importDescriptor->FirstThunk);
            var iat = (nint*)(basePtr + importDescriptor->FirstThunk);

            for (; *lookup != 0; lookup++, iat++)
            {
                var lookupEntry = *lookup;
                if ((lookupEntry & 0x8000_0000_0000_0000ul) != 0)
                {
                    continue;
                }

                var importByName = basePtr + (int)lookupEntry;
                var importName = Marshal.PtrToStringAnsi((nint)(importByName + 2));
                if (!string.Equals(importName, targetImport, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!VirtualProtect((nint)iat, (nuint)sizeof(nint), PageReadWrite, out var oldProtect))
                {
                    var error = new Win32Exception(Marshal.GetLastWin32Error());
                    VirtualCameraState.DebugLog($"XmlRpcHook: VirtualProtect failed for {targetImport}: {error.Message}");
                    return 0;
                }

                original = *iat;
                *iat = replacement;
                _ = VirtualProtect((nint)iat, (nuint)sizeof(nint), oldProtect, out _);
                VirtualCameraState.DebugLog($"XmlRpcHook: patched {targetDll}!{targetImport}");
                return 1;
            }
        }

        return 0;
    }

    private static string? PtrToStringAnsi(byte* value)
    {
        return value == null ? null : Marshal.PtrToStringAnsi((nint)value);
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
}
