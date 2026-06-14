using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RayCiBridge;

namespace VirtualUEyeProxy;

internal sealed class ImageMemory
{
    public required int MemoryId { get; init; }
    public required nint Pointer { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BitsPerPixel { get; init; }
    public required int Pitch { get; init; }
    public required int SizeBytes { get; init; }
}

internal readonly record struct ImageInfoSnapshot(
    ulong DeviceTimestampUs,
    DateTime SystemTimestamp,
    ulong FrameNumber,
    uint HostProcessTimeUs,
    uint IoStatus,
    uint Flags,
    ushort AoiIndex,
    ushort AoiCycle,
    byte SequencerIndex);

internal static unsafe class VirtualCameraState
{
    private const int ImageInfoOffsetFlags = 0;
    private const int ImageInfoOffsetTimestampDevice = 8;
    private const int ImageInfoOffsetTimestampSystem = 16;
    private const int ImageInfoOffsetIoStatus = 40;
    private const int ImageInfoOffsetAoiIndex = 44;
    private const int ImageInfoOffsetAoiCycle = 46;
    private const int ImageInfoOffsetFrameNumber = 48;
    private const int ImageInfoOffsetImageBuffers = 56;
    private const int ImageInfoOffsetImageBuffersInUse = 60;
    private const int ImageInfoOffsetReserved3 = 64;
    private const int ImageInfoOffsetImageHeight = 68;
    private const int ImageInfoOffsetImageWidth = 72;
    private const int ImageInfoOffsetHostProcessTime = 76;
    private const int ImageInfoOffsetSequencerIndex = 80;
    private const int ImageInfoOffsetFocusValue = 84;
    private const int ImageInfoOffsetFocusing = 88;
    private const int ImageInfoOffsetReserved4 = 92;
    private const uint CameraHandle = 1;
    internal const string UeyeSerial = "4103791906";
    internal const string CapturedRawSerial = "1145655880";
    internal const string BoardSerial = CapturedCameraProfile.BoardSerial;
    internal const string DisplaySerialShort = "10220034";
    internal const string DisplaySerial = "1201EL-U2-1022-0034";
    internal const string SerialTemplate = "1201EL-U2-{KW:2}{Year:2}-{Number:4}";
    internal const string RegistryModel = "uEye UI-1542LE-M";
    internal const string RegistryShortModel = "UI-1542LE-M";
    private const string Vendor = "IDS GmbH";
    private const string FirmwareVersion = "V1.0";
    private const string FirmwareDate = "20.07.2020";
    internal const string ShortModel = RegistryShortModel;
    internal const string FullModel = RegistryModel;
    internal const string CalibrationName = "CinCam CMOS 1201 EL";
    internal const string LegacyCalibrationName = CapturedCameraProfile.ModelName;
    private const double CompatibleFrameRateMinHz = 1.0;
    private const double CompatibleFrameRateMaxHz = 30.0;
    private const double CompatibleFrameRateIncHz = 0.1;

    private static readonly object Gate = new();
    private static readonly double[] RayCiGainStepsDb = [0.0, 4.0, 8.0, 12.0, 16.0];
    private static readonly Dictionary<int, ImageMemory> Memories = new();
    private static readonly Dictionary<int, ulong> MemoryFrameNumbers = new();
    private static readonly Dictionary<int, ImageInfoSnapshot> MemoryImageInfoSnapshots = new();
    private static readonly Dictionary<int, ushort> ExtendedRegisters = new();
    private static readonly Dictionary<int, nint> EventHandles = new();
    private static readonly HashSet<int> EnabledEvents = new();
    private static readonly List<int> SequenceMemoryIds = new();
    private static readonly HashSet<int> LockedSequenceMemoryIds = new();
    private static readonly Queue<int> ReadySequenceMemoryIds = new();
    private static readonly HashSet<int> QueuedSequenceMemoryIds = new();
    private static readonly string LogDirectory = ResolveLogDirectory();
    private static readonly string LogPath = Path.Combine(LogDirectory, "ueye_proxy.log");
    private static readonly string IdentityReportPath = Path.Combine(LogDirectory, "bridge_identity_report.txt");
    private static int _bridgeMissTraceCount;

    private enum FrameRateRequestEncoding
    {
        Hz,
        RayCiMilliHz,
        FrameIntervalSeconds,
    }

    private static int _nextMemoryId = 1;
    private static int _activeMemoryId;
    private static int _displayMode = UeyeNative.IS_SET_DM_DIB;
    private static int _colorMode = UeyeNative.DefaultColorMode;
    private static int _bridgeCapturePixelFormat = FrameBridgeProtocol.CapturePixelFormatMono10;
    private static int _triggerMode = UeyeNative.IS_SET_TRIGGER_OFF;
    private static int _pixelClock = UeyeNative.DefaultPixelClock;
    private static IS_RECT _aoi = new()
    {
        s32X = 0,
        s32Y = 0,
        s32Width = UeyeNative.DefaultWidth,
        s32Height = UeyeNative.DefaultHeight,
    };
    private static int _binningMode = UeyeNative.IS_BINNING_DISABLE;
    private static int _subSamplingMode = UeyeNative.IS_SUBSAMPLING_DISABLE;
    private static double _exposureMs = UeyeNative.DefaultExposureMs;
    private static double _frameRate = UeyeNative.DefaultFps;
    private static bool _autoExposureEnabled;
    private static bool _autoGainEnabled;
    private static int _masterGain;
    private static bool _gainBoostEnabled;
    private static int _blackLevel = 49;
    private static int _bridgeSensorWidth = UeyeNative.DefaultWidth;
    private static int _bridgeSensorHeight = UeyeNative.DefaultHeight;
    private static int _lastError = UeyeNative.IS_SUCCESS;
    private static string _lastErrorText = "OK";
    private static nint _lastErrorTextPtr = nint.Zero;
    private static ulong _frameNumber;
    private static readonly DateTime _deviceEpochUtc = DateTime.UtcNow;
    private static int _waitTraceCount;
    private static int _waitTimeoutTraceCount;
    private static int _sequenceTraceCount;
    private static int _queueTraceCount;
    private static int _imageInfoTraceCount;
    private static int _cameraListTraceCount;
    private static int _boardInfoTraceCount;
    private static int _sensorInfoTraceCount;
    private static int _deviceInfoTraceCount;
    private static int _configurationTraceCount;
    private static int _deviceFeatureTraceCount;
    private static int _videoStartedTraceCount;
    private static int _videoFinishedTraceCount;
    private static int _memoryTraceCount;
    private static int _displayTraceCount;
    private static int _eventSignalTraceCount;
    private static int _nextQueueIndex;
    private static int _currentSequenceMemoryId;
    private static int _lastSequenceMemoryId;
    private static ulong _producedFrameGeneration;
    private static ulong _lastDeliveredFrameGeneration;
    private static bool _imageQueueInitialized;
    private static bool _cancelWaitRequested;
    private static Thread? _captureThread;
    private static bool _captureThreadShouldStop;
    private static bool _captureActive;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint hEvent);

    static VirtualCameraState()
    {
        Directory.CreateDirectory(LogDirectory);
        ResetRegisters();
        CalibrationRegistryHook.TryInstall();
        XmlRpcHook.TryInstall();
        DiagnosticApiHook.TryInstall();
        WriteIdentityReport();
    }

    public static uint Handle => CameraHandle;

    internal static string ReportedListModelShort => RegistryShortModel;

    internal static string ReportedListSerial => DisplaySerialShort;

    internal static string ReportedListModelFull => CalibrationName;

    internal static string ReportedCalibrationRegistryName => RegistryModel;

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break exported calls.
        }
    }

    private static void TryWriteUInt16(Span<byte> buffer, int offset, ushort value)
    {
        if ((uint)offset + sizeof(ushort) > (uint)buffer.Length)
        {
            return;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, sizeof(ushort)), value);
    }

    private static void TryWriteUInt32(Span<byte> buffer, int offset, uint value)
    {
        if ((uint)offset + sizeof(uint) > (uint)buffer.Length)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset, sizeof(uint)), value);
    }

    private static void TryWriteUInt64(Span<byte> buffer, int offset, ulong value)
    {
        if ((uint)offset + sizeof(ulong) > (uint)buffer.Length)
        {
            return;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, sizeof(ulong)), value);
    }

    private static void TryWriteByte(Span<byte> buffer, int offset, byte value)
    {
        if ((uint)offset >= (uint)buffer.Length)
        {
            return;
        }

        buffer[offset] = value;
    }

    private static void WriteImageTimestamp(Span<byte> buffer, DateTime timestamp)
    {
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 0, (ushort)timestamp.Year);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 2, (ushort)timestamp.Month);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 4, (ushort)timestamp.Day);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 6, (ushort)timestamp.Hour);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 8, (ushort)timestamp.Minute);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 10, (ushort)timestamp.Second);
        TryWriteUInt16(buffer, ImageInfoOffsetTimestampSystem + 12, (ushort)timestamp.Millisecond);
    }

    private static void WriteIdentityReport()
    {
        try
        {
            var lines = new List<string>
            {
                "RayCi uEye Bridge Identity Report",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                string.Empty,
                "[uEye API identity]",
                "IdentityStyle=fixed-registry-shell",
                "ListSerialStyle=fixed-display-serial",
                $"UeyeSerial={UeyeSerial}",
                $"CapturedRawSerial={CapturedRawSerial}",
                $"BoardSerial={BoardSerial}",
                $"DisplaySerial={DisplaySerial}",
                $"DisplaySerialShort={DisplaySerialShort}",
                $"SerialTemplate={SerialTemplate}",
                $"Vendor={Vendor}",
                $"ReportedListSerial={ReportedListSerial}",
                $"ReportedListModelShort={ReportedListModelShort}",
                $"ReportedListModelFull={ReportedListModelFull}",
                $"ReportedCalibrationRegistryName={ReportedCalibrationRegistryName}",
                "RawPageIdentityTrusted=False",
                $"ShortModel={ShortModel}",
                $"FullModel={FullModel}",
                $"RegistryShortModel={RegistryShortModel}",
                $"RegistryModel={RegistryModel}",
                $"CapturedModel={CapturedCameraProfile.ModelName}",
                $"CalibrationName={CalibrationName}",
                $"LegacyCalibrationName={LegacyCalibrationName}",
                $"FirmwareVersion={CapturedCameraProfile.FirmwareVersionText}",
                $"FirmwareDate={FirmwareDate}",
                $"SensorId=0x{UeyeNative.IS_SENSOR_UI1545_M:X4}",
                $"BoardType=0x{UeyeNative.IS_BOARD_TYPE_UEYE_USB:X2}",
                $"LinkSpeedMb=480",
                $"PrepareStreamPayload={Convert.ToHexString(CapturedCameraProfile.PrepareStreamPayload)}",
                $"ObservedPrepareStreamPayloadVariants={string.Join(",", CapturedCameraProfile.ObservedPrepareStreamPayloadVariants.Select(Convert.ToHexString))}",
                $"StartStreamPayload={Convert.ToHexString(CapturedCameraProfile.StartStreamPayload)}",
                $"StopStreamPayload={Convert.ToHexString(CapturedCameraProfile.StopStreamPayload)}",
                $"StartupRegisterCount={CapturedCameraProfile.StartupRegisterValues.Count}",
                $"RegisterPageCount={CapturedCameraProfile.RegisterPages.Count}",
                string.Empty,
                "[Virtual calibration registry seed]"
            };

            CalibrationRegistryHook.AppendIdentityReport(lines);
            File.WriteAllLines(IdentityReportPath, lines);
            Log($"Identity report refreshed -> {IdentityReportPath}");
        }
        catch (Exception ex)
        {
            Log($"Identity report write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static int SetLastError(int code, string text)
    {
        lock (Gate)
        {
            _lastError = code;
            _lastErrorText = text;

            if (_lastErrorTextPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(_lastErrorTextPtr);
                _lastErrorTextPtr = nint.Zero;
            }

            _lastErrorTextPtr = Marshal.StringToHGlobalAnsi(text);
        }

        return code;
    }

    public static int GetLastError(int* errorCode, byte** errorText)
    {
        lock (Gate)
        {
            if (errorCode != null)
            {
                *errorCode = _lastError;
            }

            if (errorText != null)
            {
                *errorText = (byte*)_lastErrorTextPtr;
            }
        }

        return UeyeNative.IS_SUCCESS;
    }

    public static int InitializeCamera(uint* cameraHandle)
    {
        lock (Gate)
        {
            if (cameraHandle != null)
            {
                *cameraHandle = CameraHandle;
            }

            EnsureCaptureThreadLocked();
            TrySyncControlStateFromBridgeLocked(out _);
        }

        Log("is_InitCamera -> handle=1");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ExitCamera(uint cameraHandle)
    {
        lock (Gate)
        {
            _captureActive = false;
            EventHandles.Clear();
            EnabledEvents.Clear();
        }

        Log($"is_ExitCamera({cameraHandle})");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static void FillBoardInfo(BOARDINFO* info)
    {
        if (info == null)
        {
            return;
        }

        NativeHelpers.Zero(info, (uint)sizeof(BOARDINFO));
        NativeHelpers.WriteAnsi(&info->SerNo[0], 12, UeyeSerial);
        NativeHelpers.WriteAnsi(&info->ID[0], 20, Vendor);
        NativeHelpers.WriteAnsi(&info->Version[0], 10, FirmwareVersion);
        NativeHelpers.WriteAnsi(&info->Date[0], 12, FirmwareDate);

        info->Select = 1;
        info->Type = UeyeNative.IS_BOARD_TYPE_UEYE_USB;

        if (_boardInfoTraceCount < 16)
        {
            _boardInfoTraceCount++;
            Log($"FillBoardInfo -> boardInfoSerial={UeyeSerial}, ueyeSerial={UeyeSerial}, capturedRawSerial={CapturedRawSerial}, boardSerial={BoardSerial}, id={Vendor}, version={FirmwareVersion}, type=0x{info->Type:X2}");
        }
    }

    public static void FillSensorInfo(SENSORINFO* info)
    {
        if (info == null)
        {
            return;
        }

        NativeHelpers.Zero(info, (uint)sizeof(SENSORINFO));
        info->SensorID = UeyeNative.IS_SENSOR_UI1545_M;
        NativeHelpers.WriteAnsi(&info->strSensorName[0], 32, ShortModel);

        info->nColorMode = 1;
        info->nMaxWidth = UeyeNative.DefaultWidth;
        info->nMaxHeight = UeyeNative.DefaultHeight;
        info->bMasterGain = 1;
        info->bRGain = 0;
        info->bGGain = 0;
        info->bBGain = 0;
        info->bGlobShutter = 0;
        info->wPixelSize = 520;
        info->nUpperLeftBayerPixel = 0;

        if (_sensorInfoTraceCount < 16)
        {
            _sensorInfoTraceCount++;
            Log($"FillSensorInfo -> sensorId=0x{info->SensorID:X4}, sensorName={ShortModel}, max={info->nMaxWidth}x{info->nMaxHeight}");
        }
    }

    public static void FillCameraList(void* cameraList)
    {
        if (cameraList == null)
        {
            return;
        }

        DiagnosticApiHook.TryInstallIdentificationHook();

        var countPtr = (uint*)cameraList;
        var capacity = *countPtr;
        *countPtr = 1;

        if (capacity == 0)
        {
            return;
        }

        var info = (UEYE_CAMERA_INFO*)((byte*)cameraList + sizeof(uint));
        NativeHelpers.Zero(info, (uint)sizeof(UEYE_CAMERA_INFO));
        info->dwCameraID = 1;
        info->dwDeviceID = 1;
        info->dwSensorID = UeyeNative.IS_SENSOR_UI1545_M;
        info->dwInUse = 0;
        info->dwStatus = 0;

        // RayCi always sees the fixed compatibility shell while the captured
        // register/page data remains available for low-level emulation only.
        NativeHelpers.WriteAnsi(&info->SerNo[0], 16, ReportedListSerial);
        NativeHelpers.WriteAnsi(&info->Model[0], 16, ReportedListModelShort);
        NativeHelpers.WriteAnsi(&info->FullModelName[0], 32, ReportedListModelFull);

        if (_cameraListTraceCount < 32)
        {
            _cameraListTraceCount++;
            Log(
                $"FillCameraList -> count=1, sensorId=0x{info->dwSensorID:X4}, " +
                $"reportedModel={ReportedListModelFull}, reportedSerial={ReportedListSerial}, displaySerial={DisplaySerial}, capturedModel={CapturedCameraProfile.ModelName}, capturedRawSerial={CapturedRawSerial}, boardModel={FullModel}, boardSerial={BoardSerial}");
        }
    }

    public static int FillDeviceInfo(uint command, void* parameter, uint parameterSize)
    {
        if (command != UeyeNative.IS_DEVICE_INFO_CMD_GET_DEVICE_INFO)
        {
            Log($"FillDeviceInfo(command=0x{command:X8}, size={parameterSize}) -> IS_NOT_SUPPORTED");
            return SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"Device info command 0x{command:X8} not supported.");
        }

        if (parameter == null || parameterSize < sizeof(IS_DEVICE_INFO))
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Device info buffer is invalid.");
        }

        var info = (IS_DEVICE_INFO*)parameter;
        NativeHelpers.Zero(info, (uint)sizeof(IS_DEVICE_INFO));

        info->infoDevHeartbeat.dwRuntimeFirmwareVersion = unchecked((uint)IsGetDllVersion());
        info->infoDevHeartbeat.wTemperature = 300;
        info->infoDevHeartbeat.wLinkSpeed_Mb = 480;
        info->infoDevHeartbeat.wComportOffset = 0;
        info->infoDevControl.dwDeviceId = 1;

        if (_deviceInfoTraceCount < 32)
        {
            _deviceInfoTraceCount++;
            Log($"FillDeviceInfo -> deviceId={info->infoDevControl.dwDeviceId}, link={info->infoDevHeartbeat.wLinkSpeed_Mb}Mb, fw=0x{info->infoDevHeartbeat.dwRuntimeFirmwareVersion:X8}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int HandleConfiguration(uint command, void* parameter, uint parameterSize)
    {
        if (_configurationTraceCount < 32)
        {
            _configurationTraceCount++;
            Log($"HandleConfiguration(command={command}, size={parameterSize})");
        }

        switch (command)
        {
            case UeyeNative.IS_CONFIG_CMD_GET_CAPABILITIES:
                if (parameter == null || parameterSize < sizeof(uint))
                {
                    return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Configuration capabilities buffer is invalid.");
                }

                *(uint*)parameter = UeyeNative.IS_CONFIG_OPEN_MP_CAP_SUPPORTED;
                return SetLastError(UeyeNative.IS_SUCCESS, "OK");

            case UeyeNative.IS_CONFIG_OPEN_MP_CMD_GET_ENABLE:
            case UeyeNative.IS_CONFIG_OPEN_MP_CMD_GET_ENABLE_DEFAULT:
            case UeyeNative.IS_CONFIG_CMD_GET_IMAGE_MEMORY_COMPATIBILIY_MODE:
            case UeyeNative.IS_CONFIG_CMD_GET_IMAGE_MEMORY_COMPATIBILIY_MODE_DEFAULT:
                if (parameter == null || parameterSize < sizeof(uint))
                {
                    return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Configuration output buffer is invalid.");
                }

                *(uint*)parameter = UeyeNative.IS_CONFIG_IMAGE_MEMORY_COMPATIBILITY_MODE_OFF;
                return SetLastError(UeyeNative.IS_SUCCESS, "OK");

            default:
                ZeroCommand(parameter, parameterSize);

                return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }
    }

    public static int HandleDeviceFeature(uint command, void* parameter, uint parameterSize)
    {
        if (_deviceFeatureTraceCount < 32)
        {
            _deviceFeatureTraceCount++;
            Log($"HandleDeviceFeature(command={command}, size={parameterSize})");
        }

        switch (command)
        {
            case UeyeNative.IS_DEVICE_FEATURE_CMD_GET_SUPPORTED_FEATURES:
                if (parameter == null || parameterSize < sizeof(uint))
                {
                    return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Device feature capabilities buffer is invalid.");
                }

                *(uint*)parameter = 0;
                return SetLastError(UeyeNative.IS_SUCCESS, "OK");

            default:
                ZeroCommand(parameter, parameterSize);

                return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }
    }

    public static int SetAoi(int x, int y, int width, int height)
    {
        lock (Gate)
        {
            _aoi = NormalizeAoi(x, y, width, height);
            TryApplyGeometryToBridgeLocked("SetAoi");
            Log($"SetAoi(x={_aoi.s32X}, y={_aoi.s32Y}, width={_aoi.s32Width}, height={_aoi.s32Height})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetAoiSize(int width, int height)
    {
        lock (Gate)
        {
            _aoi = NormalizeAoi(_aoi.s32X, _aoi.s32Y, width, height);
            TryApplyGeometryToBridgeLocked("SetAoiSize");
            Log($"SetAoiSize(width={_aoi.s32Width}, height={_aoi.s32Height})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetAoiPosition(int x, int y)
    {
        lock (Gate)
        {
            _aoi = NormalizeAoi(x, y, _aoi.s32Width, _aoi.s32Height);
            TryApplyGeometryToBridgeLocked("SetAoiPosition");
            Log($"SetAoiPosition(x={_aoi.s32X}, y={_aoi.s32Y})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static IS_RECT GetAoi()
    {
        lock (Gate)
        {
            return _aoi;
        }
    }

    public static int SetBinning(int mode)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);
            if (TryHandleSamplingQueryLocked(mode, isSubSampling: false, out var queryResult))
            {
                return queryResult;
            }

            if (!TryResolveSamplingMode(mode, isSubSampling: false, out var normalizedMode, out var factorX, out var factorY))
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, $"Binning mode 0x{mode:X} is invalid.");
            }

            _binningMode = normalizedMode;
            _subSamplingMode = UeyeNative.IS_SUBSAMPLING_DISABLE;
            TryApplyGeometryToBridgeLocked("SetBinning");
            Log($"SetBinning(mode=0x{mode:X}, normalized=0x{normalizedMode:X}, factor={factorX}x{factorY})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetSubSampling(int mode)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);
            if (TryHandleSamplingQueryLocked(mode, isSubSampling: true, out var queryResult))
            {
                return queryResult;
            }

            if (!TryResolveSamplingMode(mode, isSubSampling: true, out var normalizedMode, out var factorX, out var factorY))
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, $"Subsampling mode 0x{mode:X} is invalid.");
            }

            _subSamplingMode = normalizedMode;
            _binningMode = UeyeNative.IS_BINNING_DISABLE;
            TryApplyGeometryToBridgeLocked("SetSubSampling");
            Log($"SetSubSampling(mode=0x{mode:X}, normalized=0x{normalizedMode:X}, factor={factorX}x{factorY})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int Blacklevel(uint command, void* parameter, uint parameterSize)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);

            switch (command)
            {
                case UeyeNative.IS_BLACKLEVEL_CMD_GET_CAPS:
                    if (parameter == null || parameterSize < sizeof(uint))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel caps buffer is invalid.");
                    }

                    *(uint*)parameter = UeyeNative.IS_BLACKLEVEL_CAP_SET_AUTO_BLACKLEVEL | UeyeNative.IS_BLACKLEVEL_CAP_SET_OFFSET;
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                case UeyeNative.IS_BLACKLEVEL_CMD_GET_MODE_DEFAULT:
                case UeyeNative.IS_BLACKLEVEL_CMD_GET_MODE:
                    if (parameter == null || parameterSize < sizeof(int))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel mode buffer is invalid.");
                    }

                    *(int*)parameter = UeyeNative.IS_AUTO_BLACKLEVEL_OFF;
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                case UeyeNative.IS_BLACKLEVEL_CMD_SET_MODE:
                    if (parameter == null || parameterSize < sizeof(int))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel mode input is invalid.");
                    }

                    Log($"Blacklevel mode request -> {*(int*)parameter} (kept manual)");
                    *(int*)parameter = UeyeNative.IS_AUTO_BLACKLEVEL_OFF;
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                case UeyeNative.IS_BLACKLEVEL_CMD_GET_OFFSET_DEFAULT:
                case UeyeNative.IS_BLACKLEVEL_CMD_GET_OFFSET:
                    if (parameter == null || parameterSize < sizeof(int))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel offset buffer is invalid.");
                    }

                    *(int*)parameter = _blackLevel;
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                case UeyeNative.IS_BLACKLEVEL_CMD_GET_OFFSET_RANGE:
                    if (parameter == null || parameterSize < (sizeof(int) * 3))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel range buffer is invalid.");
                    }

                    ((int*)parameter)[0] = 0;
                    ((int*)parameter)[1] = 255;
                    ((int*)parameter)[2] = 1;
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                case UeyeNative.IS_BLACKLEVEL_CMD_SET_OFFSET:
                    if (parameter == null || parameterSize < sizeof(int))
                    {
                        return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Blacklevel offset input is invalid.");
                    }

                    _blackLevel = Math.Clamp(*(int*)parameter, 0, 255);
                    if (DahengFrameBridgeClient.TrySetBlackLevel(_blackLevel, out var updatedState))
                    {
                        ApplyBridgeControlStateLocked(updatedState);
                    }

                    *(int*)parameter = _blackLevel;
                    Log($"Blacklevel set -> {_blackLevel}");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");

                default:
                    return SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"Blacklevel command {command} not supported.");
            }
        }
    }

    public static ImageMemory? GetActiveMemory()
    {
        lock (Gate)
        {
            return _activeMemoryId != 0 && Memories.TryGetValue(_activeMemoryId, out var memory)
                ? memory
                : null;
        }
    }

    public static int InitializeImageQueue(int mode)
    {
        lock (Gate)
        {
            _imageQueueInitialized = true;
            _cancelWaitRequested = false;
            LockedSequenceMemoryIds.Clear();
            ClearReadySequenceQueueLocked();
            _lastDeliveredFrameGeneration = 0;
            _nextQueueIndex = 0;
            if (_currentSequenceMemoryId != 0)
            {
                _lastSequenceMemoryId = _currentSequenceMemoryId;
            }

            Monitor.PulseAll(Gate);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ExitImageQueue()
    {
        lock (Gate)
        {
            _imageQueueInitialized = false;
            _cancelWaitRequested = true;
            LockedSequenceMemoryIds.Clear();
            ClearReadySequenceQueueLocked();
            Monitor.PulseAll(Gate);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int HandleImageQueueCommand(uint command, void* parameter, uint parameterSize)
    {
        lock (Gate)
        {
            switch (command)
            {
                case UeyeNative.IS_IMAGE_QUEUE_CMD_INIT:
                    return InitializeImageQueue(0);
                case UeyeNative.IS_IMAGE_QUEUE_CMD_EXIT:
                    return ExitImageQueue();
                case UeyeNative.IS_IMAGE_QUEUE_CMD_WAIT:
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                case UeyeNative.IS_IMAGE_QUEUE_CMD_CANCEL_WAIT:
                    _cancelWaitRequested = true;
                    Monitor.PulseAll(Gate);
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                case UeyeNative.IS_IMAGE_QUEUE_CMD_GET_PENDING:
                    if (parameter != null && parameterSize >= sizeof(int))
                    {
                        *(int*)parameter = GetPendingSequenceCountLocked();
                    }

                    if (_queueTraceCount < 128)
                    {
                        _queueTraceCount++;
                        Log($"ImageQueue(GET_PENDING) -> count={GetPendingSequenceCountLocked()}, {DescribeSequenceStateLocked()}");
                    }

                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                case UeyeNative.IS_IMAGE_QUEUE_CMD_FLUSH:
                    ClearReadySequenceQueueLocked();
                    _lastDeliveredFrameGeneration = _producedFrameGeneration;
                    _cancelWaitRequested = false;

                    if (_queueTraceCount < 128)
                    {
                        _queueTraceCount++;
                        Log($"ImageQueue(FLUSH) -> {DescribeSequenceStateLocked()}");
                    }

                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                case UeyeNative.IS_IMAGE_QUEUE_CMD_DISCARD_N_ITEMS:
                    var discardedCount = DiscardPendingSequenceItemsLocked(parameter != null && parameterSize >= sizeof(int)
                        ? Math.Max(0, *(int*)parameter)
                        : int.MaxValue);
                    if (parameter != null && parameterSize >= sizeof(int))
                    {
                        *(int*)parameter = discardedCount;
                    }

                    _lastDeliveredFrameGeneration = _producedFrameGeneration;

                    if (_queueTraceCount < 128)
                    {
                        _queueTraceCount++;
                        Log($"ImageQueue(DISCARD) -> discarded={discardedCount}, {DescribeSequenceStateLocked()}");
                    }

                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                default:
                    ZeroCommand(parameter, parameterSize);

                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
            }
        }
    }

    public static int ClearSequence()
    {
        lock (Gate)
        {
            SequenceMemoryIds.Clear();
            LockedSequenceMemoryIds.Clear();
            ClearReadySequenceQueueLocked();
            _nextQueueIndex = 0;
            _currentSequenceMemoryId = 0;
            _lastSequenceMemoryId = 0;
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int AddToSequence(byte* memory, int memoryId)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem) || mem.Pointer != (nint)memory)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown sequence image memory.");
            }

            if (!SequenceMemoryIds.Contains(memoryId))
            {
                SequenceMemoryIds.Add(memoryId);
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetActiveSequenceBuffer(int* currentSequenceId, byte** currentMemory, byte** lastMemory)
    {
        lock (Gate)
        {
            var currentMem = _currentSequenceMemoryId != 0 && Memories.TryGetValue(_currentSequenceMemoryId, out var current)
                ? current
                : GetFallbackSequenceMemoryLocked();
            var previousMem = _lastSequenceMemoryId != 0 && Memories.TryGetValue(_lastSequenceMemoryId, out var previous)
                ? previous
                : currentMem;

            if (currentSequenceId != null)
            {
                *currentSequenceId = currentMem?.MemoryId ?? 0;
            }

            if (currentMemory != null)
            {
                *currentMemory = currentMem is null ? null : (byte*)currentMem.Pointer;
            }

            if (lastMemory != null)
            {
                *lastMemory = previousMem is null ? null : (byte*)previousMem.Pointer;
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int LockSequenceBuffer(int memoryId, byte* memory)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem) || mem.Pointer != (nint)memory)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown sequence image memory.");
            }

            LockedSequenceMemoryIds.Add(memoryId);

            if (_sequenceTraceCount < 256)
            {
                _sequenceTraceCount++;
                Log($"LockSequenceBuffer(memoryId={memoryId}) -> {DescribeSequenceStateLocked()}");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int UnlockSequenceBuffer(int memoryId, byte* memory)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem) || mem.Pointer != (nint)memory)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown sequence image memory.");
            }

            LockedSequenceMemoryIds.Remove(memoryId);
            Monitor.PulseAll(Gate);

            if (_sequenceTraceCount < 256)
            {
                _sequenceTraceCount++;
                Log($"UnlockSequenceBuffer(memoryId={memoryId}) -> {DescribeSequenceStateLocked()}");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int AllocateImageMemory(int width, int height, int bitsPerPixel, byte** memory, int* memoryId)
    {
        if (memory == null || memoryId == null || width <= 0 || height <= 0 || bitsPerPixel <= 0)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Invalid image memory request.");
        }

        var requestedBitsPerPixel = bitsPerPixel;
        bitsPerPixel = NormalizeContainerBitsPerPixel(bitsPerPixel);
        var bytesPerPixel = UeyeNative.GetBytesPerPixel(bitsPerPixel);
        var pitch = width * bytesPerPixel;
        var sizeBytes = pitch * height;
        var ptr = Marshal.AllocHGlobal(sizeBytes);

        lock (Gate)
        {
            var id = _nextMemoryId++;
            var mem = new ImageMemory
            {
                MemoryId = id,
                Pointer = ptr,
                Width = width,
                Height = height,
                BitsPerPixel = bitsPerPixel,
                Pitch = pitch,
                SizeBytes = sizeBytes,
            };

            Memories[id] = mem;
            MemoryFrameNumbers[id] = 0;
            _activeMemoryId = id;

            *memory = (byte*)ptr;
            *memoryId = id;
            FillNoiseLocked(mem);
        }

        Log(requestedBitsPerPixel == bitsPerPixel
            ? $"Allocated image memory {sizeBytes} bytes for {width}x{height}x{bitsPerPixel}"
            : $"Allocated image memory {sizeBytes} bytes for {width}x{height}x{bitsPerPixel} (requested {requestedBitsPerPixel})");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetAllocatedImageMemory(int width, int height, int bitsPerPixel, byte* memory, int* memoryId)
    {
        if (memory == null || memoryId == null)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "External memory pointer is null.");
        }

        var requestedBitsPerPixel = bitsPerPixel;
        bitsPerPixel = NormalizeContainerBitsPerPixel(bitsPerPixel);
        var bytesPerPixel = UeyeNative.GetBytesPerPixel(bitsPerPixel);
        var pitch = width * bytesPerPixel;
        var sizeBytes = pitch * height;

        lock (Gate)
        {
            var id = _nextMemoryId++;
            Memories[id] = new ImageMemory
            {
                MemoryId = id,
                Pointer = (nint)memory,
                Width = width,
                Height = height,
                BitsPerPixel = bitsPerPixel,
                Pitch = pitch,
                SizeBytes = sizeBytes,
            };
            MemoryFrameNumbers[id] = 0;
            _activeMemoryId = id;
            *memoryId = id;
            FillNoiseLocked(Memories[id]);
        }

        Log(requestedBitsPerPixel == bitsPerPixel
            ? $"Registered caller-owned image memory for {width}x{height}x{bitsPerPixel}"
            : $"Registered caller-owned image memory for {width}x{height}x{bitsPerPixel} (requested {requestedBitsPerPixel})");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetImageMemory(byte* memory, int memoryId)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem) || mem.Pointer != (nint)memory)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown image memory.");
            }

            _activeMemoryId = memoryId;

            if (_memoryTraceCount < 48)
            {
                _memoryTraceCount++;
                Log($"SetImageMemory -> memoryId={mem.MemoryId}, ptr=0x{mem.Pointer.ToInt64():X}, size={mem.Width}x{mem.Height}x{mem.BitsPerPixel}, pitch={mem.Pitch}");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int FreeImageMemory(byte* memory, int memoryId)
    {
        lock (Gate)
        {
            if (!Memories.Remove(memoryId, out var mem))
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown image memory.");
            }

            if (mem.Pointer != (nint)memory)
            {
                Memories[memoryId] = mem;
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Image memory pointer mismatch.");
            }

            MemoryFrameNumbers.Remove(memoryId);
            MemoryImageInfoSnapshots.Remove(memoryId);
            SequenceMemoryIds.Remove(memoryId);
            LockedSequenceMemoryIds.Remove(memoryId);
            RemoveSequenceMemoryFromQueueLocked(memoryId);
            if (_currentSequenceMemoryId == memoryId)
            {
                _currentSequenceMemoryId = 0;
            }

            if (_lastSequenceMemoryId == memoryId)
            {
                _lastSequenceMemoryId = 0;
            }

            Marshal.FreeHGlobal(mem.Pointer);
            if (_activeMemoryId == memoryId)
            {
                _activeMemoryId = Memories.Keys.FirstOrDefault();
            }

            Monitor.PulseAll(Gate);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetImageMemory(void** memory)
    {
        if (memory == null)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Output pointer is null.");
        }

        var mem = GetActiveMemory();
        *memory = mem is null ? null : (void*)mem.Pointer;

        if (_memoryTraceCount < 48)
        {
            _memoryTraceCount++;
            Log(mem is null
                ? "GetImageMemory -> <null>"
                : $"GetImageMemory -> memoryId={mem.MemoryId}, ptr=0x{mem.Pointer.ToInt64():X}, size={mem.Width}x{mem.Height}x{mem.BitsPerPixel}, pitch={mem.Pitch}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetActiveImageMemory(byte** memory, int* memoryId)
    {
        var mem = GetActiveMemory();

        if (memory != null)
        {
            *memory = mem is null ? null : (byte*)mem.Pointer;
        }

        if (memoryId != null)
        {
            *memoryId = mem?.MemoryId ?? 0;
        }

        if (_memoryTraceCount < 48)
        {
            _memoryTraceCount++;
            Log(mem is null
                ? "GetActiveImageMemory -> <null>"
                : $"GetActiveImageMemory -> memoryId={mem.MemoryId}, ptr=0x{mem.Pointer.ToInt64():X}, size={mem.Width}x{mem.Height}x{mem.BitsPerPixel}, pitch={mem.Pitch}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int InquireImageMemory(byte* memory, int memoryId, int* width, int* height, int* bits, int* pitch)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem) || mem.Pointer != (nint)memory)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown image memory.");
            }

            if (width != null) *width = mem.Width;
            if (height != null) *height = mem.Height;
            if (bits != null) *bits = mem.BitsPerPixel;
            if (pitch != null) *pitch = mem.Pitch;

            if (_memoryTraceCount < 48)
            {
                _memoryTraceCount++;
                Log($"InquireImageMemory(memoryId={mem.MemoryId}) -> ptr=0x{mem.Pointer.ToInt64():X}, size={mem.Width}x{mem.Height}x{mem.BitsPerPixel}, pitch={mem.Pitch}");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetImagePitch(int* pitch)
    {
        if (pitch == null)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Pitch output is null.");
        }

        var mem = GetActiveMemory();
        *pitch = mem?.Pitch ?? 0;

        if (_memoryTraceCount < 48)
        {
            _memoryTraceCount++;
            Log(mem is null
                ? "GetImagePitch -> 0 (no active memory)"
                : $"GetImagePitch -> memoryId={mem.MemoryId}, pitch={mem.Pitch}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int CopyImageMemory(byte* srcMemory, int srcMemoryId, byte* dstMemory)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(srcMemoryId, out var src) || src.Pointer != (nint)srcMemory || dstMemory == null)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Invalid copy request.");
            }

            Buffer.MemoryCopy((void*)src.Pointer, dstMemory, src.SizeBytes, src.SizeBytes);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int CopyImageMemoryLines(byte* srcMemory, int srcMemoryId, int srcOffsetY, int countLines, byte* dstMemory)
    {
        lock (Gate)
        {
            if (!Memories.TryGetValue(srcMemoryId, out var src) || src.Pointer != (nint)srcMemory || dstMemory == null)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Invalid line copy request.");
            }

            var offset = Math.Clamp(srcOffsetY, 0, src.Height) * src.Pitch;
            var lines = Math.Clamp(countLines, 0, src.Height - Math.Clamp(srcOffsetY, 0, src.Height));
            var size = lines * src.Pitch;
            Buffer.MemoryCopy((byte*)src.Pointer + offset, dstMemory, size, size);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int StartCapture()
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);
            _captureActive = true;
            _cancelWaitRequested = false;
            EnsureCaptureThreadLocked();
            if (_imageQueueInitialized)
            {
                PrimeSequenceQueueLocked();
            }
            else
            {
                if (_activeMemoryId == 0)
                {
                    _activeMemoryId = ResolveCaptureMemoryIdLocked();
                }

                CaptureIntoSelectedMemoryLocked(updateGeneration: true);
            }

            Monitor.PulseAll(Gate);
        }

        Log("Capture started");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int FreezeFrame()
    {
        lock (Gate)
        {
            CaptureIntoSelectedMemoryLocked(updateGeneration: true);
        }

        Log("FreezeVideo requested");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int StopCapture()
    {
        lock (Gate)
        {
            _captureActive = false;
            _cancelWaitRequested = true;
            Monitor.PulseAll(Gate);
        }

        Log("Capture stopped");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int WaitForNextImage(uint timeoutMs, byte** memory, int* memoryId)
    {
        lock (Gate)
        {
            if (Memories.Count == 0)
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "No image memory has been allocated.");
            }

            EnsureCaptureThreadLocked();
            if (_captureActive && _producedFrameGeneration == 0)
            {
                if (_imageQueueInitialized)
                {
                    PrimeSequenceQueueLocked();
                }
                else
                {
                    CaptureIntoSelectedMemoryLocked(updateGeneration: true);
                }
            }

            if (_imageQueueInitialized)
            {
                var waitForever = timeoutMs == uint.MaxValue;
                var deadlineUtc = waitForever
                    ? DateTime.MaxValue
                    : DateTime.UtcNow.AddMilliseconds(timeoutMs);

                while (_captureActive && !_cancelWaitRequested)
                {
                    if (TryDequeueReadySequenceMemoryLocked(out var readyMemory))
                    {
                        return CompleteSequenceWaitLocked(readyMemory!, timeoutMs, memory, memoryId);
                    }

                    if (timeoutMs == 0)
                    {
                        if (_waitTimeoutTraceCount < 32)
                        {
                            _waitTimeoutTraceCount++;
                            Log($"WaitForNextImage(timeout={timeoutMs}) -> timed out (queueEmpty, produced={_producedFrameGeneration}, frame={_frameNumber}, {DescribeSequenceStateLocked()})");
                        }

                        return SetLastError(UeyeNative.IS_TIMED_OUT, "Timed out waiting for the next image.");
                    }

                    if (!waitForever)
                    {
                        var waitSliceMs = (int)Math.Ceiling((deadlineUtc - DateTime.UtcNow).TotalMilliseconds);
                        if (waitSliceMs <= 0)
                        {
                            if (_waitTimeoutTraceCount < 32)
                            {
                                _waitTimeoutTraceCount++;
                                Log($"WaitForNextImage(timeout={timeoutMs}) -> timed out (deadline, produced={_producedFrameGeneration}, frame={_frameNumber}, {DescribeSequenceStateLocked()})");
                            }

                            return SetLastError(UeyeNative.IS_TIMED_OUT, "Timed out waiting for the next image.");
                        }

                        Monitor.Wait(Gate, Math.Min(waitSliceMs, 50));
                    }
                    else
                    {
                        Monitor.Wait(Gate, 50);
                    }
                }

                if (_cancelWaitRequested)
                {
                    _cancelWaitRequested = false;
                    if (_waitTimeoutTraceCount < 32)
                    {
                        _waitTimeoutTraceCount++;
                        Log($"WaitForNextImage(timeout={timeoutMs}) -> canceled ({DescribeSequenceStateLocked()})");
                    }

                    return SetLastError(UeyeNative.IS_TIMED_OUT, "Image wait canceled.");
                }

                if (_waitTimeoutTraceCount < 32)
                {
                    _waitTimeoutTraceCount++;
                    Log($"WaitForNextImage(timeout={timeoutMs}) -> timed out (captureInactive={(!_captureActive)}, produced={_producedFrameGeneration}, frame={_frameNumber}, {DescribeSequenceStateLocked()})");
                }

                return SetLastError(UeyeNative.IS_TIMED_OUT, "Timed out waiting for the next image.");
            }

            var waitForeverLegacy = timeoutMs == uint.MaxValue;
            var deadlineUtcLegacy = waitForeverLegacy
                ? DateTime.MaxValue
                : DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (_captureActive &&
                   !_cancelWaitRequested &&
                   _producedFrameGeneration <= _lastDeliveredFrameGeneration)
            {
                if (timeoutMs == 0)
                {
                    if (_waitTimeoutTraceCount < 32)
                    {
                        _waitTimeoutTraceCount++;
                        Log($"WaitForNextImage(timeout={timeoutMs}) -> timed out (legacy, produced={_producedFrameGeneration}, delivered={_lastDeliveredFrameGeneration}, frame={_frameNumber}, {DescribeSequenceStateLocked()})");
                    }

                    return SetLastError(UeyeNative.IS_TIMED_OUT, "Timed out waiting for the next image.");
                }

                var waitSliceMs = waitForeverLegacy
                    ? 250
                    : Math.Max(1, (int)Math.Ceiling((deadlineUtcLegacy - DateTime.UtcNow).TotalMilliseconds));
                if (!waitForeverLegacy && waitSliceMs <= 0)
                {
                    if (_waitTimeoutTraceCount < 32)
                    {
                        _waitTimeoutTraceCount++;
                        Log($"WaitForNextImage(timeout={timeoutMs}) -> timed out (legacy deadline, produced={_producedFrameGeneration}, delivered={_lastDeliveredFrameGeneration}, frame={_frameNumber}, {DescribeSequenceStateLocked()})");
                    }

                    return SetLastError(UeyeNative.IS_TIMED_OUT, "Timed out waiting for the next image.");
                }

                Monitor.Wait(Gate, Math.Min(waitSliceMs, 250));
            }

            if (_cancelWaitRequested)
            {
                _cancelWaitRequested = false;
                if (_waitTimeoutTraceCount < 32)
                {
                    _waitTimeoutTraceCount++;
                    Log($"WaitForNextImage(timeout={timeoutMs}) -> canceled ({DescribeSequenceStateLocked()})");
                }

                return SetLastError(UeyeNative.IS_TIMED_OUT, "Image wait canceled.");
            }

            _activeMemoryId = ResolveNextSequenceMemoryIdLocked();
            if (_activeMemoryId == 0 || !Memories.TryGetValue(_activeMemoryId, out var mem))
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "No sequence image memory is available.");
            }

            PopulateMemoryLocked(mem, updateGeneration: false);
            return CompleteSequenceWaitLocked(mem, timeoutMs, memory, memoryId);
        }
    }

    public static int GetImageInfo(int memoryId, UEYEIMAGEINFO* imageInfo, int imageInfoSize)
    {
        if (imageInfo == null || imageInfoSize <= 0)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Image info buffer is invalid.");
        }

        lock (Gate)
        {
            if (!Memories.TryGetValue(memoryId, out var mem))
            {
                return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Unknown image memory.");
            }

            var frameNumber = MemoryFrameNumbers.TryGetValue(memoryId, out var storedFrameNumber) && storedFrameNumber > 0
                ? storedFrameNumber
                : _frameNumber;
            var snapshot = MemoryImageInfoSnapshots.TryGetValue(memoryId, out var storedSnapshot)
                ? storedSnapshot
                : CreateImageInfoSnapshotLocked(frameNumber);
            var buffer = (byte*)imageInfo;
            var bytesToClear = Math.Min(imageInfoSize, sizeof(UEYEIMAGEINFO));
            NativeHelpers.Zero(buffer, (uint)bytesToClear);
            var imageInfoSpan = new Span<byte>(buffer, imageInfoSize);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetFlags, snapshot.Flags);
            TryWriteUInt64(imageInfoSpan, ImageInfoOffsetTimestampDevice, snapshot.DeviceTimestampUs);
            WriteImageTimestamp(imageInfoSpan, snapshot.SystemTimestamp);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetIoStatus, snapshot.IoStatus);
            TryWriteUInt16(imageInfoSpan, ImageInfoOffsetAoiIndex, snapshot.AoiIndex);
            TryWriteUInt16(imageInfoSpan, ImageInfoOffsetAoiCycle, snapshot.AoiCycle);
            TryWriteUInt64(imageInfoSpan, ImageInfoOffsetFrameNumber, snapshot.FrameNumber);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetImageBuffers, (uint)Math.Max(Memories.Count, 1));
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetImageBuffersInUse, (uint)Math.Max(LockedSequenceMemoryIds.Count, _captureActive ? 1 : 0));
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetReserved3, 0);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetImageHeight, (uint)mem.Height);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetImageWidth, (uint)mem.Width);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetHostProcessTime, snapshot.HostProcessTimeUs);
            TryWriteByte(imageInfoSpan, ImageInfoOffsetSequencerIndex, snapshot.SequencerIndex);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetFocusValue, 0);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetFocusing, 0);
            TryWriteUInt32(imageInfoSpan, ImageInfoOffsetReserved4, 0);

            if (_imageInfoTraceCount < 16)
            {
                _imageInfoTraceCount++;
                Log($"GetImageInfo(memoryId={memoryId}, size={imageInfoSize}) -> {mem.Width}x{mem.Height}, frame={frameNumber}, tsUs={snapshot.DeviceTimestampUs}, io=0x{snapshot.IoStatus:X8}, flags=0x{snapshot.Flags:X8}");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetExposure(uint command, void* parameter)
    {
        lock (Gate)
        {
            var hasBridgeState = TrySyncControlStateFromBridgeLocked(out var bridgeState);
            var minExposureMs = hasBridgeState && bridgeState.ExposureMinMs > 0 ? bridgeState.ExposureMinMs : UeyeNative.MinExposureMs;
            var maxExposureMs = hasBridgeState && bridgeState.ExposureMaxMs > minExposureMs ? bridgeState.ExposureMaxMs : UeyeNative.MaxExposureMs;
            var exposureIncMs = hasBridgeState && bridgeState.ExposureIncMs > 0 ? bridgeState.ExposureIncMs : UeyeNative.ExposureIncrementMs;

            switch (command)
            {
                case UeyeNative.IS_EXPOSURE_CMD_GET_CAPS:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure caps output is null.");
                    *(uint*)parameter = UeyeNative.IS_EXPOSURE_CAP_EXPOSURE | UeyeNative.IS_EXPOSURE_CAP_FINE_INCREMENT;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE_DEFAULT:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure default output is null.");
                    *(double*)parameter = hasBridgeState && bridgeState.ExposureMs > 0 ? bridgeState.ExposureMs : UeyeNative.DefaultExposureMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_MIN:
                case UeyeNative.IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_MIN:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure min output is null.");
                    *(double*)parameter = minExposureMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_MAX:
                case UeyeNative.IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_MAX:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure max output is null.");
                    *(double*)parameter = maxExposureMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_INC:
                case UeyeNative.IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_INC:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure increment output is null.");
                    *(double*)parameter = exposureIncMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE:
                case UeyeNative.IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure range output is null.");
                    var range = (double*)parameter;
                    range[0] = minExposureMs;
                    range[1] = maxExposureMs;
                    range[2] = exposureIncMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_GET_EXPOSURE:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure output is null.");
                    *(double*)parameter = _exposureMs;
                    break;
                }
                case UeyeNative.IS_EXPOSURE_CMD_SET_EXPOSURE:
                {
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Exposure input is null.");
                    _exposureMs = Math.Clamp(*(double*)parameter, minExposureMs, maxExposureMs);
                    SetCapturedExposureRegisterLocked(_exposureMs);
                    if (DahengFrameBridgeClient.TrySetExposureMs(_exposureMs, out var updatedState))
                    {
                        ApplyBridgeControlStateLocked(updatedState);
                    }
                    Log($"Exposure set -> {_exposureMs:F3} ms");
                    break;
                }
                default:
                    return SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"Exposure command {command} not supported.");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetAutoParameter(int parameter, double* value1, double* value2)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);
            switch (parameter)
            {
                case UeyeNative.IS_SET_ENABLE_AUTO_GAIN:
                {
                    if (value1 != null)
                    {
                        _autoGainEnabled = *value1 != 0;
                        if (DahengFrameBridgeClient.TrySetAutoGain(_autoGainEnabled, out var gainState))
                        {
                            ApplyBridgeControlStateLocked(gainState);
                        }
                        *value1 = _autoGainEnabled ? 1.0 : 0.0;
                    }

                    if (value2 != null)
                    {
                        *value2 = 0.0;
                    }

                    Log($"SetAutoParameter(AutoGain={_autoGainEnabled})");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
                case UeyeNative.IS_SET_ENABLE_AUTO_SHUTTER:
                {
                    if (value1 != null)
                    {
                        _autoExposureEnabled = *value1 != 0;
                        if (DahengFrameBridgeClient.TrySetAutoExposure(_autoExposureEnabled, out var exposureState))
                        {
                            ApplyBridgeControlStateLocked(exposureState);
                        }
                        *value1 = _autoExposureEnabled ? 1.0 : 0.0;
                    }

                    if (value2 != null)
                    {
                        *value2 = 0.0;
                    }

                    Log($"SetAutoParameter(AutoExposure={_autoExposureEnabled})");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
                default:
                {
                    if (value1 != null)
                    {
                        _ = *value1;
                    }

                    if (value2 != null)
                    {
                        _ = *value2;
                    }

                    Log($"SetAutoParameter({parameter})");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                }
            }
        }
    }

    public static int SetHardwareGain(int master, int red, int green, int blue)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out _);

            if (master == UeyeNative.IS_GET_MASTER_GAIN)
            {
                SetLastError(UeyeNative.IS_SUCCESS, "OK");
                return _masterGain;
            }

            if (master == UeyeNative.IS_GET_DEFAULT_MASTER)
            {
                SetLastError(UeyeNative.IS_SUCCESS, "OK");
                return UeyeNative.IS_MIN_GAIN;
            }

            if (master == UeyeNative.IS_GET_GAINBOOST)
            {
                SetLastError(UeyeNative.IS_SUCCESS, "OK");
                return _gainBoostEnabled ? UeyeNative.IS_SET_GAINBOOST_ON : UeyeNative.IS_SET_GAINBOOST_OFF;
            }

            if (master == UeyeNative.IS_GET_RED_GAIN ||
                master == UeyeNative.IS_GET_GREEN_GAIN ||
                master == UeyeNative.IS_GET_BLUE_GAIN ||
                master == UeyeNative.IS_GET_DEFAULT_RED ||
                master == UeyeNative.IS_GET_DEFAULT_GREEN ||
                master == UeyeNative.IS_GET_DEFAULT_BLUE)
            {
                SetLastError(UeyeNative.IS_SUCCESS, "OK");
                return 0;
            }

            if (master != UeyeNative.IS_IGNORE_PARAMETER && master >= UeyeNative.IS_MIN_GAIN)
            {
                _masterGain = Math.Clamp(master, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN);
                if (DahengFrameBridgeClient.TrySetMasterGain(_masterGain, out var gainState))
                {
                    ApplyBridgeControlStateLocked(gainState);
                }
                Log($"SetHardwareGain(master={_masterGain}, red={red}, green={green}, blue={blue})");
            }

            return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }
    }

    public static int SetGainBoost(int mode)
    {
        lock (Gate)
        {
            switch (mode)
            {
                case UeyeNative.IS_GET_SUPPORTED_GAINBOOST:
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return 0;
                case UeyeNative.IS_GET_GAINBOOST:
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return _gainBoostEnabled ? UeyeNative.IS_SET_GAINBOOST_ON : UeyeNative.IS_SET_GAINBOOST_OFF;
                case UeyeNative.IS_SET_GAINBOOST_ON:
                    _gainBoostEnabled = true;
                    break;
                case UeyeNative.IS_SET_GAINBOOST_OFF:
                    _gainBoostEnabled = false;
                    break;
            }

            Log($"SetGainBoost(mode=0x{mode:X})");
            return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }
    }

    public static int SetHWGainFactor(int mode, int factor)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out var bridgeState);
            var gainMinDb = bridgeState.GainMaxDb > bridgeState.GainMinDb
                ? bridgeState.GainMinDb
                : 0.0;
            var gainMaxDb = bridgeState.GainMaxDb > bridgeState.GainMinDb
                ? bridgeState.GainMaxDb
                : 16.0;

            switch (mode)
            {
                case UeyeNative.IS_GET_MASTER_GAIN_FACTOR:
                {
                    var currentFactor = ToGainFactor(_masterGain, gainMinDb, gainMaxDb);
                    Log($"SetHWGainFactor(getMaster) -> {currentFactor}");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return currentFactor;
                }
                case UeyeNative.IS_GET_RED_GAIN_FACTOR:
                case UeyeNative.IS_GET_GREEN_GAIN_FACTOR:
                case UeyeNative.IS_GET_BLUE_GAIN_FACTOR:
                    Log($"SetHWGainFactor(getColor mode=0x{mode:X}) -> 100");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return 100;
                case UeyeNative.IS_GET_DEFAULT_MASTER_GAIN_FACTOR:
                    Log("SetHWGainFactor(getDefaultMaster) -> 100");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return 100;
                case UeyeNative.IS_GET_DEFAULT_RED_GAIN_FACTOR:
                case UeyeNative.IS_GET_DEFAULT_GREEN_GAIN_FACTOR:
                case UeyeNative.IS_GET_DEFAULT_BLUE_GAIN_FACTOR:
                    Log($"SetHWGainFactor(getDefaultColor mode=0x{mode:X}) -> 100");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return 100;
                case UeyeNative.IS_INQUIRE_MASTER_GAIN_FACTOR:
                {
                    var standardizedGain = Math.Clamp(factor, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN);
                    var inquiryFactor = ToGainFactor(standardizedGain, gainMinDb, gainMaxDb);
                    Log($"SetHWGainFactor(inquireMaster standardized={factor}) -> {inquiryFactor}");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return inquiryFactor;
                }
                case UeyeNative.IS_INQUIRE_RED_GAIN_FACTOR:
                case UeyeNative.IS_INQUIRE_GREEN_GAIN_FACTOR:
                case UeyeNative.IS_INQUIRE_BLUE_GAIN_FACTOR:
                    Log($"SetHWGainFactor(inquireColor mode=0x{mode:X}, value={factor}) -> 0");
                    SetLastError(UeyeNative.IS_SUCCESS, "OK");
                    return 0;
                case UeyeNative.IS_SET_MASTER_GAIN_FACTOR:
                    _masterGain = FromGainFactor(factor, gainMinDb, gainMaxDb);
                    if (DahengFrameBridgeClient.TrySetMasterGain(_masterGain, out var gainState))
                    {
                        ApplyBridgeControlStateLocked(gainState);
                    }
                    Log($"SetHWGainFactor(masterFactor={factor}, mappedMasterGain={_masterGain})");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                case UeyeNative.IS_SET_RED_GAIN_FACTOR:
                case UeyeNative.IS_SET_GREEN_GAIN_FACTOR:
                case UeyeNative.IS_SET_BLUE_GAIN_FACTOR:
                    Log($"SetHWGainFactor(colorMode=0x{mode:X}, factor={factor})");
                    return SetLastError(UeyeNative.IS_SUCCESS, "OK");
                default:
                    return SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"HWGainFactor mode {mode} not supported.");
            }
        }
    }

    public static int SetFrameRate(double fps, double* newFps)
    {
        lock (Gate)
        {
            var hasBridgeState = TrySyncControlStateFromBridgeLocked(out var bridgeState);
            ResolveCompatibleFrameRateRange(
                hasBridgeState ? bridgeState : default,
                out var minFrameRate,
                out var maxFrameRate,
                out _);
            var currentFrameRate = _frameRate > 0.0
                ? Math.Clamp(_frameRate, minFrameRate, maxFrameRate)
                : Math.Clamp(UeyeNative.DefaultFps, minFrameRate, maxFrameRate);
            var hasValidRequest = double.IsFinite(fps) && fps > 0.0;
            var requestedFrameRate = currentFrameRate;
            var ignoredOutOfRange = false;
            var normalizedFromRayCiScale = false;
            var normalizedFrameRate = fps;
            var requestEncoding = FrameRateRequestEncoding.Hz;

            if (hasValidRequest)
            {
                normalizedFrameRate = NormalizeFrameRateRequest(
                    fps,
                    minFrameRate,
                    maxFrameRate,
                    out requestEncoding);
                normalizedFromRayCiScale = requestEncoding != FrameRateRequestEncoding.Hz;

                if (normalizedFrameRate < minFrameRate || normalizedFrameRate > maxFrameRate)
                {
                    ignoredOutOfRange = true;
                }
                else
                {
                    requestedFrameRate = normalizedFrameRate;
                }
            }

            if (DahengFrameBridgeClient.TrySetFrameRate(requestedFrameRate, out var updatedState))
            {
                ApplyBridgeControlStateLocked(updatedState);
            }
            else
            {
                _frameRate = requestedFrameRate;
            }

            if (newFps != null)
            {
                *newFps = EncodeFrameRateResponse(_frameRate, requestEncoding);
            }

            Log($"SetFrameRate(requested={fps:F3}, normalized={normalizedFrameRate:F3}, effective={requestedFrameRate:F3}, applied={_frameRate:F3}, returned={(newFps != null ? (*newFps).ToString("F3", CultureInfo.InvariantCulture) : "<null>")}, encoding={requestEncoding}, rayciScale={normalizedFromRayCiScale}, ignoredOutOfRange={ignoredOutOfRange})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetFrameTimeRange(double* min, double* max, double* interval)
    {
        lock (Gate)
        {
            TrySyncControlStateFromBridgeLocked(out var bridgeState);
            ResolveCompatibleFrameRateRange(bridgeState, out var minFps, out var maxFps, out var incFps);
            var currentFps = _frameRate > 0
                ? Math.Clamp(_frameRate, minFps, maxFps)
                : Math.Clamp(UeyeNative.DefaultFps, minFps, maxFps);

            if (min != null)
            {
                *min = 1.0 / maxFps;
            }

            if (max != null)
            {
                *max = 1.0 / minFps;
            }

            if (interval != null)
            {
                var nextFps = Math.Clamp(currentFps + incFps, minFps, maxFps);
                *interval = nextFps > currentFps
                    ? Math.Abs((1.0 / currentFps) - (1.0 / nextFps))
                    : 0.0;
            }

            Log($"GetFrameTimeRange(min={1.0 / maxFps:F6}s, max={1.0 / minFps:F6}s, fpsRange={minFps:F3}..{maxFps:F3}, current={currentFps:F3})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetFramesPerSecond(double* fps)
    {
        if (fps != null)
        {
            lock (Gate)
            {
                TrySyncControlStateFromBridgeLocked(out _);
                *fps = _frameRate;
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetPixelClock(int pixelClock)
    {
        lock (Gate)
        {
            _pixelClock = Math.Clamp(pixelClock, 5, 86);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int PixelClock(uint command, void* parameter)
    {
        lock (Gate)
        {
            switch (command)
            {
                case UeyeNative.IS_PIXELCLOCK_CMD_GET_NUMBER:
                    if (parameter != null) *(uint*)parameter = 1;
                    break;
                case UeyeNative.IS_PIXELCLOCK_CMD_GET_LIST:
                    if (parameter != null) *(uint*)parameter = (uint)_pixelClock;
                    break;
                case UeyeNative.IS_PIXELCLOCK_CMD_GET_RANGE:
                    if (parameter != null)
                    {
                        var range = (uint*)parameter;
                        range[0] = 5;
                        range[1] = 86;
                        range[2] = 1;
                    }
                    break;
                case UeyeNative.IS_PIXELCLOCK_CMD_GET_DEFAULT:
                    if (parameter != null) *(uint*)parameter = UeyeNative.DefaultPixelClock;
                    break;
                case UeyeNative.IS_PIXELCLOCK_CMD_GET:
                    if (parameter != null) *(uint*)parameter = (uint)_pixelClock;
                    break;
                case UeyeNative.IS_PIXELCLOCK_CMD_SET:
                    if (parameter == null) return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Pixel clock input is null.");
                    _pixelClock = Math.Clamp((int)(*(uint*)parameter), 5, 86);
                    *(uint*)parameter = (uint)_pixelClock;
                    break;
                default:
                    return SetLastError(UeyeNative.IS_NOT_SUPPORTED, $"PixelClock command {command} not supported.");
            }
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetExtendedRegister(int index, ushort* value)
    {
        if (value == null)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Extended register output is null.");
        }

        lock (Gate)
        {
            *value = ExtendedRegisters.TryGetValue(index, out var registerValue) ? registerValue : (ushort)0;
        }

        Log($"GetExtendedRegister({index}) -> {*value}");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetExtendedRegister(int index, ushort value)
    {
        lock (Gate)
        {
            ExtendedRegisters[index] = value;
        }

        Log($"SetExtendedRegister({index}) = {value}");
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ReportHasVideoStarted(int* value)
    {
        int started = 0;
        if (value != null)
        {
            lock (Gate)
            {
                started = _captureActive ? 1 : 0;
                *value = started;
            }
        }

        if (_videoStartedTraceCount < 8)
        {
            _videoStartedTraceCount++;
            Log($"ReportHasVideoStarted -> {started}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ReportVideoFinished(int* value)
    {
        int finished;
        if (value != null)
        {
            lock (Gate)
            {
                finished = _captureActive ? UeyeNative.IS_VIDEO_NOT_FINISH : UeyeNative.IS_VIDEO_FINISH;
                *value = finished;
            }
        }
        else
        {
            lock (Gate)
            {
                finished = _captureActive ? UeyeNative.IS_VIDEO_NOT_FINISH : UeyeNative.IS_VIDEO_FINISH;
            }
        }

        if (_videoFinishedTraceCount < 8)
        {
            _videoFinishedTraceCount++;
            Log($"ReportVideoFinished -> {finished}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int InitEvent(nint hEvent, int which)
    {
        if (hEvent == nint.Zero)
        {
            return SetLastError(UeyeNative.IS_INVALID_PARAMETER, "Event handle is null.");
        }

        lock (Gate)
        {
            EventHandles[which] = hEvent;
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int EnableEvent(int which)
    {
        lock (Gate)
        {
            EnabledEvents.Add(which);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int DisableEvent(int which)
    {
        lock (Gate)
        {
            EnabledEvents.Remove(which);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ExitEvent(int which)
    {
        lock (Gate)
        {
            EnabledEvents.Remove(which);
            EventHandles.Remove(which);
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetColorModeOrBits(int request)
    {
        int value;
        int storedColorMode;
        lock (Gate)
        {
            storedColorMode = _colorMode;
            value = request == UeyeNative.IS_GET_BITS_PER_PIXEL
                ? ResolveReportedBitsPerPixelLocked()
                : storedColorMode;
        }

        if (_displayTraceCount < 32)
        {
            _displayTraceCount++;
            Log($"GetColorModeOrBits(request=0x{request:X}) -> {value} (storedColorMode=0x{storedColorMode:X})");
        }

        return value;
    }

    public static int SetColorMode(int mode)
    {
        int normalizedMode;
        lock (Gate)
        {
            normalizedMode = NormalizeReportedColorMode(mode);
            _colorMode = normalizedMode;
        }

        if (_displayTraceCount < 32)
        {
            _displayTraceCount++;
            Log($"SetColorMode(mode=0x{mode:X}) -> storedColorMode=0x{normalizedMode:X}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int GetDisplayMode()
    {
        int mode;
        lock (Gate)
        {
            mode = _displayMode;
        }

        if (_displayTraceCount < 32)
        {
            _displayTraceCount++;
            Log($"GetDisplayMode -> 0x{mode:X}");
        }

        return mode;
    }

    public static int SetDisplayMode(int mode)
    {
        lock (Gate)
        {
            _displayMode = mode;
        }

        if (_displayTraceCount < 32)
        {
            _displayTraceCount++;
            Log($"SetDisplayMode(mode=0x{mode:X})");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int SetTriggerMode(int mode)
    {
        lock (Gate)
        {
            _triggerMode = mode;
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    public static int ZeroCommand(void* parameter, uint parameterSize)
    {
        const uint maxSafeZeroSize = 4096;

        if (parameter == null || parameterSize == 0)
        {
            return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }

        if (parameterSize > maxSafeZeroSize)
        {
            Log($"ZeroCommand skipped oversized buffer size={parameterSize}");
            return SetLastError(UeyeNative.IS_SUCCESS, "OK");
        }

        NativeHelpers.Zero(parameter, parameterSize);
        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    private static string ResolveLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        return Path.Combine(localAppData, "Ultron", "RayCiUeyeBridge", "logs");
    }

    private static void ResetRegisters()
    {
        ExtendedRegisters.Clear();

        foreach (var pair in CapturedCameraProfile.EnumerateStartupWords())
        {
            ExtendedRegisters[pair.Key] = pair.Value;
        }

        // Keep a tiny compatibility shim for legacy uEye callers that probe these synthetic indexes.
        ExtendedRegisters[0x000B] = 0;
        ExtendedRegisters[0x0007] = 3;
        ExtendedRegisters[0x0006] = 24;
        ExtendedRegisters[0x002B] = 4;
        ExtendedRegisters[0x0035] = 8;
        ExtendedRegisters[0x004E] = 4;
        ExtendedRegisters[0x0060] = 0;
        ExtendedRegisters[0x0061] = 0;
        ExtendedRegisters[0x0062] = 0x0498;
        ExtendedRegisters[0x0063] = 0;
        ExtendedRegisters[0x0064] = 0;
    }

    private static void EnsureCaptureThreadLocked()
    {
        if (_captureThread is { IsAlive: true })
        {
            return;
        }

        _captureThreadShouldStop = false;
        _captureThread = new Thread(CaptureWorker)
        {
            IsBackground = true,
            Name = "VirtualUEyeCapture",
        };
        _captureThread.Start();
    }

    private static void CaptureWorker()
    {
        while (!_captureThreadShouldStop)
        {
            int sleepMs;
            lock (Gate)
            {
                sleepMs = Math.Max(5, (int)Math.Round(1000.0 / Math.Max(1.0, _frameRate)));
                if (_captureActive)
                {
                    CaptureIntoSelectedMemoryLocked(updateGeneration: true);
                }
            }

            Thread.Sleep(sleepMs);
        }
    }

    private static void CaptureIntoSelectedMemoryLocked(bool updateGeneration)
    {
        if (_imageQueueInitialized && updateGeneration)
        {
            _activeMemoryId = ResolveCaptureMemoryIdLocked();
        }
        else if (_activeMemoryId == 0 || !Memories.ContainsKey(_activeMemoryId))
        {
            _activeMemoryId = ResolveCaptureMemoryIdLocked();
        }

        if (_activeMemoryId != 0 && Memories.TryGetValue(_activeMemoryId, out var memory))
        {
            PopulateMemoryLocked(memory, updateGeneration);
            if (_imageQueueInitialized && updateGeneration)
            {
                EnqueueReadySequenceMemoryLocked(memory.MemoryId);
            }
        }
    }

    private static void PopulateMemoryLocked(ImageMemory memory, bool updateGeneration)
    {
        if (DahengFrameBridgeClient.TryCopyLatestFrame(memory, out var bridgeFrameId))
        {
            var observedFrameNumber = unchecked((ulong)Math.Max(bridgeFrameId, 1));
            if (_frameNumber == 0)
            {
                _frameNumber = observedFrameNumber;
            }
            else if (updateGeneration)
            {
                // A live capture delivery needs a fresh sequence number even if the upstream source frame is unchanged.
                _frameNumber = Math.Max(_frameNumber + 1, observedFrameNumber);
            }
            else
            {
                _frameNumber = Math.Max(_frameNumber, observedFrameNumber);
            }

            MemoryFrameNumbers[memory.MemoryId] = _frameNumber;
            MemoryImageInfoSnapshots[memory.MemoryId] = CreateImageInfoSnapshotLocked(_frameNumber);

            if (updateGeneration)
            {
                _producedFrameGeneration++;
                SignalFrameReadyEventsLocked();
                Monitor.PulseAll(Gate);
            }

            return;
        }
        var hasPriorFrame = MemoryFrameNumbers.TryGetValue(memory.MemoryId, out var priorFrameNumber) && priorFrameNumber > 0;
        if (!hasPriorFrame && _frameNumber > 0)
        {
            priorFrameNumber = _frameNumber;
            hasPriorFrame = true;
        }

        if (!hasPriorFrame)
        {
            FillNoiseLocked(memory);
            _frameNumber = 1;
            MemoryFrameNumbers[memory.MemoryId] = _frameNumber;
            MemoryImageInfoSnapshots[memory.MemoryId] = CreateImageInfoSnapshotLocked(_frameNumber);
            if (_bridgeMissTraceCount < 64)
            {
                _bridgeMissTraceCount++;
                Log($"bridge frame unavailable with no prior frame; seeded noise once for memoryId={memory.MemoryId}");
            }
        }
        else
        {
            _frameNumber = updateGeneration ? Math.Max(_frameNumber + 1, priorFrameNumber) : Math.Max(_frameNumber, priorFrameNumber);
            MemoryFrameNumbers[memory.MemoryId] = _frameNumber;
            MemoryImageInfoSnapshots[memory.MemoryId] = CreateImageInfoSnapshotLocked(_frameNumber);
            if (_bridgeMissTraceCount < 64)
            {
                _bridgeMissTraceCount++;
                Log($"bridge frame unavailable; reusing prior image buffer for memoryId={memory.MemoryId}, frame={priorFrameNumber}");
            }
        }

        if (updateGeneration)
        {
            _producedFrameGeneration++;
            SignalFrameReadyEventsLocked();
            Monitor.PulseAll(Gate);
        }
    }

    private static int ResolveCaptureMemoryIdLocked()
    {
        if (_imageQueueInitialized)
        {
            var orderedIds = GetOrderedSequenceMemoryIdsLocked();
            if (orderedIds.Length == 0)
            {
                return 0;
            }

            _nextQueueIndex %= orderedIds.Length;
            for (var attempt = 0; attempt < orderedIds.Length; attempt++)
            {
                var candidateId = orderedIds[_nextQueueIndex];
                _nextQueueIndex = (_nextQueueIndex + 1) % orderedIds.Length;
                if (LockedSequenceMemoryIds.Contains(candidateId) || QueuedSequenceMemoryIds.Contains(candidateId))
                {
                    continue;
                }

                return candidateId;
            }

            return 0;
        }

        return Memories.Keys.OrderBy(id => id).FirstOrDefault();
    }

    private static int ResolveNextSequenceMemoryIdLocked()
    {
        var orderedIds = GetOrderedSequenceMemoryIdsLocked();

        if (orderedIds.Length == 0)
        {
            return 0;
        }

        _nextQueueIndex %= orderedIds.Length;
        for (var attempt = 0; attempt < orderedIds.Length; attempt++)
        {
            var candidateId = orderedIds[_nextQueueIndex];
            _nextQueueIndex = (_nextQueueIndex + 1) % orderedIds.Length;
            if (!LockedSequenceMemoryIds.Contains(candidateId) || orderedIds.Length == 1)
            {
                return candidateId;
            }
        }

        return orderedIds[0];
    }

    private static int[] GetOrderedSequenceMemoryIdsLocked()
    {
        var orderedIds = SequenceMemoryIds
            .Where(Memories.ContainsKey)
            .ToArray();
        if (orderedIds.Length == 0)
        {
            orderedIds = Memories.Keys.OrderBy(id => id).ToArray();
        }

        return orderedIds;
    }

    private static void PrimeSequenceQueueLocked()
    {
        if (!_imageQueueInitialized || ReadySequenceMemoryIds.Count > 0)
        {
            return;
        }

        foreach (var memoryId in GetOrderedSequenceMemoryIdsLocked())
        {
            if (!Memories.TryGetValue(memoryId, out var memory) ||
                LockedSequenceMemoryIds.Contains(memoryId) ||
                QueuedSequenceMemoryIds.Contains(memoryId))
            {
                continue;
            }

            _activeMemoryId = memoryId;
            PopulateMemoryLocked(memory, updateGeneration: true);
            EnqueueReadySequenceMemoryLocked(memoryId);
        }
    }

    private static int GetPendingSequenceCountLocked()
    {
        NormalizeReadySequenceQueueLocked();
        if (_imageQueueInitialized)
        {
            return ReadySequenceMemoryIds.Count;
        }

        return GetOrderedSequenceMemoryIdsLocked()
            .Count(memoryId => !LockedSequenceMemoryIds.Contains(memoryId) && MemoryFrameNumbers.TryGetValue(memoryId, out var frameNumber) && frameNumber > 0);
    }

    private static int DiscardPendingSequenceItemsLocked(int requestedCount)
    {
        NormalizeReadySequenceQueueLocked();
        var discardedCount = 0;
        while (discardedCount < requestedCount && ReadySequenceMemoryIds.Count > 0)
        {
            var memoryId = ReadySequenceMemoryIds.Dequeue();
            QueuedSequenceMemoryIds.Remove(memoryId);
            discardedCount++;
        }

        return discardedCount;
    }

    private static void NormalizeReadySequenceQueueLocked()
    {
        if (ReadySequenceMemoryIds.Count == 0)
        {
            return;
        }

        var remainingIds = new Queue<int>();
        var remainingSet = new HashSet<int>();
        while (ReadySequenceMemoryIds.Count > 0)
        {
            var memoryId = ReadySequenceMemoryIds.Dequeue();
            if (!Memories.ContainsKey(memoryId) || !remainingSet.Add(memoryId))
            {
                continue;
            }

            remainingIds.Enqueue(memoryId);
        }

        ReadySequenceMemoryIds.Clear();
        QueuedSequenceMemoryIds.Clear();
        while (remainingIds.Count > 0)
        {
            var memoryId = remainingIds.Dequeue();
            ReadySequenceMemoryIds.Enqueue(memoryId);
            QueuedSequenceMemoryIds.Add(memoryId);
        }
    }

    private static void ClearReadySequenceQueueLocked()
    {
        ReadySequenceMemoryIds.Clear();
        QueuedSequenceMemoryIds.Clear();
    }

    private static void RemoveSequenceMemoryFromQueueLocked(int memoryId)
    {
        if (ReadySequenceMemoryIds.Count == 0 && !QueuedSequenceMemoryIds.Contains(memoryId))
        {
            return;
        }

        var remainingIds = ReadySequenceMemoryIds.Where(id => id != memoryId).ToArray();
        ReadySequenceMemoryIds.Clear();
        QueuedSequenceMemoryIds.Clear();
        foreach (var remainingId in remainingIds)
        {
            ReadySequenceMemoryIds.Enqueue(remainingId);
            QueuedSequenceMemoryIds.Add(remainingId);
        }
    }

    private static bool EnqueueReadySequenceMemoryLocked(int memoryId)
    {
        if (memoryId == 0 ||
            !Memories.ContainsKey(memoryId) ||
            LockedSequenceMemoryIds.Contains(memoryId) ||
            !QueuedSequenceMemoryIds.Add(memoryId))
        {
            return false;
        }

        ReadySequenceMemoryIds.Enqueue(memoryId);
        Monitor.PulseAll(Gate);
        return true;
    }

    private static bool TryDequeueReadySequenceMemoryLocked(out ImageMemory? memory)
    {
        NormalizeReadySequenceQueueLocked();
        while (ReadySequenceMemoryIds.Count > 0)
        {
            var memoryId = ReadySequenceMemoryIds.Dequeue();
            QueuedSequenceMemoryIds.Remove(memoryId);
            if (LockedSequenceMemoryIds.Contains(memoryId))
            {
                continue;
            }

            if (Memories.TryGetValue(memoryId, out var readyMemory))
            {
                memory = readyMemory;
                return true;
            }
        }

        memory = null;
        return false;
    }

    private static bool TryCaptureImmediateSequenceMemoryLocked(out ImageMemory? memory)
    {
        var previousReadyCount = ReadySequenceMemoryIds.Count;
        CaptureIntoSelectedMemoryLocked(updateGeneration: true);
        if (ReadySequenceMemoryIds.Count <= previousReadyCount)
        {
            memory = null;
            return false;
        }

        return TryDequeueReadySequenceMemoryLocked(out memory);
    }

    private static bool TryReuseLatestSequenceMemoryLocked(out ImageMemory? memory)
    {
        if (!_captureActive || _producedFrameGeneration == 0)
        {
            memory = null;
            return false;
        }

        if (TryResolveReusableSequenceMemoryLocked(_currentSequenceMemoryId, out memory) ||
            TryResolveReusableSequenceMemoryLocked(_lastSequenceMemoryId, out memory) ||
            TryResolveReusableSequenceMemoryLocked(_activeMemoryId, out memory))
        {
            return true;
        }

        var fallbackMemory = GetFallbackSequenceMemoryLocked();
        if (fallbackMemory is not null &&
            !LockedSequenceMemoryIds.Contains(fallbackMemory.MemoryId))
        {
            memory = fallbackMemory;
            return true;
        }

        memory = null;
        return false;
    }

    private static bool TryResolveReusableSequenceMemoryLocked(int memoryId, out ImageMemory? memory)
    {
        if (memoryId != 0 &&
            !LockedSequenceMemoryIds.Contains(memoryId) &&
            Memories.TryGetValue(memoryId, out var resolvedMemory))
        {
            memory = resolvedMemory;
            return true;
        }

        memory = null;
        return false;
    }

    private static int CompleteSequenceWaitLocked(ImageMemory memoryState, uint timeoutMs, byte** memory, int* memoryId)
    {
        LockedSequenceMemoryIds.Add(memoryState.MemoryId);
        _lastSequenceMemoryId = _currentSequenceMemoryId == 0 ? memoryState.MemoryId : _currentSequenceMemoryId;
        _currentSequenceMemoryId = memoryState.MemoryId;
        _activeMemoryId = memoryState.MemoryId;

        var deliveredFrame = MemoryFrameNumbers.TryGetValue(memoryState.MemoryId, out var frameNumber)
            ? frameNumber
            : _frameNumber;
        _lastDeliveredFrameGeneration = Math.Max(_lastDeliveredFrameGeneration, _producedFrameGeneration);

        if (memory != null) *memory = (byte*)memoryState.Pointer;
        if (memoryId != null) *memoryId = memoryState.MemoryId;

        if (_waitTraceCount < 256)
        {
            _waitTraceCount++;
            Log($"WaitForNextImage(timeout={timeoutMs}) -> memoryId={memoryState.MemoryId}, frame={deliveredFrame}, generation={_producedFrameGeneration}, {DescribeSequenceStateLocked()}");
        }

        return SetLastError(UeyeNative.IS_SUCCESS, "OK");
    }

    private static int ResolveBitsPerPixelLocked()
    {
        var bridgeBits = ResolveBridgeSignalBitsPerPixelLocked();
        if (bridgeBits > 0)
        {
            return bridgeBits;
        }

        var normalizedMode = _colorMode & UeyeNative.IS_CM_MODE_MASK;
        return normalizedMode switch
        {
            UeyeNative.IS_CM_MONO8 => 8,
            UeyeNative.IS_CM_MONO10 => 10,
            UeyeNative.IS_CM_SENSOR_RAW10 => 10,
            UeyeNative.IS_CM_MONO12 => 12,
            UeyeNative.IS_CM_MONO16 => 16,
            _ when _activeMemoryId != 0 && Memories.TryGetValue(_activeMemoryId, out var memory) && memory.BitsPerPixel > 0 => memory.BitsPerPixel,
            _ => UeyeNative.DefaultBitsPerPixel,
        };
    }

    private static int NormalizeReportedColorMode(int requestedMode)
    {
        var bridgeColorMode = ResolveBridgeReportedColorModeLocked();
        if (bridgeColorMode != 0)
        {
            return bridgeColorMode;
        }

        var normalizedMode = requestedMode & UeyeNative.IS_CM_MODE_MASK;
        return normalizedMode switch
        {
            UeyeNative.IS_CM_MONO8 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            UeyeNative.IS_CM_MONO10 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            UeyeNative.IS_CM_SENSOR_RAW10 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            UeyeNative.IS_CM_MONO12 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            UeyeNative.IS_CM_MONO16 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            _ => requestedMode,
        };
    }

    private static int ResolveReportedBitsPerPixelLocked()
    {
        var resolvedBits = ResolveBitsPerPixelLocked();
        return resolvedBits switch
        {
            > 0 and < 16 => resolvedBits,
            _ => UeyeNative.DefaultSignalBitsPerPixel,
        };
    }

    private static int NormalizeContainerBitsPerPixel(int requestedBitsPerPixel)
    {
        if (requestedBitsPerPixel <= 0)
        {
            return requestedBitsPerPixel;
        }

        if (requestedBitsPerPixel == 16)
        {
            return 16;
        }

        // RayCi 2022's 10bpp path expects a Y16-style transport container even when
        // the app probes with 8/10/12-bit requests during compatibility fallback.
        return UeyeNative.GetBytesPerPixel(requestedBitsPerPixel) >= 1 ? 16 : requestedBitsPerPixel;
    }

    private static int ResolveBridgeSignalBitsPerPixelLocked()
    {
        return _bridgeCapturePixelFormat switch
        {
            FrameBridgeProtocol.CapturePixelFormatMono8 => 8,
            FrameBridgeProtocol.CapturePixelFormatMono10 => 10,
            FrameBridgeProtocol.CapturePixelFormatMono12 => 12,
            FrameBridgeProtocol.CapturePixelFormatMono14 => 14,
            FrameBridgeProtocol.CapturePixelFormatMono16 => 16,
            _ => 0
        };
    }

    private static int ResolveBridgeReportedColorModeLocked()
    {
        return _bridgeCapturePixelFormat switch
        {
            FrameBridgeProtocol.CapturePixelFormatMono8 => UeyeNative.IS_CM_MONO8,
            FrameBridgeProtocol.CapturePixelFormatMono10 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            FrameBridgeProtocol.CapturePixelFormatMono12 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            FrameBridgeProtocol.CapturePixelFormatMono14 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            FrameBridgeProtocol.CapturePixelFormatMono16 => UeyeNative.IS_CM_MONO10_COMPAT_Y16,
            _ => 0
        };
    }

    public static int GetMemoryImageCount()
    {
        lock (Gate)
        {
            var sequenceCount = SequenceMemoryIds.Count(id => Memories.ContainsKey(id));
            if (sequenceCount > 0)
            {
                return sequenceCount;
            }

            return Memories.Count;
        }
    }

    private static ImageMemory? GetFallbackSequenceMemoryLocked()
    {
        var memoryId = _activeMemoryId != 0 ? _activeMemoryId : ResolveNextSequenceMemoryIdLocked();
        return memoryId != 0 && Memories.TryGetValue(memoryId, out var memory)
            ? memory
            : null;
    }

    private static void FillNoiseLocked(ImageMemory memory)
    {
        var span = new Span<byte>((void*)memory.Pointer, memory.SizeBytes);
        if (UeyeNative.GetBytesPerPixel(memory.BitsPerPixel) >= 2)
        {
            var pixels = new Span<ushort>((void*)memory.Pointer, memory.SizeBytes / sizeof(ushort));
            var signalBits = memory.BitsPerPixel is > 8 and < 16
                ? memory.BitsPerPixel
                : UeyeNative.DefaultSignalBitsPerPixel;
            var maxValueExclusive = 1 << Math.Clamp(signalBits, 1, 15);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (ushort)Random.Shared.Next(0, maxValueExclusive);
            }

            return;
        }

        Random.Shared.NextBytes(span);
    }

    private static IS_RECT NormalizeAoi(int x, int y, int width, int height)
    {
        var clampedWidth = Math.Clamp(width, 1, UeyeNative.DefaultWidth);
        var clampedHeight = Math.Clamp(height, 1, UeyeNative.DefaultHeight);
        return new IS_RECT
        {
            s32Width = clampedWidth,
            s32Height = clampedHeight,
            s32X = Math.Clamp(x, 0, UeyeNative.DefaultWidth - clampedWidth),
            s32Y = Math.Clamp(y, 0, UeyeNative.DefaultHeight - clampedHeight),
        };
    }

    private static bool TryHandleSamplingQueryLocked(int mode, bool isSubSampling, out int result)
    {
        var currentMode = isSubSampling
            ? _subSamplingMode
            : _binningMode;
        var supportedModes = isSubSampling
            ? UeyeNative.IS_SUBSAMPLING_MASK_HORIZONTAL | UeyeNative.IS_SUBSAMPLING_MASK_VERTICAL
            : UeyeNative.IS_BINNING_MASK_HORIZONTAL | UeyeNative.IS_BINNING_MASK_VERTICAL;
        var samplingType = isSubSampling
            ? UeyeNative.IS_SUBSAMPLING_MONO
            : UeyeNative.IS_BINNING_MONO;

        if (mode == (isSubSampling ? UeyeNative.IS_GET_SUBSAMPLING : UeyeNative.IS_GET_BINNING))
        {
            result = currentMode;
            SetLastError(UeyeNative.IS_SUCCESS, "OK");
            return true;
        }

        if (mode == (isSubSampling ? UeyeNative.IS_GET_SUPPORTED_SUBSAMPLING : UeyeNative.IS_GET_SUPPORTED_BINNING))
        {
            result = supportedModes;
            SetLastError(UeyeNative.IS_SUCCESS, "OK");
            return true;
        }

        if (mode == (isSubSampling ? UeyeNative.IS_GET_SUBSAMPLING_TYPE : UeyeNative.IS_GET_BINNING_TYPE))
        {
            result = samplingType;
            SetLastError(UeyeNative.IS_SUCCESS, "OK");
            return true;
        }

        if (mode == (isSubSampling ? UeyeNative.IS_GET_SUBSAMPLING_FACTOR_HORIZONTAL : UeyeNative.IS_GET_BINNING_FACTOR_HORIZONTAL))
        {
            result = GetSamplingFactor(currentMode, isSubSampling, horizontal: true);
            SetLastError(UeyeNative.IS_SUCCESS, "OK");
            return true;
        }

        if (mode == (isSubSampling ? UeyeNative.IS_GET_SUBSAMPLING_FACTOR_VERTICAL : UeyeNative.IS_GET_BINNING_FACTOR_VERTICAL))
        {
            result = GetSamplingFactor(currentMode, isSubSampling, horizontal: false);
            SetLastError(UeyeNative.IS_SUCCESS, "OK");
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryResolveSamplingMode(int mode, bool isSubSampling, out int normalizedMode, out int factorX, out int factorY)
    {
        normalizedMode = 0;
        factorX = 1;
        factorY = 1;

        if (mode == (isSubSampling ? UeyeNative.IS_SUBSAMPLING_DISABLE : UeyeNative.IS_BINNING_DISABLE))
        {
            return true;
        }

        if ((mode & unchecked((int)0x8000)) != 0)
        {
            return false;
        }

        factorX = ResolveSamplingAxisFactor(mode, isSubSampling, horizontal: true, out var horizontalMask);
        factorY = ResolveSamplingAxisFactor(mode, isSubSampling, horizontal: false, out var verticalMask);

        if (factorX == 0 || factorY == 0)
        {
            return false;
        }

        normalizedMode = horizontalMask | verticalMask;
        return true;
    }

    private static int ResolveSamplingAxisFactor(int mode, bool isSubSampling, bool horizontal, out int normalizedMask)
    {
        normalizedMask = 0;
        var factorBits = horizontal
            ? (isSubSampling
                ? new (int Factor, int Flag)[]
                {
                    (2, UeyeNative.IS_SUBSAMPLING_2X_HORIZONTAL),
                    (3, UeyeNative.IS_SUBSAMPLING_3X_HORIZONTAL),
                    (4, UeyeNative.IS_SUBSAMPLING_4X_HORIZONTAL),
                    (5, UeyeNative.IS_SUBSAMPLING_5X_HORIZONTAL),
                    (6, UeyeNative.IS_SUBSAMPLING_6X_HORIZONTAL),
                    (8, UeyeNative.IS_SUBSAMPLING_8X_HORIZONTAL),
                    (16, UeyeNative.IS_SUBSAMPLING_16X_HORIZONTAL),
                }
                : new (int Factor, int Flag)[]
                {
                    (2, UeyeNative.IS_BINNING_2X_HORIZONTAL),
                    (3, UeyeNative.IS_BINNING_3X_HORIZONTAL),
                    (4, UeyeNative.IS_BINNING_4X_HORIZONTAL),
                    (5, UeyeNative.IS_BINNING_5X_HORIZONTAL),
                    (6, UeyeNative.IS_BINNING_6X_HORIZONTAL),
                    (8, UeyeNative.IS_BINNING_8X_HORIZONTAL),
                    (16, UeyeNative.IS_BINNING_16X_HORIZONTAL),
                })
            : (isSubSampling
                ? new (int Factor, int Flag)[]
                {
                    (2, UeyeNative.IS_SUBSAMPLING_2X_VERTICAL),
                    (3, UeyeNative.IS_SUBSAMPLING_3X_VERTICAL),
                    (4, UeyeNative.IS_SUBSAMPLING_4X_VERTICAL),
                    (5, UeyeNative.IS_SUBSAMPLING_5X_VERTICAL),
                    (6, UeyeNative.IS_SUBSAMPLING_6X_VERTICAL),
                    (8, UeyeNative.IS_SUBSAMPLING_8X_VERTICAL),
                    (16, UeyeNative.IS_SUBSAMPLING_16X_VERTICAL),
                }
                : new (int Factor, int Flag)[]
                {
                    (2, UeyeNative.IS_BINNING_2X_VERTICAL),
                    (3, UeyeNative.IS_BINNING_3X_VERTICAL),
                    (4, UeyeNative.IS_BINNING_4X_VERTICAL),
                    (5, UeyeNative.IS_BINNING_5X_VERTICAL),
                    (6, UeyeNative.IS_BINNING_6X_VERTICAL),
                    (8, UeyeNative.IS_BINNING_8X_VERTICAL),
                    (16, UeyeNative.IS_BINNING_16X_VERTICAL),
                });

        var factor = 1;
        foreach (var (candidateFactor, candidateFlag) in factorBits)
        {
            if ((mode & candidateFlag) == 0)
            {
                continue;
            }

            if (normalizedMask != 0)
            {
                normalizedMask = 0;
                return 0;
            }

            normalizedMask = candidateFlag;
            factor = candidateFactor;
        }

        return factor;
    }

    private static int GetSamplingFactor(int mode, bool isSubSampling, bool horizontal)
    {
        return ResolveSamplingAxisFactor(mode, isSubSampling, horizontal, out _) switch
        {
            > 0 => ResolveSamplingAxisFactor(mode, isSubSampling, horizontal, out _),
            _ => 1
        };
    }

    private static (int FactorX, int FactorY) GetRequestedSamplingFactorsLocked()
    {
        if (_subSamplingMode != UeyeNative.IS_SUBSAMPLING_DISABLE)
        {
            return (
                GetSamplingFactor(_subSamplingMode, isSubSampling: true, horizontal: true),
                GetSamplingFactor(_subSamplingMode, isSubSampling: true, horizontal: false));
        }

        return (
            GetSamplingFactor(_binningMode, isSubSampling: false, horizontal: true),
            GetSamplingFactor(_binningMode, isSubSampling: false, horizontal: false));
    }

    private static bool TryApplyGeometryToBridgeLocked(string source)
    {
        var bridgeSensorWidth = Math.Max(1, _bridgeSensorWidth);
        var bridgeSensorHeight = Math.Max(1, _bridgeSensorHeight);
        var requestedWidth = ScaleLocalLengthToBridge(_aoi.s32Width, UeyeNative.DefaultWidth, bridgeSensorWidth);
        var requestedHeight = ScaleLocalLengthToBridge(_aoi.s32Height, UeyeNative.DefaultHeight, bridgeSensorHeight);
        var requestedOffsetX = ScaleLocalOffsetToBridge(_aoi.s32X, _aoi.s32Width, UeyeNative.DefaultWidth, bridgeSensorWidth);
        var requestedOffsetY = ScaleLocalOffsetToBridge(_aoi.s32Y, _aoi.s32Height, UeyeNative.DefaultHeight, bridgeSensorHeight);
        var (factorX, factorY) = GetRequestedSamplingFactorsLocked();

        if (!DahengFrameBridgeClient.TrySetGeometry(requestedWidth, requestedHeight, requestedOffsetX, requestedOffsetY, factorX, factorY, out var state))
        {
            Log($"{source} geometry bridge update pending local={_aoi.s32Width}x{_aoi.s32Height}@{_aoi.s32X},{_aoi.s32Y} bridge={requestedWidth}x{requestedHeight}@{requestedOffsetX},{requestedOffsetY} sampling={factorX}x{factorY}");
            return false;
        }

        ApplyBridgeControlStateLocked(state);
        Log($"{source} geometry bridged local={_aoi.s32Width}x{_aoi.s32Height}@{_aoi.s32X},{_aoi.s32Y} bridge={requestedWidth}x{requestedHeight}@{requestedOffsetX},{requestedOffsetY} sampling={factorX}x{factorY}");
        return true;
    }

    private static int ScaleLocalLengthToBridge(int localLength, int localMax, int bridgeMax)
    {
        if (localLength >= localMax)
        {
            return bridgeMax;
        }

        return Math.Clamp((int)Math.Round(localLength * (bridgeMax / (double)localMax)), 1, bridgeMax);
    }

    private static int ScaleLocalOffsetToBridge(int localOffset, int localLength, int localMax, int bridgeMax)
    {
        if (localOffset <= 0 || localLength >= localMax)
        {
            return 0;
        }

        var bridgeLength = ScaleLocalLengthToBridge(localLength, localMax, bridgeMax);
        var localSpan = Math.Max(1, localMax - localLength);
        var bridgeSpan = Math.Max(0, bridgeMax - bridgeLength);
        return Math.Clamp((int)Math.Round(localOffset * (bridgeSpan / (double)localSpan)), 0, bridgeSpan);
    }

    private static bool TrySyncControlStateFromBridgeLocked(out BridgeControlState state)
    {
        if (!DahengFrameBridgeClient.TryGetControlState(out state))
        {
            return false;
        }

        ApplyBridgeControlStateLocked(state);
        return true;
    }

    private static void ApplyBridgeControlStateLocked(BridgeControlState state)
    {
        if (state.ExposureMs > 0)
        {
            _exposureMs = state.ExposureMs;
        }

        if (state.FrameRateHz > 0)
        {
            _frameRate = Math.Clamp(state.FrameRateHz, 1.0, 60.0);
        }

        _autoExposureEnabled = state.AutoExposureEnabled;
        _autoGainEnabled = state.AutoGainEnabled;
        _masterGain = Math.Clamp(state.MasterGain, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN);
        _gainBoostEnabled = state.GainBoostSupported && state.GainBoostEnabled;
        _blackLevel = Math.Clamp(state.BlackLevel, 0, 255);
        if (state.CapturePixelFormat > 0)
        {
            _bridgeCapturePixelFormat = state.CapturePixelFormat;
            _colorMode = ResolveBridgeReportedColorModeLocked();
        }

        if (state.Width > 0)
        {
            _bridgeSensorWidth = Math.Max(_bridgeSensorWidth, state.Width);
        }

        if (state.Height > 0)
        {
            _bridgeSensorHeight = Math.Max(_bridgeSensorHeight, state.Height);
        }

        if (_binningMode == UeyeNative.IS_BINNING_DISABLE &&
            _subSamplingMode == UeyeNative.IS_SUBSAMPLING_DISABLE &&
            (state.BinningX > 1 || state.BinningY > 1))
        {
            _binningMode = ComposeSamplingMode(state.BinningX, state.BinningY, isSubSampling: false);
        }

        SetCapturedExposureRegisterLocked(_exposureMs);
        SetCapturedGainRegisterLocked(state.GainDb);
    }

    private static void ResolveCompatibleFrameRateRange(
        BridgeControlState state,
        out double minFrameRateHz,
        out double maxFrameRateHz,
        out double incrementFrameRateHz)
    {
        minFrameRateHz = state.FrameRateMinHz > 0.0 ? state.FrameRateMinHz : CompatibleFrameRateMinHz;
        maxFrameRateHz = state.FrameRateMaxHz >= minFrameRateHz ? state.FrameRateMaxHz : CompatibleFrameRateMaxHz;
        incrementFrameRateHz = state.FrameRateIncHz > 0.0 ? state.FrameRateIncHz : CompatibleFrameRateIncHz;

        if (maxFrameRateHz <= 0.0 || maxFrameRateHz < minFrameRateHz)
        {
            minFrameRateHz = CompatibleFrameRateMinHz;
            maxFrameRateHz = CompatibleFrameRateMaxHz;
            incrementFrameRateHz = CompatibleFrameRateIncHz;
            return;
        }

        minFrameRateHz = Math.Clamp(minFrameRateHz, CompatibleFrameRateMinHz, CompatibleFrameRateMaxHz);
        maxFrameRateHz = Math.Clamp(maxFrameRateHz, minFrameRateHz, CompatibleFrameRateMaxHz);
        incrementFrameRateHz = Math.Clamp(incrementFrameRateHz, 0.001, CompatibleFrameRateIncHz);
    }

    private static double NormalizeFrameRateRequest(
        double requestedFrameRate,
        double minFrameRateHz,
        double maxFrameRateHz,
        out FrameRateRequestEncoding requestEncoding)
    {
        requestEncoding = FrameRateRequestEncoding.Hz;

        if (!double.IsFinite(requestedFrameRate) || requestedFrameRate <= 0.0)
        {
            return requestedFrameRate;
        }

        if (requestedFrameRate >= minFrameRateHz && requestedFrameRate <= maxFrameRateHz)
        {
            return requestedFrameRate;
        }

        // RayCi's Control tab sends frame-rate slider values scaled by 1/1000
        // (for example 0.030 for 30 fps), while the uEye API expects Hz.
        if (requestedFrameRate < minFrameRateHz)
        {
            var scaledFrameRate = requestedFrameRate * 1000.0;
            if (scaledFrameRate >= minFrameRateHz && scaledFrameRate <= maxFrameRateHz)
            {
                requestEncoding = FrameRateRequestEncoding.RayCiMilliHz;
                return scaledFrameRate;
            }

            var frameIntervalFrameRate = 1.0 / requestedFrameRate;
            if (frameIntervalFrameRate >= minFrameRateHz && frameIntervalFrameRate <= maxFrameRateHz)
            {
                requestEncoding = FrameRateRequestEncoding.FrameIntervalSeconds;
                return frameIntervalFrameRate;
            }
        }

        return requestedFrameRate;
    }

    private static double EncodeFrameRateResponse(double frameRateHz, FrameRateRequestEncoding requestEncoding)
    {
        return requestEncoding switch
        {
            FrameRateRequestEncoding.RayCiMilliHz => frameRateHz / 1000.0,
            FrameRateRequestEncoding.FrameIntervalSeconds => frameRateHz > 0.0 ? 1.0 / frameRateHz : 0.0,
            _ => frameRateHz,
        };
    }

    private static int ComposeSamplingMode(int factorX, int factorY, bool isSubSampling)
    {
        return ComposeSamplingAxisMode(Math.Max(1, factorX), isSubSampling, horizontal: true) |
               ComposeSamplingAxisMode(Math.Max(1, factorY), isSubSampling, horizontal: false);
    }

    private static int ComposeSamplingAxisMode(int factor, bool isSubSampling, bool horizontal)
    {
        return (factor, isSubSampling, horizontal) switch
        {
            (2, true, true) => UeyeNative.IS_SUBSAMPLING_2X_HORIZONTAL,
            (3, true, true) => UeyeNative.IS_SUBSAMPLING_3X_HORIZONTAL,
            (4, true, true) => UeyeNative.IS_SUBSAMPLING_4X_HORIZONTAL,
            (5, true, true) => UeyeNative.IS_SUBSAMPLING_5X_HORIZONTAL,
            (6, true, true) => UeyeNative.IS_SUBSAMPLING_6X_HORIZONTAL,
            (8, true, true) => UeyeNative.IS_SUBSAMPLING_8X_HORIZONTAL,
            (16, true, true) => UeyeNative.IS_SUBSAMPLING_16X_HORIZONTAL,
            (2, true, false) => UeyeNative.IS_SUBSAMPLING_2X_VERTICAL,
            (3, true, false) => UeyeNative.IS_SUBSAMPLING_3X_VERTICAL,
            (4, true, false) => UeyeNative.IS_SUBSAMPLING_4X_VERTICAL,
            (5, true, false) => UeyeNative.IS_SUBSAMPLING_5X_VERTICAL,
            (6, true, false) => UeyeNative.IS_SUBSAMPLING_6X_VERTICAL,
            (8, true, false) => UeyeNative.IS_SUBSAMPLING_8X_VERTICAL,
            (16, true, false) => UeyeNative.IS_SUBSAMPLING_16X_VERTICAL,
            (2, false, true) => UeyeNative.IS_BINNING_2X_HORIZONTAL,
            (3, false, true) => UeyeNative.IS_BINNING_3X_HORIZONTAL,
            (4, false, true) => UeyeNative.IS_BINNING_4X_HORIZONTAL,
            (5, false, true) => UeyeNative.IS_BINNING_5X_HORIZONTAL,
            (6, false, true) => UeyeNative.IS_BINNING_6X_HORIZONTAL,
            (8, false, true) => UeyeNative.IS_BINNING_8X_HORIZONTAL,
            (16, false, true) => UeyeNative.IS_BINNING_16X_HORIZONTAL,
            (2, false, false) => UeyeNative.IS_BINNING_2X_VERTICAL,
            (3, false, false) => UeyeNative.IS_BINNING_3X_VERTICAL,
            (4, false, false) => UeyeNative.IS_BINNING_4X_VERTICAL,
            (5, false, false) => UeyeNative.IS_BINNING_5X_VERTICAL,
            (6, false, false) => UeyeNative.IS_BINNING_6X_VERTICAL,
            (8, false, false) => UeyeNative.IS_BINNING_8X_VERTICAL,
            (16, false, false) => UeyeNative.IS_BINNING_16X_VERTICAL,
            _ => 0,
        };
    }

    private static int ToGainFactor(int masterGain, double minGainDb, double maxGainDb)
    {
        var gainDb = ToGainDbFromMasterGain(masterGain, minGainDb, maxGainDb);
        return GainDbToFactor(gainDb);
    }

    private static int FromGainFactor(int factor, double minGainDb, double maxGainDb)
    {
        if (factor <= 0)
        {
            return UeyeNative.IS_MIN_GAIN;
        }

        if (factor <= RayCiGainStepsDb.Length)
        {
            var gainDb = RayCiGainStepsDb[Math.Clamp(factor - 1, 0, RayCiGainStepsDb.Length - 1)];
            return FromGainDb(gainDb, minGainDb, maxGainDb);
        }

        // Real uEye gain factors are multiplicative values where 100 means 1.0x
        // (0 dB), 158 means ~4 dB, 251 means ~8 dB, etc. Keep the small RayCi
        // compatibility indices above, but do not reinterpret 100 as "100% gain".
        if (factor < 100)
        {
            return Math.Clamp(factor, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN);
        }

        var mappedGainDb = FactorToGainDb(factor);
        return FromGainDb(mappedGainDb, minGainDb, maxGainDb);
    }

    private static double ToGainDbFromMasterGain(int masterGain, double minGainDb, double maxGainDb)
    {
        if (!(maxGainDb > minGainDb))
        {
            return minGainDb;
        }

        var normalized = Math.Clamp(masterGain, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN) / 100.0;
        return minGainDb + (normalized * (maxGainDb - minGainDb));
    }

    private static int FromGainDb(double gainDb, double minGainDb, double maxGainDb)
    {
        if (!(maxGainDb > minGainDb))
        {
            return UeyeNative.IS_MIN_GAIN;
        }

        var clampedGainDb = Math.Clamp(gainDb, minGainDb, maxGainDb);
        var normalized = (clampedGainDb - minGainDb) / (maxGainDb - minGainDb);
        return (int)Math.Round(Math.Clamp(normalized * 100.0, UeyeNative.IS_MIN_GAIN, UeyeNative.IS_MAX_GAIN));
    }

    private static int GainDbToFactor(double gainDb)
        => (int)Math.Clamp(Math.Round(100.0 * Math.Pow(10.0, gainDb / 20.0)), 100.0, short.MaxValue);

    private static double FactorToGainDb(int factor)
    {
        if (factor <= 0)
        {
            return 0.0;
        }

        return 20.0 * Math.Log10(factor / 100.0);
    }

    private static void SetCapturedExposureRegisterLocked(double exposureMs)
    {
        var exposureUs = (uint)Math.Clamp(Math.Round(exposureMs * 1000.0), 0.0, uint.MaxValue);
        SetCapturedRegister32Locked(0x0458, exposureUs);
    }

    private static void SetCapturedGainRegisterLocked(double gainDb)
    {
        var gainCode = (uint)Math.Clamp(Math.Round(8.0 * Math.Pow(10.0, gainDb / 20.0)), 0.0, ushort.MaxValue);
        SetCapturedRegister32Locked(0x0704, gainCode);
    }

    private static void SetCapturedRegister32Locked(int index, uint value)
    {
        ExtendedRegisters[index] = unchecked((ushort)(value & 0xffffu));
        ExtendedRegisters[index + sizeof(ushort)] = unchecked((ushort)(value >> 16));
    }

    private static void SignalFrameReadyEventsLocked()
    {
        SignalEventLocked(UeyeNative.IS_SET_EVENT_FRAME);
    }

    private static void SignalEventLocked(int which)
    {
        if (!EnabledEvents.Contains(which))
        {
            return;
        }

        if (EventHandles.TryGetValue(which, out var handle) && handle != nint.Zero)
        {
            _ = SetEvent(handle);

            if (_eventSignalTraceCount < 32)
            {
                _eventSignalTraceCount++;
                Log($"SignalEvent({DescribeEvent(which)})");
            }
        }
    }

    private static string DescribeEvent(int which) => which switch
    {
        UeyeNative.IS_SET_EVENT_FRAME => "IS_SET_EVENT_FRAME",
        UeyeNative.IS_SET_EVENT_CAPTURE_STATUS => "IS_SET_EVENT_CAPTURE_STATUS",
        UeyeNative.IS_SET_EVENT_DEVICE_RECONNECTED => "IS_SET_EVENT_DEVICE_RECONNECTED",
        UeyeNative.IS_SET_EVENT_CONNECTIONSPEED_CHANGED => "IS_SET_EVENT_CONNECTIONSPEED_CHANGED",
        UeyeNative.IS_SET_EVENT_REMOVE => "IS_SET_EVENT_REMOVE",
        UeyeNative.IS_SET_EVENT_REMOVAL => "IS_SET_EVENT_REMOVAL",
        UeyeNative.IS_SET_EVENT_NEW_DEVICE => "IS_SET_EVENT_NEW_DEVICE",
        UeyeNative.IS_SET_EVENT_STATUS_CHANGED => "IS_SET_EVENT_STATUS_CHANGED",
        _ => $"event#{which}",
    };

    private static string DescribeSequenceStateLocked()
        => $"ready={ReadySequenceMemoryIds.Count}, queued={QueuedSequenceMemoryIds.Count}, locked={LockedSequenceMemoryIds.Count}, active={_activeMemoryId}, current={_currentSequenceMemoryId}, last={_lastSequenceMemoryId}, produced={_producedFrameGeneration}, delivered={_lastDeliveredFrameGeneration}, colorMode=0x{_colorMode:X}, bpp={ResolveBitsPerPixelLocked()}";

    private static ImageInfoSnapshot CreateImageInfoSnapshotLocked(ulong frameNumber)
    {
        var now = DateTime.Now;
        var elapsedUs = (DateTime.UtcNow - _deviceEpochUtc).TotalMilliseconds * 1000.0;
        var deviceTimestampUs = unchecked((ulong)Math.Max(1.0, elapsedUs));
        var hostProcessTimeUs = (uint)Math.Clamp(Math.Round(_exposureMs * 1000.0), 0.0, uint.MaxValue);
        var ioStatus = _captureActive ? 1u : 0u;
        var flags = 1u;
        var aoiIndex = (ushort)Math.Clamp(_activeMemoryId, 0, ushort.MaxValue);
        var aoiCycle = (ushort)(_producedFrameGeneration & 0xFFFF);
        var sequencerIndex = (byte)Math.Clamp(_currentSequenceMemoryId == 0 ? 0 : _currentSequenceMemoryId - 1, 0, byte.MaxValue);
        return new ImageInfoSnapshot(deviceTimestampUs, now, frameNumber, hostProcessTimeUs, ioStatus, flags, aoiIndex, aoiCycle, sequencerIndex);
    }

    private static void WriteByte(byte* buffer, int availableSize, int offset, byte value)
    {
        if (offset + sizeof(byte) <= availableSize)
        {
            buffer[offset] = value;
        }
    }

    private static void WriteUInt16(byte* buffer, int availableSize, int offset, ushort value)
    {
        if (offset + sizeof(ushort) <= availableSize)
        {
            *(ushort*)(buffer + offset) = value;
        }
    }

    private static void WriteUInt32(byte* buffer, int availableSize, int offset, uint value)
    {
        if (offset + sizeof(uint) <= availableSize)
        {
            *(uint*)(buffer + offset) = value;
        }
    }

    private static void WriteUInt64(byte* buffer, int availableSize, int offset, ulong value)
    {
        if (offset + sizeof(ulong) <= availableSize)
        {
            *(ulong*)(buffer + offset) = value;
        }
    }

    private static int IsGetDllVersion() => unchecked((int)0x04610000);
}
