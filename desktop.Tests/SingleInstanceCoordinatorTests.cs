using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Program_routes_secondary_launches_to_the_existing_window_before_startup()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var program = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Program.cs"));

        program.Should().Contain("using var singleInstance = SingleInstanceCoordinator.Create();");
        program.Should().Contain("if (!singleInstance.IsPrimary)");
        program.Should().Contain("singleInstance.SignalExistingInstance();");
        program.Should().Contain("_ = mainForm.Handle;");
        program.Should().Contain("singleInstance.StartActivationListener");
        program.Should().Contain("ShowFromExternalActivation");
        program.Should().Contain("catch (InvalidOperationException)");
        program.Should().Contain("catch (ObjectDisposedException)");

        program.IndexOf("if (!singleInstance.IsPrimary)", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("LoadActivationConfig()", StringComparison.Ordinal));
        program.IndexOf("_ = mainForm.Handle;", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("singleInstance.StartActivationListener", StringComparison.Ordinal));
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
    public void Secondary_instance_signal_invokes_primary_activation_callback()
    {
        var instanceName = $"MyPrinter.Tests.{Guid.NewGuid():N}";
        using var activationRequested = new ManualResetEventSlim();

        using var first = SingleInstanceCoordinator.Create(instanceName);
        using var listener = first.StartActivationListener(() => activationRequested.Set());
        var thread = new Thread(() =>
        {
            using var second = SingleInstanceCoordinator.Create(instanceName);
            second.SignalExistingInstance();
        });
        thread.Start();
        thread.Join();

        activationRequested.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
    }
}
