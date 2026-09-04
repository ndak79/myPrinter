using System.Text;

namespace MyPrinter.Desktop;

internal sealed class StartupDiagnostics
{
    private const long MaximumLogBytes = 1024 * 1024;
    private readonly object _writeSync = new();

    public StartupDiagnostics(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        LogPath = Path.GetFullPath(logPath);
    }

    public static StartupDiagnostics Default { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartPrinter",
            "Logs",
            "startup.log"));

    public string LogPath { get; }

    public void RecordMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        TryAppend($"{BuildPrefix()} {message}{Environment.NewLine}");
    }

    public void RecordException(string context, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(exception);
        TryAppend(
            $"{BuildPrefix()} {context}{Environment.NewLine}" +
            $"{exception}{Environment.NewLine}");
    }

    private void TryAppend(string entry)
    {
        try
        {
            lock (_writeSync)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                RotateIfNeeded();
                File.AppendAllText(LogPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Diagnostics must never become a new startup failure.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes)
            return;

        var previousLogPath = Path.ChangeExtension(LogPath, ".previous.log");
        File.Move(LogPath, previousLogPath, overwrite: true);
    }

    private static string BuildPrefix()
        => $"[{DateTimeOffset.Now:O}] [PID {Environment.ProcessId}]";
}
