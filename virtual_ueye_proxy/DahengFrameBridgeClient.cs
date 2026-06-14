using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using RayCiBridge;

namespace VirtualUEyeProxy;

internal static unsafe class DahengFrameBridgeClient
{
    private const string HelperOverrideEnvVar = "ULTRON_RAYCI_BRIDGE_HELPER";
    private const string HelperSubdirectoryName = "DahengBridgeHelper";
    private static readonly TimeSpan HelperStartupGracePeriod = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FrameProbeInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan CachedFrameReuseWindow = TimeSpan.FromSeconds(1);
    private static readonly object Gate = new();
    private static readonly byte[] ScratchBuffer = new byte[FrameBridgeProtocol.FrameByteCount];
    private static MemoryMappedFile? _map;
    private static MemoryMappedViewAccessor? _accessor;
    private static Mutex? _mutex;
    private static Process? _helperProcess;
    private static string? _helperProcessPath;
    private static nint _helperJobHandle;
    private static DateTime _lastHelperStartAttemptUtc = DateTime.MinValue;
    private static DateTime _lastFrameProbeUtc = DateTime.MinValue;
    private static DateTime _lastFrameRefreshUtc = DateTime.MinValue;
    private static long _cachedFrameId;
    private static int _cachedFrameWidth;
    private static int _cachedFrameHeight;
    private static int _cachedFramePayloadLength;
    private static bool _hasLoggedStreaming;

    static DahengFrameBridgeClient()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupHelperLifetime("ProcessExit");
        AppDomain.CurrentDomain.DomainUnload += (_, _) => CleanupHelperLifetime("DomainUnload");
    }

    public static bool TryCopyLatestFrame(ImageMemory memory, out long bridgeFrameId)
    {
        bridgeFrameId = 0;

        if ((!UsesWideMonochromeContainer(memory.BitsPerPixel) && memory.BitsPerPixel != 8) ||
            memory.Width <= 0 ||
            memory.Height <= 0 ||
            memory.Pitch <= 0)
        {
            return false;
        }

        lock (Gate)
        {
            var bridgeReady = EnsureBridgeReadyLocked();
            if (bridgeReady && ShouldProbeBridgeFrameLocked(DateTime.UtcNow))
            {
                bridgeReady = TryRefreshCachedFrameLocked(DateTime.UtcNow);
            }

            if (!bridgeReady && !HasReusableCachedFrameLocked(DateTime.UtcNow))
            {
                return false;
            }

            bridgeFrameId = _cachedFrameId;
            WriteIntoImageMemory(memory, ScratchBuffer, _cachedFrameWidth, _cachedFrameHeight);

            if (!_hasLoggedStreaming)
            {
                _hasLoggedStreaming = true;
                VirtualCameraState.Log($"Daheng bridge streaming, frameId={bridgeFrameId}");
            }

            return true;
        }
    }

    public static bool TryGetControlState(out BridgeControlState state)
    {
        lock (Gate)
        {
            if (!EnsureBridgeReadyLocked())
            {
                state = default;
                return false;
            }

            return TryReadControlStateLocked(out state);
        }
    }

    public static bool TrySetExposureMs(double exposureMs, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with { ExposureUs = Math.Max(0.0, exposureMs * 1000.0) },
            out state);

    public static bool TrySetAutoExposure(bool enabled, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with
            {
                Flags = enabled
                    ? request.Flags | FrameBridgeProtocol.ControlFlagAutoExposure
                    : request.Flags & ~FrameBridgeProtocol.ControlFlagAutoExposure
            },
            out state);

    public static bool TrySetAutoGain(bool enabled, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with
            {
                Flags = enabled
                    ? request.Flags | FrameBridgeProtocol.ControlFlagAutoGain
                    : request.Flags & ~FrameBridgeProtocol.ControlFlagAutoGain
            },
            out state);

    public static bool TrySetMasterGain(int masterGain, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with
            {
                MasterGain = Math.Clamp(masterGain, 0, 100),
                GainDb = ToGainDb(Math.Clamp(masterGain, 0, 100), request.GainMinDb, request.GainMaxDb)
            },
            out state);

    public static bool TrySetFrameRate(double frameRateHz, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with { FrameRateHz = Math.Max(0.0, frameRateHz) },
            out state);

    public static bool TrySetGeometry(int width, int height, int offsetX, int offsetY, int binningX, int binningY, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with
            {
                Width = Math.Max(0, width),
                Height = Math.Max(0, height),
                OffsetX = Math.Max(0, offsetX),
                OffsetY = Math.Max(0, offsetY),
                BinningX = Math.Max(1, binningX),
                BinningY = Math.Max(1, binningY)
            },
            out state);

    public static bool TrySetBlackLevel(int blackLevel, out BridgeControlState state)
        => TryUpdateControlRequest(
            request => request with { BlackLevel = Math.Clamp(blackLevel, 0, 255) },
            out state);

    private static bool TryUpdateControlRequest(Func<ControlRequest, ControlRequest> updater, out BridgeControlState state)
    {
        lock (Gate)
        {
            if (!EnsureBridgeReadyLocked())
            {
                state = default;
                return false;
            }

            int nextSequence;
            using (var holder = AcquireMutexLocked(100))
            {
                if (holder is null || _accessor is null)
                {
                    state = default;
                    return false;
                }

                var currentRequest = ReadCurrentRequestLocked();
                var nextRequest = updater(currentRequest);
                nextSequence = Math.Max(_accessor.ReadInt32(FrameBridgeProtocol.RequestSequenceOffset), 0) + 1;

                _accessor.Write(FrameBridgeProtocol.RequestedFlagsOffset, nextRequest.Flags);
                _accessor.Write(FrameBridgeProtocol.RequestedExposureUsOffset, nextRequest.ExposureUs);
                _accessor.Write(FrameBridgeProtocol.RequestedGainDbOffset, nextRequest.GainDb);
                _accessor.Write(FrameBridgeProtocol.RequestedFrameRateHzOffset, nextRequest.FrameRateHz);
                _accessor.Write(FrameBridgeProtocol.RequestedMasterGainOffset, nextRequest.MasterGain);
                _accessor.Write(FrameBridgeProtocol.RequestedCapturePixelFormatOffset, nextRequest.CapturePixelFormat);
                _accessor.Write(FrameBridgeProtocol.RequestedWidthOffset, nextRequest.Width);
                _accessor.Write(FrameBridgeProtocol.RequestedHeightOffset, nextRequest.Height);
                _accessor.Write(FrameBridgeProtocol.RequestedOffsetXOffset, nextRequest.OffsetX);
                _accessor.Write(FrameBridgeProtocol.RequestedOffsetYOffset, nextRequest.OffsetY);
                _accessor.Write(FrameBridgeProtocol.RequestedBinningXOffset, nextRequest.BinningX);
                _accessor.Write(FrameBridgeProtocol.RequestedBinningYOffset, nextRequest.BinningY);
                _accessor.Write(FrameBridgeProtocol.RequestedBlackLevelOffset, nextRequest.BlackLevel);
                _accessor.Write(FrameBridgeProtocol.RequestSequenceOffset, nextSequence);
                _accessor.Flush();

                state = new BridgeControlState(
                    ExposureMs: nextRequest.ExposureUs / 1000.0,
                    ExposureMinMs: nextRequest.ExposureMinUs / 1000.0,
                    ExposureMaxMs: nextRequest.ExposureMaxUs / 1000.0,
                    ExposureIncMs: nextRequest.ExposureIncUs / 1000.0,
                    GainDb: nextRequest.GainDb,
                    GainMinDb: nextRequest.GainMinDb,
                    GainMaxDb: nextRequest.GainMaxDb,
                    GainIncDb: nextRequest.GainIncDb,
                    FrameRateHz: nextRequest.FrameRateHz,
                    FrameRateMinHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateMinHzOffset),
                    FrameRateMaxHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateMaxHzOffset),
                    FrameRateIncHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateIncHzOffset),
                    MasterGain: nextRequest.MasterGain,
                    AppliedFlags: nextRequest.Flags,
                    RequestedFlags: nextRequest.Flags,
                    AppliedSequence: _accessor.ReadInt32(FrameBridgeProtocol.AppliedSequenceOffset),
                    RequestSequence: nextSequence,
                    GainBoostSupported: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostSupportedOffset) != 0,
                    GainBoostEnabled: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostEnabledOffset) != 0,
                    PixelFormatCapabilityFlags: _accessor.ReadInt32(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset),
                    CapturePixelFormat: nextRequest.CapturePixelFormat,
                    Width: nextRequest.Width,
                    Height: nextRequest.Height,
                    OffsetX: nextRequest.OffsetX,
                    OffsetY: nextRequest.OffsetY,
                    BinningX: nextRequest.BinningX,
                    BinningY: nextRequest.BinningY,
                    BlackLevel: nextRequest.BlackLevel);
            }

            return TryWaitForAppliedSequenceLocked(nextSequence, out state);
        }
    }

    private static bool TryWaitForAppliedSequenceLocked(int sequence, out BridgeControlState state)
    {
        state = default;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var holder = AcquireMutexLocked(100);
            if (holder is null || _accessor is null)
            {
                return false;
            }

            var appliedSequence = _accessor.ReadInt32(FrameBridgeProtocol.AppliedSequenceOffset);
            holder.Dispose();

            if (appliedSequence >= sequence && TryReadControlStateLocked(out state))
            {
                return true;
            }

            Monitor.Exit(Gate);
            try
            {
                Thread.Sleep(10);
            }
            finally
            {
                Monitor.Enter(Gate);
            }
        }

        return TryReadControlStateLocked(out state);
    }

    private static bool TryReadControlStateLocked(out BridgeControlState state)
    {
        using var holder = AcquireMutexLocked(100);
        if (holder is null || _accessor is null)
        {
            state = default;
            return false;
        }

        if (_accessor.ReadInt32(FrameBridgeProtocol.MagicOffset) != FrameBridgeProtocol.Magic ||
            _accessor.ReadInt32(FrameBridgeProtocol.VersionOffset) != FrameBridgeProtocol.Version)
        {
            state = default;
            return false;
        }

        state = new BridgeControlState(
            ExposureMs: _accessor.ReadDouble(FrameBridgeProtocol.AppliedExposureUsOffset) / 1000.0,
            ExposureMinMs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureMinUsOffset) / 1000.0,
            ExposureMaxMs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureMaxUsOffset) / 1000.0,
            ExposureIncMs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureIncUsOffset) / 1000.0,
            GainDb: _accessor.ReadDouble(FrameBridgeProtocol.AppliedGainDbOffset),
            GainMinDb: _accessor.ReadDouble(FrameBridgeProtocol.GainMinDbOffset),
            GainMaxDb: _accessor.ReadDouble(FrameBridgeProtocol.GainMaxDbOffset),
            GainIncDb: _accessor.ReadDouble(FrameBridgeProtocol.GainIncDbOffset),
            FrameRateHz: _accessor.ReadDouble(FrameBridgeProtocol.AppliedFrameRateHzOffset),
            FrameRateMinHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateMinHzOffset),
            FrameRateMaxHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateMaxHzOffset),
            FrameRateIncHz: _accessor.ReadDouble(FrameBridgeProtocol.FrameRateIncHzOffset),
            MasterGain: _accessor.ReadInt32(FrameBridgeProtocol.AppliedMasterGainOffset),
            AppliedFlags: _accessor.ReadInt32(FrameBridgeProtocol.AppliedFlagsOffset),
            RequestedFlags: _accessor.ReadInt32(FrameBridgeProtocol.RequestedFlagsOffset),
            AppliedSequence: _accessor.ReadInt32(FrameBridgeProtocol.AppliedSequenceOffset),
            RequestSequence: _accessor.ReadInt32(FrameBridgeProtocol.RequestSequenceOffset),
            GainBoostSupported: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostSupportedOffset) != 0,
            GainBoostEnabled: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostEnabledOffset) != 0,
            PixelFormatCapabilityFlags: _accessor.ReadInt32(FrameBridgeProtocol.PixelFormatCapabilityFlagsOffset),
            CapturePixelFormat: _accessor.ReadInt32(FrameBridgeProtocol.AppliedCapturePixelFormatOffset),
            Width: _accessor.ReadInt32(FrameBridgeProtocol.AppliedWidthOffset),
            Height: _accessor.ReadInt32(FrameBridgeProtocol.AppliedHeightOffset),
            OffsetX: _accessor.ReadInt32(FrameBridgeProtocol.AppliedOffsetXOffset),
            OffsetY: _accessor.ReadInt32(FrameBridgeProtocol.AppliedOffsetYOffset),
            BinningX: Math.Max(1, _accessor.ReadInt32(FrameBridgeProtocol.AppliedBinningXOffset)),
            BinningY: Math.Max(1, _accessor.ReadInt32(FrameBridgeProtocol.AppliedBinningYOffset)),
            BlackLevel: _accessor.ReadInt32(FrameBridgeProtocol.AppliedBlackLevelOffset));
        return true;
    }

    private static bool ShouldProbeBridgeFrameLocked(DateTime nowUtc)
        => _cachedFrameId == 0 || nowUtc - _lastFrameProbeUtc >= FrameProbeInterval;

    private static bool TryRefreshCachedFrameLocked(DateTime nowUtc)
    {
        _lastFrameProbeUtc = nowUtc;

        using var holder = AcquireMutexLocked(100);
        if (holder is null || _accessor is null)
        {
            return false;
        }

        if (!TryReadFrameHeaderLocked(out var frameHeader))
        {
            return false;
        }

        if (frameHeader.PayloadLength > ScratchBuffer.Length)
        {
            return false;
        }

        if (frameHeader.FrameId != _cachedFrameId ||
            frameHeader.Width != _cachedFrameWidth ||
            frameHeader.Height != _cachedFrameHeight ||
            frameHeader.PayloadLength != _cachedFramePayloadLength)
        {
            _accessor.ReadArray(FrameBridgeProtocol.HeaderSize, ScratchBuffer, 0, frameHeader.PayloadLength);
            _cachedFrameId = frameHeader.FrameId;
            _cachedFrameWidth = frameHeader.Width;
            _cachedFrameHeight = frameHeader.Height;
            _cachedFramePayloadLength = frameHeader.PayloadLength;
        }

        _lastFrameRefreshUtc = nowUtc;
        return _cachedFrameId > 0;
    }

    private static bool HasReusableCachedFrameLocked(DateTime nowUtc)
        => _cachedFrameId > 0 &&
           _cachedFrameWidth > 0 &&
           _cachedFrameHeight > 0 &&
           _cachedFramePayloadLength > 0 &&
           nowUtc - _lastFrameRefreshUtc <= CachedFrameReuseWindow;

    private static ControlRequest ReadCurrentRequestLocked()
    {
        if (_accessor is null)
        {
            return default;
        }

        return new ControlRequest(
            Flags: _accessor.ReadInt32(FrameBridgeProtocol.RequestedFlagsOffset),
            ExposureUs: _accessor.ReadDouble(FrameBridgeProtocol.RequestedExposureUsOffset),
            GainDb: _accessor.ReadDouble(FrameBridgeProtocol.RequestedGainDbOffset),
            GainMinDb: _accessor.ReadDouble(FrameBridgeProtocol.GainMinDbOffset),
            GainMaxDb: _accessor.ReadDouble(FrameBridgeProtocol.GainMaxDbOffset),
            GainIncDb: _accessor.ReadDouble(FrameBridgeProtocol.GainIncDbOffset),
            ExposureMinUs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureMinUsOffset),
            ExposureMaxUs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureMaxUsOffset),
            ExposureIncUs: _accessor.ReadDouble(FrameBridgeProtocol.ExposureIncUsOffset),
            FrameRateHz: _accessor.ReadDouble(FrameBridgeProtocol.RequestedFrameRateHzOffset),
            MasterGain: _accessor.ReadInt32(FrameBridgeProtocol.RequestedMasterGainOffset),
            CapturePixelFormat: _accessor.ReadInt32(FrameBridgeProtocol.RequestedCapturePixelFormatOffset),
            Width: _accessor.ReadInt32(FrameBridgeProtocol.RequestedWidthOffset),
            Height: _accessor.ReadInt32(FrameBridgeProtocol.RequestedHeightOffset),
            OffsetX: _accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetXOffset),
            OffsetY: _accessor.ReadInt32(FrameBridgeProtocol.RequestedOffsetYOffset),
            BinningX: Math.Max(1, _accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningXOffset)),
            BinningY: Math.Max(1, _accessor.ReadInt32(FrameBridgeProtocol.RequestedBinningYOffset)),
            BlackLevel: _accessor.ReadInt32(FrameBridgeProtocol.RequestedBlackLevelOffset));
    }

    private static bool TryReadFrameHeaderLocked(out FrameHeader frameHeader)
    {
        frameHeader = default;
        if (_accessor is null)
        {
            return false;
        }

        var magic = _accessor.ReadInt32(FrameBridgeProtocol.MagicOffset);
        var version = _accessor.ReadInt32(FrameBridgeProtocol.VersionOffset);
        var status = _accessor.ReadInt32(FrameBridgeProtocol.StatusOffset);
        var width = _accessor.ReadInt32(FrameBridgeProtocol.WidthOffset);
        var height = _accessor.ReadInt32(FrameBridgeProtocol.HeightOffset);
        var payloadLength = _accessor.ReadInt32(FrameBridgeProtocol.PayloadLengthOffset);
        var frameId = _accessor.ReadInt64(FrameBridgeProtocol.FrameIdOffset);
        var ticks = _accessor.ReadInt64(FrameBridgeProtocol.TimestampTicksOffset);

        if (magic != FrameBridgeProtocol.Magic ||
            version != FrameBridgeProtocol.Version ||
            status != FrameBridgeProtocol.StatusStreaming ||
            width <= 0 ||
            height <= 0 ||
            payloadLength != width * height * 2 ||
            payloadLength > ScratchBuffer.Length ||
            frameId <= 0 ||
            ticks <= 0)
        {
            return false;
        }

        if (new DateTime(ticks, DateTimeKind.Utc) < DateTime.UtcNow.AddSeconds(-3))
        {
            return false;
        }

        frameHeader = new FrameHeader(frameId, width, height, payloadLength);
        return true;
    }

    private static void WriteIntoImageMemory(ImageMemory memory, byte[] sourceFrame, int sourceWidth, int sourceHeight)
    {
        fixed (byte* sourceBytes = sourceFrame)
        {
            var sourceStrideBytes = sourceWidth * sizeof(ushort);
            var source16 = (ushort*)sourceBytes;

            if (UsesWideMonochromeContainer(memory.BitsPerPixel))
            {
                if (memory.Width == sourceWidth && memory.Height == sourceHeight)
                {
                    if (memory.Pitch == sourceStrideBytes)
                    {
                        Buffer.MemoryCopy(
                            sourceBytes,
                            (void*)memory.Pointer,
                            memory.SizeBytes,
                            sourceHeight * sourceStrideBytes);
                        return;
                    }

                    for (var y = 0; y < sourceHeight; y++)
                    {
                        Buffer.MemoryCopy(
                            sourceBytes + (y * sourceStrideBytes),
                            (void*)((byte*)memory.Pointer + (y * memory.Pitch)),
                            memory.Pitch,
                            sourceStrideBytes);
                    }

                    return;
                }

                for (var y = 0; y < memory.Height; y++)
                {
                    var sourceY = y * sourceHeight / memory.Height;
                    var destinationRow = new Span<ushort>((void*)((byte*)memory.Pointer + (y * memory.Pitch)), memory.Pitch / sizeof(ushort));
                    var pixelsToWrite = Math.Min(memory.Width, destinationRow.Length);
                    for (var x = 0; x < pixelsToWrite; x++)
                    {
                        var sourceX = x * sourceWidth / memory.Width;
                        destinationRow[x] = source16[(sourceY * sourceWidth) + sourceX];
                    }
                }

                return;
            }

            var destination = new Span<byte>((void*)memory.Pointer, memory.SizeBytes);
            for (var y = 0; y < memory.Height; y++)
            {
                var sourceY = y * sourceHeight / memory.Height;
                var destinationRow = destination.Slice(y * memory.Pitch, Math.Min(memory.Width, memory.Pitch));
                for (var x = 0; x < memory.Width && x < destinationRow.Length; x++)
                {
                    var sourceX = x * sourceWidth / memory.Width;
                    destinationRow[x] = (byte)(source16[(sourceY * sourceWidth) + sourceX] >> 8);
                }
            }
        }
    }

    private static bool UsesWideMonochromeContainer(int bitsPerPixel)
        => UeyeNative.GetBytesPerPixel(bitsPerPixel) >= 2;

    private static bool EnsureBridgeReadyLocked()
    {
        if (!EnsureBridgeOpenedLocked())
        {
            if (EnsureHelperStartedLocked())
            {
                Thread.Sleep(HelperStartupGracePeriod);
            }

            return EnsureBridgeOpenedLocked();
        }

        return true;
    }

    private static bool EnsureHelperStartedLocked()
    {
        if (IsTrackedHelperRunningLocked())
        {
            return true;
        }

        if (DateTime.UtcNow - _lastHelperStartAttemptUtc < TimeSpan.FromSeconds(5))
        {
            return false;
        }

        _lastHelperStartAttemptUtc = DateTime.UtcNow;
        foreach (var candidate in EnumerateHelperCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (TryAttachExistingHelperProcessLocked(candidate))
            {
                return true;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    WorkingDirectory = Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                if (process is null)
                {
                    VirtualCameraState.Log($"Process.Start returned null for Daheng helper: {candidate}");
                    continue;
                }

                ReplaceTrackedHelperProcessLocked(process, candidate);
                var boundToJob = TryBindTrackedHelperToJobLocked(process);
                VirtualCameraState.Log($"Started Daheng frame helper: {candidate}, pid={SafeGetProcessId(process)}, jobBound={boundToJob}");
                return true;
            }
            catch (Exception ex)
            {
                VirtualCameraState.Log($"Failed to start Daheng helper '{candidate}': {ex.Message}");

                if (TryAttachExistingHelperProcessLocked(candidate))
                {
                    return true;
                }
            }
        }

        VirtualCameraState.Log("No Daheng frame helper candidate could be started.");
        return true;
    }

    private static bool IsTrackedHelperRunningLocked()
    {
        if (_helperProcess is null)
        {
            return false;
        }

        try
        {
            if (_helperProcess.HasExited)
            {
                VirtualCameraState.Log(
                    $"Tracked Daheng helper exited: pid={SafeGetProcessId(_helperProcess)}, " +
                    $"path={_helperProcessPath ?? "<unknown>"}");
                ClearTrackedHelperProcessLocked();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"Failed to inspect tracked Daheng helper: {ex.GetType().Name}: {ex.Message}");
            ClearTrackedHelperProcessLocked();
            return false;
        }
    }

    private static void ReplaceTrackedHelperProcessLocked(Process process, string candidate)
    {
        if (!ReferenceEquals(_helperProcess, process))
        {
            TryDisposeTrackedHelperProcessLocked();
        }

        _helperProcess = process;
        _helperProcessPath = candidate;
    }

    private static void ClearTrackedHelperProcessLocked()
    {
        TryDisposeTrackedHelperProcessLocked();
        _helperProcess = null;
        _helperProcessPath = null;
    }

    private static void TryDisposeTrackedHelperProcessLocked()
    {
        if (_helperProcess is null)
        {
            return;
        }

        try
        {
            _helperProcess.Dispose();
        }
        catch
        {
            // Best-effort process cleanup only.
        }
    }

    private static bool TryAttachExistingHelperProcessLocked(string candidate)
    {
        try
        {
            var normalizedCandidate = Path.GetFullPath(candidate);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(FrameBridgeProtocol.HelperExeName)))
            {
                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    var processPath = process.MainModule?.FileName;
                    if (!string.Equals(processPath, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Dispose();
                        continue;
                    }

                    ReplaceTrackedHelperProcessLocked(process, normalizedCandidate);
                    var boundToJob = TryBindTrackedHelperToJobLocked(process);
                    VirtualCameraState.Log(
                        $"Attached to existing Daheng frame helper: {normalizedCandidate}, " +
                        $"pid={SafeGetProcessId(process)}, jobBound={boundToJob}");
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }

                    VirtualCameraState.Log(
                        $"Failed to inspect existing Daheng helper candidate '{candidate}': " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log(
                $"Failed while attaching existing Daheng helper '{candidate}': " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        return false;
    }

    private static void TryStopTrackedHelperProcessLocked(string reason)
    {
        if (_helperProcess is null)
        {
            return;
        }

        try
        {
            if (!_helperProcess.HasExited)
            {
                VirtualCameraState.Log(
                    $"Stopping tracked Daheng helper: pid={SafeGetProcessId(_helperProcess)}, " +
                    $"path={_helperProcessPath ?? "<unknown>"}, reason={reason}");
                _helperProcess.Kill(entireProcessTree: true);
                _helperProcess.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log(
                $"Failed to stop tracked Daheng helper pid={SafeGetProcessId(_helperProcess)} " +
                $"reason={reason}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ClearTrackedHelperProcessLocked();
        }
    }

    private static void CleanupHelperLifetime(string reason)
    {
        lock (Gate)
        {
            TryStopTrackedHelperProcessLocked(reason);
            ResetBridgeHandlesLocked($"helper lifetime cleanup ({reason})");

            if (_helperJobHandle != 0)
            {
                try
                {
                    CloseHandle(_helperJobHandle);
                }
                catch
                {
                    // Best-effort handle cleanup only.
                }

                _helperJobHandle = 0;
            }
        }
    }

    private static bool TryBindTrackedHelperToJobLocked(Process process)
    {
        if (process.HasExited)
        {
            return false;
        }

        if (!EnsureHelperJobObjectLocked())
        {
            return false;
        }

        try
        {
            if (AssignProcessToJobObject(_helperJobHandle, process.Handle))
            {
                return true;
            }

            var lastError = Marshal.GetLastWin32Error();
            VirtualCameraState.Log(
                $"AssignProcessToJobObject failed for Daheng helper pid={SafeGetProcessId(process)} " +
                $"path={_helperProcessPath ?? "<unknown>"} win32={lastError}");
            return false;
        }
        catch (Exception ex)
        {
            VirtualCameraState.Log($"Job binding failed for Daheng helper pid={SafeGetProcessId(process)}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool EnsureHelperJobObjectLocked()
    {
        if (_helperJobHandle != 0)
        {
            return true;
        }

        var jobHandle = CreateJobObjectW(nint.Zero, null);
        if (jobHandle == 0)
        {
            VirtualCameraState.Log($"CreateJobObjectW failed for Daheng helper lifecycle binding, win32={Marshal.GetLastWin32Error()}");
            return false;
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        if (!SetInformationJobObject(
                jobHandle,
                JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                ref limits,
                (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            var lastError = Marshal.GetLastWin32Error();
            CloseHandle(jobHandle);
            VirtualCameraState.Log($"SetInformationJobObject failed for Daheng helper lifecycle binding, win32={lastError}");
            return false;
        }

        _helperJobHandle = jobHandle;
        VirtualCameraState.Log("Created Daheng helper lifecycle job object.");
        return true;
    }

    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    private static IEnumerable<string> EnumerateHelperCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateRawHelperCandidates())
        {
            var normalized = NormalizeHelperCandidate(candidate);
            if (normalized is null || !seen.Add(normalized))
            {
                continue;
            }

            yield return normalized;
        }
    }

    private static IEnumerable<string?> EnumerateRawHelperCandidates()
    {
        yield return Environment.GetEnvironmentVariable(HelperOverrideEnvVar);
        yield return Path.Combine(AppContext.BaseDirectory, HelperSubdirectoryName, FrameBridgeProtocol.HelperExeName);
        yield return Path.Combine(AppContext.BaseDirectory, FrameBridgeProtocol.HelperExeName);
    }

    private static string? NormalizeHelperCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim().Trim('"');
        var path = Directory.Exists(trimmed)
            ? Path.Combine(trimmed, FrameBridgeProtocol.HelperExeName)
            : trimmed;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool EnsureBridgeOpenedLocked()
    {
        if (_accessor is not null && _mutex is not null)
        {
            return true;
        }

        try
        {
            _map = MemoryMappedFile.OpenExisting(FrameBridgeProtocol.MapName, MemoryMappedFileRights.ReadWrite);
            _accessor = _map.CreateViewAccessor(0, FrameBridgeProtocol.MapSize, MemoryMappedFileAccess.ReadWrite);
            _mutex = Mutex.OpenExisting(FrameBridgeProtocol.FrameMutexName);
            return true;
        }
        catch
        {
            ResetBridgeHandlesLocked("bridge open failed");
            return false;
        }
    }

    private static IDisposable? AcquireMutexLocked(int timeoutMs)
    {
        if (_mutex is null)
        {
            return null;
        }

        try
        {
            if (!_mutex.WaitOne(timeoutMs))
            {
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // Previous owner exited unexpectedly; mutex is still acquired for this thread.
        }
        catch (Exception ex)
        {
            ResetBridgeHandlesLocked($"bridge wait failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        return new Releaser(_mutex);
    }

    private static void ResetBridgeHandlesLocked(string reason)
    {
        try
        {
            VirtualCameraState.Log($"Daheng bridge reset: {reason}");
        }
        catch
        {
            // Best-effort logging only.
        }

        _accessor?.Dispose();
        _accessor = null;
        _map?.Dispose();
        _map = null;
        _mutex?.Dispose();
        _mutex = null;
        _cachedFrameId = 0;
        _cachedFrameWidth = 0;
        _cachedFrameHeight = 0;
        _cachedFramePayloadLength = 0;
        _lastFrameProbeUtc = DateTime.MinValue;
        _lastFrameRefreshUtc = DateTime.MinValue;
        _hasLoggedStreaming = false;
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
                // Ignore if ownership was lost.
            }
        }
    }

    private readonly record struct FrameHeader(long FrameId, int Width, int Height, int PayloadLength);

    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [Flags]
    private enum JobObjectLimitFlags : uint
    {
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = JobObjectLimitKillOnJobClose,
    }

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimitFlags LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        JOBOBJECTINFOCLASS jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    private readonly record struct ControlRequest(
        int Flags,
        double ExposureUs,
        double GainDb,
        double GainMinDb,
        double GainMaxDb,
        double GainIncDb,
        double ExposureMinUs,
        double ExposureMaxUs,
        double ExposureIncUs,
        double FrameRateHz,
        int MasterGain,
        int CapturePixelFormat,
        int Width,
        int Height,
        int OffsetX,
        int OffsetY,
        int BinningX,
        int BinningY,
        int BlackLevel);
}

internal readonly record struct BridgeControlState(
    double ExposureMs,
    double ExposureMinMs,
    double ExposureMaxMs,
    double ExposureIncMs,
    double GainDb,
    double GainMinDb,
    double GainMaxDb,
    double GainIncDb,
    double FrameRateHz,
    double FrameRateMinHz,
    double FrameRateMaxHz,
    double FrameRateIncHz,
    int MasterGain,
    int AppliedFlags,
    int RequestedFlags,
    int AppliedSequence,
    int RequestSequence,
    bool GainBoostSupported,
    bool GainBoostEnabled,
    int PixelFormatCapabilityFlags,
    int CapturePixelFormat,
    int Width,
    int Height,
    int OffsetX,
    int OffsetY,
    int BinningX,
    int BinningY,
    int BlackLevel)
{
    public bool AutoExposureEnabled => (AppliedFlags & FrameBridgeProtocol.ControlFlagAutoExposure) != 0;
    public bool AutoGainEnabled => (AppliedFlags & FrameBridgeProtocol.ControlFlagAutoGain) != 0;
}
