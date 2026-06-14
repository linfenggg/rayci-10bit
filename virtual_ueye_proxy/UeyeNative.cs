using System.Runtime.InteropServices;
using System.Text;

namespace VirtualUEyeProxy;

internal static class UeyeNative
{
    public const int IS_NO_SUCCESS = -1;
    public const int IS_SUCCESS = 0;
    public const int IS_INVALID_PARAMETER = 125;
    public const int IS_TIMED_OUT = 122;
    public const int IS_NOT_SUPPORTED = 155;

    public const int IS_SET_DM_DIB = 1;
    public const int IS_GET_DISPLAY_MODE = unchecked((int)0x8000);
    public const int IS_CM_MONO8 = 6;
    public const int IS_CM_MONO12 = 26;
    public const int IS_CM_MONO16 = 28;
    public const int IS_CM_SENSOR_RAW10 = 33;
    public const int IS_CM_MONO10 = 34;
    public const int IS_CM_MONO10_COMPAT_Y16 = 0x4022;
    public const int IS_CM_MODE_MASK = 0x007F;
    public const int IS_GET_COLOR_MODE = unchecked((int)0x8000);
    public const int IS_GET_BITS_PER_PIXEL = unchecked((int)0x9000);

    public const int IS_WAIT = 0x0001;
    public const int IS_DONT_WAIT = 0x0000;
    public const int IS_VIDEO_NOT_FINISH = 0;
    public const int IS_VIDEO_FINISH = 1;
    public const int IS_SET_EVENT_FRAME = 2;
    public const int IS_SET_EVENT_CAPTURE_STATUS = 8;
    public const int IS_SET_EVENT_DEVICE_RECONNECTED = 9;
    public const int IS_SET_EVENT_CONNECTIONSPEED_CHANGED = 18;
    public const int IS_SET_EVENT_REMOVE = 128;
    public const int IS_SET_EVENT_REMOVAL = 129;
    public const int IS_SET_EVENT_NEW_DEVICE = 130;
    public const int IS_SET_EVENT_STATUS_CHANGED = 131;
    public const uint IS_IMAGE_QUEUE_CMD_INIT = 0;
    public const uint IS_IMAGE_QUEUE_CMD_EXIT = 1;
    public const uint IS_IMAGE_QUEUE_CMD_WAIT = 2;
    public const uint IS_IMAGE_QUEUE_CMD_CANCEL_WAIT = 3;
    public const uint IS_IMAGE_QUEUE_CMD_GET_PENDING = 4;
    public const uint IS_IMAGE_QUEUE_CMD_FLUSH = 5;
    public const uint IS_IMAGE_QUEUE_CMD_DISCARD_N_ITEMS = 6;

    public const uint IS_AOI_IMAGE_SET_AOI = 0x0001;
    public const uint IS_AOI_IMAGE_GET_AOI = 0x0002;
    public const uint IS_AOI_IMAGE_SET_POS = 0x0003;
    public const uint IS_AOI_IMAGE_GET_POS = 0x0004;
    public const uint IS_AOI_IMAGE_SET_SIZE = 0x0005;
    public const uint IS_AOI_IMAGE_GET_SIZE = 0x0006;
    public const uint IS_AOI_IMAGE_GET_POS_MIN = 0x0007;
    public const uint IS_AOI_IMAGE_GET_SIZE_MIN = 0x0008;
    public const uint IS_AOI_IMAGE_GET_POS_MAX = 0x0009;
    public const uint IS_AOI_IMAGE_GET_SIZE_MAX = 0x0010;
    public const uint IS_AOI_IMAGE_GET_POS_INC = 0x0011;
    public const uint IS_AOI_IMAGE_GET_SIZE_INC = 0x0012;
    public const uint IS_AOI_IMAGE_GET_ORIGINAL_AOI = 0x0015;

    public const int IS_GET_SUBSAMPLING = unchecked((int)0x8000);
    public const int IS_GET_SUPPORTED_SUBSAMPLING = unchecked((int)0x8001);
    public const int IS_GET_SUBSAMPLING_TYPE = unchecked((int)0x8002);
    public const int IS_GET_SUBSAMPLING_FACTOR_HORIZONTAL = unchecked((int)0x8004);
    public const int IS_GET_SUBSAMPLING_FACTOR_VERTICAL = unchecked((int)0x8008);

    public const int IS_SUBSAMPLING_DISABLE = 0x0000;
    public const int IS_SUBSAMPLING_2X_VERTICAL = 0x0001;
    public const int IS_SUBSAMPLING_2X_HORIZONTAL = 0x0002;
    public const int IS_SUBSAMPLING_4X_VERTICAL = 0x0004;
    public const int IS_SUBSAMPLING_4X_HORIZONTAL = 0x0008;
    public const int IS_SUBSAMPLING_3X_VERTICAL = 0x0010;
    public const int IS_SUBSAMPLING_3X_HORIZONTAL = 0x0020;
    public const int IS_SUBSAMPLING_5X_VERTICAL = 0x0040;
    public const int IS_SUBSAMPLING_5X_HORIZONTAL = 0x0080;
    public const int IS_SUBSAMPLING_6X_VERTICAL = 0x0100;
    public const int IS_SUBSAMPLING_6X_HORIZONTAL = 0x0200;
    public const int IS_SUBSAMPLING_8X_VERTICAL = 0x0400;
    public const int IS_SUBSAMPLING_8X_HORIZONTAL = 0x0800;
    public const int IS_SUBSAMPLING_16X_VERTICAL = 0x1000;
    public const int IS_SUBSAMPLING_16X_HORIZONTAL = 0x2000;
    public const int IS_SUBSAMPLING_MONO = 0x02;
    public const int IS_SUBSAMPLING_MASK_VERTICAL =
        IS_SUBSAMPLING_2X_VERTICAL |
        IS_SUBSAMPLING_3X_VERTICAL |
        IS_SUBSAMPLING_4X_VERTICAL |
        IS_SUBSAMPLING_5X_VERTICAL |
        IS_SUBSAMPLING_6X_VERTICAL |
        IS_SUBSAMPLING_8X_VERTICAL |
        IS_SUBSAMPLING_16X_VERTICAL;
    public const int IS_SUBSAMPLING_MASK_HORIZONTAL =
        IS_SUBSAMPLING_2X_HORIZONTAL |
        IS_SUBSAMPLING_3X_HORIZONTAL |
        IS_SUBSAMPLING_4X_HORIZONTAL |
        IS_SUBSAMPLING_5X_HORIZONTAL |
        IS_SUBSAMPLING_6X_HORIZONTAL |
        IS_SUBSAMPLING_8X_HORIZONTAL |
        IS_SUBSAMPLING_16X_HORIZONTAL;

    public const int IS_GET_BINNING = unchecked((int)0x8000);
    public const int IS_GET_SUPPORTED_BINNING = unchecked((int)0x8001);
    public const int IS_GET_BINNING_TYPE = unchecked((int)0x8002);
    public const int IS_GET_BINNING_FACTOR_HORIZONTAL = unchecked((int)0x8004);
    public const int IS_GET_BINNING_FACTOR_VERTICAL = unchecked((int)0x8008);

    public const int IS_BINNING_DISABLE = 0x0000;
    public const int IS_BINNING_2X_VERTICAL = 0x0001;
    public const int IS_BINNING_2X_HORIZONTAL = 0x0002;
    public const int IS_BINNING_4X_VERTICAL = 0x0004;
    public const int IS_BINNING_4X_HORIZONTAL = 0x0008;
    public const int IS_BINNING_3X_VERTICAL = 0x0010;
    public const int IS_BINNING_3X_HORIZONTAL = 0x0020;
    public const int IS_BINNING_5X_VERTICAL = 0x0040;
    public const int IS_BINNING_5X_HORIZONTAL = 0x0080;
    public const int IS_BINNING_6X_VERTICAL = 0x0100;
    public const int IS_BINNING_6X_HORIZONTAL = 0x0200;
    public const int IS_BINNING_8X_VERTICAL = 0x0400;
    public const int IS_BINNING_8X_HORIZONTAL = 0x0800;
    public const int IS_BINNING_16X_VERTICAL = 0x1000;
    public const int IS_BINNING_16X_HORIZONTAL = 0x2000;
    public const int IS_BINNING_MONO = 0x02;
    public const int IS_BINNING_MASK_VERTICAL =
        IS_BINNING_2X_VERTICAL |
        IS_BINNING_3X_VERTICAL |
        IS_BINNING_4X_VERTICAL |
        IS_BINNING_5X_VERTICAL |
        IS_BINNING_6X_VERTICAL |
        IS_BINNING_8X_VERTICAL |
        IS_BINNING_16X_VERTICAL;
    public const int IS_BINNING_MASK_HORIZONTAL =
        IS_BINNING_2X_HORIZONTAL |
        IS_BINNING_3X_HORIZONTAL |
        IS_BINNING_4X_HORIZONTAL |
        IS_BINNING_5X_HORIZONTAL |
        IS_BINNING_6X_HORIZONTAL |
        IS_BINNING_8X_HORIZONTAL |
        IS_BINNING_16X_HORIZONTAL;

    public const uint IS_EXPOSURE_CMD_GET_CAPS = 1;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE_DEFAULT = 2;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_MIN = 3;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_MAX = 4;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE_INC = 5;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE_RANGE = 6;
    public const uint IS_EXPOSURE_CMD_GET_EXPOSURE = 7;
    public const uint IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_MIN = 8;
    public const uint IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_MAX = 9;
    public const uint IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE_INC = 10;
    public const uint IS_EXPOSURE_CMD_GET_FINE_INCREMENT_RANGE = 11;
    public const uint IS_EXPOSURE_CMD_SET_EXPOSURE = 12;

    public const uint IS_EXPOSURE_CAP_EXPOSURE = 0x00000001;
    public const uint IS_EXPOSURE_CAP_FINE_INCREMENT = 0x00000002;

    public const uint IS_BLACKLEVEL_CMD_GET_CAPS = 1;
    public const uint IS_BLACKLEVEL_CMD_GET_MODE_DEFAULT = 2;
    public const uint IS_BLACKLEVEL_CMD_GET_MODE = 3;
    public const uint IS_BLACKLEVEL_CMD_SET_MODE = 4;
    public const uint IS_BLACKLEVEL_CMD_GET_OFFSET_DEFAULT = 5;
    public const uint IS_BLACKLEVEL_CMD_GET_OFFSET_RANGE = 6;
    public const uint IS_BLACKLEVEL_CMD_GET_OFFSET = 7;
    public const uint IS_BLACKLEVEL_CMD_SET_OFFSET = 8;

    public const int IS_AUTO_BLACKLEVEL_OFF = 0;
    public const int IS_AUTO_BLACKLEVEL_ON = 1;
    public const uint IS_BLACKLEVEL_CAP_SET_AUTO_BLACKLEVEL = 0x00000001;
    public const uint IS_BLACKLEVEL_CAP_SET_OFFSET = 0x00000002;

    public const int IS_SET_ENABLE_AUTO_GAIN = 0x8800;
    public const int IS_SET_ENABLE_AUTO_SHUTTER = 0x8802;
    public const int IS_SET_TRIGGER_OFF = 0x0000;
    public const int IS_IGNORE_PARAMETER = -1;

    public const int IS_GET_MASTER_GAIN = unchecked((int)0x8000);
    public const int IS_GET_RED_GAIN = unchecked((int)0x8001);
    public const int IS_GET_GREEN_GAIN = unchecked((int)0x8002);
    public const int IS_GET_BLUE_GAIN = unchecked((int)0x8003);
    public const int IS_GET_DEFAULT_MASTER = unchecked((int)0x8004);
    public const int IS_GET_DEFAULT_RED = unchecked((int)0x8005);
    public const int IS_GET_DEFAULT_GREEN = unchecked((int)0x8006);
    public const int IS_GET_DEFAULT_BLUE = unchecked((int)0x8007);
    public const int IS_GET_GAINBOOST = unchecked((int)0x8008);
    public const int IS_SET_GAINBOOST_OFF = 0x0000;
    public const int IS_SET_GAINBOOST_ON = 0x0001;
    public const int IS_GET_SUPPORTED_GAINBOOST = 0x0002;
    public const int IS_MIN_GAIN = 0;
    public const int IS_MAX_GAIN = 100;

    public const int IS_GET_MASTER_GAIN_FACTOR = unchecked((int)0x8000);
    public const int IS_GET_RED_GAIN_FACTOR = unchecked((int)0x8001);
    public const int IS_GET_GREEN_GAIN_FACTOR = unchecked((int)0x8002);
    public const int IS_GET_BLUE_GAIN_FACTOR = unchecked((int)0x8003);
    public const int IS_SET_MASTER_GAIN_FACTOR = unchecked((int)0x8004);
    public const int IS_SET_RED_GAIN_FACTOR = unchecked((int)0x8005);
    public const int IS_SET_GREEN_GAIN_FACTOR = unchecked((int)0x8006);
    public const int IS_SET_BLUE_GAIN_FACTOR = unchecked((int)0x8007);
    public const int IS_GET_DEFAULT_MASTER_GAIN_FACTOR = unchecked((int)0x8008);
    public const int IS_GET_DEFAULT_RED_GAIN_FACTOR = unchecked((int)0x8009);
    public const int IS_GET_DEFAULT_GREEN_GAIN_FACTOR = unchecked((int)0x800A);
    public const int IS_GET_DEFAULT_BLUE_GAIN_FACTOR = unchecked((int)0x800B);
    public const int IS_INQUIRE_MASTER_GAIN_FACTOR = unchecked((int)0x800C);
    public const int IS_INQUIRE_RED_GAIN_FACTOR = unchecked((int)0x800D);
    public const int IS_INQUIRE_GREEN_GAIN_FACTOR = unchecked((int)0x800E);
    public const int IS_INQUIRE_BLUE_GAIN_FACTOR = unchecked((int)0x800F);

    public const uint IS_DEVICE_INFO_CMD_GET_DEVICE_INFO = 0x02010001;
    public const uint IS_DEVICE_FEATURE_CMD_GET_SUPPORTED_FEATURES = 1;

    public const uint IS_CONFIG_CMD_GET_CAPABILITIES = 1;
    public const uint IS_CONFIG_OPEN_MP_CMD_GET_ENABLE = 6;
    public const uint IS_CONFIG_OPEN_MP_CMD_GET_ENABLE_DEFAULT = 8;
    public const uint IS_CONFIG_CMD_GET_IMAGE_MEMORY_COMPATIBILIY_MODE = 20;
    public const uint IS_CONFIG_CMD_GET_IMAGE_MEMORY_COMPATIBILIY_MODE_DEFAULT = 21;

    public const uint IS_CONFIG_OPEN_MP_CAP_SUPPORTED = 0x00000002;
    public const uint IS_CONFIG_IMAGE_MEMORY_COMPATIBILITY_MODE_OFF = 0;

    public const uint IS_PIXELCLOCK_CMD_GET_NUMBER = 1;
    public const uint IS_PIXELCLOCK_CMD_GET_LIST = 2;
    public const uint IS_PIXELCLOCK_CMD_GET_RANGE = 3;
    public const uint IS_PIXELCLOCK_CMD_GET_DEFAULT = 4;
    public const uint IS_PIXELCLOCK_CMD_GET = 5;
    public const uint IS_PIXELCLOCK_CMD_SET = 6;

    public const byte IS_BOARD_TYPE_UEYE_USB = 0x40;
    public const ushort IS_SENSOR_UI1545_M = 0x0028;

    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 1024;
    public const int DefaultBitsPerPixel = 16;
    public const int DefaultSignalBitsPerPixel = 10;
    public const int DefaultPitch = DefaultWidth * ((DefaultBitsPerPixel + 7) / 8);
    public const int DefaultColorMode = IS_CM_MONO10_COMPAT_Y16;
    public const double MinExposureMs = 0.1;
    public const double MaxExposureMs = 1000.0;
    public const double ExposureIncrementMs = 0.1;
    public const double DefaultExposureMs = 30.0;
    public const double DefaultFps = 15.0;
    public const int DefaultPixelClock = 34;

    public static int GetBytesPerPixel(int bitsPerPixel)
        => Math.Max(1, (bitsPerPixel + 7) / 8);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BOARDINFO
{
    public fixed byte SerNo[12];
    public fixed byte ID[20];
    public fixed byte Version[10];
    public fixed byte Date[12];
    public byte Select;
    public byte Type;
    public fixed byte Reserved[8];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SENSORINFO
{
    public ushort SensorID;
    public fixed byte strSensorName[32];
    public sbyte nColorMode;
    public uint nMaxWidth;
    public uint nMaxHeight;
    public int bMasterGain;
    public int bRGain;
    public int bGGain;
    public int bBGain;
    public int bGlobShutter;
    public ushort wPixelSize;
    public sbyte nUpperLeftBayerPixel;
    public fixed byte Reserved[13];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UEYE_CAMERA_INFO
{
    public uint dwCameraID;
    public uint dwDeviceID;
    public uint dwSensorID;
    public uint dwInUse;
    public fixed byte SerNo[16];
    public fixed byte Model[16];
    public uint dwStatus;
    public fixed uint dwReserved[2];
    public fixed byte FullModelName[32];
    public fixed uint dwReserved2[5];
}

[StructLayout(LayoutKind.Sequential)]
internal struct IS_SIZE_2D
{
    public int s32Width;
    public int s32Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IS_RECT
{
    public int s32X;
    public int s32Y;
    public int s32Width;
    public int s32Height;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UEYETIME
{
    public ushort wYear;
    public ushort wMonth;
    public ushort wDay;
    public ushort wHour;
    public ushort wMinute;
    public ushort wSecond;
    public ushort wMilliseconds;
    public fixed byte byReserved[10];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UEYEIMAGEINFO
{
    public uint dwFlags;
    public fixed byte byReserved1[4];
    public ulong u64TimestampDevice;
    public UEYETIME TimestampSystem;
    public uint dwIoStatus;
    public ushort wAOIIndex;
    public ushort wAOICycle;
    public ulong u64FrameNumber;
    public uint dwImageBuffers;
    public uint dwImageBuffersInUse;
    public uint dwReserved3;
    public uint dwImageHeight;
    public uint dwImageWidth;
    public uint dwHostProcessTime;
    public byte bySequencerIndex;
    public fixed byte byReserved2[3];
    public uint dwFocusValue;
    public int bFocusing;
    public uint dwReserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct IS_DEVICE_INFO_HEARTBEAT
{
    public fixed byte reserved_1[24];
    public uint dwRuntimeFirmwareVersion;
    public fixed byte reserved_2[8];
    public ushort wTemperature;
    public ushort wLinkSpeed_Mb;
    public fixed byte reserved_3[6];
    public ushort wComportOffset;
    public fixed byte reserved[200];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct IS_DEVICE_INFO_CONTROL
{
    public uint dwDeviceId;
    public fixed byte reserved[148];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct IS_DEVICE_INFO
{
    public IS_DEVICE_INFO_HEARTBEAT infoDevHeartbeat;
    public IS_DEVICE_INFO_CONTROL infoDevControl;
    public fixed byte reserved[240];
}

internal static unsafe class NativeHelpers
{
    public static void Zero(void* ptr, uint size)
    {
        if (ptr == null || size == 0)
        {
            return;
        }

        new Span<byte>(ptr, checked((int)size)).Clear();
    }

    public static void WriteAnsi(byte* destination, int destinationLength, string value)
    {
        if (destination == null || destinationLength <= 0)
        {
            return;
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        var span = new Span<byte>(destination, destinationLength);
        span.Clear();
        bytes.AsSpan(0, Math.Min(bytes.Length, destinationLength - 1)).CopyTo(span);
    }
}
