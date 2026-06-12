using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using GxIAPINET;
using RayCiBridge;

var app = new DahengFrameServerApp();
return app.Run(args);

internal sealed class DahengFrameServerApp
{
    private static readonly string LogDirectory = ResolveLogDirectory();
    private static readonly string LogPath = Path.Combine(LogDirectory, "daheng_frame_server.log");
    private const double DefaultStartupFrameRate = 15.0;
    private const double DefaultStartupExposureUs = 30000.0;
    private const double DefaultStartupGainDb = 0.0;
    private const int DefaultAutoSimulationDelayMs = 2500;
    private const int BeamMicBlackLevelMin = 0;
    private const int BeamMicBlackLevelMax = 255;
    private const double BeamMicBlackLevelDeviceMax = 510.0;
    private const bool DefaultReverseX = true;
    private const bool DefaultReverseY = false;
    private const int FrameDequeueTimeoutMs = 500;
    private const double SimulationExposureMinUs = 100.0;
    private const double SimulationExposureMaxUs = 1_000_000.0;
    private const double SimulationExposureIncUs = 100.0;
    private const double SimulationGainMinDb = 0.0;
    private const double SimulationGainMaxDb = 24.0;
    private const double SimulationGainIncDb = 0.1;
    private const double SimulationFrameRateMinHz = 1.0;
    private const double SimulationFrameRateMaxHz = 60.0;
    private const double SimulationFrameRateIncHz = 0.1;
    private const int SimulationSamplingMax = 4;
    private const int SimulationBlackLevelDefault = 49;
    private const int CapturedFrameWidth = 1280;
    private const int CapturedFrameHeight = 1024;
    private readonly CancellationTokenSource _shutdown = new();
    private IGXFactory? _factory;
    private IGXImageFormatConvert? _formatConvert;
    private IntPtr _convertBuffer = IntPtr.Zero;
    private ulong _convertBufferSize;
    private long _publishedFrameId;
    private int _lastAppliedRequestSequence;
    private bool _loggedNativeSamplePreview;
    private readonly SimulationPattern _simulationPattern = GetSimulationPattern();
    private ushort[] _simulationFrameBuffer = Array.Empty<ushort>();
    private double[] _simulationXProfile = Array.Empty<double>();
    private double[] _simulationYProfile = Array.Empty<double>();

    private static string ResolveLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        return Path.Combine(localAppData, "Ultron", "RayCiUeyeBridge", "logs");
    }

    public int Run(string[] args)
    {
        Directory.CreateDirectory(LogDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _shutdown.Cancel();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _shutdown.Cancel();
        };

        if (args.Any(arg => string.Equals(arg, "--probe", StringComparison.OrdinalIgnoreCase)))
        {
            return RunProbe();
        }

        if (args.Any(arg => string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase)))
        {
            return RunListDevices();
        }

        if (args.Any(arg => string.Equals(arg, "--probe-frame", StringComparison.OrdinalIgnoreCase)))
        {
            return RunProbeFrame();
        }

        using var instanceMutex = new Mutex(true, FrameBridgeProtocol.ServerInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Log("helper already running");
            return 0;
        }

        using var mmf = MemoryMappedFile.CreateOrOpen(FrameBridgeProtocol.MapName, FrameBridgeProtocol.MapSize, MemoryMappedFileAccess.ReadWrite);
        using var accessor = mmf.CreateViewAccessor(0, FrameBridgeProtocol.MapSize, MemoryMappedFileAccess.ReadWrite);
        using var frameMutex = new Mutex(false, FrameBridgeProtocol.FrameMutexName);
        var forceSimulation = args.Any(arg => string.Equals(arg, "--simulate", StringComparison.OrdinalIgnoreCase)) ||
                              IsSimulationForced();
        var allowSimulationFallback = forceSimulation || IsAutoSimulationFallbackEnabled();

        PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusWaiting, 0, 0, 0, 0);

        try
        {
            try
            {
                _factory = IGXFactory.GetInstance();
                _factory.Init();
                _formatConvert = _factory.CreateImageFormatConvert();
            }
            catch (Exception ex) when (allowSimulationFallback)
            {
                _factory = null;
                _formatConvert = null;
                Log("factory initialization failed, continuing with synthetic camera mode: " + ex.Message);
            }

            Log("helper started");
            RunCaptureLoop(accessor, frameMutex, forceSimulation, _shutdown.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Log("fatal: " + ex);
            PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusError, 0, 0, 0, 0);
            return 1;
        }
        finally
        {
            if (_convertBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_convertBuffer);
                _convertBuffer = IntPtr.Zero;
                _convertBufferSize = 0;
            }

            try
            {
                _factory?.Uninit();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }

    private int RunProbe()
    {
        try
        {
            _factory = IGXFactory.GetInstance();
            _factory.Init();

            var serial = FindCameraSerial(1000);
            if (serial is null)
            {
                Console.WriteLine("No Daheng USB/U3V camera found.");
                return 2;
            }

            IGXDevice? device = null;
            try
            {
                try
                {
                    device = _factory.OpenDeviceBySN(serial, GX_ACCESS_MODE.GX_ACCESS_CONTROL);
                }
                catch
                {
                    device = _factory.OpenDeviceBySN(serial, GX_ACCESS_MODE.GX_ACCESS_READONLY);
                }
                var remote = device.GetRemoteFeatureControl();

                Console.WriteLine($"Camera SN: {serial}");
                LogCameraCapabilities(remote, Console.WriteLine);
                ProbeFrameRateForPixelFormats(remote, Console.WriteLine);
                return 0;
            }
            finally
            {
                try
                {
                    device?.Close();
                }
                catch
                {
                    // Ignore teardown failures.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                _factory?.Uninit();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }

    private int RunListDevices()
    {
        try
        {
            _factory = IGXFactory.GetInstance();
            _factory.Init();
            var devices = new List<IGXDeviceInfo>();
            _factory.UpdateAllDeviceList(1000, devices);
            Console.WriteLine($"Device Count: {devices.Count}");
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                Console.WriteLine($"[{i}] SN={device.GetSN()} Vendor={device.GetVendorName()} Model={device.GetModelName()} Class={device.GetDeviceClass()} USB={IsUsbDevice(device)}");
            }

            return devices.Count > 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                _factory?.Uninit();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }

    private int RunProbeFrame()
    {
        try
        {
            _factory = IGXFactory.GetInstance();
            _factory.Init();
            _formatConvert = _factory.CreateImageFormatConvert();

            var serial = FindCameraSerial(1500);
            if (serial is null)
            {
                Console.WriteLine("No Daheng USB/U3V camera found.");
                return 2;
            }

            IGXDevice? device = null;
            IGXStream? stream = null;
            IGXFeatureControl? remote = null;
            IFrameData? frame = null;
            try
            {
                device = OpenDeviceForCapture(serial);
                remote = device.GetRemoteFeatureControl();
                ConfigureCamera(remote);
                Console.WriteLine($"Configured AcquisitionMode={ReadEnumValue(remote, "AcquisitionMode")} TriggerMode={ReadEnumValue(remote, "TriggerMode")} PixelFormat={ReadEnumValue(remote, "PixelFormat")}");

                stream = device.OpenStream(0);
                ConfigureStream(stream);
                TryExecuteCommand(remote, "AcquisitionStop");
                stream.StartGrab();
                ExecuteCommand(remote, "AcquisitionStart");
                Console.WriteLine("AcquisitionStart executed.");
                try
                {
                    frame = stream.DQBuf(3000);
                }
                catch (Exception ex) when (IsTimeout(ex))
                {
                    Console.WriteLine("DQBuf timed out; retrying with SingleFrame/GetImage.");
                    ExecuteCommand(remote, "AcquisitionStop");
                    stream.StopGrab();
                    TrySetEnum(remote, "AcquisitionMode", "SingleFrame");
                    Console.WriteLine($"Retry AcquisitionMode={ReadEnumValue(remote, "AcquisitionMode")} TriggerMode={ReadEnumValue(remote, "TriggerMode")} PixelFormat={ReadEnumValue(remote, "PixelFormat")}");
                    stream.StartGrab();
                    ExecuteCommand(remote, "AcquisitionStart");
                    Console.WriteLine("SingleFrame AcquisitionStart executed.");
                    var image = stream.GetImage(3000);
                    Console.WriteLine($"Camera SN: {serial}");
                    Console.WriteLine($"GetImage Status: {image.GetStatus()}");
                    Console.WriteLine($"GetImage Size: {image.GetWidth()}x{image.GetHeight()}");
                    Console.WriteLine($"GetImage Pixel Format: {image.GetPixelFormat()}");
                    return image.GetStatus() == GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS ? 0 : 3;
                }

                var status = frame.GetStatus();
                var width = checked((int)frame.GetWidth());
                var height = checked((int)frame.GetHeight());
                var pixelFormat = frame.GetPixelFormat();
                Console.WriteLine($"Camera SN: {serial}");
                Console.WriteLine($"Frame Status: {status}");
                Console.WriteLine($"Frame Size: {width}x{height}");
                Console.WriteLine($"Pixel Format: {pixelFormat}");

                if (status == GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS &&
                    TryGetMono16Pointer(frame, width, height, out var mono16Pointer))
                {
                    unsafe
                    {
                        var samples = (ushort*)mono16Pointer.ToPointer();
                        var preview = new System.Text.StringBuilder();
                        preview.Append("Mono16 Samples: [");
                        for (var i = 0; i < Math.Min(16, width * height); i++)
                        {
                            if (i > 0)
                            {
                                preview.Append(", ");
                            }

                            preview.Append("0x");
                            preview.Append(samples[i].ToString("X4"));
                        }

                        preview.Append(']');
                        Console.WriteLine(preview.ToString());
                    }

                    return 0;
                }

                return 3;
            }
            finally
            {
                try
                {
                    if (frame is not null)
                    {
                        stream?.QBuf(frame);
                    }
                }
                catch
                {
                    // Ignore probe teardown failures.
                }

                try
                {
                    TryExecuteCommand(remote, "AcquisitionStop");
                }
                catch
                {
                    // Ignore probe teardown failures.
                }

                try
                {
                    stream?.StopGrab();
                }
                catch
                {
                    // Ignore probe teardown failures.
                }

                try
                {
                    stream?.Close();
                }
                catch
                {
                    // Ignore probe teardown failures.
                }

                try
                {
                    device?.Close();
                }
                catch
                {
                    // Ignore probe teardown failures.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (_convertBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_convertBuffer);
                _convertBuffer = IntPtr.Zero;
                _convertBufferSize = 0;
            }

            try
            {
                _factory?.Uninit();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }

    private void RunCaptureLoop(MemoryMappedViewAccessor accessor, Mutex frameMutex, bool forceSimulation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var captureSource = WaitForCaptureSource(accessor, frameMutex, forceSimulation, cancellationToken);
            if (captureSource is null)
            {
                return;
            }

            if (captureSource.Value.UseSimulation)
            {
                try
                {
                    RunSimulationLoop(accessor, frameMutex, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log("synthetic capture error: " + ex);
                    PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusError, 0, 0, 0, 0);
                    Thread.Sleep(1000);
                }

                continue;
            }

            var serial = captureSource.Value.Serial;
            if (string.IsNullOrWhiteSpace(serial))
            {
                return;
            }

            IGXDevice? device = null;
            IGXStream? stream = null;
            IGXFeatureControl? remote = null;

            try
            {
                Log($"opening camera SN={serial}");
                _loggedNativeSamplePreview = false;
                device = OpenDeviceForCapture(serial);
                remote = device.GetRemoteFeatureControl();
                LogCameraCapabilities(remote);
                ConfigureCamera(remote);
                InitializeControlState(remote, accessor, frameMutex);

                stream = device.OpenStream(0);
                ConfigureStream(stream);
                TryExecuteCommand(remote, "AcquisitionStop");
                stream.StartGrab();
                TryExecuteCommand(remote, "AcquisitionStart");

                while (!cancellationToken.IsCancellationRequested)
                {
                    ApplyPendingControls(remote, stream, accessor, frameMutex);
                    IFrameData? frame = null;
                    try
                    {
                        frame = stream.DQBuf(FrameDequeueTimeoutMs);
                    }
                    catch (Exception ex) when (IsTimeout(ex))
                    {
                        continue;
                    }

                    try
                    {
                        if (frame.GetStatus() != GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS)
                        {
                            continue;
                        }

                        var sourceWidth = checked((int)frame.GetWidth());
                        var sourceHeight = checked((int)frame.GetHeight());
                        if (sourceWidth <= 0 || sourceHeight <= 0)
                        {
                            continue;
                        }

                        if (!TryGetMono16Pointer(frame, sourceWidth, sourceHeight, out var mono16Pointer))
                        {
                            continue;
                        }

                        PublishFrame(accessor, frameMutex, mono16Pointer, sourceWidth, sourceHeight);
                    }
                    finally
                    {
                        try
                        {
                            stream.QBuf(frame);
                        }
                        catch
                        {
                            // Ignore requeue failures during disconnect.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var isUsbTransportError = IsUsbTransportError(ex);
                Log($"capture error for SN={serial} transport={isUsbTransportError}: {ex}");
                PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusError, 0, 0, 0, 0);
                if (isUsbTransportError)
                {
                    ResetFactoryAfterUsbError();
                }

                Thread.Sleep(1000);
            }
            finally
            {
                try
                {
                    TryExecuteCommand(remote, "AcquisitionStop");
                }
                catch
                {
                    // Ignore shutdown failures.
                }

                try
                {
                    stream?.StopGrab();
                }
                catch
                {
                    // Ignore shutdown failures.
                }

                try
                {
                    stream?.Close();
                }
                catch
                {
                    // Ignore shutdown failures.
                }

                try
                {
                    device?.Close();
                }
                catch
                {
                    // Ignore shutdown failures.
                }
            }
        }
    }

    private void RunSimulationLoop(MemoryMappedViewAccessor accessor, Mutex frameMutex, CancellationToken cancellationToken)
    {
        var state = CreateSimulationControlState();
        InitializeControlState(state, accessor, frameMutex);
        Log(
            $"synthetic camera started pattern={DescribeSimulationPattern(_simulationPattern)} " +
            $"exposureUs={state.ExposureUs:F2} gainDb={state.GainDb:F2} frameRateHz={state.FrameRateHz:F3} size={state.Width}x{state.Height}");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ApplyPendingSimulationControls(ref state, accessor, frameMutex);
            }
            catch (TimeoutException ex)
            {
                Log($"simulation mutex timeout while syncing controls: {ex.Message}");
                cancellationToken.WaitHandle.WaitOne(10);
                continue;
            }

            var sourceWidth = Math.Max(1, state.Width / Math.Max(1, state.BinningX));
            var sourceHeight = Math.Max(1, state.Height / Math.Max(1, state.BinningY));
            GenerateSimulationFrame(state, sourceWidth, sourceHeight, _publishedFrameId + 1);
            try
            {
                PublishFrame(accessor, frameMutex, _simulationFrameBuffer, sourceWidth, sourceHeight);
            }
            catch (TimeoutException ex)
            {
                Log($"simulation mutex timeout while publishing frame: {ex.Message}");
            }

            var frameRateHz = Math.Clamp(state.FrameRateHz, SimulationFrameRateMinHz, SimulationFrameRateMaxHz);
            var delayMs = Math.Max(5, (int)Math.Round(1000.0 / frameRateHz));
            cancellationToken.WaitHandle.WaitOne(delayMs);
        }
    }

    private void ResetFactoryAfterUsbError()
    {
        try
        {
            _factory?.Uninit();
        }
        catch (Exception ex)
        {
            Log($"factory uninit after USB error ignored: {ex.Message}");
        }

        try
        {
            _factory = IGXFactory.GetInstance();
            _factory.Init();
            _formatConvert = _factory.CreateImageFormatConvert();
            Log("factory reinitialized after USB transport error");
        }
        catch (Exception ex)
        {
            Log($"factory reinitialize after USB error failed: {ex}");
        }
    }

    private CaptureSource? WaitForCaptureSource(MemoryMappedViewAccessor accessor, Mutex frameMutex, bool forceSimulation, CancellationToken cancellationToken)
    {
        if (forceSimulation)
        {
            Log("simulation forced by argument or environment");
            return new CaptureSource(true, null);
        }

        var autoSimulationFallback = IsAutoSimulationFallbackEnabled();
        var autoSimulationDelayMs = GetEnvironmentInt("ULTRON_RAYCI_AUTO_SIMULATE_DELAY_MS", DefaultAutoSimulationDelayMs);
        var firstNoCameraUtc = DateTime.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            var deviceInfo = TryFindPreferredUsbDeviceInfo();

            if (deviceInfo is not null)
            {
                PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusWaiting, 0, 0, 0, 0);
                return new CaptureSource(false, deviceInfo.GetSN());
            }

            PublishStatus(accessor, frameMutex, FrameBridgeProtocol.StatusNoCamera, 0, 0, 0, 0);
            if (firstNoCameraUtc == DateTime.MinValue)
            {
                firstNoCameraUtc = DateTime.UtcNow;
            }

            if (autoSimulationFallback && DateTime.UtcNow - firstNoCameraUtc >= TimeSpan.FromMilliseconds(autoSimulationDelayMs))
            {
                Log($"no Daheng camera detected after {autoSimulationDelayMs} ms, switching to synthetic camera mode");
                return new CaptureSource(true, null);
            }

            Thread.Sleep(1000);
        }

        return null;
    }

    private IGXDeviceInfo? TryFindPreferredUsbDeviceInfo()
    {
        if (_factory is null)
        {
            return null;
        }

        var requestedSerial =
            Environment.GetEnvironmentVariable("BEAMMIC_DAHENG_SN") ??
            Environment.GetEnvironmentVariable("VIRTUAL_UEYE_DAHENG_SN");
        var devices = new List<IGXDeviceInfo>();
        _factory.UpdateAllDeviceList(500, devices);

        return devices.FirstOrDefault(device =>
                IsUsbDevice(device) &&
                (string.IsNullOrWhiteSpace(requestedSerial) ||
                 string.Equals(device.GetSN(), requestedSerial, StringComparison.OrdinalIgnoreCase)))
            ?? devices.FirstOrDefault(IsUsbDevice);
    }

    private IGXDevice OpenDeviceForCapture(string serial)
    {
        try
        {
            return _factory!.OpenDeviceBySN(serial, GX_ACCESS_MODE.GX_ACCESS_EXCLUSIVE);
        }
        catch (Exception ex)
        {
            Log($"exclusive open failed for SN={serial}, falling back to control access: {ex.Message}");
            return _factory!.OpenDeviceBySN(serial, GX_ACCESS_MODE.GX_ACCESS_CONTROL);
        }
    }

    private static void ConfigureStream(IGXStream stream)
    {
        try
        {
            var streamFeatureControl = stream.GetFeatureControl();
            if (streamFeatureControl.IsImplemented("StreamBufferHandlingMode"))
            {
                streamFeatureControl.GetEnumFeature("StreamBufferHandlingMode").SetValue("OldestFirst");
            }
        }
        catch
        {
            // Some stream layers do not expose buffer handling mode.
        }
    }

    private void ConfigureCamera(IGXFeatureControl remote)
    {
        TryLoadDefaultUserSet(remote);
        TrySetEnum(remote, "AcquisitionMode", "Continuous");
        TrySetEnum(remote, "TriggerMode", "Off");
        ConfigurePixelFormat(remote);
        ConfigureGeometry(remote);
        TrySetBool(remote, "ReverseX", GetEnvironmentBool("BEAMMIC_DAHENG_REVERSE_X", DefaultReverseX));
        TrySetBool(remote, "ReverseY", GetEnvironmentBool("BEAMMIC_DAHENG_REVERSE_Y", DefaultReverseY));
        TrySetEnum(remote, "ExposureAuto", "Off");
        TrySetEnum(remote, "GainAuto", "Off");
        TrySetEnum(remote, "AcquisitionFrameRateMode", "On");
        TrySetFloat(remote, "ExposureTime", GetEnvironmentDouble("BEAMMIC_DAHENG_EXPOSURE_US", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_EXPOSURE_US", DefaultStartupExposureUs)));
        TrySetFloat(remote, "Gain", GetEnvironmentDouble("BEAMMIC_DAHENG_GAIN_DB", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_GAIN_DB", DefaultStartupGainDb)));
        TrySetFrameRate(remote, GetEnvironmentDouble("BEAMMIC_DAHENG_FPS", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_FPS", DefaultStartupFrameRate)));
        var startupState = ReadCurrentControlState(remote);
        Log($"configured startup reverseX={ReadBoolValue(remote, "ReverseX")} reverseY={ReadBoolValue(remote, "ReverseY")} exposureUs={startupState.ExposureUs:F2} gainDb={startupState.GainDb:F2} frameRateHz={startupState.FrameRateHz:F3}");
    }

    private static void TryLoadDefaultUserSet(IGXFeatureControl remote)
    {
        if (!GetEnvironmentBool("BEAMMIC_DAHENG_LOAD_DEFAULT_USERSET", true))
        {
            return;
        }

        try
        {
            if (remote.IsImplemented("UserSetSelector") && remote.IsImplemented("UserSetLoad"))
            {
                remote.GetEnumFeature("UserSetSelector").SetValue("Default");
                remote.GetCommandFeature("UserSetLoad").Execute();
            }
        }
        catch
        {
            // Keep going; bridge startup still sets the critical nodes explicitly.
        }
    }


    private void ConfigureGeometry(IGXFeatureControl remote)
    {
        var sensorWidth = ReadSensorDimension(remote, "SensorWidth", "WidthMax", "Width", FrameBridgeProtocol.TargetWidth);
        var sensorHeight = ReadSensorDimension(remote, "SensorHeight", "HeightMax", "Height", FrameBridgeProtocol.TargetHeight);
        var requestedWidth = GetEnvironmentInt("BEAMMIC_DAHENG_START_WIDTH", sensorWidth);
        var requestedHeight = GetEnvironmentInt("BEAMMIC_DAHENG_START_HEIGHT", sensorHeight);
        TryApplyRequestedGeometry(remote, requestedWidth, requestedHeight, 0, 0, 1, 1);
    }

    private static bool TryApplyRequestedGeometry(
        IGXFeatureControl remote,
        int requestedWidth,
        int requestedHeight,
        int requestedOffsetX,
        int requestedOffsetY,
        int requestedBinningX,
        int requestedBinningY)
    {
        if (!remote.IsImplemented("Width") || !remote.IsImplemented("Height"))
        {
            return false;
        }

        var widthFeature = remote.GetIntFeature("Width");
        var heightFeature = remote.GetIntFeature("Height");
        dynamic? offsetXFeature = remote.IsImplemented("OffsetX") ? remote.GetIntFeature("OffsetX") : null;
        dynamic? offsetYFeature = remote.IsImplemented("OffsetY") ? remote.GetIntFeature("OffsetY") : null;

        var sensorWidth = ReadSensorDimension(remote, "SensorWidth", "WidthMax", "Width", (int)widthFeature.GetMax());
        var sensorHeight = ReadSensorDimension(remote, "SensorHeight", "HeightMax", "Height", (int)heightFeature.GetMax());
        var factorX = Math.Max(1, requestedBinningX);
        var factorY = Math.Max(1, requestedBinningY);
        var isFullFov = requestedWidth >= sensorWidth &&
                        requestedHeight >= sensorHeight &&
                        Math.Max(0, requestedOffsetX) == 0 &&
                        Math.Max(0, requestedOffsetY) == 0;

        try
        {
            if (isFullFov)
            {
                if (offsetXFeature is not null)
                {
                    offsetXFeature.SetValue(offsetXFeature.GetMin());
                }

                if (offsetYFeature is not null)
                {
                    offsetYFeature.SetValue(offsetYFeature.GetMin());
                }

                ApplySamplingReduction(remote, factorX, factorY);
                widthFeature.SetValue(widthFeature.GetMax());
                heightFeature.SetValue(heightFeature.GetMax());
                return true;
            }

            ResetSamplingReduction(remote);

            var targetWidth = AlignIntFeatureValue(
                Math.Max(1, requestedWidth / factorX),
                widthFeature.GetMin(),
                widthFeature.GetMax(),
                widthFeature.GetInc());
            var targetHeight = AlignIntFeatureValue(
                Math.Max(1, requestedHeight / factorY),
                heightFeature.GetMin(),
                heightFeature.GetMax(),
                heightFeature.GetInc());

            if (offsetXFeature is not null)
            {
                offsetXFeature.SetValue(offsetXFeature.GetMin());
            }

            if (offsetYFeature is not null)
            {
                offsetYFeature.SetValue(offsetYFeature.GetMin());
            }

            widthFeature.SetValue(targetWidth);
            heightFeature.SetValue(targetHeight);

            if (offsetXFeature is not null)
            {
                var centeredOffset = AlignOffsetValue(
                    requestedOffsetX > 0 ? requestedOffsetX : Math.Max(0, (sensorWidth - (int)targetWidth) / 2),
                    offsetXFeature.GetMin(),
                    offsetXFeature.GetMax(),
                    offsetXFeature.GetInc());
                offsetXFeature.SetValue(centeredOffset);
            }

            if (offsetYFeature is not null)
            {
                var centeredOffset = AlignOffsetValue(
                    requestedOffsetY > 0 ? requestedOffsetY : Math.Max(0, (sensorHeight - (int)targetHeight) / 2),
                    offsetYFeature.GetMin(),
                    offsetYFeature.GetMax(),
                    offsetYFeature.GetInc());
                offsetYFeature.SetValue(centeredOffset);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"geometry apply failed requested={requestedWidth}x{requestedHeight} offset={requestedOffsetX},{requestedOffsetY} binning={factorX}x{factorY}: {ex.Message}");
            return false;
        }
    }

    private static void ApplySamplingReduction(IGXFeatureControl remote, int factorX, int factorY)
    {
        ResetSamplingReduction(remote);
        if (factorX <= 1 && factorY <= 1)
        {
            return;
        }

        if (TrySetSamplingAxis(remote, "BinningHorizontal", factorX) &&
            TrySetSamplingAxis(remote, "BinningVertical", factorY))
        {
            return;
        }

        ResetSamplingReduction(remote);
        if (TrySetSamplingAxis(remote, "DecimationHorizontal", factorX) &&
            TrySetSamplingAxis(remote, "DecimationVertical", factorY))
        {
            return;
        }

        ResetSamplingReduction(remote);
    }

    private static void ResetSamplingReduction(IGXFeatureControl remote)
    {
        TryDisableSamplingAxis(remote, "BinningHorizontal");
        TryDisableSamplingAxis(remote, "BinningVertical");
        TryDisableSamplingAxis(remote, "DecimationHorizontal");
        TryDisableSamplingAxis(remote, "DecimationVertical");
    }

    private static bool TrySetSamplingAxis(IGXFeatureControl remote, string featureName, int factor)
    {
        if (factor <= 1)
        {
            return TryDisableSamplingAxis(remote, featureName);
        }

        if (!remote.IsImplemented(featureName))
        {
            return false;
        }

        if (TrySetEnum(remote, featureName, $"X{factor}", factor.ToString()))
        {
            return true;
        }

        try
        {
            var feature = remote.GetIntFeature(featureName);
            feature.SetValue(AlignIntFeatureValue(factor, feature.GetMin(), feature.GetMax(), feature.GetInc()));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDisableSamplingAxis(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return true;
        }

        if (TrySetEnum(remote, featureName, "Off", "Disable", "Disabled", "None", "X1", "1"))
        {
            return true;
        }

        try
        {
            var feature = remote.GetIntFeature(featureName);
            feature.SetValue(AlignIntFeatureValue(1, feature.GetMin(), feature.GetMax(), feature.GetInc()));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long AlignIntFeatureValue(long value, long min, long max, long increment)
    {
        var safeMin = Math.Min(min, max);
        var safeMax = Math.Max(min, max);
        var clamped = Math.Clamp(value, safeMin, safeMax);
        var safeIncrement = Math.Max(1, increment);
        var steps = (long)Math.Round((clamped - safeMin) / (double)safeIncrement);
        return Math.Clamp(safeMin + (steps * safeIncrement), safeMin, safeMax);
    }

    private static long AlignOffsetValue(long value, long min, long max, long increment)
    {
        return AlignIntFeatureValue(value, min, max, increment);
    }

    private static bool MatchesRequestedGeometry(
        BridgeControlState currentState,
        int sensorWidth,
        int sensorHeight,
        int requestedWidth,
        int requestedHeight,
        int requestedOffsetX,
        int requestedOffsetY,
        int requestedBinningX,
        int requestedBinningY)
    {
        if (requestedWidth <= 0 || requestedHeight <= 0)
        {
            return true;
        }

        var factorX = Math.Max(1, requestedBinningX);
        var factorY = Math.Max(1, requestedBinningY);
        var isFullFov = requestedWidth >= sensorWidth &&
                        requestedHeight >= sensorHeight &&
                        Math.Max(0, requestedOffsetX) == 0 &&
                        Math.Max(0, requestedOffsetY) == 0;

        if (isFullFov)
        {
            return currentState.BinningX == factorX &&
                   currentState.BinningY == factorY &&
                   currentState.Width >= sensorWidth &&
                   currentState.Height >= sensorHeight &&
                   currentState.OffsetX == 0 &&
                   currentState.OffsetY == 0;
        }

        var expectedWidth = Math.Max(1, requestedWidth / factorX);
        var expectedHeight = Math.Max(1, requestedHeight / factorY);
        var expectedOffsetX = Math.Max(0, requestedOffsetX > 0 ? requestedOffsetX : (sensorWidth - expectedWidth) / 2);
        var expectedOffsetY = Math.Max(0, requestedOffsetY > 0 ? requestedOffsetY : (sensorHeight - expectedHeight) / 2);

        return currentState.BinningX == 1 &&
               currentState.BinningY == 1 &&
               currentState.Width == expectedWidth &&
               currentState.Height == expectedHeight &&
               Math.Abs(currentState.OffsetX - expectedOffsetX) <= 2 &&
               Math.Abs(currentState.OffsetY - expectedOffsetY) <= 2;
    }

    private static int ReadSensorDimension(
        IGXFeatureControl remote,
        string primaryFeature,
        string secondaryFeature,
        string currentFeature,
        int fallback)
    {
        return Math.Max(
            Math.Max(ReadIntFeature(remote, primaryFeature), ReadIntFeature(remote, secondaryFeature)),
            Math.Max(ReadIntFeature(remote, currentFeature), fallback));
    }

    private void InitializeControlState(IGXFeatureControl remote, MemoryMappedViewAccessor accessor, Mutex frameMutex)
    {
        InitializeControlState(ReadCurrentControlState(remote), accessor, frameMutex);
    }

    private void InitializeControlState(BridgeControlState state, MemoryMappedViewAccessor accessor, Mutex frameMutex)
    {
        using var holder = AcquireMutex(frameMutex, 250);
        accessor.Write(FrameBridgeProtocol.RequestSequenceOffset, 1);
        accessor.Write(FrameBridgeProtocol.AppliedSequenceOffset, 1);
        accessor.Write(FrameBridgeProtocol.RequestedFlagsOffset, state.Flags);
        accessor.Write(FrameBridgeProtocol.AppliedFlagsOffset, state.Flags);
        accessor.Write(FrameBridgeProtocol.RequestedExposureUsOffset, state.ExposureUs);
        accessor.Write(FrameBridgeProtocol.AppliedExposureUsOffset, state.ExposureUs);
        accessor.Write(FrameBridgeProtocol.ExposureMinUsOffset, state.ExposureMinUs);
        accessor.Write(FrameBridgeProtocol.ExposureMaxUsOffset, state.ExposureMaxUs);
        accessor.Write(FrameBridgeProtocol.ExposureIncUsOffset, state.ExposureIncUs);
        accessor.Write(FrameBridgeProtocol.RequestedGainDbOffset, state.GainDb);
        accessor.Write(FrameBridgeProtocol.AppliedGainDbOffset, state.GainDb);
        accessor.Write(FrameBridgeProtocol.GainMinDbOffset, state.GainMinDb);
        accessor.Write(FrameBridgeProtocol.GainMaxDbOffset, state.GainMaxDb);
        accessor.Write(FrameBridgeProtocol.GainIncDbOffset, state.GainIncDb);
        accessor.Write(FrameBridgeProtocol.RequestedFrameRateHzOffset, state.FrameRateHz);
        accessor.Write(FrameBridgeProtocol.AppliedFrameRateHzOffset, state.FrameRateHz);
        accessor.Write(FrameBridgeProtocol.FrameRateMinHzOffset, state.FrameRateMinHz);
        accessor.Write(FrameBridgeProtocol.FrameRateMaxHzOffset, state.FrameRateMaxHz);
        accessor.Write(FrameBridgeProtocol.FrameRateIncHzOffset, state.FrameRateIncHz);
        accessor.Write(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset, state.PixelFormatCapabilityFlags);
        accessor.Write(FrameBridgeProtocol.RequestedCapturePixelFormatOffset, state.CapturePixelFormat);
        accessor.Write(FrameBridgeProtocol.AppliedCapturePixelFormatOffset, state.CapturePixelFormat);
        accessor.Write(FrameBridgeProtocol.RequestedWidthOffset, state.Width);
        accessor.Write(FrameBridgeProtocol.RequestedHeightOffset, state.Height);
        accessor.Write(FrameBridgeProtocol.RequestedOffsetXOffset, state.OffsetX);
        accessor.Write(FrameBridgeProtocol.RequestedOffsetYOffset, state.OffsetY);
        accessor.Write(FrameBridgeProtocol.RequestedBinningXOffset, state.BinningX);
        accessor.Write(FrameBridgeProtocol.RequestedBinningYOffset, state.BinningY);
        accessor.Write(FrameBridgeProtocol.AppliedWidthOffset, state.Width);
        accessor.Write(FrameBridgeProtocol.AppliedHeightOffset, state.Height);
        accessor.Write(FrameBridgeProtocol.AppliedOffsetXOffset, state.OffsetX);
        accessor.Write(FrameBridgeProtocol.AppliedOffsetYOffset, state.OffsetY);
        accessor.Write(FrameBridgeProtocol.AppliedBinningXOffset, state.BinningX);
        accessor.Write(FrameBridgeProtocol.AppliedBinningYOffset, state.BinningY);
        accessor.Write(FrameBridgeProtocol.RequestedBlackLevelOffset, state.BlackLevel);
        accessor.Write(FrameBridgeProtocol.AppliedBlackLevelOffset, state.BlackLevel);
        accessor.Write(FrameBridgeProtocol.RequestedMasterGainOffset, state.MasterGain);
        accessor.Write(FrameBridgeProtocol.AppliedMasterGainOffset, state.MasterGain);
        accessor.Write(FrameBridgeProtocol.GainBoostSupportedOffset, 0);
        accessor.Write(FrameBridgeProtocol.GainBoostEnabledOffset, 0);
        _lastAppliedRequestSequence = 1;
    }

    private void ApplyPendingSimulationControls(ref BridgeControlState state, MemoryMappedViewAccessor accessor, Mutex frameMutex)
    {
        int requestSequence;
        int requestedFlags;
        double requestedExposureUs;
        double requestedGainDb;
        double requestedFrameRateHz;
        int requestedCapturePixelFormat;
        int requestedWidth;
        int requestedHeight;
        int requestedOffsetX;
        int requestedOffsetY;
        int requestedBinningX;
        int requestedBinningY;
        int requestedBlackLevel;
        int requestedMasterGain;

        using (AcquireMutex(frameMutex, 250))
        {
            requestSequence = accessor.ReadInt32(FrameBridgeProtocol.RequestSequenceOffset);
            if (requestSequence <= 0 || requestSequence == _lastAppliedRequestSequence)
            {
                return;
            }

            requestedFlags = accessor.ReadInt32(FrameBridgeProtocol.RequestedFlagsOffset);
            requestedExposureUs = accessor.ReadDouble(FrameBridgeProtocol.RequestedExposureUsOffset);
            requestedGainDb = accessor.ReadDouble(FrameBridgeProtocol.RequestedGainDbOffset);
            requestedFrameRateHz = accessor.ReadDouble(FrameBridgeProtocol.RequestedFrameRateHzOffset);
            requestedCapturePixelFormat = accessor.ReadInt32(FrameBridgeProtocol.RequestedCapturePixelFormatOffset);
            requestedWidth = accessor.ReadInt32(FrameBridgeProtocol.RequestedWidthOffset);
            requestedHeight = accessor.ReadInt32(FrameBridgeProtocol.RequestedHeightOffset);
            requestedOffsetX = accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetXOffset);
            requestedOffsetY = accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetYOffset);
            requestedBinningX = accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningXOffset);
            requestedBinningY = accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningYOffset);
            requestedBlackLevel = accessor.ReadInt32(FrameBridgeProtocol.RequestedBlackLevelOffset);
            requestedMasterGain = accessor.ReadInt32(FrameBridgeProtocol.RequestedMasterGainOffset);
        }

        var appliedState = state with
        {
            Flags = requestedFlags & (FrameBridgeProtocol.ControlFlagAutoExposure | FrameBridgeProtocol.ControlFlagAutoGain),
            ExposureUs = double.IsFinite(requestedExposureUs) && requestedExposureUs > 0.0
                ? Math.Clamp(requestedExposureUs, state.ExposureMinUs, state.ExposureMaxUs)
                : state.ExposureUs,
            GainDb = double.IsFinite(requestedGainDb)
                ? Math.Clamp(requestedGainDb, state.GainMinDb, state.GainMaxDb)
                : Math.Clamp(ToGainDb(requestedMasterGain, state.GainMinDb, state.GainMaxDb), state.GainMinDb, state.GainMaxDb),
            FrameRateHz = double.IsFinite(requestedFrameRateHz) && requestedFrameRateHz > 0.0
                ? Math.Clamp(requestedFrameRateHz, state.FrameRateMinHz, state.FrameRateMaxHz)
                : state.FrameRateHz,
            CapturePixelFormat = NormalizeSimulationCapturePixelFormat(requestedCapturePixelFormat, state.CapturePixelFormat),
            BlackLevel = Math.Clamp(requestedBlackLevel, BeamMicBlackLevelMin, BeamMicBlackLevelMax)
        };

        appliedState = ApplySimulationGeometry(
            appliedState,
            requestedWidth,
            requestedHeight,
            requestedOffsetX,
            requestedOffsetY,
            requestedBinningX,
            requestedBinningY);
        appliedState = appliedState with { MasterGain = ToMasterGain(appliedState.GainDb, appliedState.GainMinDb, appliedState.GainMaxDb) };

        using (AcquireMutex(frameMutex, 250))
        {
            accessor.Write(FrameBridgeProtocol.AppliedSequenceOffset, requestSequence);
            accessor.Write(FrameBridgeProtocol.AppliedFlagsOffset, appliedState.Flags);
            accessor.Write(FrameBridgeProtocol.AppliedExposureUsOffset, appliedState.ExposureUs);
            accessor.Write(FrameBridgeProtocol.ExposureMinUsOffset, appliedState.ExposureMinUs);
            accessor.Write(FrameBridgeProtocol.ExposureMaxUsOffset, appliedState.ExposureMaxUs);
            accessor.Write(FrameBridgeProtocol.ExposureIncUsOffset, appliedState.ExposureIncUs);
            accessor.Write(FrameBridgeProtocol.AppliedGainDbOffset, appliedState.GainDb);
            accessor.Write(FrameBridgeProtocol.GainMinDbOffset, appliedState.GainMinDb);
            accessor.Write(FrameBridgeProtocol.GainMaxDbOffset, appliedState.GainMaxDb);
            accessor.Write(FrameBridgeProtocol.GainIncDbOffset, appliedState.GainIncDb);
            accessor.Write(FrameBridgeProtocol.AppliedFrameRateHzOffset, appliedState.FrameRateHz);
            accessor.Write(FrameBridgeProtocol.FrameRateMinHzOffset, appliedState.FrameRateMinHz);
            accessor.Write(FrameBridgeProtocol.FrameRateMaxHzOffset, appliedState.FrameRateMaxHz);
            accessor.Write(FrameBridgeProtocol.FrameRateIncHzOffset, appliedState.FrameRateIncHz);
            accessor.Write(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset, appliedState.PixelFormatCapabilityFlags);
            accessor.Write(FrameBridgeProtocol.AppliedCapturePixelFormatOffset, appliedState.CapturePixelFormat);
            accessor.Write(FrameBridgeProtocol.AppliedWidthOffset, appliedState.Width);
            accessor.Write(FrameBridgeProtocol.AppliedHeightOffset, appliedState.Height);
            accessor.Write(FrameBridgeProtocol.AppliedOffsetXOffset, appliedState.OffsetX);
            accessor.Write(FrameBridgeProtocol.AppliedOffsetYOffset, appliedState.OffsetY);
            accessor.Write(FrameBridgeProtocol.AppliedBinningXOffset, appliedState.BinningX);
            accessor.Write(FrameBridgeProtocol.AppliedBinningYOffset, appliedState.BinningY);
            accessor.Write(FrameBridgeProtocol.AppliedBlackLevelOffset, appliedState.BlackLevel);
            accessor.Write(FrameBridgeProtocol.AppliedMasterGainOffset, appliedState.MasterGain);
            accessor.Write(FrameBridgeProtocol.GainBoostSupportedOffset, 0);
            accessor.Write(FrameBridgeProtocol.GainBoostEnabledOffset, 0);
        }

        state = appliedState;
        _lastAppliedRequestSequence = requestSequence;
        Log($"applied synthetic controls seq={requestSequence} exposureUs={appliedState.ExposureUs:F2} gainDb={appliedState.GainDb:F2} blackLevel={appliedState.BlackLevel} frameRateHz={appliedState.FrameRateHz:F3} capturePixelFormat={appliedState.CapturePixelFormat} size={appliedState.Width}x{appliedState.Height} offset={appliedState.OffsetX},{appliedState.OffsetY} binning={appliedState.BinningX}x{appliedState.BinningY} flags=0x{appliedState.Flags:X}");
    }

    private void ApplyPendingControls(IGXFeatureControl remote, IGXStream stream, MemoryMappedViewAccessor accessor, Mutex frameMutex)
    {
        int requestSequence;
        int requestedFlags;
        double requestedExposureUs;
        double requestedGainDb;
        double requestedFrameRateHz;
        int requestedCapturePixelFormat;
        int requestedWidth;
        int requestedHeight;
        int requestedOffsetX;
        int requestedOffsetY;
        int requestedBinningX;
        int requestedBinningY;
        int requestedBlackLevel;
        int requestedMasterGain;

        using (AcquireMutex(frameMutex, 250))
        {
            requestSequence = accessor.ReadInt32(FrameBridgeProtocol.RequestSequenceOffset);
            if (requestSequence <= 0 || requestSequence == _lastAppliedRequestSequence)
            {
                return;
            }

            requestedFlags = accessor.ReadInt32(FrameBridgeProtocol.RequestedFlagsOffset);
            requestedExposureUs = accessor.ReadDouble(FrameBridgeProtocol.RequestedExposureUsOffset);
            requestedGainDb = accessor.ReadDouble(FrameBridgeProtocol.RequestedGainDbOffset);
            requestedFrameRateHz = accessor.ReadDouble(FrameBridgeProtocol.RequestedFrameRateHzOffset);
            requestedCapturePixelFormat = accessor.ReadInt32(FrameBridgeProtocol.RequestedCapturePixelFormatOffset);
            requestedWidth = accessor.ReadInt32(FrameBridgeProtocol.RequestedWidthOffset);
            requestedHeight = accessor.ReadInt32(FrameBridgeProtocol.RequestedHeightOffset);
            requestedOffsetX = accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetXOffset);
            requestedOffsetY = accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetYOffset);
            requestedBinningX = accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningXOffset);
            requestedBinningY = accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningYOffset);
            requestedBlackLevel = accessor.ReadInt32(FrameBridgeProtocol.RequestedBlackLevelOffset);
            requestedMasterGain = accessor.ReadInt32(FrameBridgeProtocol.RequestedMasterGainOffset);
        }

        var currentState = ReadCurrentControlState(remote);
        var sensorWidth = ReadSensorDimension(remote, "SensorWidth", "WidthMax", "Width", FrameBridgeProtocol.TargetWidth);
        var sensorHeight = ReadSensorDimension(remote, "SensorHeight", "HeightMax", "Height", FrameBridgeProtocol.TargetHeight);
        var pixelFormatChanged = requestedCapturePixelFormat > 0 && requestedCapturePixelFormat != currentState.CapturePixelFormat;
        var geometryChanged = !MatchesRequestedGeometry(
            currentState,
            sensorWidth,
            sensorHeight,
            requestedWidth,
            requestedHeight,
            requestedOffsetX,
            requestedOffsetY,
            requestedBinningX,
            requestedBinningY);

        if (pixelFormatChanged || geometryChanged)
        {
            TryExecuteCommand(remote, "AcquisitionStop");
            try
            {
                stream.StopGrab();
            }
            catch
            {
                // Ignore transient stop failures while reconfiguring pixel format.
            }

            if (pixelFormatChanged)
            {
                TrySetCapturePixelFormat(remote, requestedCapturePixelFormat);
            }

            if (geometryChanged)
            {
                TryApplyRequestedGeometry(
                    remote,
                    requestedWidth,
                    requestedHeight,
                    requestedOffsetX,
                    requestedOffsetY,
                    requestedBinningX,
                    requestedBinningY);
            }

            try
            {
                stream.StartGrab();
            }
            catch
            {
                // The next dequeue will surface a hard failure if restart did not recover.
            }

            TryExecuteCommand(remote, "AcquisitionStart");
        }

        var wantAutoExposure = (requestedFlags & FrameBridgeProtocol.ControlFlagAutoExposure) != 0;
        var wantAutoGain = (requestedFlags & FrameBridgeProtocol.ControlFlagAutoGain) != 0;

        TrySetAutoMode(remote, "ExposureAuto", wantAutoExposure);
        TrySetAutoMode(remote, "GainAuto", wantAutoGain);

        if (!wantAutoExposure)
        {
            TrySetFloat(remote, "ExposureTime", requestedExposureUs);
        }

        if (!wantAutoGain)
        {
            if (double.IsFinite(requestedGainDb))
            {
                TrySetFloat(remote, "Gain", requestedGainDb);
            }
            else
            {
                var state = ReadCurrentControlState(remote);
                TrySetFloat(remote, "Gain", ToGainDb(requestedMasterGain, state.GainMinDb, state.GainMaxDb));
            }
        }

        TrySetBlackLevel(remote, requestedBlackLevel);

        if (double.IsFinite(requestedFrameRateHz) && requestedFrameRateHz > 0.0)
        {
            TrySetFrameRate(remote, requestedFrameRateHz);
        }

        var appliedState = ReadCurrentControlState(remote);

        using (AcquireMutex(frameMutex, 250))
        {
            accessor.Write(FrameBridgeProtocol.AppliedSequenceOffset, requestSequence);
            accessor.Write(FrameBridgeProtocol.AppliedFlagsOffset, appliedState.Flags);
            accessor.Write(FrameBridgeProtocol.AppliedExposureUsOffset, appliedState.ExposureUs);
            accessor.Write(FrameBridgeProtocol.ExposureMinUsOffset, appliedState.ExposureMinUs);
            accessor.Write(FrameBridgeProtocol.ExposureMaxUsOffset, appliedState.ExposureMaxUs);
            accessor.Write(FrameBridgeProtocol.ExposureIncUsOffset, appliedState.ExposureIncUs);
            accessor.Write(FrameBridgeProtocol.AppliedGainDbOffset, appliedState.GainDb);
            accessor.Write(FrameBridgeProtocol.GainMinDbOffset, appliedState.GainMinDb);
            accessor.Write(FrameBridgeProtocol.GainMaxDbOffset, appliedState.GainMaxDb);
            accessor.Write(FrameBridgeProtocol.GainIncDbOffset, appliedState.GainIncDb);
            accessor.Write(FrameBridgeProtocol.AppliedFrameRateHzOffset, appliedState.FrameRateHz);
            accessor.Write(FrameBridgeProtocol.FrameRateMinHzOffset, appliedState.FrameRateMinHz);
            accessor.Write(FrameBridgeProtocol.FrameRateMaxHzOffset, appliedState.FrameRateMaxHz);
            accessor.Write(FrameBridgeProtocol.FrameRateIncHzOffset, appliedState.FrameRateIncHz);
            accessor.Write(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset, appliedState.PixelFormatCapabilityFlags);
            accessor.Write(FrameBridgeProtocol.AppliedCapturePixelFormatOffset, appliedState.CapturePixelFormat);
            accessor.Write(FrameBridgeProtocol.AppliedWidthOffset, appliedState.Width);
            accessor.Write(FrameBridgeProtocol.AppliedHeightOffset, appliedState.Height);
            accessor.Write(FrameBridgeProtocol.AppliedOffsetXOffset, appliedState.OffsetX);
            accessor.Write(FrameBridgeProtocol.AppliedOffsetYOffset, appliedState.OffsetY);
            accessor.Write(FrameBridgeProtocol.AppliedBinningXOffset, appliedState.BinningX);
            accessor.Write(FrameBridgeProtocol.AppliedBinningYOffset, appliedState.BinningY);
            accessor.Write(FrameBridgeProtocol.AppliedBlackLevelOffset, appliedState.BlackLevel);
            accessor.Write(FrameBridgeProtocol.AppliedMasterGainOffset, appliedState.MasterGain);
            accessor.Write(FrameBridgeProtocol.GainBoostSupportedOffset, 0);
            accessor.Write(FrameBridgeProtocol.GainBoostEnabledOffset, 0);
        }

        _lastAppliedRequestSequence = requestSequence;
        var currentFrameRateText = TryReadCurrentFrameRateHz(remote, out var currentFrameRateHz)
            ? $" currentFrameRateHz={currentFrameRateHz:F3}"
            : string.Empty;
        Log($"applied controls seq={requestSequence} exposureUs={appliedState.ExposureUs:F2} gainDb={appliedState.GainDb:F2} blackLevel={appliedState.BlackLevel} frameRateHz={appliedState.FrameRateHz:F3}{currentFrameRateText} capturePixelFormat={appliedState.CapturePixelFormat} size={appliedState.Width}x{appliedState.Height} offset={appliedState.OffsetX},{appliedState.OffsetY} binning={appliedState.BinningX}x{appliedState.BinningY} flags=0x{appliedState.Flags:X} supports12bit={appliedState.PixelFormatCapabilityFlags != 0}");
    }

    private BridgeControlState CreateSimulationControlState()
    {
        var exposureUs = Math.Clamp(
            GetEnvironmentDouble("BEAMMIC_DAHENG_EXPOSURE_US", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_EXPOSURE_US", DefaultStartupExposureUs)),
            SimulationExposureMinUs,
            SimulationExposureMaxUs);
        var gainDb = Math.Clamp(
            GetEnvironmentDouble("BEAMMIC_DAHENG_GAIN_DB", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_GAIN_DB", DefaultStartupGainDb)),
            SimulationGainMinDb,
            SimulationGainMaxDb);
        var frameRateHz = Math.Clamp(
            GetEnvironmentDouble("BEAMMIC_DAHENG_FPS", GetEnvironmentDouble("VIRTUAL_UEYE_DAHENG_FPS", DefaultStartupFrameRate)),
            SimulationFrameRateMinHz,
            SimulationFrameRateMaxHz);
        var blackLevel = Math.Clamp(
            GetEnvironmentInt("ULTRON_RAYCI_SIM_BLACK_LEVEL", SimulationBlackLevelDefault),
            BeamMicBlackLevelMin,
            BeamMicBlackLevelMax);
        var capturePixelFormat = NormalizeSimulationCapturePixelFormat(
            GetEnvironmentInt("ULTRON_RAYCI_SIM_CAPTURE_PIXEL_FORMAT", FrameBridgeProtocol.CapturePixelFormatMono10),
            FrameBridgeProtocol.CapturePixelFormatMono10);

        var state = new BridgeControlState(
            Flags: 0,
            ExposureUs: exposureUs,
            ExposureMinUs: SimulationExposureMinUs,
            ExposureMaxUs: SimulationExposureMaxUs,
            ExposureIncUs: SimulationExposureIncUs,
            GainDb: gainDb,
            GainMinDb: SimulationGainMinDb,
            GainMaxDb: SimulationGainMaxDb,
            GainIncDb: SimulationGainIncDb,
            BlackLevel: blackLevel,
            FrameRateHz: frameRateHz,
            FrameRateMinHz: SimulationFrameRateMinHz,
            FrameRateMaxHz: SimulationFrameRateMaxHz,
            FrameRateIncHz: SimulationFrameRateIncHz,
            CapturePixelFormat: capturePixelFormat,
            PixelFormatCapabilityFlags: FrameBridgeProtocol.PixelFormatCapabilitySupports12BitFlag,
            MasterGain: ToMasterGain(gainDb, SimulationGainMinDb, SimulationGainMaxDb),
            Width: CapturedFrameWidth,
            Height: CapturedFrameHeight,
            OffsetX: 0,
            OffsetY: 0,
            BinningX: 1,
            BinningY: 1);

        return ApplySimulationGeometry(
            state,
            GetEnvironmentInt("BEAMMIC_DAHENG_START_WIDTH", CapturedFrameWidth),
            GetEnvironmentInt("BEAMMIC_DAHENG_START_HEIGHT", CapturedFrameHeight),
            0,
            0,
            1,
            1);
    }

    private static BridgeControlState ApplySimulationGeometry(
        BridgeControlState state,
        int requestedWidth,
        int requestedHeight,
        int requestedOffsetX,
        int requestedOffsetY,
        int requestedBinningX,
        int requestedBinningY)
    {
        var width = Math.Clamp(requestedWidth > 0 ? requestedWidth : state.Width, 1, FrameBridgeProtocol.TargetWidth);
        var height = Math.Clamp(requestedHeight > 0 ? requestedHeight : state.Height, 1, FrameBridgeProtocol.TargetHeight);
        var binningX = Math.Clamp(requestedBinningX > 0 ? requestedBinningX : state.BinningX, 1, SimulationSamplingMax);
        var binningY = Math.Clamp(requestedBinningY > 0 ? requestedBinningY : state.BinningY, 1, SimulationSamplingMax);
        var offsetX = Math.Clamp(requestedOffsetX, 0, Math.Max(0, FrameBridgeProtocol.TargetWidth - width));
        var offsetY = Math.Clamp(requestedOffsetY, 0, Math.Max(0, FrameBridgeProtocol.TargetHeight - height));

        return state with
        {
            Width = width,
            Height = height,
            OffsetX = offsetX,
            OffsetY = offsetY,
            BinningX = binningX,
            BinningY = binningY
        };
    }

    private static int NormalizeSimulationCapturePixelFormat(int requestedCapturePixelFormat, int fallback)
    {
        return requestedCapturePixelFormat switch
        {
            FrameBridgeProtocol.CapturePixelFormatMono8 => FrameBridgeProtocol.CapturePixelFormatMono8,
            FrameBridgeProtocol.CapturePixelFormatMono10 => FrameBridgeProtocol.CapturePixelFormatMono10,
            FrameBridgeProtocol.CapturePixelFormatMono12 => FrameBridgeProtocol.CapturePixelFormatMono12,
            FrameBridgeProtocol.CapturePixelFormatMono14 => FrameBridgeProtocol.CapturePixelFormatMono14,
            FrameBridgeProtocol.CapturePixelFormatMono16 => FrameBridgeProtocol.CapturePixelFormatMono16,
            _ => fallback
        };
    }

    private void GenerateSimulationFrame(BridgeControlState state, int sourceWidth, int sourceHeight, long frameId)
    {
        EnsureSimulationBuffers(sourceWidth, sourceHeight);

        if (_simulationPattern == SimulationPattern.WhiteNoise)
        {
            GenerateWhiteNoiseSimulationFrame(state, sourceWidth, sourceHeight, frameId);
            return;
        }

        var timeSeconds = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        var centerX = FrameBridgeProtocol.TargetWidth * (0.5 + (0.08 * Math.Sin(timeSeconds * 0.65)));
        var centerY = FrameBridgeProtocol.TargetHeight * (0.5 + (0.06 * Math.Cos(timeSeconds * 0.5)));
        var radiusX = Math.Max(32.0, FrameBridgeProtocol.TargetWidth * 0.12);
        var radiusY = Math.Max(32.0, FrameBridgeProtocol.TargetHeight * 0.15);
        var logicalWidth = Math.Max(1, state.Width);
        var logicalHeight = Math.Max(1, state.Height);
        var baseLevel = 512.0 + (state.BlackLevel * 80.0);
        var signalScale = Math.Clamp((state.ExposureUs / DefaultStartupExposureUs) * (1.0 + (state.GainDb / 12.0)), 0.1, 12.0);
        var animationOffset = unchecked((int)(frameId % 256));

        for (var x = 0; x < sourceWidth; x++)
        {
            var sensorX = state.OffsetX + (((x + 0.5) * logicalWidth) / sourceWidth);
            var dx = (sensorX - centerX) / radiusX;
            _simulationXProfile[x] = Math.Max(0.0, 1.0 - (dx * dx));
        }

        for (var y = 0; y < sourceHeight; y++)
        {
            var sensorY = state.OffsetY + (((y + 0.5) * logicalHeight) / sourceHeight);
            var dy = (sensorY - centerY) / radiusY;
            _simulationYProfile[y] = Math.Max(0.0, 1.0 - (dy * dy));
        }

        var bufferIndex = 0;
        for (var y = 0; y < sourceHeight; y++)
        {
            var rowProfile = _simulationYProfile[y];
            var animatedY = (y + animationOffset) & 0x3F;
            for (var x = 0; x < sourceWidth; x++)
            {
                var beam = _simulationXProfile[x] * rowProfile;
                var core = beam * beam;
                var texture = (((x + animationOffset) ^ (animatedY * 3)) & 0x1F) * 24.0;
                var value = baseLevel + (beam * 7000.0) + (core * 42000.0 * signalScale) + texture;
                var clamped = (ushort)Math.Clamp((int)Math.Round(value), 0, ushort.MaxValue);
                _simulationFrameBuffer[bufferIndex++] = QuantizeSimulationSample(clamped, state.CapturePixelFormat);
            }
        }
    }

    private void GenerateWhiteNoiseSimulationFrame(BridgeControlState state, int sourceWidth, int sourceHeight, long frameId)
    {
        var logicalWidth = Math.Max(1, state.Width);
        var logicalHeight = Math.Max(1, state.Height);
        var baseLevel = 256.0 + (state.BlackLevel * 96.0);
        var signalScale = Math.Clamp((state.ExposureUs / DefaultStartupExposureUs) * (1.0 + (state.GainDb / 12.0)), 0.05, 16.0);
        var midLevel = baseLevel + (512.0 * signalScale);
        var amplitude = Math.Min(28000.0, 2048.0 + (4096.0 * signalScale));

        var bufferIndex = 0;
        for (var y = 0; y < sourceHeight; y++)
        {
            var sensorY = state.OffsetY + (((y + 0.5) * logicalHeight) / sourceHeight);
            for (var x = 0; x < sourceWidth; x++)
            {
                var sensorX = state.OffsetX + (((x + 0.5) * logicalWidth) / sourceWidth);
                var noise = HashSimulationNoise((uint)Math.Round(sensorX), (uint)Math.Round(sensorY), (uint)frameId);
                var centered = ((noise & 0xFFFF) / 65535.0) - 0.5;
                var fine = (((noise >> 16) & 0xFF) / 255.0) - 0.5;
                var value = midLevel + (centered * amplitude) + (fine * 1024.0);
                var clamped = (ushort)Math.Clamp((int)Math.Round(value), 0, ushort.MaxValue);
                _simulationFrameBuffer[bufferIndex++] = QuantizeSimulationSample(clamped, state.CapturePixelFormat);
            }
        }
    }

    private void EnsureSimulationBuffers(int sourceWidth, int sourceHeight)
    {
        var pixelCount = checked(sourceWidth * sourceHeight);
        if (_simulationFrameBuffer.Length != pixelCount)
        {
            _simulationFrameBuffer = new ushort[pixelCount];
        }

        if (_simulationXProfile.Length != sourceWidth)
        {
            _simulationXProfile = new double[sourceWidth];
        }

        if (_simulationYProfile.Length != sourceHeight)
        {
            _simulationYProfile = new double[sourceHeight];
        }
    }

    private static ushort QuantizeSimulationSample(ushort value, int capturePixelFormat)
    {
        return capturePixelFormat switch
        {
            FrameBridgeProtocol.CapturePixelFormatMono8 => QuantizeToMono16ContainerSample(value, 8),
            FrameBridgeProtocol.CapturePixelFormatMono10 => QuantizeToMono16ContainerSample(value, 10),
            FrameBridgeProtocol.CapturePixelFormatMono12 => QuantizeToMono16ContainerSample(value, 12),
            FrameBridgeProtocol.CapturePixelFormatMono14 => QuantizeToMono16ContainerSample(value, 14),
            _ => value
        };
    }

    private static ushort QuantizeToMono16ContainerSample(ushort value, int bitDepth)
    {
        if (bitDepth <= 0 || bitDepth >= 16)
        {
            return value;
        }

        var shift = 16 - bitDepth;
        var rounding = 1u << (shift - 1);
        var maxValue = (1u << bitDepth) - 1u;
        var quantized = Math.Min(maxValue, (((uint)value) + rounding) >> shift);
        return (ushort)(quantized << shift);
    }

    private static ushort NormalizeToMono16ContainerSample(ushort value, int bitDepth)
    {
        if (bitDepth <= 0 || bitDepth >= 16)
        {
            return value;
        }

        var shift = 16 - bitDepth;
        var maxValue = (1u << bitDepth) - 1u;
        var raw = (uint)value;
        if ((raw & ~maxValue) == 0)
        {
            return (ushort)(raw << shift);
        }

        var containerMask = maxValue << shift;
        var paddingMask = (1u << shift) - 1u;
        if ((raw & ~containerMask) == 0 && (raw & paddingMask) == 0)
        {
            return value;
        }

        var quantized = Math.Min(maxValue, raw >> shift);
        return (ushort)(quantized << shift);
    }

    private static uint HashSimulationNoise(uint x, uint y, uint frameId)
    {
        var value = x * 0x1F123BB5u;
        value ^= y * 0x6C8E9CF5u;
        value ^= frameId * 0x9E3779B9u;
        value ^= 0x85EBCA6Bu;
        value ^= value >> 15;
        value *= 0x2C1B3C6Du;
        value ^= value >> 12;
        value *= 0x297A2D39u;
        value ^= value >> 15;
        return value;
    }

    private bool TryGetMono16Pointer(IFrameData frame, int width, int height, out IntPtr buffer)
    {
        buffer = frame.GetBuffer();
        var pixelFormat = frame.GetPixelFormat();
        if (buffer == IntPtr.Zero)
        {
            return false;
        }

        if (pixelFormat == GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8)
        {
            var requiredSize = checked((ulong)width * (ulong)height * 2UL);
            EnsureConvertBuffer(requiredSize);
            unsafe
            {
                var source = (byte*)buffer.ToPointer();
                var destination = (ushort*)_convertBuffer.ToPointer();
                var count = checked(width * height);
                for (var i = 0; i < count; i++)
                {
                    destination[i] = source[i];
                }
            }

            buffer = _convertBuffer;
            return true;
        }

        if (TryGetNativeMonoBitDepth(pixelFormat, out var bitDepth))
        {
            var requiredSize = checked((ulong)width * (ulong)height * 2UL);
            EnsureConvertBuffer(requiredSize);
            unsafe
            {
                if (!_loggedNativeSamplePreview)
                {
                    _loggedNativeSamplePreview = true;
                    var sourceWords = (ushort*)buffer.ToPointer();
                    var preview = new System.Text.StringBuilder();
                    preview.Append("native mono preview pixelFormat=");
                    preview.Append(pixelFormat);
                    preview.Append(" bitDepth=");
                    preview.Append(bitDepth);
                    preview.Append(" samples=");
                    preview.Append('[');
                    for (var i = 0; i < 16; i++)
                    {
                        if (i > 0)
                        {
                            preview.Append(", ");
                        }

                        preview.Append("0x");
                        preview.Append(sourceWords[i].ToString("X4"));
                    }
                    preview.Append(']');
                    Log(preview.ToString());
                }

                var source = (ushort*)buffer.ToPointer();
                var destination = (ushort*)_convertBuffer.ToPointer();
                var count = checked(width * height);
                for (var i = 0; i < count; i++)
                {
                    destination[i] = NormalizeToMono16ContainerSample(source[i], bitDepth);
                }
            }

            buffer = _convertBuffer;
            return true;
        }

        if (_formatConvert is null)
        {
            Log($"pixel format {pixelFormat} requires conversion but converter is unavailable");
            return false;
        }

        try
        {
            _formatConvert.SetDstFormat(GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16);
            _formatConvert.SetValidBits(GetValidBits(pixelFormat));
            var requiredSize = _formatConvert.GetBufferSizeForConversion(frame);
            EnsureConvertBuffer(requiredSize);
            _formatConvert.Convert(frame, _convertBuffer, requiredSize, false);
            if (TryGetConvertedMonoBitDepth(pixelFormat, out var convertedBitDepth))
            {
                NormalizeConvertedMono16Container(_convertBuffer, width, height, convertedBitDepth);
            }
            buffer = _convertBuffer;
            return buffer != IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Log($"conversion failed for {pixelFormat}: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetNativeMonoBitDepth(GX_PIXEL_FORMAT_ENTRY pixelFormat, out int bitDepth)
    {
        switch (pixelFormat)
        {
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10:
                bitDepth = 10;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12:
                bitDepth = 12;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14_P:
                bitDepth = 14;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16:
                bitDepth = 16;
                return true;
            default:
                bitDepth = 0;
                return false;
        }
    }

    private static bool TryGetConvertedMonoBitDepth(GX_PIXEL_FORMAT_ENTRY pixelFormat, out int bitDepth)
    {
        switch (pixelFormat)
        {
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_P:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_PACKED:
                bitDepth = 10;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_P:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_PACKED:
                bitDepth = 12;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14:
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14_P:
                bitDepth = 14;
                return true;
            case GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16:
                bitDepth = 16;
                return true;
            default:
                bitDepth = 0;
                return false;
        }
    }

    private static unsafe void NormalizeConvertedMono16Container(IntPtr buffer, int width, int height, int bitDepth)
    {
        if (buffer == IntPtr.Zero || width <= 0 || height <= 0 || bitDepth <= 0)
        {
            return;
        }

        var sampleCount = checked(width * height);
        var samples = (ushort*)buffer.ToPointer();
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = NormalizeToMono16ContainerSample(samples[i], bitDepth);
        }
    }

    private void EnsureConvertBuffer(ulong requiredSize)
    {
        if (_convertBuffer != IntPtr.Zero && _convertBufferSize >= requiredSize)
        {
            return;
        }

        if (_convertBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_convertBuffer);
        }

        _convertBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        _convertBufferSize = requiredSize;
    }

    private unsafe void PublishFrame(MemoryMappedViewAccessor accessor, Mutex frameMutex, IntPtr frameData, int sourceWidth, int sourceHeight)
    {
        using var holder = AcquireMutex(frameMutex, 250);
        var nextFrameId = Interlocked.Increment(ref _publishedFrameId);
        var payloadLength = checked(sourceWidth * sourceHeight * FrameBridgeProtocol.TargetBytesPerPixel);
        accessor.Write(FrameBridgeProtocol.MagicOffset, FrameBridgeProtocol.Magic);
        accessor.Write(FrameBridgeProtocol.VersionOffset, FrameBridgeProtocol.Version);
        accessor.Write(FrameBridgeProtocol.WidthOffset, sourceWidth);
        accessor.Write(FrameBridgeProtocol.HeightOffset, sourceHeight);
        accessor.Write(FrameBridgeProtocol.StrideOffset, sourceWidth * FrameBridgeProtocol.TargetBytesPerPixel);
        accessor.Write(FrameBridgeProtocol.PixelFormatOffset, FrameBridgeProtocol.PixelFormatMono16);
        accessor.Write(FrameBridgeProtocol.StatusOffset, FrameBridgeProtocol.StatusStreaming);
        accessor.Write(FrameBridgeProtocol.PayloadLengthOffset, payloadLength);
        accessor.Write(FrameBridgeProtocol.SourceWidthOffset, sourceWidth);
        accessor.Write(FrameBridgeProtocol.SourceHeightOffset, sourceHeight);
        accessor.Write(FrameBridgeProtocol.FrameIdOffset, nextFrameId);
        accessor.Write(FrameBridgeProtocol.TimestampTicksOffset, DateTime.UtcNow.Ticks);
        byte* mapPointer = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref mapPointer);
        try
        {
            Buffer.MemoryCopy((void*)frameData, mapPointer + FrameBridgeProtocol.HeaderSize, FrameBridgeProtocol.FrameByteCount, payloadLength);
        }
        finally
        {
            if (mapPointer != null)
            {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

    }

    private unsafe void PublishFrame(MemoryMappedViewAccessor accessor, Mutex frameMutex, ushort[] frameData, int sourceWidth, int sourceHeight)
    {
        fixed (ushort* framePointer = frameData)
        {
            PublishFrame(accessor, frameMutex, (IntPtr)framePointer, sourceWidth, sourceHeight);
        }
    }

    private void PublishStatus(
        MemoryMappedViewAccessor accessor,
        Mutex frameMutex,
        int status,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight)
    {
        using var holder = AcquireMutex(frameMutex, 250);
        accessor.Write(FrameBridgeProtocol.MagicOffset, FrameBridgeProtocol.Magic);
        accessor.Write(FrameBridgeProtocol.VersionOffset, FrameBridgeProtocol.Version);
        accessor.Write(FrameBridgeProtocol.WidthOffset, width);
        accessor.Write(FrameBridgeProtocol.HeightOffset, height);
        accessor.Write(FrameBridgeProtocol.StrideOffset, width * FrameBridgeProtocol.TargetBytesPerPixel);
        accessor.Write(FrameBridgeProtocol.PixelFormatOffset, FrameBridgeProtocol.PixelFormatMono16);
        accessor.Write(FrameBridgeProtocol.StatusOffset, status);
        accessor.Write(FrameBridgeProtocol.PayloadLengthOffset, 0);
        accessor.Write(FrameBridgeProtocol.SourceWidthOffset, sourceWidth);
        accessor.Write(FrameBridgeProtocol.SourceHeightOffset, sourceHeight);
        accessor.Write(FrameBridgeProtocol.FrameIdOffset, _publishedFrameId);
        accessor.Write(FrameBridgeProtocol.TimestampTicksOffset, DateTime.UtcNow.Ticks);
        accessor.Write(FrameBridgeProtocol.RequestSequenceOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedSequenceOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedFlagsOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedFlagsOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedExposureUsOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.AppliedExposureUsOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.ExposureMinUsOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.ExposureMaxUsOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.ExposureIncUsOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.RequestedGainDbOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.AppliedGainDbOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.GainMinDbOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.GainMaxDbOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.GainIncDbOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.RequestedFrameRateHzOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.AppliedFrameRateHzOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.FrameRateMinHzOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.FrameRateMaxHzOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.FrameRateIncHzOffset, 0.0);
        accessor.Write(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedCapturePixelFormatOffset, FrameBridgeProtocol.CapturePixelFormatAuto);
        accessor.Write(FrameBridgeProtocol.AppliedCapturePixelFormatOffset, FrameBridgeProtocol.CapturePixelFormatAuto);
        accessor.Write(FrameBridgeProtocol.RequestedWidthOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedHeightOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedOffsetXOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedOffsetYOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedBinningXOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedBinningYOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedWidthOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedHeightOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedOffsetXOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedOffsetYOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedBinningXOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedBinningYOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedBlackLevelOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedBlackLevelOffset, 0);
        accessor.Write(FrameBridgeProtocol.RequestedMasterGainOffset, 0);
        accessor.Write(FrameBridgeProtocol.AppliedMasterGainOffset, 0);
        accessor.Write(FrameBridgeProtocol.GainBoostSupportedOffset, 0);
        accessor.Write(FrameBridgeProtocol.GainBoostEnabledOffset, 0);
    }

    private static IDisposable AcquireMutex(Mutex mutex, int timeoutMs)
    {
        try
        {
            if (!mutex.WaitOne(timeoutMs))
            {
                throw new TimeoutException("Timed out waiting for frame bridge mutex.");
            }
        }
        catch (AbandonedMutexException)
        {
            // Previous owner exited unexpectedly; mutex is still acquired for this thread.
        }

        return new Releaser(mutex);
    }

    private static bool IsUsbDevice(IGXDeviceInfo deviceInfo)
    {
        var deviceClass = deviceInfo.GetDeviceClass().ToString();
        return deviceClass.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
               deviceClass.Contains("U3", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindCameraSerial(int timeoutMs)
    {
        var factory = IGXFactory.GetInstance();
        var requestedSerial =
            Environment.GetEnvironmentVariable("BEAMMIC_DAHENG_SN") ??
            Environment.GetEnvironmentVariable("VIRTUAL_UEYE_DAHENG_SN");
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromMilliseconds(timeoutMs))
        {
            var devices = new List<IGXDeviceInfo>();
            factory.UpdateAllDeviceList(500, devices);
            var device = devices.FirstOrDefault(deviceInfo =>
                    IsUsbDevice(deviceInfo) &&
                    (string.IsNullOrWhiteSpace(requestedSerial) ||
                     string.Equals(deviceInfo.GetSN(), requestedSerial, StringComparison.OrdinalIgnoreCase)))
                ?? devices.FirstOrDefault(IsUsbDevice);
            if (device is not null)
            {
                return device.GetSN();
            }

            Thread.Sleep(100);
        }

        return null;
    }

    private static void LogCameraCapabilities(IGXFeatureControl remote, Action<string>? sink = null)
    {
        var pixelFormats = GetEnumEntries(remote, "PixelFormat");
        var currentPixelFormat = ReadEnumValue(remote, "PixelFormat");
        var supports12Bit = pixelFormats.Any(IsHighBitDepthPixelFormat);
        var exposure = ReadFloatState(remote, "ExposureTime");
        var gain = ReadFloatState(remote, "Gain");
        var frameRate = ReadFrameRateState(remote);
        var blackLevel = ReadBlackLevelState(remote);
        var reverseX = ReadBoolValue(remote, "ReverseX");
        var reverseY = ReadBoolValue(remote, "ReverseY");
        var width = ReadIntFeature(remote, "Width");
        var height = ReadIntFeature(remote, "Height");
        var offsetX = ReadIntFeature(remote, "OffsetX");
        var offsetY = ReadIntFeature(remote, "OffsetY");
        var binningX = Math.Max(ReadSamplingFactor(remote, "BinningHorizontal"), ReadSamplingFactor(remote, "DecimationHorizontal"));
        var binningY = Math.Max(ReadSamplingFactor(remote, "BinningVertical"), ReadSamplingFactor(remote, "DecimationVertical"));
        var blackLevelText = blackLevel.IsImplemented
            ? $" blackLevelDevice={blackLevel.Value:F2} range={blackLevel.Min:F2}..{blackLevel.Max:F2} inc={blackLevel.Increment:F3} beamMic={blackLevel.LogicalValue}"
            : " blackLevelDevice=NA";
        var currentFrameRateText = TryReadCurrentFrameRateHz(remote, out var currentFrameRateHz)
            ? $" currentFrameRateHz={currentFrameRateHz:F2}"
            : string.Empty;
        var line = $"capabilities currentPixelFormat={currentPixelFormat} supports12bit={supports12Bit} pixelFormats=[{string.Join(", ", pixelFormats)}] size={width}x{height} offset={offsetX},{offsetY} sampling={binningX}x{binningY} reverse={reverseX},{reverseY} exposureUs={exposure.Value:F2} range={exposure.Min:F2}..{exposure.Max:F2} gainDb={gain.Value:F2} range={gain.Min:F2}..{gain.Max:F2}{blackLevelText} frameRateHz={frameRate.Value:F2} range={frameRate.Min:F2}..{frameRate.Max:F2} inc={frameRate.Increment:F3}{currentFrameRateText}";
        Log(line);
        sink?.Invoke(line);
    }

    private static void ProbeFrameRateForPixelFormats(IGXFeatureControl remote, Action<string>? sink = null)
    {
        var candidates = new[]
        {
            "Mono8",
            "Mono10",
            "Mono10_Packed",
            "Mono10_P",
            "Mono12",
            "Mono12_Packed",
            "Mono12_P"
        };

        var pixelFormat = ReadEnumValue(remote, "PixelFormat");
        foreach (var candidate in candidates)
        {
            if (!TrySetEnum(remote, "PixelFormat", candidate))
            {
                continue;
            }

            var state = ReadFrameRateState(remote);
            var currentFrameRateText = TryReadCurrentFrameRateHz(remote, out var currentFrameRateHz)
                ? $" currentFrameRate={currentFrameRateHz:F3}"
                : string.Empty;
            var line = $"pixelFormat={candidate} frameRate={state.Value:F3} range={state.Min:F3}..{state.Max:F3} inc={state.Increment:F3}{currentFrameRateText}";
            Log(line);
            sink?.Invoke(line);
        }

        if (!string.IsNullOrWhiteSpace(pixelFormat))
        {
            TrySetEnum(remote, "PixelFormat", pixelFormat);
        }
    }

    private static void ConfigurePixelFormat(IGXFeatureControl remote)
    {
        var requested = Environment.GetEnvironmentVariable("BEAMMIC_DAHENG_PIXEL_FORMAT");
        if (string.IsNullOrWhiteSpace(requested))
        {
            requested = "AutoHighBit";
        }

        var candidates = new List<string>();
        if (string.Equals(requested, "AutoHighBit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requested, "Auto12", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("Mono12");
            candidates.Add("Mono12_Packed");
            candidates.Add("Mono12_P");
            candidates.Add("Mono10");
            candidates.Add("Mono10_Packed");
            candidates.Add("Mono10_P");
            candidates.Add("Mono8");
        }
        else if (string.Equals(requested, "Mono12", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("Mono12");
            candidates.Add("Mono12_Packed");
            candidates.Add("Mono12_P");
            candidates.Add("Mono10");
            candidates.Add("Mono10_Packed");
            candidates.Add("Mono10_P");
            candidates.Add("Mono8");
        }
        else if (string.Equals(requested, "Mono10", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("Mono10");
            candidates.Add("Mono10_Packed");
            candidates.Add("Mono10_P");
            candidates.Add("Mono12");
            candidates.Add("Mono12_Packed");
            candidates.Add("Mono12_P");
            candidates.Add("Mono8");
        }
        else
        {
            candidates.Add(requested);
            if (!string.Equals(requested, "Mono8", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("Mono8");
            }
        }

        var selected = TrySetEnum(remote, "PixelFormat", candidates.ToArray())
            ? ReadEnumValue(remote, "PixelFormat")
            : "Mono8";

        Log($"selected pixel format: {selected}");
    }

    private static bool TrySetCapturePixelFormat(IGXFeatureControl remote, int capturePixelFormat)
    {
        var candidates = GetCapturePixelFormatCandidates(capturePixelFormat);
        if (candidates.Length == 0)
        {
            return false;
        }

        var success = TrySetEnum(remote, "PixelFormat", candidates);
        if (success)
        {
            Log($"requested capture pixel format={capturePixelFormat} selected={ReadEnumValue(remote, "PixelFormat")}");
        }

        return success;
    }

    private static string[] GetCapturePixelFormatCandidates(int capturePixelFormat)
    {
        return capturePixelFormat switch
        {
            FrameBridgeProtocol.CapturePixelFormatMono8 => ["Mono8"],
            FrameBridgeProtocol.CapturePixelFormatMono10 => ["Mono10", "Mono10_Packed", "Mono10_P"],
            FrameBridgeProtocol.CapturePixelFormatMono12 => ["Mono12", "Mono12_Packed", "Mono12_P"],
            FrameBridgeProtocol.CapturePixelFormatMono14 => ["Mono14", "Mono14_P"],
            FrameBridgeProtocol.CapturePixelFormatMono16 => ["Mono16"],
            _ => []
        };
    }

    private static bool TrySetEnum(IGXFeatureControl remote, string featureName, params string[] values)
    {
        if (!remote.IsImplemented(featureName))
        {
            return false;
        }

        try
        {
            var feature = remote.GetEnumFeature(featureName);
            foreach (var value in values)
            {
                try
                {
                    feature.SetValue(value);
                    return true;
                }
                catch
                {
                    // Try next candidate.
                }
            }
        }
        catch
        {
            // Ignore unsupported enum features.
        }

        return false;
    }

    private static string ReadEnumValue(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return string.Empty;
        }

        try
        {
            return remote.GetEnumFeature(featureName).GetValue();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ReadIntFeature(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return 0;
        }

        try
        {
            return checked((int)remote.GetIntFeature(featureName).GetValue());
        }
        catch
        {
            return 0;
        }
    }

    private static List<string> GetEnumEntries(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return new List<string>();
        }

        try
        {
            return remote.GetEnumFeature(featureName).GetEnumEntryList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool IsHighBitDepthPixelFormat(string value)
    {
        return string.Equals(value, "Mono10", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono10_Packed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono10_P", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono12", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono12_Packed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono12_P", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono14", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono14_P", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Mono16", StringComparison.OrdinalIgnoreCase);
    }

    private static int ToCapturePixelFormat(string value)
    {
        if (string.Equals(value, "Mono8", StringComparison.OrdinalIgnoreCase))
        {
            return FrameBridgeProtocol.CapturePixelFormatMono8;
        }

        if (string.Equals(value, "Mono10", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Mono10_Packed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Mono10_P", StringComparison.OrdinalIgnoreCase))
        {
            return FrameBridgeProtocol.CapturePixelFormatMono10;
        }

        if (string.Equals(value, "Mono12", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Mono12_Packed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Mono12_P", StringComparison.OrdinalIgnoreCase))
        {
            return FrameBridgeProtocol.CapturePixelFormatMono12;
        }

        if (string.Equals(value, "Mono14", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Mono14_P", StringComparison.OrdinalIgnoreCase))
        {
            return FrameBridgeProtocol.CapturePixelFormatMono14;
        }

        if (string.Equals(value, "Mono16", StringComparison.OrdinalIgnoreCase))
        {
            return FrameBridgeProtocol.CapturePixelFormatMono16;
        }

        return FrameBridgeProtocol.CapturePixelFormatAuto;
    }

    private static FloatState ReadFloatState(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return new FloatState(0.0, 0.0, 0.0, 0.0);
        }

        try
        {
            var feature = remote.GetFloatFeature(featureName);
            return new FloatState(
                Value: feature.GetValue(),
                Min: feature.GetMin(),
                Max: feature.GetMax(),
                Increment: TryGetFloatIncrement(feature));
        }
        catch
        {
            return new FloatState(0.0, 0.0, 0.0, 0.0);
        }
    }

    private static FloatState ReadFrameRateState(IGXFeatureControl remote)
    {
        return ReadFloatState(remote, "AcquisitionFrameRate");
    }

    private static bool TryReadCurrentFrameRateHz(IGXFeatureControl remote, out double currentValue)
    {
        currentValue = 0.0;
        if (!remote.IsImplemented("CurrentAcquisitionFrameRate"))
        {
            return false;
        }

        try
        {
            currentValue = remote.GetFloatFeature("CurrentAcquisitionFrameRate").GetValue();
            return double.IsFinite(currentValue) && currentValue > 0.0;
        }
        catch
        {
            return false;
        }
    }

    private static BlackLevelState ReadBlackLevelState(IGXFeatureControl remote)
    {
        if (remote.IsImplemented("BlackLevel"))
        {
            try
            {
                var feature = remote.GetFloatFeature("BlackLevel");
                var min = feature.GetMin();
                var max = feature.GetMax();
                var inc = TryGetFloatIncrement(feature);
                return BuildBlackLevelState(feature.GetValue(), min, max, inc);
            }
            catch
            {
                // Fall through to raw feature.
            }
        }

        if (remote.IsImplemented("BlackLevelRaw"))
        {
            try
            {
                var feature = remote.GetIntFeature("BlackLevelRaw");
                var value = feature.GetValue();
                var min = feature.GetMin();
                var max = feature.GetMax();
                var inc = Math.Max(1, feature.GetInc());
                return BuildBlackLevelState(value, min, max, inc);
            }
            catch
            {
                // Ignore unsupported raw black level.
            }
        }

        return default;
    }

    private static BlackLevelState BuildBlackLevelState(double value, double min, double max, double increment)
    {
        if (max <= min)
        {
            return new BlackLevelState(false, 0.0, 0.0, 0.0, 0.0, 0);
        }

        var targetMax = Math.Min(max, min + BeamMicBlackLevelDeviceMax);
        if (targetMax <= min)
        {
            return new BlackLevelState(false, value, min, max, increment, 0);
        }

        var clampedValue = Math.Clamp(value, min, targetMax);
        var logical = Math.Clamp((int)Math.Round((clampedValue - min) / (targetMax - min) * BeamMicBlackLevelMax), BeamMicBlackLevelMin, BeamMicBlackLevelMax);
        return new BlackLevelState(true, clampedValue, min, targetMax, increment, logical);
    }

    private static bool TrySetBlackLevel(IGXFeatureControl remote, int beamMicBlackLevel)
    {
        var state = ReadBlackLevelState(remote);
        if (!state.IsImplemented)
        {
            return false;
        }

        TrySetEnum(remote, "BlackLevelAuto", "Off");
        TrySetEnum(remote, "BlackLevelSelector", "All");

        var logical = Math.Clamp(beamMicBlackLevel, BeamMicBlackLevelMin, BeamMicBlackLevelMax);
        var deviceValue = state.Min + (logical / (double)BeamMicBlackLevelMax) * (state.Max - state.Min);

        if (remote.IsImplemented("BlackLevel"))
        {
            try
            {
                remote.GetFloatFeature("BlackLevel").SetValue(deviceValue);
                return true;
            }
            catch
            {
                // Fall through to raw feature.
            }
        }

        if (remote.IsImplemented("BlackLevelRaw"))
        {
            try
            {
                var feature = remote.GetIntFeature("BlackLevelRaw");
                feature.SetValue(AlignIntFeatureValue(
                    (long)Math.Round(deviceValue),
                    feature.GetMin(),
                    feature.GetMax(),
                    feature.GetInc()));
                return true;
            }
            catch
            {
                // Ignore unsupported raw black level.
            }
        }

        return false;
    }

    private static int ReadSamplingFactor(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return 1;
        }

        try
        {
            try
            {
                var rawValue = remote.GetEnumFeature(featureName).GetValue();
                if (TryParseSamplingFactor(rawValue, out var parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // Fall through to integer read.
            }

            try
            {
                return Math.Max(1, checked((int)remote.GetIntFeature(featureName).GetValue()));
            }
            catch
            {
                return 1;
            }
        }
        catch
        {
            return 1;
        }
    }

    private static bool TryParseSamplingFactor(string? rawValue, out int factor)
    {
        factor = 1;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var digits = new string(rawValue.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out factor) && factor > 0;
    }

    private static void TrySetFloat(IGXFeatureControl remote, string featureName, double value)
    {
        if (!remote.IsImplemented(featureName))
        {
            return;
        }

        try
        {
            var feature = remote.GetFloatFeature(featureName);
            feature.SetValue(Math.Clamp(value, feature.GetMin(), feature.GetMax()));
        }
        catch
        {
            // Ignore unsupported float features.
        }
    }

    private static void TrySetFrameRate(IGXFeatureControl remote, double value)
    {
        if (!remote.IsImplemented("AcquisitionFrameRate"))
        {
            return;
        }

        try
        {
            var feature = remote.GetFloatFeature("AcquisitionFrameRate");
            var min = feature.GetMin();
            var max = feature.GetMax();
            if (!double.IsFinite(min) || !double.IsFinite(max) || max <= 0.0 || max < min)
            {
                return;
            }

            feature.SetValue(Math.Clamp(value, min, max));
        }
        catch
        {
            // Some Daheng models expose the node but do not allow writing it.
        }
    }

    private static void TrySetBool(IGXFeatureControl remote, string featureName, bool value)
    {
        if (!remote.IsImplemented(featureName))
        {
            return;
        }

        try
        {
            remote.GetBoolFeature(featureName).SetValue(value);
        }
        catch
        {
            // Ignore unsupported bool features.
        }
    }

    private static bool ReadBoolValue(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            return false;
        }

        try
        {
            return remote.GetBoolFeature(featureName).GetValue();
        }
        catch
        {
            return false;
        }
    }

    private static void TrySetAutoMode(IGXFeatureControl remote, string featureName, bool enabled)
    {
        TrySetEnum(remote, featureName, enabled ? "Continuous" : "Off");
    }

    private static void TryExecuteCommand(IGXFeatureControl? remote, string featureName)
    {
        if (remote is null || !remote.IsImplemented(featureName))
        {
            return;
        }

        remote.GetCommandFeature(featureName).Execute();
    }

    private static void ExecuteCommand(IGXFeatureControl remote, string featureName)
    {
        if (!remote.IsImplemented(featureName))
        {
            throw new InvalidOperationException($"Required command is not implemented: {featureName}");
        }

        try
        {
            remote.GetCommandFeature(featureName).Execute();
        }
        catch (Exception ex) when (featureName == "AcquisitionStart" && ex.ToString().Contains("STATUS_IN_WORK", StringComparison.OrdinalIgnoreCase))
        {
            // Some USB2 cameras report STATUS_IN_WORK when acquisition is already armed.
        }
    }

    private static bool IsTimeout(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("258", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("time out", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsbTransportError(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("-1010", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("TL Error", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("IO error", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("USB::", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("0x1b1", StringComparison.OrdinalIgnoreCase);
    }

    private static GX_VALID_BIT_LIST GetValidBits(GX_PIXEL_FORMAT_ENTRY pixelFormat)
    {
        return pixelFormat switch
        {
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10 => GX_VALID_BIT_LIST.GX_BIT_2_9,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_P => GX_VALID_BIT_LIST.GX_BIT_2_9,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO10_PACKED => GX_VALID_BIT_LIST.GX_BIT_2_9,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12 => GX_VALID_BIT_LIST.GX_BIT_4_11,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_P => GX_VALID_BIT_LIST.GX_BIT_4_11,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO12_PACKED => GX_VALID_BIT_LIST.GX_BIT_4_11,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14 => GX_VALID_BIT_LIST.GX_BIT_6_13,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO14_P => GX_VALID_BIT_LIST.GX_BIT_6_13,
            GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO16 => GX_VALID_BIT_LIST.GX_BIT_8_15,
            _ => GX_VALID_BIT_LIST.GX_BIT_0_7
        };
    }

    private static double GetEnvironmentDouble(string name, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return double.TryParse(raw, out var value) ? value : fallback;
    }

    private static bool IsSimulationForced()
    {
        return GetEnvironmentBool("ULTRON_RAYCI_SIMULATE", GetEnvironmentBool("BEAMMIC_DAHENG_SIMULATE", false));
    }

    private static bool IsAutoSimulationFallbackEnabled()
    {
        return GetEnvironmentBool("ULTRON_RAYCI_AUTO_SIMULATE", GetEnvironmentBool("BEAMMIC_DAHENG_AUTO_SIMULATE", true));
    }

    private static bool GetEnvironmentBool(string name, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw is "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON";
    }

    private static int GetEnvironmentInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static SimulationPattern GetSimulationPattern()
    {
        var raw =
            Environment.GetEnvironmentVariable("ULTRON_RAYCI_SIM_PATTERN") ??
            Environment.GetEnvironmentVariable("BEAMMIC_DAHENG_SIM_PATTERN");

        return raw?.Trim().ToLowerInvariant() switch
        {
            "noise" => SimulationPattern.WhiteNoise,
            "white-noise" => SimulationPattern.WhiteNoise,
            "white_noise" => SimulationPattern.WhiteNoise,
            "whitenoise" => SimulationPattern.WhiteNoise,
            _ => SimulationPattern.BeamTarget
        };
    }

    private static string DescribeSimulationPattern(SimulationPattern pattern)
    {
        return pattern switch
        {
            SimulationPattern.WhiteNoise => "white-noise",
            _ => "beam-target"
        };
    }

    private static BridgeControlState ReadCurrentControlState(IGXFeatureControl remote)
    {
        var exposureState = ReadFloatState(remote, "ExposureTime");
        var gainState = ReadFloatState(remote, "Gain");
        var frameRateState = ReadFrameRateState(remote);
        var blackLevelState = ReadBlackLevelState(remote);
        var currentPixelFormat = ReadEnumValue(remote, "PixelFormat");
        var currentWidth = Math.Max(1, ReadIntFeature(remote, "Width"));
        var currentHeight = Math.Max(1, ReadIntFeature(remote, "Height"));
        var offsetX = Math.Max(0, ReadIntFeature(remote, "OffsetX"));
        var offsetY = Math.Max(0, ReadIntFeature(remote, "OffsetY"));
        var samplingX = Math.Max(ReadSamplingFactor(remote, "BinningHorizontal"), ReadSamplingFactor(remote, "DecimationHorizontal"));
        var samplingY = Math.Max(ReadSamplingFactor(remote, "BinningVertical"), ReadSamplingFactor(remote, "DecimationVertical"));
        var pixelFormatFlags = GetEnumEntries(remote, "PixelFormat").Any(IsHighBitDepthPixelFormat)
            ? FrameBridgeProtocol.PixelFormatCapabilitySupports12BitFlag
            : 0;
        var flags = 0;

        if (IsEnumValue(remote, "ExposureAuto", "Continuous"))
        {
            flags |= FrameBridgeProtocol.ControlFlagAutoExposure;
        }

        if (IsEnumValue(remote, "GainAuto", "Continuous"))
        {
            flags |= FrameBridgeProtocol.ControlFlagAutoGain;
        }

        return new BridgeControlState(
            Flags: flags,
            ExposureUs: exposureState.Value,
            ExposureMinUs: exposureState.Min,
            ExposureMaxUs: exposureState.Max,
            ExposureIncUs: exposureState.Increment,
            GainDb: gainState.Value,
            GainMinDb: gainState.Min,
            GainMaxDb: gainState.Max,
            GainIncDb: gainState.Increment,
            BlackLevel: blackLevelState.IsImplemented ? blackLevelState.LogicalValue : 49,
            FrameRateHz: frameRateState.Value,
            FrameRateMinHz: frameRateState.Min,
            FrameRateMaxHz: frameRateState.Max,
            FrameRateIncHz: frameRateState.Increment,
            CapturePixelFormat: ToCapturePixelFormat(currentPixelFormat),
            PixelFormatCapabilityFlags: pixelFormatFlags,
            MasterGain: ToMasterGain(gainState.Value, gainState.Min, gainState.Max),
            Width: currentWidth * Math.Max(1, samplingX),
            Height: currentHeight * Math.Max(1, samplingY),
            OffsetX: offsetX * Math.Max(1, samplingX),
            OffsetY: offsetY * Math.Max(1, samplingY),
            BinningX: Math.Max(1, samplingX),
            BinningY: Math.Max(1, samplingY));
    }

    private static double TryGetFloatIncrement(object feature)
    {
        try
        {
            var method = feature.GetType().GetMethod("GetInc");
            if (method?.Invoke(feature, null) is double value)
            {
                return value;
            }
        }
        catch
        {
            // Ignore reflection failures.
        }

        return 0.0;
    }

    private static bool IsEnumValue(IGXFeatureControl remote, string featureName, string expected)
    {
        if (!remote.IsImplemented(featureName))
        {
            return false;
        }

        try
        {
            return string.Equals(
                remote.GetEnumFeature(featureName).GetValue(),
                expected,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int ToMasterGain(double gainDb, double minGainDb, double maxGainDb)
    {
        if (maxGainDb <= minGainDb)
        {
            return 0;
        }

        var normalized = (gainDb - minGainDb) / (maxGainDb - minGainDb);
        return Math.Clamp((int)Math.Round(normalized * 100.0), 0, 100);
    }

    private static double ToGainDb(int masterGain, double minGainDb, double maxGainDb)
    {
        if (maxGainDb <= minGainDb)
        {
            return minGainDb;
        }

        var normalized = Math.Clamp(masterGain, 0, 100) / 100.0;
        return minGainDb + normalized * (maxGainDb - minGainDb);
    }

    private static unsafe void ResizeToTarget(ushort* source, int sourceWidth, int sourceHeight, byte[] destination)
    {
        fixed (byte* destinationBytes = destination)
        {
            var destinationWords = (ushort*)destinationBytes;
            for (var y = 0; y < FrameBridgeProtocol.TargetHeight; y++)
            {
                var sourceY = y * sourceHeight / FrameBridgeProtocol.TargetHeight;
                var sourceRow = source + (sourceY * sourceWidth);
                var destinationRow = destinationWords + (y * FrameBridgeProtocol.TargetWidth);
                for (var x = 0; x < FrameBridgeProtocol.TargetWidth; x++)
                {
                    var sourceX = x * sourceWidth / FrameBridgeProtocol.TargetWidth;
                    destinationRow[x] = sourceRow[sourceX];
                }
            }
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not crash the helper.
        }
    }

    private sealed class Releaser(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if the mutex is no longer owned.
            }
        }
    }

    private readonly record struct CaptureSource(bool UseSimulation, string? Serial);
    private readonly record struct FloatState(double Value, double Min, double Max, double Increment);
    private readonly record struct BlackLevelState(bool IsImplemented, double Value, double Min, double Max, double Increment, int LogicalValue);
    private enum SimulationPattern
    {
        BeamTarget,
        WhiteNoise
    }

    private readonly record struct BridgeControlState(
        int Flags,
        double ExposureUs,
        double ExposureMinUs,
        double ExposureMaxUs,
        double ExposureIncUs,
        double GainDb,
        double GainMinDb,
        double GainMaxDb,
        double GainIncDb,
        int BlackLevel,
        double FrameRateHz,
        double FrameRateMinHz,
        double FrameRateMaxHz,
        double FrameRateIncHz,
        int CapturePixelFormat,
        int PixelFormatCapabilityFlags,
        int MasterGain,
        int Width,
        int Height,
        int OffsetX,
        int OffsetY,
        int BinningX,
        int BinningY);
}
