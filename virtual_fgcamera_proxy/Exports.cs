using System.Runtime.InteropServices;

namespace VirtualFGCameraProxy;

internal static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "FGInitModule")]
    public static nuint FgInitModule(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGInitModule", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "FGExitModule")]
    public static nuint FgExitModule(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGExitModule", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "FGGetNodeList")]
    public static nuint FgGetNodeList(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGGetNodeList", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "FGGetHostLicenseRequest")]
    public static nuint FgGetHostLicenseRequest(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGGetHostLicenseRequest", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "FGGetLicenseInfo")]
    public static nuint FgGetLicenseInfo(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGGetLicenseInfo", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "FGGetLicenseType")]
    public static nuint FgGetLicenseType(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("FGGetLicenseType", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "??0CFGCamera@@QEAA@XZ")]
    public static nuint CfgCameraCtor(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("??0CFGCamera@@QEAA@XZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "??1CFGCamera@@UEAA@XZ")]
    public static nuint CfgCameraDtor(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("??1CFGCamera@@UEAA@XZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetPtrDCam@CFGCamera@@UEAAPEAVCCamera@@XZ")]
    public static nuint GetPtrDCam(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetPtrDCam@CFGCamera@@UEAAPEAVCCamera@@XZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z")]
    public static nuint Connect(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?Disconnect@CFGCamera@@UEAAKXZ")]
    public static nuint Disconnect(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?Disconnect@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?SetParameter@CFGCamera@@UEAAKGK@Z")]
    public static nuint SetParameter(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?SetParameter@CFGCamera@@UEAAKGK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetParameter@CFGCamera@@UEAAKGPEAK@Z")]
    public static nuint GetParameter(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetParameter@CFGCamera@@UEAAKGPEAK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetParameterInfo@CFGCamera@@UEAAKGPEAUFGPINFO@@@Z")]
    public static nuint GetParameterInfo(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetParameterInfo@CFGCamera@@UEAAKGPEAUFGPINFO@@@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?OpenCapture@CFGCamera@@UEAAKXZ")]
    public static nuint OpenCapture(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?OpenCapture@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?CloseCapture@CFGCamera@@UEAAKXZ")]
    public static nuint CloseCapture(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?CloseCapture@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?AssignUserBuffers@CFGCamera@@UEAAKKKPEAPEAX@Z")]
    public static nuint AssignUserBuffers(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?AssignUserBuffers@CFGCamera@@UEAAKKKPEAPEAX@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?StartDevice@CFGCamera@@UEAAKXZ")]
    public static nuint StartDevice(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?StartDevice@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?StopDevice@CFGCamera@@UEAAKXZ")]
    public static nuint StopDevice(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?StopDevice@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetFrame@CFGCamera@@UEAAKPEAUFGFRAME@@K@Z")]
    public static nuint GetFrame(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetFrame@CFGCamera@@UEAAKPEAUFGFRAME@@K@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?PutFrame@CFGCamera@@UEAAKPEAUFGFRAME@@@Z")]
    public static nuint PutFrame(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?PutFrame@CFGCamera@@UEAAKPEAUFGFRAME@@@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?DiscardFrames@CFGCamera@@UEAAKXZ")]
    public static nuint DiscardFrames(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?DiscardFrames@CFGCamera@@UEAAKXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z")]
    public static nuint GetDeviceName(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetContext@CFGCamera@@UEAAPEAXXZ")]
    public static nuint GetContext(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetContext@CFGCamera@@UEAAPEAXXZ", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?GetLicenseRequest@CFGCamera@@UEAAKPEADK@Z")]
    public static nuint GetLicenseRequest(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?GetLicenseRequest@CFGCamera@@UEAAKPEADK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?WriteRegister@CFGCamera@@UEAAKKK@Z")]
    public static nuint WriteRegister(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?WriteRegister@CFGCamera@@UEAAKKK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?ReadRegister@CFGCamera@@UEAAKKPEAK@Z")]
    public static nuint ReadRegister(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?ReadRegister@CFGCamera@@UEAAKKPEAK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?WriteBlock@CFGCamera@@UEAAKKPEAEK@Z")]
    public static nuint WriteBlock(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?WriteBlock@CFGCamera@@UEAAKKPEAEK@Z", a1, a2, a3, a4, a5, a6, a7, a8);

    [UnmanagedCallersOnly(EntryPoint = "?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z")]
    public static nuint ReadBlock(nuint a1, nuint a2, nuint a3, nuint a4, nuint a5, nuint a6, nuint a7, nuint a8) =>
        Forwarder.Call("?ReadBlock@CFGCamera@@UEAAKKPEAEK@Z", a1, a2, a3, a4, a5, a6, a7, a8);
}
