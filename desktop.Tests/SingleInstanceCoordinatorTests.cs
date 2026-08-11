using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Program_exits_secondary_launches_without_window_activation()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var program = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Program.cs"));

        program.Should().Contain("var startHidden = args.Contains(WindowsStartupService.StartHiddenArgument, StringComparer.OrdinalIgnoreCase);");
        program.Should().Contain("using var singleInstance = SingleInstanceCoordinator.Create();");
        program.Should().Contain("if (!singleInstance.IsPrimary)");
        program.Should().NotContain("singleInstance.SignalExistingInstance();");
        program.Should().Contain("WindowsStartupService.Default.EnsureEnabledByDefault();");
        program.Should().Contain("_ = mainForm.Handle;");
        program.Should().NotContain("singleInstance.StartActivationListener");
        program.Should().NotContain("ShowFromExternalActivation");
        program.Should().Contain("using var mainForm = new MainForm(startHidden);");

        program.IndexOf("WindowsStartupService.Default.EnsureEnabledByDefault();", StringComparison.Ordinal)
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
    public void Single_instance_coordinator_exposes_no_window_activation_channel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "SingleInstanceCoordinator.cs"));

        typeof(SingleInstanceCoordinator).GetMethod("StartActivationListener").Should().BeNull();
        typeof(SingleInstanceCoordinator).GetMethod("SignalExistingInstance").Should().BeNull();
        source.Should().NotContain("EventWaitHandle")
            .And.NotContain("RegisteredWaitHandle")
            .And.NotContain("Activation");
    }
}
