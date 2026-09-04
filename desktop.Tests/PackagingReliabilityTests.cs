using FluentAssertions;

namespace desktop.Tests;

public class PackagingReliabilityTests
{
    [Fact]
    public void Publish_is_self_contained_without_single_file_temp_extraction()
    {
        var buildScript = ReadRepoFile("build-installer.ps1");

        buildScript.Should().Contain("--self-contained true");
        buildScript.Should().Contain("PublishSingleFile=false");
        buildScript.Should().NotContain("PublishSingleFile=true");
        buildScript.Should().Contain("/p:Version=$Version");
        buildScript.Should().Contain("MyPrinter.runtimeconfig.json");
        buildScript.Should().Contain("WebView2Loader.dll");
    }

    [Fact]
    public void Installer_bootstraps_webview2_before_installation_completes()
    {
        var buildScript = ReadRepoFile("build-installer.ps1");
        var installer = ReadRepoFile("installer", "myPrinter.iss");

        buildScript.Should().Contain("MicrosoftEdgeWebview2Setup.exe");
        buildScript.Should().Contain("Get-AuthenticodeSignature");
        buildScript.Should().Contain("Microsoft Corporation");

        installer.Should().Contain("MicrosoftEdgeWebview2Setup.exe");
        installer.Should().Contain("PrepareToInstall");
        installer.Should().Contain("F3017226-FE2A-4295-8BDF-00C3A9A7E4C5");
        installer.Should().Contain("/silent /install");
        installer.Should().Contain("recursesubdirs createallsubdirs");
    }

    [Fact]
    public void Installer_bundles_and_silently_installs_pinned_sumatra_pdf()
    {
        var buildScript = ReadRepoFile("build-installer.ps1");
        var installer = ReadRepoFile("installer", "myPrinter.iss");
        var notices = ReadRepoFile("THIRD-PARTY-NOTICES.txt");

        buildScript.Should().Contain("SumatraPDF-3.6.1-64-install.exe");
        buildScript.Should().Contain("1EEE71CCCD2EA6E94D5BCEA54EE2F759844DA3E1A0EE2F6045035B1D17B94381");
        buildScript.Should().Contain("Get-FileHash");
        buildScript.Should().Contain("Krzysztof Kowalczyk");

        installer.Should().Contain("SumatraPDF-3.6.1-64-install.exe");
        installer.Should().Contain("IsSumatraPdfInstalled");
        installer.Should().Contain("-install -silent -all-users");
        installer.Should().Contain("THIRD-PARTY-NOTICES.txt");

        notices.Should().Contain("SumatraPDF 3.6.1");
        notices.Should().Contain("https://github.com/sumatrapdfreader/sumatrapdf/tree/3.6.1rel");
    }

    [Fact]
    public void Convenience_build_launcher_does_not_pin_an_obsolete_version()
    {
        var launcher = ReadRepoFile("publish.bat");

        launcher.Should().Contain("build-installer.ps1");
        launcher.Should().NotContain("-Version \"1.0.0\"");
        launcher.Should().NotContain("smartPrinter-setup-1.0.0.exe");
    }

    [Fact]
    public void Window_open_clears_initial_hidden_state_before_showing_the_form()
    {
        var mainForm = ReadRepoFile("desktop", "MainForm.cs");
        var showWindowStart = mainForm.IndexOf("private void ShowWindow()", StringComparison.Ordinal);
        var nextMethodStart = mainForm.IndexOf("internal void ShowFromExternalRequest()", showWindowStart, StringComparison.Ordinal);
        var showWindow = mainForm.Substring(showWindowStart, nextMethodStart - showWindowStart);

        showWindow.Should().Contain("_suppressInitialShow = false;");
        showWindow.Should().Contain("ShowInTaskbar = true;");
        showWindow.IndexOf("_suppressInitialShow = false;", StringComparison.Ordinal)
            .Should().BeLessThan(showWindow.IndexOf("Show();", StringComparison.Ordinal));
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]));
}
