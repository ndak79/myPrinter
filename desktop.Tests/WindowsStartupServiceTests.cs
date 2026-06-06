using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class WindowsStartupServiceTests
{
    [Fact]
    public void Enable_creates_highest_logon_task_that_starts_current_executable_hidden()
    {
        var commands = new List<(string FileName, string[] Arguments)>();
        var service = CreateService(
            executablePath: @"C:\Program Files\Smart Printer\MyPrinter.exe",
            commandRunner: (fileName, arguments) =>
            {
                commands.Add((fileName, arguments.ToArray()));
                return new WindowsStartupCommandResult(0, "", "");
            });

        var result = service.Enable();

        result.Succeeded.Should().BeTrue();
        commands.Should().ContainSingle();
        commands[0].FileName.Should().Be("schtasks.exe");
        commands[0].Arguments.Should().Equal(
            "/Create",
            "/TN",
            WindowsStartupService.TaskName,
            "/TR",
            "\"C:\\Program Files\\Smart Printer\\MyPrinter.exe\" --start-hidden",
            "/SC",
            "ONLOGON",
            "/RL",
            "HIGHEST",
            "/F");
    }

    [Fact]
    public void SetEnabledByUser_false_deletes_task_and_persists_user_opt_out()
    {
        using var temp = new TemporaryDirectory();
        var commands = new List<(string FileName, string[] Arguments)>();
        var service = CreateService(
            preferencePath: temp.PreferencePath,
            commandRunner: (fileName, arguments) =>
            {
                commands.Add((fileName, arguments.ToArray()));
                return new WindowsStartupCommandResult(0, "", "");
            });

        var result = service.SetEnabledByUser(false);

        result.Succeeded.Should().BeTrue();
        commands.Should().ContainSingle();
        commands[0].Arguments.Should().Equal(
            "/Delete",
            "/TN",
            WindowsStartupService.TaskName,
            "/F");
        File.ReadAllText(temp.PreferencePath).Should().Contain("disabled");
    }

    [Fact]
    public void SetEnabledByUser_true_enables_task_and_clears_previous_user_opt_out()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(temp.PreferencePath, "disabled");
        var service = CreateService(
            preferencePath: temp.PreferencePath,
            commandRunner: (_, _) => new WindowsStartupCommandResult(0, "", ""));

        var result = service.SetEnabledByUser(true);

        result.Succeeded.Should().BeTrue();
        File.Exists(temp.PreferencePath).Should().BeFalse();
    }

    [Fact]
    public void EnsureEnabledByDefault_does_not_reenable_after_user_opted_out()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.PreferencePath)!);
        File.WriteAllText(temp.PreferencePath, "disabled");
        var service = CreateService(
            preferencePath: temp.PreferencePath,
            commandRunner: (_, _) => throw new InvalidOperationException("No schtasks call expected"));

        var result = service.EnsureEnabledByDefault();

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void EnsureEnabledByDefault_recreates_task_to_keep_executable_path_current()
    {
        using var temp = new TemporaryDirectory();
        var commands = new List<string[]>();
        var service = CreateService(
            executablePath: @"D:\Apps\SmartPrinter\MyPrinter.exe",
            preferencePath: temp.PreferencePath,
            commandRunner: (_, arguments) =>
            {
                commands.Add(arguments.ToArray());
                return new WindowsStartupCommandResult(0, "", "");
            });

        var result = service.EnsureEnabledByDefault();

        result.Succeeded.Should().BeTrue();
        commands.Should().ContainSingle();
        commands[0].Should().StartWith([
            "/Create",
            "/TN",
            WindowsStartupService.TaskName,
        ]);
        commands[0].Should().Contain("/F");
    }

    [Fact]
    public void IsEnabled_queries_the_named_scheduled_task()
    {
        var commands = new List<(string FileName, string[] Arguments)>();
        var service = CreateService(
            commandRunner: (fileName, arguments) =>
            {
                commands.Add((fileName, arguments.ToArray()));
                return new WindowsStartupCommandResult(0, "", "");
            });

        service.IsEnabled().Should().BeTrue();

        commands.Should().ContainSingle();
        commands[0].Arguments.Should().Equal(
            "/Query",
            "/TN",
            WindowsStartupService.TaskName);
    }

    private static WindowsStartupService CreateService(
        string executablePath = @"C:\Apps\MyPrinter.exe",
        string? preferencePath = null,
        Func<string, IReadOnlyList<string>, WindowsStartupCommandResult>? commandRunner = null)
    {
        return new WindowsStartupService(
            () => executablePath,
            preferencePath ?? System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"myprinter-startup-tests-{Guid.NewGuid():N}",
                "startup-preference.txt"),
            commandRunner ?? ((_, _) => new WindowsStartupCommandResult(0, "", "")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"myprinter-startup-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PreferencePath => System.IO.Path.Combine(Path, "startup-preference.txt");

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
