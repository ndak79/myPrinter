using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class StartupDiagnosticsTests
{
    [Fact]
    public void Program_wraps_the_earliest_managed_startup_steps_in_fatal_reporting()
    {
        var program = File.ReadAllText(RepoPath("desktop", "Program.cs"));
        var diagnosticsCreation = program.IndexOf("var diagnostics = StartupDiagnostics.Default;", StringComparison.Ordinal);
        var startupTry = program.IndexOf("try", diagnosticsCreation, StringComparison.Ordinal);
        var handlerSetup = program.IndexOf("ConfigureUnhandledExceptionReporting(diagnostics);", StringComparison.Ordinal);

        diagnosticsCreation.Should().BeGreaterThanOrEqualTo(0);
        startupTry.Should().BeGreaterThan(diagnosticsCreation);
        handlerSetup.Should().BeGreaterThan(startupTry);
        program.Should().Contain("ReportFatalError(diagnostics, \"Smart Printer could not start\", ex);");
    }

    [Fact]
    public void RecordException_creates_a_persistent_diagnostic_with_context()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myprinter-diagnostics-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "startup.log");

        try
        {
            var diagnostics = new StartupDiagnostics(logPath);

            diagnostics.RecordException(
                "desktop startup",
                new InvalidOperationException("diagnostic-test-failure"));

            File.Exists(logPath).Should().BeTrue();
            File.ReadAllText(logPath)
                .Should().Contain("desktop startup")
                .And.Contain("InvalidOperationException")
                .And.Contain("diagnostic-test-failure");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecordMessage_appends_instead_of_discarding_previous_startup_evidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myprinter-diagnostics-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "startup.log");

        try
        {
            var diagnostics = new StartupDiagnostics(logPath);

            diagnostics.RecordMessage("first checkpoint");
            diagnostics.RecordMessage("second checkpoint");

            File.ReadAllText(logPath)
                .Should().Contain("first checkpoint")
                .And.Contain("second checkpoint");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Diagnostics_io_failure_never_becomes_a_startup_failure()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myprinter-diagnostics-{Guid.NewGuid():N}");
        var blockingFile = Path.Combine(directory, "not-a-directory");
        Directory.CreateDirectory(directory);
        File.WriteAllText(blockingFile, "block directory creation");

        try
        {
            var diagnostics = new StartupDiagnostics(Path.Combine(blockingFile, "startup.log"));

            var action = () => diagnostics.RecordMessage("must not escape");

            action.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RepoPath(params string[] parts)
        => Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]);
}
