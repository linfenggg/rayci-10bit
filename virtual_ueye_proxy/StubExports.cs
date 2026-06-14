using System.Runtime.InteropServices;

namespace VirtualUEyeProxy;

internal static unsafe partial class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "is_AutoParameter")]
    public static int Stub_AutoParameter() => Unsupported("is_AutoParameter");

    [UnmanagedCallersOnly(EntryPoint = "is_Blacklevel")]
    public static int Stub_Blacklevel(uint cameraHandle, uint command, void* parameter, uint parameterSize)
        => VirtualCameraState.Blacklevel(command, parameter, parameterSize);

    [UnmanagedCallersOnly(EntryPoint = "is_BoardStatus")]
    public static int Stub_BoardStatus() => Unsupported("is_BoardStatus");

    [UnmanagedCallersOnly(EntryPoint = "is_BootBoost")]
    public static int Stub_BootBoost() => Unsupported("is_BootBoost");

    [UnmanagedCallersOnly(EntryPoint = "is_CameraStatus")]
    public static int Stub_CameraStatus() => SuccessNoOp("is_CameraStatus");

    [UnmanagedCallersOnly(EntryPoint = "is_CaptureStatus")]
    public static int Stub_CaptureStatus() => SuccessNoOp("is_CaptureStatus");

    [UnmanagedCallersOnly(EntryPoint = "is_ColorTemperature")]
    public static int Stub_ColorTemperature() => Unsupported("is_ColorTemperature");

    [UnmanagedCallersOnly(EntryPoint = "is_Convert")]
    public static int Stub_Convert() => Unsupported("is_Convert");

    [UnmanagedCallersOnly(EntryPoint = "is_ConvertImage")]
    public static int Stub_ConvertImage() => Unsupported("is_ConvertImage");

    [UnmanagedCallersOnly(EntryPoint = "is_DirectRenderer")]
    public static int Stub_DirectRenderer() => Unsupported("is_DirectRenderer");

    [UnmanagedCallersOnly(EntryPoint = "is_DisableDDOverlay")]
    public static int Stub_DisableDDOverlay() => Unsupported("is_DisableDDOverlay");

    [UnmanagedCallersOnly(EntryPoint = "is_EdgeEnhancement")]
    public static int Stub_EdgeEnhancement() => Unsupported("is_EdgeEnhancement");

    [UnmanagedCallersOnly(EntryPoint = "is_EnableAutoExit")]
    public static int Stub_EnableAutoExit() => Unsupported("is_EnableAutoExit");

    [UnmanagedCallersOnly(EntryPoint = "is_EnableDDOverlay")]
    public static int Stub_EnableDDOverlay() => Unsupported("is_EnableDDOverlay");

    [UnmanagedCallersOnly(EntryPoint = "is_EnableHdr")]
    public static int Stub_EnableHdr() => Unsupported("is_EnableHdr");

    [UnmanagedCallersOnly(EntryPoint = "is_ExitBoard")]
    public static int Stub_ExitBoard() => Unsupported("is_ExitBoard");

    [UnmanagedCallersOnly(EntryPoint = "is_ExitFalcon")]
    public static int Stub_ExitFalcon() => Unsupported("is_ExitFalcon");

    [UnmanagedCallersOnly(EntryPoint = "is_FaceDetection")]
    public static int Stub_FaceDetection() => Unsupported("is_FaceDetection");

    [UnmanagedCallersOnly(EntryPoint = "is_Focus")]
    public static int Stub_Focus() => Unsupported("is_Focus");

    [UnmanagedCallersOnly(EntryPoint = "is_ForceTrigger")]
    public static int Stub_ForceTrigger() => Unsupported("is_ForceTrigger");

    [UnmanagedCallersOnly(EntryPoint = "is_GetCameraLUT")]
    public static int Stub_GetCameraLUT() => Unsupported("is_GetCameraLUT");

    [UnmanagedCallersOnly(EntryPoint = "is_GetColorConverter")]
    public static int Stub_GetColorConverter() => Unsupported("is_GetColorConverter");

    [UnmanagedCallersOnly(EntryPoint = "is_GetComportNumber")]
    public static int Stub_GetComportNumber() => Unsupported("is_GetComportNumber");

    [UnmanagedCallersOnly(EntryPoint = "is_GetCurrentField")]
    public static int Stub_GetCurrentField() => Unsupported("is_GetCurrentField");

    [UnmanagedCallersOnly(EntryPoint = "is_GetDC")]
    public static int Stub_GetDC() => Unsupported("is_GetDC");

    [UnmanagedCallersOnly(EntryPoint = "is_GetDDOvlSurface")]
    public static int Stub_GetDDOvlSurface() => Unsupported("is_GetDDOvlSurface");

    [UnmanagedCallersOnly(EntryPoint = "is_GetEthDeviceInfo")]
    public static int Stub_GetEthDeviceInfo() => Unsupported("is_GetEthDeviceInfo");

    [UnmanagedCallersOnly(EntryPoint = "is_GetExposureRange")]
    public static int Stub_GetExposureRange() => Unsupported("is_GetExposureRange");

    [UnmanagedCallersOnly(EntryPoint = "is_GetGlobalFlashDelays")]
    public static int Stub_GetGlobalFlashDelays() => Unsupported("is_GetGlobalFlashDelays");

    [UnmanagedCallersOnly(EntryPoint = "is_GetHdrKneepointInfo")]
    public static int Stub_GetHdrKneepointInfo() => Unsupported("is_GetHdrKneepointInfo");

    [UnmanagedCallersOnly(EntryPoint = "is_GetHdrKneepoints")]
    public static int Stub_GetHdrKneepoints() => Unsupported("is_GetHdrKneepoints");

    [UnmanagedCallersOnly(EntryPoint = "is_GetHdrMode")]
    public static int Stub_GetHdrMode() => Unsupported("is_GetHdrMode");

    [UnmanagedCallersOnly(EntryPoint = "is_GetImageHistogram")]
    public static int Stub_GetImageHistogram() => Unsupported("is_GetImageHistogram");

    [UnmanagedCallersOnly(EntryPoint = "is_GetIRQ")]
    public static int Stub_GetIRQ() => Unsupported("is_GetIRQ");

    [UnmanagedCallersOnly(EntryPoint = "is_GetLastMemorySequence")]
    public static int Stub_GetLastMemorySequence() => Unsupported("is_GetLastMemorySequence");

    [UnmanagedCallersOnly(EntryPoint = "is_GetMemorySequenceWindow")]
    public static int Stub_GetMemorySequenceWindow() => Unsupported("is_GetMemorySequenceWindow");

    [UnmanagedCallersOnly(EntryPoint = "is_GetOsVersion")]
    public static int Stub_GetOsVersion() => Unsupported("is_GetOsVersion");

    [UnmanagedCallersOnly(EntryPoint = "is_GetPciSlot")]
    public static int Stub_GetPciSlot() => Unsupported("is_GetPciSlot");

    [UnmanagedCallersOnly(EntryPoint = "is_GetPixelClockRange")]
    public static int Stub_GetPixelClockRange() => Unsupported("is_GetPixelClockRange");

    [UnmanagedCallersOnly(EntryPoint = "is_GetSensorScalerInfo")]
    public static int Stub_GetSensorScalerInfo() => Unsupported("is_GetSensorScalerInfo");

    [UnmanagedCallersOnly(EntryPoint = "is_GetTestImageValueRange")]
    public static int Stub_GetTestImageValueRange() => Unsupported("is_GetTestImageValueRange");

    [UnmanagedCallersOnly(EntryPoint = "is_GetTimeout")]
    public static int Stub_GetTimeout() => Unsupported("is_GetTimeout");

    [UnmanagedCallersOnly(EntryPoint = "is_GetVsyncCount")]
    public static int Stub_GetVsyncCount() => Unsupported("is_GetVsyncCount");

    [UnmanagedCallersOnly(EntryPoint = "is_GetWhiteBalanceMultipliers")]
    public static int Stub_GetWhiteBalanceMultipliers() => Unsupported("is_GetWhiteBalanceMultipliers");

    [UnmanagedCallersOnly(EntryPoint = "is_HideDDOverlay")]
    public static int Stub_HideDDOverlay() => Unsupported("is_HideDDOverlay");

    [UnmanagedCallersOnly(EntryPoint = "is_HotPixel")]
    public static int Stub_HotPixel() => SuccessNoOp("is_HotPixel");

    [UnmanagedCallersOnly(EntryPoint = "is_ImageFile")]
    public static int Stub_ImageFile() => Unsupported("is_ImageFile");

    [UnmanagedCallersOnly(EntryPoint = "is_ImageFormat")]
    public static int Stub_ImageFormat() => Unsupported("is_ImageFormat");

    [UnmanagedCallersOnly(EntryPoint = "is_ImageStabilization")]
    public static int Stub_ImageStabilization() => Unsupported("is_ImageStabilization");

    [UnmanagedCallersOnly(EntryPoint = "is_InitBoard")]
    public static int Stub_InitBoard() => Unsupported("is_InitBoard");

    [UnmanagedCallersOnly(EntryPoint = "is_InitFalcon")]
    public static int Stub_InitFalcon() => Unsupported("is_InitFalcon");

    [UnmanagedCallersOnly(EntryPoint = "is_IpConfig")]
    public static int Stub_IpConfig() => Unsupported("is_IpConfig");

    [UnmanagedCallersOnly(EntryPoint = "is_IsMemoryBoardConnected")]
    public static int Stub_IsMemoryBoardConnected() => Unsupported("is_IsMemoryBoardConnected");

    [UnmanagedCallersOnly(EntryPoint = "is_LoadBadPixelCorrectionTable")]
    public static int Stub_LoadBadPixelCorrectionTable() => Unsupported("is_LoadBadPixelCorrectionTable");

    [UnmanagedCallersOnly(EntryPoint = "is_LoadImage")]
    public static int Stub_LoadImage() => Unsupported("is_LoadImage");

    [UnmanagedCallersOnly(EntryPoint = "is_LoadImageMem")]
    public static int Stub_LoadImageMem() => Unsupported("is_LoadImageMem");

    [UnmanagedCallersOnly(EntryPoint = "is_LockDDMem")]
    public static int Stub_LockDDMem() => Unsupported("is_LockDDMem");

    [UnmanagedCallersOnly(EntryPoint = "is_LockDDOverlayMem")]
    public static int Stub_LockDDOverlayMem() => Unsupported("is_LockDDOverlayMem");

    [UnmanagedCallersOnly(EntryPoint = "is_MemoryFreezeVideo")]
    public static int Stub_MemoryFreezeVideo() => Unsupported("is_MemoryFreezeVideo");

    [UnmanagedCallersOnly(EntryPoint = "is_OvlSurfaceOffWhileMove")]
    public static int Stub_OvlSurfaceOffWhileMove() => Unsupported("is_OvlSurfaceOffWhileMove");

    [UnmanagedCallersOnly(EntryPoint = "is_ParameterSet")]
    public static int Stub_ParameterSet() => Unsupported("is_ParameterSet");

    [UnmanagedCallersOnly(EntryPoint = "is_PrepareStealVideo")]
    public static int Stub_PrepareStealVideo() => Unsupported("is_PrepareStealVideo");

    [UnmanagedCallersOnly(EntryPoint = "is_ReadEEPROM")]
    public static int Stub_ReadEEPROM() => SuccessNoOp("is_ReadEEPROM");

    [UnmanagedCallersOnly(EntryPoint = "is_ReadI2C")]
    public static int Stub_ReadI2C() => Unsupported("is_ReadI2C");

    [UnmanagedCallersOnly(EntryPoint = "is_ReleaseDC")]
    public static int Stub_ReleaseDC() => Unsupported("is_ReleaseDC");

    [UnmanagedCallersOnly(EntryPoint = "is_RenderBitmap")]
    public static int Stub_RenderBitmap() => Unsupported("is_RenderBitmap");

    [UnmanagedCallersOnly(EntryPoint = "is_ResetMemory")]
    public static int Stub_ResetMemory() => Unsupported("is_ResetMemory");

    [UnmanagedCallersOnly(EntryPoint = "is_Saturation")]
    public static int Stub_Saturation() => Unsupported("is_Saturation");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveBadPixelCorrectionTable")]
    public static int Stub_SaveBadPixelCorrectionTable() => Unsupported("is_SaveBadPixelCorrectionTable");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveImage")]
    public static int Stub_SaveImage() => Unsupported("is_SaveImage");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveImageEx")]
    public static int Stub_SaveImageEx() => Unsupported("is_SaveImageEx");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveImageMem")]
    public static int Stub_SaveImageMem() => Unsupported("is_SaveImageMem");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveImageMemEx")]
    public static int Stub_SaveImageMemEx() => Unsupported("is_SaveImageMemEx");

    [UnmanagedCallersOnly(EntryPoint = "is_ScaleDDOverlay")]
    public static int Stub_ScaleDDOverlay() => Unsupported("is_ScaleDDOverlay");

    [UnmanagedCallersOnly(EntryPoint = "is_ScenePreset")]
    public static int Stub_ScenePreset() => Unsupported("is_ScenePreset");

    [UnmanagedCallersOnly(EntryPoint = "is_SetAGC")]
    public static int Stub_SetAGC() => Unsupported("is_SetAGC");

    [UnmanagedCallersOnly(EntryPoint = "is_SetAutoCfgIpSetup")]
    public static int Stub_SetAutoCfgIpSetup() => Unsupported("is_SetAutoCfgIpSetup");

    [UnmanagedCallersOnly(EntryPoint = "is_SetBadPixelCorrection")]
    public static int Stub_SetBadPixelCorrection() => Unsupported("is_SetBadPixelCorrection");

    [UnmanagedCallersOnly(EntryPoint = "is_SetBadPixelCorrectionTable")]
    public static int Stub_SetBadPixelCorrectionTable() => Unsupported("is_SetBadPixelCorrectionTable");

    [UnmanagedCallersOnly(EntryPoint = "is_SetBayerConversion")]
    public static int Stub_SetBayerConversion() => Unsupported("is_SetBayerConversion");

    [UnmanagedCallersOnly(EntryPoint = "is_SetBinning")]
    public static int Stub_SetBinning(uint cameraHandle, int mode) => VirtualCameraState.SetBinning(mode);

    [UnmanagedCallersOnly(EntryPoint = "is_SetBlCompensation")]
    public static int Stub_SetBlCompensation() => Unsupported("is_SetBlCompensation");

    [UnmanagedCallersOnly(EntryPoint = "is_SetBrightness")]
    public static int Stub_SetBrightness() => Unsupported("is_SetBrightness");

    [UnmanagedCallersOnly(EntryPoint = "is_SetCameraID")]
    public static int Stub_SetCameraID() => Unsupported("is_SetCameraID");

    [UnmanagedCallersOnly(EntryPoint = "is_SetCameraLUT")]
    public static int Stub_SetCameraLUT() => Unsupported("is_SetCameraLUT");

    [UnmanagedCallersOnly(EntryPoint = "is_SetCaptureMode")]
    public static int Stub_SetCaptureMode() => Unsupported("is_SetCaptureMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetColorConverter")]
    public static int Stub_SetColorConverter() => Unsupported("is_SetColorConverter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetColorCorrection")]
    public static int Stub_SetColorCorrection() => Unsupported("is_SetColorCorrection");

    [UnmanagedCallersOnly(EntryPoint = "is_SetContrast")]
    public static int Stub_SetContrast() => Unsupported("is_SetContrast");

    [UnmanagedCallersOnly(EntryPoint = "is_SetConvertParam")]
    public static int Stub_SetConvertParam() => Unsupported("is_SetConvertParam");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDDUpdateTime")]
    public static int Stub_SetDDUpdateTime() => Unsupported("is_SetDDUpdateTime");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDisplayPos")]
    public static int Stub_SetDisplayPos() => Unsupported("is_SetDisplayPos");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDisplaySize")]
    public static int Stub_SetDisplaySize() => Unsupported("is_SetDisplaySize");

    [UnmanagedCallersOnly(EntryPoint = "is_SetEdgeEnhancement")]
    public static int Stub_SetEdgeEnhancement() => Unsupported("is_SetEdgeEnhancement");

    [UnmanagedCallersOnly(EntryPoint = "is_SetExposureTime")]
    public static int Stub_SetExposureTime() => Unsupported("is_SetExposureTime");

    [UnmanagedCallersOnly(EntryPoint = "is_SetFlashDelay")]
    public static int Stub_SetFlashDelay() => Unsupported("is_SetFlashDelay");

    [UnmanagedCallersOnly(EntryPoint = "is_SetFlashStrobe")]
    public static int Stub_SetFlashStrobe() => Unsupported("is_SetFlashStrobe");

    [UnmanagedCallersOnly(EntryPoint = "is_SetGainBoost")]
    public static int Stub_SetGainBoost(uint cameraHandle, int mode) => VirtualCameraState.SetGainBoost(mode);

    [UnmanagedCallersOnly(EntryPoint = "is_SetGamma")]
    public static int Stub_SetGamma() => SuccessNoOp("is_SetGamma");

    [UnmanagedCallersOnly(EntryPoint = "is_SetGlobalShutter")]
    public static int Stub_SetGlobalShutter() => SuccessNoOp("is_SetGlobalShutter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetHardwareGamma")]
    public static int Stub_SetHardwareGamma() => SuccessNoOp("is_SetHardwareGamma");

    [UnmanagedCallersOnly(EntryPoint = "is_SetHdrKneepoints")]
    public static int Stub_SetHdrKneepoints() => Unsupported("is_SetHdrKneepoints");

    [UnmanagedCallersOnly(EntryPoint = "is_SetHorFilter")]
    public static int Stub_SetHorFilter() => Unsupported("is_SetHorFilter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetHue")]
    public static int Stub_SetHue() => Unsupported("is_SetHue");

    [UnmanagedCallersOnly(EntryPoint = "is_SetHWGainFactor")]
    public static int Stub_SetHWGainFactor(uint cameraHandle, int mode, int factor) => VirtualCameraState.SetHWGainFactor(mode, factor);

    [UnmanagedCallersOnly(EntryPoint = "is_SetHwnd")]
    public static int Stub_SetHwnd() => Unsupported("is_SetHwnd");

    [UnmanagedCallersOnly(EntryPoint = "is_SetIO")]
    public static int Stub_SetIO() => Unsupported("is_SetIO");

    [UnmanagedCallersOnly(EntryPoint = "is_SetIOMask")]
    public static int Stub_SetIOMask() => Unsupported("is_SetIOMask");

    [UnmanagedCallersOnly(EntryPoint = "is_SetKeyColor")]
    public static int Stub_SetKeyColor() => Unsupported("is_SetKeyColor");

    [UnmanagedCallersOnly(EntryPoint = "is_SetKeyOffset")]
    public static int Stub_SetKeyOffset() => Unsupported("is_SetKeyOffset");

    [UnmanagedCallersOnly(EntryPoint = "is_SetLED")]
    public static int Stub_SetLED() => Unsupported("is_SetLED");

    [UnmanagedCallersOnly(EntryPoint = "is_SetMemoryMode")]
    public static int Stub_SetMemoryMode() => Unsupported("is_SetMemoryMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetOptimalCameraTiming")]
    public static int Stub_SetOptimalCameraTiming() => Unsupported("is_SetOptimalCameraTiming");

    [UnmanagedCallersOnly(EntryPoint = "is_SetPacketFilter")]
    public static int Stub_SetPacketFilter() => Unsupported("is_SetPacketFilter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetParentHwnd")]
    public static int Stub_SetParentHwnd() => Unsupported("is_SetParentHwnd");

    [UnmanagedCallersOnly(EntryPoint = "is_SetPersistentIpCfg")]
    public static int Stub_SetPersistentIpCfg() => Unsupported("is_SetPersistentIpCfg");

    [UnmanagedCallersOnly(EntryPoint = "is_SetRenderMode")]
    public static int Stub_SetRenderMode() => Unsupported("is_SetRenderMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetRopEffect")]
    public static int Stub_SetRopEffect() => Unsupported("is_SetRopEffect");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSaturation")]
    public static int Stub_SetSaturation() => Unsupported("is_SetSaturation");

    [UnmanagedCallersOnly(EntryPoint = "is_SetScaler")]
    public static int Stub_SetScaler() => Unsupported("is_SetScaler");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSensorScaler")]
    public static int Stub_SetSensorScaler() => Unsupported("is_SetSensorScaler");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSensorTestImage")]
    public static int Stub_SetSensorTestImage() => Unsupported("is_SetSensorTestImage");

    [UnmanagedCallersOnly(EntryPoint = "is_SetStarterFirmware")]
    public static int Stub_SetStarterFirmware() => Unsupported("is_SetStarterFirmware");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSubSampling")]
    public static int Stub_SetSubSampling(uint cameraHandle, int mode) => VirtualCameraState.SetSubSampling(mode);

    [UnmanagedCallersOnly(EntryPoint = "is_SetSync")]
    public static int Stub_SetSync() => Unsupported("is_SetSync");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSyncLevel")]
    public static int Stub_SetSyncLevel() => Unsupported("is_SetSyncLevel");

    [UnmanagedCallersOnly(EntryPoint = "is_SetTimeout")]
    public static int Stub_SetTimeout() => Unsupported("is_SetTimeout");

    [UnmanagedCallersOnly(EntryPoint = "is_SetToggleMode")]
    public static int Stub_SetToggleMode() => Unsupported("is_SetToggleMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetTriggerDelay")]
    public static int Stub_SetTriggerDelay() => Unsupported("is_SetTriggerDelay");

    [UnmanagedCallersOnly(EntryPoint = "is_SetUpdateMode")]
    public static int Stub_SetUpdateMode() => Unsupported("is_SetUpdateMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetVertFilter")]
    public static int Stub_SetVertFilter() => Unsupported("is_SetVertFilter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetVideoCrossbar")]
    public static int Stub_SetVideoCrossbar() => Unsupported("is_SetVideoCrossbar");

    [UnmanagedCallersOnly(EntryPoint = "is_SetVideoInput")]
    public static int Stub_SetVideoInput() => Unsupported("is_SetVideoInput");

    [UnmanagedCallersOnly(EntryPoint = "is_SetVideoMode")]
    public static int Stub_SetVideoMode() => Unsupported("is_SetVideoMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetVideoSize")]
    public static int Stub_SetVideoSize() => Unsupported("is_SetVideoSize");

    [UnmanagedCallersOnly(EntryPoint = "is_SetWhiteBalance")]
    public static int Stub_SetWhiteBalance() => Unsupported("is_SetWhiteBalance");

    [UnmanagedCallersOnly(EntryPoint = "is_SetWhiteBalanceMultipliers")]
    public static int Stub_SetWhiteBalanceMultipliers() => Unsupported("is_SetWhiteBalanceMultipliers");

    [UnmanagedCallersOnly(EntryPoint = "is_Sharpness")]
    public static int Stub_Sharpness() => Unsupported("is_Sharpness");

    [UnmanagedCallersOnly(EntryPoint = "is_ShowColorBars")]
    public static int Stub_ShowColorBars() => Unsupported("is_ShowColorBars");

    [UnmanagedCallersOnly(EntryPoint = "is_ShowDDOverlay")]
    public static int Stub_ShowDDOverlay() => Unsupported("is_ShowDDOverlay");

    [UnmanagedCallersOnly(EntryPoint = "is_StealVideo")]
    public static int Stub_StealVideo() => Unsupported("is_StealVideo");

    [UnmanagedCallersOnly(EntryPoint = "is_Transfer")]
    public static int Stub_Transfer() => Unsupported("is_Transfer");

    [UnmanagedCallersOnly(EntryPoint = "is_TransferImage")]
    public static int Stub_TransferImage() => Unsupported("is_TransferImage");

    [UnmanagedCallersOnly(EntryPoint = "is_TransferMemorySequence")]
    public static int Stub_TransferMemorySequence() => Unsupported("is_TransferMemorySequence");

    [UnmanagedCallersOnly(EntryPoint = "is_Trigger")]
    public static int Stub_Trigger() => Unsupported("is_Trigger");

    [UnmanagedCallersOnly(EntryPoint = "is_TriggerDebounce")]
    public static int Stub_TriggerDebounce() => Unsupported("is_TriggerDebounce");

    [UnmanagedCallersOnly(EntryPoint = "is_UnlockDDMem")]
    public static int Stub_UnlockDDMem() => Unsupported("is_UnlockDDMem");

    [UnmanagedCallersOnly(EntryPoint = "is_UnlockDDOverlayMem")]
    public static int Stub_UnlockDDOverlayMem() => Unsupported("is_UnlockDDOverlayMem");

    [UnmanagedCallersOnly(EntryPoint = "is_UpdateDisplay")]
    public static int Stub_UpdateDisplay() => Unsupported("is_UpdateDisplay");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteEEPROM")]
    public static int Stub_WriteEEPROM() => Unsupported("is_WriteEEPROM");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteI2C")]
    public static int Stub_WriteI2C() => Unsupported("is_WriteI2C");

    [UnmanagedCallersOnly(EntryPoint = "is_Zoom")]
    public static int Stub_Zoom() => Unsupported("is_Zoom");

    [UnmanagedCallersOnly(EntryPoint = "is_AccessAdapterCfg")]
    public static int Stub_AccessAdapterCfg() => SuccessNoOp("is_AccessAdapterCfg");

    [UnmanagedCallersOnly(EntryPoint = "is_AccessAdapterCfgByMAC")]
    public static int Stub_AccessAdapterCfgByMac() => SuccessNoOp("is_AccessAdapterCfgByMAC");

    [UnmanagedCallersOnly(EntryPoint = "is_AccessDeviceCfg")]
    public static int Stub_AccessDeviceCfg() => SuccessNoOp("is_AccessDeviceCfg");

    [UnmanagedCallersOnly(EntryPoint = "is_AutoOffsetAdjustment")]
    public static int Stub_AutoOffsetAdjustment() => SuccessNoOp("is_AutoOffsetAdjustment");

    [UnmanagedCallersOnly(EntryPoint = "is_CalibrateFPN")]
    public static int Stub_CalibrateFpn() => SuccessNoOp("is_CalibrateFPN");

    [UnmanagedCallersOnly(EntryPoint = "is_Callback")]
    public static int Stub_Callback() => SuccessNoOp("is_Callback");

    [UnmanagedCallersOnly(EntryPoint = "is_CaptureConfiguration")]
    public static int Stub_CaptureConfiguration(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_CaptureConfiguration(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_ComportWrite")]
    public static int Stub_ComportWrite() => SuccessNoOp("is_ComportWrite");

    [UnmanagedCallersOnly(EntryPoint = "is_DSPCheckMemory")]
    public static int Stub_DspCheckMemory() => SuccessNoOp("is_DSPCheckMemory");

    [UnmanagedCallersOnly(EntryPoint = "is_DownloadDSPFirmware")]
    public static int Stub_DownloadDspFirmware() => SuccessNoOp("is_DownloadDSPFirmware");

    [UnmanagedCallersOnly(EntryPoint = "is_Event")]
    public static int Stub_Event(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Event(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_FetchRegistryValues")]
    public static int Stub_FetchRegistryValues() => SuccessNoOp("is_FetchRegistryValues");

    [UnmanagedCallersOnly(EntryPoint = "is_FetchTCPIP_Setup")]
    public static int Stub_FetchTcpipSetup() => SuccessNoOp("is_FetchTCPIP_Setup");

    [UnmanagedCallersOnly(EntryPoint = "is_Func")]
    public static int Stub_Func(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Func(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Gamma")]
    public static int Stub_Gamma(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Gamma(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetCaptureErrorInfo")]
    public static int Stub_GetCaptureErrorInfo(uint cameraHandle, void* captureErrorInfo, uint captureErrorInfoSize)
    {
        VirtualCameraState.Log($"is_GetCaptureErrorInfo(size={captureErrorInfoSize})");
        return VirtualCameraState.ZeroCommand(captureErrorInfo, captureErrorInfoSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetDebugOutMask")]
    public static int Stub_GetDebugOutMask()
    {
        VirtualCameraState.Log("is_GetDebugOutMask -> 0");
        _ = VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetDeviceID")]
    public static int Stub_GetDeviceId()
    {
        VirtualCameraState.Log("is_GetDeviceID -> 1");
        _ = VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
        return 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetMemoryStatus")]
    public static int Stub_GetMemoryStatus() => SuccessNoOp("is_GetMemoryStatus");

    [UnmanagedCallersOnly(EntryPoint = "is_ImageBuffer")]
    public static int Stub_ImageBuffer(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_ImageBuffer(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_IsCCDSensor")]
    public static int Stub_IsCcdSensor()
    {
        VirtualCameraState.Log("is_IsCCDSensor -> 0");
        _ = VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_LUT")]
    public static int Stub_Lut(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_LUT(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Measure")]
    public static int Stub_Measure(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Measure(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_MemContent")]
    public static int Stub_MemContent(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_MemContent(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Memory")]
    public static int Stub_Memory(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Memory(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Multicast")]
    public static int Stub_Multicast(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Multicast(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_OptimalCameraTiming")]
    public static int Stub_OptimalCameraTiming(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_OptimalCameraTiming(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_PersistentMemory")]
    public static int Stub_PersistentMemory(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_PersistentMemory(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_PowerDelivery")]
    public static int Stub_PowerDelivery(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_PowerDelivery(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_ReadEEPROMEx")]
    public static int Stub_ReadEepromEx() => SuccessNoOp("is_ReadEEPROMEx");

    [UnmanagedCallersOnly(EntryPoint = "is_ReadFPGARegister")]
    public static int Stub_ReadFpgaRegister() => SuccessNoOp("is_ReadFPGARegister");

    [UnmanagedCallersOnly(EntryPoint = "is_ResetCaptureErrorInfo")]
    public static int Stub_ResetCaptureErrorInfo() => SuccessNoOp("is_ResetCaptureErrorInfo");

    [UnmanagedCallersOnly(EntryPoint = "is_RetrievePixelsizeFromColormode_B")]
    public static int Stub_RetrievePixelsizeFromColormodeBytes()
    {
        var resolvedBits = VirtualCameraState.GetColorModeOrBits(UeyeNative.IS_GET_BITS_PER_PIXEL);
        var compatibilityPixelSize = Math.Max(1, (resolvedBits + 7) / 8);
        VirtualCameraState.Log($"is_RetrievePixelsizeFromColormode_B -> {compatibilityPixelSize} (resolvedBits={resolvedBits})");
        _ = VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
        return compatibilityPixelSize;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_RetrievePixelsizeFromColormode_b")]
    public static int Stub_RetrievePixelsizeFromColormodeBits()
    {
        var compatibilityPixelSize = VirtualCameraState.GetColorModeOrBits(UeyeNative.IS_GET_BITS_PER_PIXEL);
        VirtualCameraState.Log($"is_RetrievePixelsizeFromColormode_b -> {compatibilityPixelSize}");
        _ = VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
        return compatibilityPixelSize;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SPIExchangeBuffer")]
    public static int Stub_SpiExchangeBuffer() => SuccessNoOp("is_SPIExchangeBuffer");

    [UnmanagedCallersOnly(EntryPoint = "is_SPIExchangeByte")]
    public static int Stub_SpiExchangeByte() => SuccessNoOp("is_SPIExchangeByte");

    [UnmanagedCallersOnly(EntryPoint = "is_Sequencer")]
    public static int Stub_Sequencer(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Sequencer(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetBadPixelEEPROMList")]
    public static int Stub_SetBadPixelEepromList() => SuccessNoOp("is_SetBadPixelEEPROMList");

    [UnmanagedCallersOnly(EntryPoint = "is_SetCameraID_Eth")]
    public static int Stub_SetCameraIdEth() => SuccessNoOp("is_SetCameraID_Eth");

    [UnmanagedCallersOnly(EntryPoint = "is_SetComportConfig")]
    public static int Stub_SetComportConfig() => SuccessNoOp("is_SetComportConfig");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDebugOutMask")]
    public static int Stub_SetDebugOutMask() => SuccessNoOp("is_SetDebugOutMask");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDecimationMode")]
    public static int Stub_SetDecimationMode() => SuccessNoOp("is_SetDecimationMode");

    [UnmanagedCallersOnly(EntryPoint = "is_SetDHCP_Status")]
    public static int Stub_SetDhcpStatus() => SuccessNoOp("is_SetDHCP_Status");

    [UnmanagedCallersOnly(EntryPoint = "is_SetPassthrough")]
    public static int Stub_SetPassthrough() => SuccessNoOp("is_SetPassthrough");

    [UnmanagedCallersOnly(EntryPoint = "is_SetSensorID")]
    public static int Stub_SetSensorId() => SuccessNoOp("is_SetSensorID");

    [UnmanagedCallersOnly(EntryPoint = "is_SetTriggerCounter")]
    public static int Stub_SetTriggerCounter() => SuccessNoOp("is_SetTriggerCounter");

    [UnmanagedCallersOnly(EntryPoint = "is_SetUSBBoardID")]
    public static int Stub_SetUsbBoardId() => SuccessNoOp("is_SetUSBBoardID");

    [UnmanagedCallersOnly(EntryPoint = "is_TestMemory")]
    public static int Stub_TestMemory() => SuccessNoOp("is_TestMemory");

    [UnmanagedCallersOnly(EntryPoint = "is_Watchdog")]
    public static int Stub_Watchdog() => SuccessNoOp("is_Watchdog");

    [UnmanagedCallersOnly(EntryPoint = "is_WatchdogTime")]
    public static int Stub_WatchdogTime() => SuccessNoOp("is_WatchdogTime");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteDACRegister")]
    public static int Stub_WriteDacRegister() => SuccessNoOp("is_WriteDACRegister");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteEEPROMEx")]
    public static int Stub_WriteEepromEx() => SuccessNoOp("is_WriteEEPROMEx");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteFPGARegister")]
    public static int Stub_WriteFpgaRegister() => SuccessNoOp("is_WriteFPGARegister");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteRevisionInfo")]
    public static int Stub_WriteRevisionInfo() => SuccessNoOp("is_WriteRevisionInfo");

    [UnmanagedCallersOnly(EntryPoint = "is_WriteTimingRegister")]
    public static int Stub_WriteTimingRegister() => SuccessNoOp("is_WriteTimingRegister");
}
