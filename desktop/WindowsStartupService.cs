using System.Diagnostics;

namespace MyPrinter.Desktop;

public sealed record WindowsStartupCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed record WindowsStartupOperationResult(
    bool Succeeded,
    string? Error = null);

public sealed class WindowsStartupService
{
    public const string TaskName = "SmartPrinterAutoStart";
    public const string StartHiddenArgument = "--start-hidden";

    private const string DisabledPreferenceValue = "disabled";

    private readonly Func<string> _executablePathProvider;
    private readonly string _preferencePath;
    private readonly Func<string, IReadOnlyList<string>, WindowsStartupCommandResult> _commandRunner;

    public static WindowsStartupService Default { get; } = new();

    public WindowsStartupService()
        : this(
            GetCurrentExecutablePath,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartPrinter",
                "startup-preference.txt"),
            RunCommand)
    {
    }

    public WindowsStartupService(
        Func<string> executablePathProvider,
        string preferencePath,
        Func<string, IReadOnlyList<string>, WindowsStartupCommandResult> commandRunner)
    {
        _executablePathProvider = executablePathProvider;
        _preferencePath = preferencePath;
        _commandRunner = commandRunner;
    }

    public bool IsEnabled()
    {
        var result = RunSchtasks("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
    }

    public WindowsStartupOperationResult EnsureEnabledByDefault()
    {
        if (UserDisabledStartup())
            return new WindowsStartupOperationResult(true);

        return Enable();
    }

    public WindowsStartupOperationResult SetEnabledByUser(bool enabled)
    {
        if (enabled)
        {
            var result = Enable();
            if (!result.Succeeded)
                return result;

            ClearUserDisabledStartup();
            return result;
        }

        var saveResult = SaveUserDisabledStartup();
        if (!saveResult.Succeeded)
            return saveResult;

        var disableResult = Disable();
        if (!disableResult.Succeeded)
        {
            ClearUserDisabledStartup();
            return disableResult;
        }

        return disableResult;
    }

    public WindowsStartupOperationResult Enable()
    {
        var executablePath = _executablePathProvider();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new WindowsStartupOperationResult(
                false,
                "Could not determine the Smart Printer executable path.");
        }

        var taskRun = $"\"{executablePath}\" {StartHiddenArgument}";
        var result = RunSchtasks(
            "/Create",
            "/TN",
            TaskName,
            "/TR",
            taskRun,
            "/SC",
            "ONLOGON",
            "/RL",
            "HIGHEST",
            "/F");

        return ToOperationResult(result, "enable");
    }

    public WindowsStartupOperationResult Disable()
    {
        var result = RunSchtasks(
            "/Delete",
            "/TN",
            TaskName,
            "/F");

        return result.ExitCode == 0
            ? new WindowsStartupOperationResult(true)
            : new WindowsStartupOperationResult(false, BuildError(result, "disable"));
    }

    private WindowsStartupCommandResult RunSchtasks(params string[] arguments)
        => _commandRunner("schtasks.exe", arguments);

    private bool UserDisabledStartup()
    {
        try
        {
            return File.Exists(_preferencePath)
                && File.ReadAllText(_preferencePath)
                    .Trim()
                    .Equals(DisabledPreferenceValue, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private WindowsStartupOperationResult SaveUserDisabledStartup()
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_preferencePath, DisabledPreferenceValue);
            return new WindowsStartupOperationResult(true);
        }
        catch (Exception ex)
        {
            return new WindowsStartupOperationResult(
                false,
                $"Could not save Smart Printer auto-start preference. {ex.Message}");
        }
    }

    private void ClearUserDisabledStartup()
    {
        try
        {
            if (File.Exists(_preferencePath))
                File.Delete(_preferencePath);
        }
        catch
        {
        }
    }

    private static WindowsStartupOperationResult ToOperationResult(
        WindowsStartupCommandResult result,
        string operation)
        => result.ExitCode == 0
            ? new WindowsStartupOperationResult(true)
            : new WindowsStartupOperationResult(false, BuildError(result, operation));

    private static string BuildError(WindowsStartupCommandResult result, string operation)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        detail = string.IsNullOrWhiteSpace(detail)
            ? $"schtasks.exe exited with code {result.ExitCode}."
            : detail.Trim();
        return $"Could not {operation} Smart Printer auto-start. {detail}";
    }

    private static string GetCurrentExecutablePath()
        => Environment.ProcessPath
            ?? Application.ExecutablePath
            ?? AppContext.BaseDirectory;

    private static WindowsStartupCommandResult RunCommand(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 10000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return new WindowsStartupCommandResult(
                -1,
                "",
                "schtasks.exe timed out after 10 seconds.");
        }

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();

        return new WindowsStartupCommandResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }
}
