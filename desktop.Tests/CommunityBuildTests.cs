using FluentAssertions;

namespace desktop.Tests;

public class CommunityBuildTests
{
    [Fact]
    public void Desktop_startup_and_main_window_have_no_product_gate()
    {
        ReadRepoFile("desktop", "Program.cs")
            .Should().NotContain("LicenseGuard")
            .And.NotContain("LoadActivationConfig")
            .And.NotContain("LoadPublicKeysetJson")
            .And.NotContain("ActivationForm");

        ReadRepoFile("desktop", "MainForm.cs")
            .Should().NotContain("RuntimeLicense")
            .And.NotContain("LicenseGuard")
            .And.NotContain("ActivationForm");
    }

    [Fact]
    public void Desktop_project_has_no_product_activation_assets_or_packages()
    {
        if (Directory.Exists(RepoPath("desktop", "Activation")))
            Directory.GetFileSystemEntries(RepoPath("desktop", "Activation")).Should().BeEmpty();
        File.Exists(RepoPath("desktop", "ActivationForm.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "ActivationForm.Designer.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "RuntimeLicenseMonitor.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "smartprinter.appsettings.json")).Should().BeFalse();

        ReadRepoFile("desktop", "MyPrinter.Desktop.csproj")
            .Should().NotContain("BouncyCastle")
            .And.NotContain("NSec.Cryptography")
            .And.NotContain("license_keyset")
            .And.NotContain("smartprinter.appsettings");
    }

    [Fact]
    public void Installer_does_not_require_product_activation_inputs()
    {
        ReadRepoFile("build-installer.ps1")
            .Should().NotContain("Activation")
            .And.NotContain("ServerUrl")
            .And.NotContain("ProductId")
            .And.NotContain("TransportKey")
            .And.NotContain("Keyset");

        ReadRepoFile("installer", "myPrinter.iss")
            .Should().NotContain("KeysetFileName")
            .And.NotContain("smartprinter.appsettings")
            .And.NotContain("Activation");

        ReadRepoFile("publish.bat")
            .Should().NotContain("-ServerUrl")
            .And.NotContain("-ProductId")
            .And.NotContain("-TransportKey")
            .And.NotContain("-AllowInsecureHttp");
    }

    private static string RepoPath(params string[] parts)
        => Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]);

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(RepoPath(parts));
}
