using System.Diagnostics;
using System.IO.MemoryMappedFiles;
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
    private static DateTime _lastHelperStartAttemptUtc = DateTime.MinValue;
    private static DateTime _lastFrameProbeUtc = DateTime.MinValue;
    private static DateTime _lastFrameRefreshUtc = DateTime.MinValue;
    private static long _cachedFrameId;
    private static int _cachedFrameWidth;
    private static int _cachedFrameHeight;
    private static int _cachedFramePayloadLength;
    private static bool _hasLoggedStreaming;

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
                    GainBoostEnabled: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostEnabledOffset) != 0);
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
            GainBoostEnabled: _accessor.ReadInt32(FrameBridgeProtocol.GainBoostEnabledOffset) != 0);
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
            MasterGain: _accessor.ReadInt32(FrameBridgeProtocol.RequestedMasterGainOffset));
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
            var source16 = (ushort*)sourceBytes;

            if (UsesWideMonochromeContainer(memory.BitsPerPixel))
            {
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

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    WorkingDirectory = Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                VirtualCameraState.Log($"Started Daheng frame helper: {candidate}");
                return true;
            }
            catch (Exception ex)
            {
                VirtualCameraState.Log($"Failed to start Daheng helper '{candidate}': {ex.Message}");
            }
        }

        VirtualCameraState.Log("No Daheng frame helper candidate could be started.");
        return true;
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

        var workspaceRoot = TryFindWorkspaceRoot();
        if (workspaceRoot is null)
        {
            yield break;
        }

        yield return Path.Combine(workspaceRoot, "daheng_frame_server", "bin", "Release", "net8.0", "win-x64", "publish", FrameBridgeProtocol.HelperExeName);
        yield return Path.Combine(workspaceRoot, "daheng_frame_server", "bin", "Release", "net8.0", "publish", FrameBridgeProtocol.HelperExeName);
        yield return Path.Combine(workspaceRoot, "daheng_frame_server", "bin", "Release", "net8.0", "win-x64", FrameBridgeProtocol.HelperExeName);
        yield return Path.Combine(workspaceRoot, "daheng_frame_server", "bin", "Release", "net8.0", FrameBridgeProtocol.HelperExeName);
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

    private static string? TryFindWorkspaceRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "FrameBridgeProtocol.cs")) &&
                Directory.Exists(Path.Combine(current.FullName, "daheng_frame_server")))
            {
                return current.FullName;
            }
        }

        return null;
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
        int MasterGain);
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
    bool GainBoostEnabled)
{
    public bool AutoExposureEnabled => (AppliedFlags & FrameBridgeProtocol.ControlFlagAutoExposure) != 0;
    public bool AutoGainEnabled => (AppliedFlags & FrameBridgeProtocol.ControlFlagAutoGain) != 0;
}
