using System.Runtime.InteropServices;

namespace VirtualUEyeProxy;

internal static unsafe partial class Exports
{
    private static int _exportTraceCount;
    private static int _waitReturnTraceCount;
    private static int _sequenceReturnTraceCount;

    private static void TraceExport(string message, int limit = 64)
    {
        if (System.Threading.Interlocked.Increment(ref _exportTraceCount) <= limit)
        {
            VirtualCameraState.Log(message);
        }
    }

    private static string DescribeImageQueueCommand(uint command)
        => command switch
        {
            UeyeNative.IS_IMAGE_QUEUE_CMD_INIT => "INIT",
            UeyeNative.IS_IMAGE_QUEUE_CMD_EXIT => "EXIT",
            UeyeNative.IS_IMAGE_QUEUE_CMD_WAIT => "WAIT",
            UeyeNative.IS_IMAGE_QUEUE_CMD_CANCEL_WAIT => "CANCEL_WAIT",
            UeyeNative.IS_IMAGE_QUEUE_CMD_GET_PENDING => "GET_PENDING",
            UeyeNative.IS_IMAGE_QUEUE_CMD_FLUSH => "FLUSH",
            UeyeNative.IS_IMAGE_QUEUE_CMD_DISCARD_N_ITEMS => "DISCARD_N_ITEMS",
            _ => $"0x{command:X}",
        };

    [UnmanagedCallersOnly(EntryPoint = "is_GetDLLVersion")]
    public static int IsGetDllVersion() => unchecked((int)0x04610000);

    [UnmanagedCallersOnly(EntryPoint = "is_GetNumberOfCameras")]
    public static int IsGetNumberOfCameras(int* count)
    {
        if (count != null)
        {
            *count = 1;
        }

        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetNumberOfDevices")]
    public static int IsGetNumberOfDevices()
    {
        return 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetNumberOfBoards")]
    public static int IsGetNumberOfBoards(int* count)
    {
        if (count != null)
        {
            *count = 1;
        }

        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetCameraList")]
    public static int IsGetCameraList(void* cameraList)
    {
        if (cameraList == null)
        {
            return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Camera list pointer is null.");
        }

        VirtualCameraState.FillCameraList(cameraList);
        VirtualCameraState.Log("is_GetCameraList -> 1 camera");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_InitCamera")]
    public static int IsInitCamera(uint* cameraHandle, nint hwnd) => VirtualCameraState.InitializeCamera(cameraHandle);

    [UnmanagedCallersOnly(EntryPoint = "is_ExitCamera")]
    public static int IsExitCamera(uint cameraHandle) => VirtualCameraState.ExitCamera(cameraHandle);

    [UnmanagedCallersOnly(EntryPoint = "is_GetCameraInfo")]
    public static int IsGetCameraInfo(uint cameraHandle, BOARDINFO* info)
    {
        VirtualCameraState.FillBoardInfo(info);
        VirtualCameraState.Log("is_GetCameraInfo -> OK");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetBoardInfo")]
    public static int IsGetBoardInfo(uint cameraHandle, BOARDINFO* info)
    {
        VirtualCameraState.FillBoardInfo(info);
        VirtualCameraState.Log("is_GetBoardInfo -> OK");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetSensorInfo")]
    public static int IsGetSensorInfo(uint cameraHandle, SENSORINFO* info)
    {
        VirtualCameraState.FillSensorInfo(info);
        VirtualCameraState.Log("is_GetSensorInfo -> OK");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetCameraType")]
    public static int IsGetCameraType(uint cameraHandle) => UeyeNative.IS_BOARD_TYPE_UEYE_USB;

    [UnmanagedCallersOnly(EntryPoint = "is_GetBoardType")]
    public static int IsGetBoardType(uint cameraHandle) => UeyeNative.IS_BOARD_TYPE_UEYE_USB;

    [UnmanagedCallersOnly(EntryPoint = "is_GetBusSpeed")]
    public static int IsGetBusSpeed(uint cameraHandle) => 480;

    [UnmanagedCallersOnly(EntryPoint = "is_GetUsedBandwidth")]
    public static int IsGetUsedBandwidth(uint cameraHandle) => 0;

    [UnmanagedCallersOnly(EntryPoint = "is_GetColorDepth")]
    public static int IsGetColorDepth(uint cameraHandle, int* bitsPerPixel, int* colorMode)
    {
        var resolvedBitsPerPixel = VirtualCameraState.GetColorModeOrBits(UeyeNative.IS_GET_BITS_PER_PIXEL);
        var resolvedColorMode = VirtualCameraState.GetColorModeOrBits(UeyeNative.IS_GET_COLOR_MODE);

        if (bitsPerPixel != null)
        {
            *bitsPerPixel = resolvedBitsPerPixel;
        }

        if (colorMode != null)
        {
            *colorMode = resolvedColorMode;
        }

        TraceExport($"is_GetColorDepth -> bits={resolvedBitsPerPixel}, colorMode=0x{resolvedColorMode:X}");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetDisplayMode")]
    public static int IsSetDisplayMode(uint cameraHandle, int mode)
    {
        if (mode == UeyeNative.IS_GET_DISPLAY_MODE)
        {
            return VirtualCameraState.GetDisplayMode();
        }

        return VirtualCameraState.SetDisplayMode(mode);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetColorMode")]
    public static int IsSetColorMode(uint cameraHandle, int mode)
    {
        if (mode == UeyeNative.IS_GET_COLOR_MODE || mode == UeyeNative.IS_GET_BITS_PER_PIXEL)
        {
            return VirtualCameraState.GetColorModeOrBits(mode);
        }

        return VirtualCameraState.SetColorMode(mode);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetDuration")]
    public static int IsGetDuration(uint cameraHandle, uint mode, int* timeMs)
    {
        if (timeMs != null)
        {
            *timeMs = 0;
        }

        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_AOI")]
    public static int IsAoi(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        switch (command)
        {
            case UeyeNative.IS_AOI_IMAGE_SET_AOI:
                if (parameter == null || parameterSize < sizeof(IS_RECT))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI rect input is invalid.");
                }
                {
                    var rect = (IS_RECT*)parameter;
                    return VirtualCameraState.SetAoi(rect->s32X, rect->s32Y, rect->s32Width, rect->s32Height);
                }
            case UeyeNative.IS_AOI_IMAGE_GET_AOI:
            case UeyeNative.IS_AOI_IMAGE_GET_ORIGINAL_AOI:
                if (parameter == null || parameterSize < sizeof(IS_RECT))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI rect output is invalid.");
                }
                *(IS_RECT*)parameter = VirtualCameraState.GetAoi();
                return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
            case UeyeNative.IS_AOI_IMAGE_SET_POS:
                if (parameter == null || parameterSize < sizeof(IS_POINT))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI position input is invalid.");
                }
                {
                    var point = (IS_POINT*)parameter;
                    return VirtualCameraState.SetAoiPosition(point->s32X, point->s32Y);
                }
            case UeyeNative.IS_AOI_IMAGE_GET_POS:
            case UeyeNative.IS_AOI_IMAGE_GET_POS_MIN:
            case UeyeNative.IS_AOI_IMAGE_GET_POS_INC:
                if (parameter == null || parameterSize < sizeof(IS_POINT))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI position output is invalid.");
                }
                {
                    var aoi = VirtualCameraState.GetAoi();
                    var point = (IS_POINT*)parameter;
                    point->s32X = command == UeyeNative.IS_AOI_IMAGE_GET_POS ? aoi.s32X : 0;
                    point->s32Y = command == UeyeNative.IS_AOI_IMAGE_GET_POS ? aoi.s32Y : 0;
                    return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
            case UeyeNative.IS_AOI_IMAGE_GET_POS_MAX:
                if (parameter == null || parameterSize < sizeof(IS_POINT))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI max position output is invalid.");
                }
                {
                    var aoi = VirtualCameraState.GetAoi();
                    var point = (IS_POINT*)parameter;
                    point->s32X = UeyeNative.DefaultWidth - aoi.s32Width;
                    point->s32Y = UeyeNative.DefaultHeight - aoi.s32Height;
                    return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
            case UeyeNative.IS_AOI_IMAGE_SET_SIZE:
                if (parameter == null || parameterSize < sizeof(IS_SIZE_2D))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI size input is invalid.");
                }
                {
                    var size = (IS_SIZE_2D*)parameter;
                    return VirtualCameraState.SetAoiSize(size->s32Width, size->s32Height);
                }
            case UeyeNative.IS_AOI_IMAGE_GET_SIZE:
            case UeyeNative.IS_AOI_IMAGE_GET_SIZE_MAX:
            case UeyeNative.IS_AOI_IMAGE_GET_SIZE_MIN:
            case UeyeNative.IS_AOI_IMAGE_GET_SIZE_INC:
                if (parameter == null || parameterSize < sizeof(IS_SIZE_2D))
                {
                    return VirtualCameraState.SetLastError(UeyeNative.IS_INVALID_PARAMETER, "AOI size output is invalid.");
                }
                {
                    var aoi = VirtualCameraState.GetAoi();
                    var size = (IS_SIZE_2D*)parameter;
                    switch (command)
                    {
                        case UeyeNative.IS_AOI_IMAGE_GET_SIZE:
                            size->s32Width = aoi.s32Width;
                            size->s32Height = aoi.s32Height;
                            break;
                        case UeyeNative.IS_AOI_IMAGE_GET_SIZE_MAX:
                            size->s32Width = UeyeNative.DefaultWidth;
                            size->s32Height = UeyeNative.DefaultHeight;
                            break;
                        case UeyeNative.IS_AOI_IMAGE_GET_SIZE_MIN:
                        case UeyeNative.IS_AOI_IMAGE_GET_SIZE_INC:
                            size->s32Width = 1;
                            size->s32Height = 1;
                            break;
                    }
                    return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
            default:
                return VirtualCameraState.SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"AOI command {command} not supported.");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetAOI")]
    public static int IsSetAoi(uint cameraHandle, int x, int y, int width, int height)
        => VirtualCameraState.SetAoi(x, y, width, height);

    [UnmanagedCallersOnly(EntryPoint = "is_SetImageAOI")]
    public static int IsSetImageAoi(uint cameraHandle, int x, int y, int width, int height)
        => VirtualCameraState.SetAoi(x, y, width, height);

    [UnmanagedCallersOnly(EntryPoint = "is_SetImagePos")]
    public static int IsSetImagePos(uint cameraHandle, int x, int y) => VirtualCameraState.SetAoiPosition(x, y);

    [UnmanagedCallersOnly(EntryPoint = "is_SetImageSize")]
    public static int IsSetImageSize(uint cameraHandle, int width, int height) => VirtualCameraState.SetAoiSize(width, height);

    [UnmanagedCallersOnly(EntryPoint = "is_AllocImageMem")]
    public static int IsAllocImageMem(uint cameraHandle, int width, int height, int bitsPerPixel, byte** memory, int* memoryId)
        => VirtualCameraState.AllocateImageMemory(width, height, bitsPerPixel, memory, memoryId);

    [UnmanagedCallersOnly(EntryPoint = "is_SetAllocatedImageMem")]
    public static int IsSetAllocatedImageMem(uint cameraHandle, int width, int height, int bitsPerPixel, byte* memory, int* memoryId)
        => VirtualCameraState.SetAllocatedImageMemory(width, height, bitsPerPixel, memory, memoryId);

    [UnmanagedCallersOnly(EntryPoint = "is_SetImageMem")]
    public static int IsSetImageMem(uint cameraHandle, byte* memory, int memoryId)
    {
        TraceExport($"is_SetImageMem(memoryId={memoryId}, ptr=0x{((nint)memory).ToInt64():X})", 256);
        return VirtualCameraState.SetImageMemory(memory, memoryId);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_FreeImageMem")]
    public static int IsFreeImageMem(uint cameraHandle, byte* memory, int memoryId)
        => VirtualCameraState.FreeImageMemory(memory, memoryId);

    [UnmanagedCallersOnly(EntryPoint = "is_GetImageMem")]
    public static int IsGetImageMem(uint cameraHandle, void** memory)
    {
        TraceExport("is_GetImageMem", 256);
        return VirtualCameraState.GetImageMemory(memory);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetActiveImageMem")]
    public static int IsGetActiveImageMem(uint cameraHandle, byte** memory, int* memoryId)
    {
        TraceExport("is_GetActiveImageMem", 256);
        return VirtualCameraState.GetActiveImageMemory(memory, memoryId);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_InquireImageMem")]
    public static int IsInquireImageMem(uint cameraHandle, byte* memory, int memoryId, int* width, int* height, int* bits, int* pitch)
    {
        TraceExport($"is_InquireImageMem(memoryId={memoryId}, ptr=0x{((nint)memory).ToInt64():X})", 256);
        return VirtualCameraState.InquireImageMemory(memory, memoryId, width, height, bits, pitch);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetImageMemPitch")]
    public static int IsGetImageMemPitch(uint cameraHandle, int* pitch)
    {
        TraceExport("is_GetImageMemPitch", 256);
        return VirtualCameraState.GetImagePitch(pitch);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_CopyImageMem")]
    public static int IsCopyImageMem(uint cameraHandle, byte* srcMemory, int srcMemoryId, byte* dstMemory)
        => VirtualCameraState.CopyImageMemory(srcMemory, srcMemoryId, dstMemory);

    [UnmanagedCallersOnly(EntryPoint = "is_CopyImageMemLines")]
    public static int IsCopyImageMemLines(uint cameraHandle, byte* srcMemory, int srcMemoryId, int srcOffsetY, int countLines, byte* dstMemory)
        => VirtualCameraState.CopyImageMemoryLines(srcMemory, srcMemoryId, srcOffsetY, countLines, dstMemory);

    [UnmanagedCallersOnly(EntryPoint = "is_CaptureVideo")]
    public static int IsCaptureVideo(uint cameraHandle, int waitMode)
    {
        TraceExport($"is_CaptureVideo(waitMode={waitMode})", 256);
        return VirtualCameraState.StartCapture();
    }

    [UnmanagedCallersOnly(EntryPoint = "is_FreezeVideo")]
    public static int IsFreezeVideo(uint cameraHandle, int waitMode)
    {
        TraceExport($"is_FreezeVideo(waitMode={waitMode})", 256);
        return VirtualCameraState.FreezeFrame();
    }

    [UnmanagedCallersOnly(EntryPoint = "is_StopLiveVideo")]
    public static int IsStopLiveVideo(uint cameraHandle, int waitMode)
    {
        TraceExport($"is_StopLiveVideo(waitMode={waitMode})", 256);
        return VirtualCameraState.StopCapture();
    }

    [UnmanagedCallersOnly(EntryPoint = "is_WaitForNextImage")]
    public static int IsWaitForNextImage(uint cameraHandle, uint timeout, byte** memory, int* memoryId)
    {
        TraceExport($"is_WaitForNextImage(timeout={timeout})", 256);
        var result = VirtualCameraState.WaitForNextImage(timeout, memory, memoryId);
        if (_waitReturnTraceCount < 256)
        {
            _waitReturnTraceCount++;
            var resolvedMemoryId = memoryId == null ? 0 : *memoryId;
            var resolvedPointer = memory == null ? nint.Zero : (nint)(*memory);
            VirtualCameraState.Log($"is_WaitForNextImage(timeout={timeout}) -> result={result}, memoryId={resolvedMemoryId}, ptr=0x{resolvedPointer.ToInt64():X}");
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetImageInfo")]
    public static int IsGetImageInfo(uint cameraHandle, int memoryId, UEYEIMAGEINFO* imageInfo, int imageInfoSize)
    {
        TraceExport($"is_GetImageInfo(memoryId={memoryId}, size={imageInfoSize})", 256);
        try
        {
            return VirtualCameraState.GetImageInfo(memoryId, imageInfo, imageInfoSize);
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"is_GetImageInfo exception: {ex}");
            return UeyeNative.IS_INVALID_PARAMETER;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "is_InitImageQueue")]
    public static int IsInitImageQueue(uint cameraHandle, int mode)
    {
        TraceExport($"is_InitImageQueue(mode={mode})");
        return VirtualCameraState.InitializeImageQueue(mode);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_ExitImageQueue")]
    public static int IsExitImageQueue(uint cameraHandle)
    {
        TraceExport("is_ExitImageQueue");
        return VirtualCameraState.ExitImageQueue();
    }

    [UnmanagedCallersOnly(EntryPoint = "is_ImageQueue")]
    public static int IsImageQueue(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        TraceExport($"is_ImageQueue(command={DescribeImageQueueCommand(command)}, size={parameterSize})");
        return VirtualCameraState.HandleImageQueueCommand(command, parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Exposure")]
    public static int IsExposure(uint cameraHandle, uint command, void* parameter, uint parameterSize)
        => VirtualCameraState.SetExposure(command, parameter);

    [UnmanagedCallersOnly(EntryPoint = "is_SetExternalTrigger")]
    public static int IsSetExternalTrigger(uint cameraHandle, int mode) => VirtualCameraState.SetTriggerMode(mode);

    [UnmanagedCallersOnly(EntryPoint = "is_SetAutoParameter")]
    public static int IsSetAutoParameter(uint cameraHandle, int parameter, double* value1, double* value2)
        => VirtualCameraState.SetAutoParameter(parameter, value1, value2);

    [UnmanagedCallersOnly(EntryPoint = "is_SetHardwareGain")]
    public static int IsSetHardwareGain(uint cameraHandle, int master, int red, int green, int blue)
        => VirtualCameraState.SetHardwareGain(master, red, green, blue);

    [UnmanagedCallersOnly(EntryPoint = "is_SetFrameRate")]
    public static int IsSetFrameRate(uint cameraHandle, double fps, double* newFps)
        => VirtualCameraState.SetFrameRate(fps, newFps);

    [UnmanagedCallersOnly(EntryPoint = "is_GetFramesPerSecond")]
    public static int IsGetFramesPerSecond(uint cameraHandle, double* fps)
        => VirtualCameraState.GetFramesPerSecond(fps);

    [UnmanagedCallersOnly(EntryPoint = "is_GetFrameTimeRange")]
    public static int IsGetFrameTimeRange(uint cameraHandle, double* min, double* max, double* interval)
        => VirtualCameraState.GetFrameTimeRange(min, max, interval);

    [UnmanagedCallersOnly(EntryPoint = "is_SetPixelClock")]
    public static int IsSetPixelClock(uint cameraHandle, int pixelClock)
        => VirtualCameraState.SetPixelClock(pixelClock);

    [UnmanagedCallersOnly(EntryPoint = "is_PixelClock")]
    public static int IsPixelClock(uint cameraHandle, uint command, void* parameter, uint parameterSize)
        => VirtualCameraState.PixelClock(command, parameter);

    [UnmanagedCallersOnly(EntryPoint = "is_HasVideoStarted")]
    public static int IsHasVideoStarted(uint cameraHandle, int* started)
        => VirtualCameraState.ReportHasVideoStarted(started);

    [UnmanagedCallersOnly(EntryPoint = "is_IsVideoFinish")]
    public static int IsIsVideoFinish(uint cameraHandle, int* finished)
        => VirtualCameraState.ReportVideoFinished(finished);

    [UnmanagedCallersOnly(EntryPoint = "is_GetError")]
    public static int IsGetError(uint cameraHandle, int* errorCode, byte** errorText)
        => VirtualCameraState.GetLastError(errorCode, errorText);

    [UnmanagedCallersOnly(EntryPoint = "is_EnableEvent")]
    public static int IsEnableEvent(uint cameraHandle, int which)
    {
        VirtualCameraState.Log($"is_EnableEvent(which={which})");
        return VirtualCameraState.EnableEvent(which);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_DisableEvent")]
    public static int IsDisableEvent(uint cameraHandle, int which)
    {
        VirtualCameraState.Log($"is_DisableEvent(which={which})");
        return VirtualCameraState.DisableEvent(which);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_InitEvent")]
    public static int IsInitEvent(uint cameraHandle, nint hEvent, int which)
    {
        VirtualCameraState.Log($"is_InitEvent(which={which}, hEvent=0x{hEvent.ToInt64():X})");
        return VirtualCameraState.InitEvent(hEvent, which);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_ExitEvent")]
    public static int IsExitEvent(uint cameraHandle, int which)
    {
        VirtualCameraState.Log($"is_ExitEvent(which={which})");
        return VirtualCameraState.ExitEvent(which);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_EnableMessage")]
    public static int IsEnableMessage(uint cameraHandle, int which, nint hwnd)
    {
        VirtualCameraState.Log($"is_EnableMessage(which={which}, hwnd=0x{hwnd.ToInt64():X})");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetErrorReport")]
    public static int IsSetErrorReport(uint cameraHandle, int mode) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_GetSupportedTestImages")]
    public static int IsGetSupportedTestImages(uint cameraHandle, uint* flags)
    {
        if (flags != null)
        {
            *flags = 0;
        }

        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    [UnmanagedCallersOnly(EntryPoint = "is_SetTestImage")]
    public static int IsSetTestImage(uint cameraHandle, int mode) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_GetExtendedRegister")]
    public static int IsGetExtendedRegister(uint cameraHandle, int index, ushort* value)
        => VirtualCameraState.GetExtendedRegister(index, value);

    [UnmanagedCallersOnly(EntryPoint = "is_SetExtendedRegister")]
    public static int IsSetExtendedRegister(uint cameraHandle, int index, ushort value)
        => VirtualCameraState.SetExtendedRegister(index, value);

    [UnmanagedCallersOnly(EntryPoint = "is_DeviceInfo")]
    public static int IsDeviceInfo(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_DeviceInfo(command={command}, size={parameterSize})");
        return VirtualCameraState.FillDeviceInfo(command, parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_DeviceFeature")]
    public static int IsDeviceFeature(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_DeviceFeature(command={command}, size={parameterSize})");
        return VirtualCameraState.HandleDeviceFeature(command, parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Configuration")]
    public static int IsConfiguration(uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_Configuration(command={command}, size={parameterSize})");
        return VirtualCameraState.HandleConfiguration(command, parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_IO")]
    public static int IsIo(uint cameraHandle, uint command, void* parameter, uint parameterSize)
    {
        VirtualCameraState.Log($"is_IO(command={command}, size={parameterSize})");
        return VirtualCameraState.ZeroCommand(parameter, parameterSize);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_Renumerate")]
    public static int IsRenumerate(uint cameraHandle, int mode) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_ResetToDefault")]
    public static int IsResetToDefault(uint cameraHandle) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_ClearSequence")]
    public static int IsClearSequence(uint cameraHandle)
    {
        TraceExport("is_ClearSequence");
        return VirtualCameraState.ClearSequence();
    }

    [UnmanagedCallersOnly(EntryPoint = "is_AddToSequence")]
    public static int IsAddToSequence(uint cameraHandle, byte* memory, int memoryId)
    {
        TraceExport($"is_AddToSequence(memoryId={memoryId}, ptr=0x{((nint)memory).ToInt64():X})");
        return VirtualCameraState.AddToSequence(memory, memoryId);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_GetActSeqBuf")]
    public static int IsGetActSeqBuf(uint cameraHandle, int* currentSequenceId, byte** currentMemory, byte** lastMemory)
    {
        var mem = VirtualCameraState.GetActiveMemory();
        TraceExport(mem is null
            ? "is_GetActSeqBuf -> <null>"
            : $"is_GetActSeqBuf -> memoryId={mem.MemoryId}, ptr=0x{mem.Pointer.ToInt64():X}");
        return VirtualCameraState.GetActiveSequenceBuffer(currentSequenceId, currentMemory, lastMemory);
    }

    [UnmanagedCallersOnly(EntryPoint = "is_LockSeqBuf")]
    public static int IsLockSeqBuf(uint cameraHandle, int memoryId, byte* memory)
    {
        TraceExport($"is_LockSeqBuf(memoryId={memoryId}, ptr=0x{((nint)memory).ToInt64():X})");
        var result = VirtualCameraState.LockSequenceBuffer(memoryId, memory);
        if (_sequenceReturnTraceCount < 256)
        {
            _sequenceReturnTraceCount++;
            VirtualCameraState.Log($"is_LockSeqBuf(memoryId={memoryId}) -> result={result}");
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_UnlockSeqBuf")]
    public static int IsUnlockSeqBuf(uint cameraHandle, int memoryId, byte* memory)
    {
        TraceExport($"is_UnlockSeqBuf(memoryId={memoryId}, ptr=0x{((nint)memory).ToInt64():X})");
        var result = VirtualCameraState.UnlockSequenceBuffer(memoryId, memory);
        if (_sequenceReturnTraceCount < 256)
        {
            _sequenceReturnTraceCount++;
            VirtualCameraState.Log($"is_UnlockSeqBuf(memoryId={memoryId}) -> result={result}");
        }

        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "is_LoadParameters")]
    public static int IsLoadParameters(uint cameraHandle, byte* fileName) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_SaveParameters")]
    public static int IsSaveParameters(uint cameraHandle, byte* fileName) => VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");

    [UnmanagedCallersOnly(EntryPoint = "is_GetRevisionInfo")]
    public static int IsGetRevisionInfo(uint cameraHandle, void* revisionInfo, uint revisionInfoSize)
        => VirtualCameraState.ZeroCommand(revisionInfo, revisionInfoSize);

    [UnmanagedCallersOnly(EntryPoint = "is_GetAutoInfo")]
    public static int IsGetAutoInfo(uint cameraHandle, void* autoInfo, uint autoInfoSize)
        => VirtualCameraState.ZeroCommand(autoInfo, autoInfoSize);

    [UnmanagedCallersOnly(EntryPoint = "is_GetNumberOfMemoryImages")]
    public static int IsGetNumberOfMemoryImages(uint cameraHandle, int* count)
    {
        VirtualCameraState.Log("is_GetNumberOfMemoryImages");
        if (count != null)
        {
            *count = VirtualCameraState.GetMemoryImageCount();
        }

        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    private static int Unsupported(string name)
    {
        VirtualCameraState.Log($"{name} -> IS_NOT_SUPPORTED");
        return VirtualCameraState.SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"{name} is not supported by the virtual camera.");
    }

    private static int SuccessNoOp(string name)
    {
        VirtualCameraState.Log($"{name} -> OK (no-op)");
        return VirtualCameraState.SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct IS_POINT
{
    public int s32X;
    public int s32Y;
}
