using System.Security.Cryptography;
using System.Text;

namespace RayCiBridge;

public static class FrameBridgeProtocol
{
    public const int Magic = 0x31424644; // DFB1
    public const int Version = 3;

    public const int TargetWidth = 1280;
    public const int TargetHeight = 1024;
    public const int TargetBytesPerPixel = 2;
    public const int TargetStride = TargetWidth * TargetBytesPerPixel;
    public const int FrameByteCount = TargetStride * TargetHeight;

    public const int PixelFormatMono8 = 1;
    public const int PixelFormatMono16 = 2;

    public const int StatusWaiting = 1;
    public const int StatusNoCamera = 2;
    public const int StatusStreaming = 3;
    public const int StatusError = 4;

    public const int ControlFlagAutoExposure = 0x01;
    public const int ControlFlagAutoGain = 0x02;

    public const string HelperExeName = "DahengFrameServer.exe";
    public const string BridgeNamespaceEnvVar = "ULTRON_RAYCI_BRIDGE_NAMESPACE";
    private const string MapNameBase = "Ultron_Daheng_FrameBridge_v3";
    private const string FrameMutexNameBase = "Ultron_Daheng_FrameBridge_FrameMutex_v3";
    private const string ServerInstanceMutexNameBase = "Ultron_Daheng_FrameBridge_ServerMutex_v3";
    private static readonly string? BridgeNamespaceSuffix = ResolveBridgeNamespaceSuffix();

    public static string MapName => AppendBridgeNamespace(MapNameBase);
    public static string FrameMutexName => AppendBridgeNamespace(FrameMutexNameBase);
    public static string ServerInstanceMutexName => AppendBridgeNamespace(ServerInstanceMutexNameBase);

    public const int MagicOffset = 0;
    public const int VersionOffset = 4;
    public const int WidthOffset = 8;
    public const int HeightOffset = 12;
    public const int StrideOffset = 16;
    public const int PixelFormatOffset = 20;
    public const int StatusOffset = 24;
    public const int PayloadLengthOffset = 28;
    public const int SourceWidthOffset = 32;
    public const int SourceHeightOffset = 36;
    public const int FrameIdOffset = 40;
    public const int TimestampTicksOffset = 48;

    public const int RequestSequenceOffset = 56;
    public const int AppliedSequenceOffset = 60;
    public const int RequestedFlagsOffset = 64;
    public const int AppliedFlagsOffset = 68;
    public const int RequestedExposureUsOffset = 72;
    public const int AppliedExposureUsOffset = 80;
    public const int ExposureMinUsOffset = 88;
    public const int ExposureMaxUsOffset = 96;
    public const int ExposureIncUsOffset = 104;
    public const int RequestedGainDbOffset = 112;
    public const int AppliedGainDbOffset = 120;
    public const int GainMinDbOffset = 128;
    public const int GainMaxDbOffset = 136;
    public const int GainIncDbOffset = 144;
    public const int RequestedMasterGainOffset = 152;
    public const int AppliedMasterGainOffset = 156;
    public const int GainBoostSupportedOffset = 160;
    public const int GainBoostEnabledOffset = 164;
    public const int RequestedFrameRateHzOffset = 168;
    public const int AppliedFrameRateHzOffset = 176;
    public const int FrameRateMinHzOffset = 184;
    public const int FrameRateMaxHzOffset = 192;
    public const int FrameRateIncHzOffset = 200;
    public const int PixelFormatCapabilityFlagsOffset = 208;
    public const int PixelFormatCapabilitySupports12BitFlag = 0x01;
    public const int RequestedCapturePixelFormatOffset = 212;
    public const int AppliedCapturePixelFormatOffset = 216;
    public const int RequestedWidthOffset = 220;
    public const int RequestedHeightOffset = 224;
    public const int RequestedOffsetXOffset = 228;
    public const int RequestedOffsetYOffset = 232;
    public const int RequestedBinningXOffset = 236;
    public const int RequestedBinningYOffset = 240;
    public const int AppliedWidthOffset = 244;
    public const int AppliedHeightOffset = 248;
    public const int AppliedOffsetXOffset = 252;
    public const int AppliedOffsetYOffset = 256;
    public const int AppliedBinningXOffset = 260;
    public const int AppliedBinningYOffset = 264;
    public const int RequestedBlackLevelOffset = 268;
    public const int AppliedBlackLevelOffset = 272;

    public const int CapturePixelFormatAuto = 0;
    public const int CapturePixelFormatMono8 = 1;
    public const int CapturePixelFormatMono10 = 2;
    public const int CapturePixelFormatMono12 = 3;
    public const int CapturePixelFormatMono14 = 4;
    public const int CapturePixelFormatMono16 = 5;

    public const int HeaderSize = 320;
    public const int MapSize = HeaderSize + FrameByteCount;

    private static string AppendBridgeNamespace(string baseName)
        => string.IsNullOrWhiteSpace(BridgeNamespaceSuffix)
            ? baseName
            : $"{baseName}_{BridgeNamespaceSuffix}";

    private static string? ResolveBridgeNamespaceSuffix()
    {
        var rawNamespace = Environment.GetEnvironmentVariable(BridgeNamespaceEnvVar);
        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            rawNamespace = TryResolvePortableRoot();
        }

        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return null;
        }

        var normalized = rawNamespace.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12];
        return hash;
    }

    private static string? TryResolvePortableRoot()
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(directory.Name, "DahengBridgeHelper", StringComparison.OrdinalIgnoreCase))
            {
                directory = directory.Parent ?? directory;
            }

            if (File.Exists(Path.Combine(directory.FullName, "RayCi.exe")))
            {
                return directory.FullName;
            }

            var parent = directory.Parent;
            if (parent is not null && File.Exists(Path.Combine(parent.FullName, "RayCi.exe")))
            {
                return parent.FullName;
            }
        }
        catch
        {
            // Fall back to the historical global names when path discovery fails.
        }

        return null;
    }
}
