using FluentAssertions;
using MyPrinter.Desktop;
using System.IO.Pipes;

namespace desktop.Tests;

public class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Program_starts_acknowledged_window_open_listener_before_slow_startup_work()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var program = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Program.cs"));

        program.Should().Contain("var startHidden = args.Contains(");
        program.Should().Contain("WindowsStartupService.StartHiddenArgument");
        program.Should().Contain("StringComparer.OrdinalIgnoreCase");
        program.Should().Contain("using var singleInstance = SingleInstanceCoordinator.Create();");
        program.Should().Contain("if (!singleInstance.IsPrimary)");
        program.Should().Contain("singleInstance.TryOpenExistingWindow");
        program.Should().Contain("new WindowOpenRequestRouter()");
        program.Should().Contain("WindowsStartupService.Default.EnsureEnabledByDefault();");
        program.Should().Contain("_ = mainForm.Handle;");
        program.Should().Contain("singleInstance.StartWindowOpenListener");
        program.Should().Contain("ShowFromExternalRequest");
        program.Should().Contain("using var mainForm = new MainForm(startHidden);");
        program.Should().NotContain("SignalExistingInstance");

        var secondaryBranchStart = program.IndexOf("if (!singleInstance.IsPrimary)", StringComparison.Ordinal);
        var primaryStartupStart = program.IndexOf(
            "var windowOpenRouter = new WindowOpenRequestRouter();",
            secondaryBranchStart,
            StringComparison.Ordinal);
        var secondaryBranch = program.Substring(
            secondaryBranchStart,
            primaryStartupStart - secondaryBranchStart);

        secondaryBranch.IndexOf("singleInstance.TryOpenExistingWindow", StringComparison.Ordinal)
            .Should().BeGreaterThanOrEqualTo(0)
            .And.BeLessThan(secondaryBranch.IndexOf("return;", StringComparison.Ordinal));
        secondaryBranch.Should().Contain("startHidden = false;");
        program.IndexOf("singleInstance.StartWindowOpenListener", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("WindowsStartupService.Default.EnsureEnabledByDefault();", StringComparison.Ordinal));
        program.IndexOf("singleInstance.StartWindowOpenListener", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("using var mainForm = new MainForm(startHidden);", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_first_coordinator_for_a_name_becomes_primary()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        bool? secondIsPrimary = null;

        using var first = SingleInstanceCoordinator.Create(instanceName);
        var thread = new Thread(() =>
        {
            using var second = SingleInstanceCoordinator.Create(instanceName);
            secondIsPrimary = second.IsPrimary;
        });
        thread.Start();
        thread.Join();

        first.IsPrimary.Should().BeTrue();
        secondIsPrimary.Should().BeFalse();
    }

    [Fact]
    public void Secondary_window_open_is_acknowledged_after_primary_accepts_the_request()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        using var windowOpenRequested = new ManualResetEventSlim();
        using var first = SingleInstanceCoordinator.Create(instanceName);
        using var listener = first.StartWindowOpenListener(() => windowOpenRequested.Set());
        bool? requestAcknowledged = null;

        var thread = new Thread(() =>
        {
            using var second = SingleInstanceCoordinator.Create(instanceName);
            second.IsPrimary.Should().BeFalse();
            requestAcknowledged = second.TryOpenExistingWindow(TimeSpan.FromSeconds(2));
        });
        thread.Start();
        thread.Join();

        requestAcknowledged.Should().BeTrue();
        windowOpenRequested.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public void Secondary_window_open_is_not_reported_as_success_when_primary_has_no_listener()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceCoordinator.Create(instanceName);
        bool? requestAcknowledged = null;

        var thread = new Thread(() =>
        {
            using var second = SingleInstanceCoordinator.Create(instanceName);
            second.IsPrimary.Should().BeFalse();
            requestAcknowledged = second.TryOpenExistingWindow(TimeSpan.FromMilliseconds(250));
        });
        thread.Start();
        thread.Join();

        requestAcknowledged.Should().BeFalse();
    }

    [Fact]
    public void Current_protocol_name_does_not_collide_with_the_legacy_release()
    {
        SingleInstanceCoordinator.CurrentProtocolInstanceName
            .Should().NotBe(SingleInstanceCoordinator.LegacyProtocolInstanceName);
    }

    [Fact]
    public void Window_open_pipe_is_scoped_to_the_current_windows_session()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        using var coordinator = SingleInstanceCoordinator.Create(instanceName);

        coordinator.WindowOpenPipeName.Should().Contain(
            $".Session{System.Diagnostics.Process.GetCurrentProcess().SessionId}.");
    }

    [Fact]
    public void Listener_survives_a_client_that_disconnects_before_acknowledgement()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var requestCount = 0;
        using var first = SingleInstanceCoordinator.Create(instanceName);
        using var listener = first.StartWindowOpenListener(() =>
        {
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(2));
            }
        });

        using (var abandonedClient = new NamedPipeClientStream(
            ".",
            first.WindowOpenPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            abandonedClient.Connect(2000);
            abandonedClient.WriteByte(SingleInstanceCoordinator.WindowOpenRequestCode);
            abandonedClient.Flush();
            callbackEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
        releaseCallback.Set();

        bool? secondRequestAcknowledged = null;
        var thread = new Thread(() =>
        {
            using var second = SingleInstanceCoordinator.Create(instanceName);
            secondRequestAcknowledged = second.TryOpenExistingWindow(TimeSpan.FromSeconds(2));
        });
        thread.Start();
        thread.Join();

        secondRequestAcknowledged.Should().BeTrue();
        requestCount.Should().Be(2);
    }
}
