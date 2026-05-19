using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class RuntimeLicenseMonitorTests
{
    [Fact]
    public async Task CheckNowAsync_notifies_once_when_license_becomes_invalid()
    {
        var callbackCount = 0;
        using var monitor = new RuntimeLicenseMonitor(
            () => false,
            TimeSpan.FromMinutes(1),
            () => callbackCount++);

        await monitor.CheckNowAsync();
        await monitor.CheckNowAsync();

        callbackCount.Should().Be(1);
    }

    [Fact]
    public void MainForm_reopens_activation_when_runtime_license_check_fails()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var mainForm = File.ReadAllText(Path.Combine(repoRoot, "desktop", "MainForm.cs"));

        mainForm.Should().Contain("RuntimeLicenseMonitor");
        mainForm.Should().Contain("HandleRuntimeLicenseInvalidAsync");
        mainForm.Should().Contain("using var activationForm = new ActivationForm();");
        mainForm.Should().Contain("activationForm.ShowDialog(this)");
        mainForm.Should().Contain("ExitApp();");
        mainForm.Should().Contain("ShowFromExternalActivation();");
    }
}
