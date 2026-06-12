using System.Globalization;

namespace VirtualFGCameraProxy;

internal static class ProxyLog
{
    private static readonly object Sync = new();
    private static readonly string[] LogPaths = BuildLogPaths();

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

        lock (Sync)
        {
            foreach (var logPath in LogPaths)
            {
                TryAppend(logPath, line);
            }
        }
    }

    public static string Hex(nuint value)
    {
        return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string[] BuildLogPaths()
    {
        var paths = new List<string>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            paths.Add(Path.Combine(localAppData, "Ultron", "RayCiFGCameraBridge", "logs", "fgcamera_proxy.log"));
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            paths.Add(Path.Combine(baseDirectory, "logs", "fgcamera_proxy.log"));
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void TryAppend(string logPath, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            File.AppendAllText(logPath, line);
        }
        catch
        {
            // Logging is best-effort only and must never interfere with proxy behavior.
        }
    }
}
