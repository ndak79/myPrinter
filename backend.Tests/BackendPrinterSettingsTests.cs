using FluentAssertions;
using PrinterApp;

namespace backend.Tests;

public sealed class BackendPrinterSettingsTests
{
    [Fact]
    public void BuildPrinterSettingsStartInfo_passes_printer_name_as_argument_list_item()
    {
        const string printerName = @"\\print-host\Office Printer";

        var startInfo = BackendStartup.BuildPrinterSettingsStartInfo(printerName);

        startInfo.FileName.Should().Be("rundll32.exe");
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.Arguments.Should().BeEmpty();
        startInfo.ArgumentList.Should().Equal(
            "printui.dll,PrintUIEntry",
            "/e",
            "/n",
            printerName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Office \"Printer\"")]
    [InlineData("Office\r\nPrinter")]
    public void BuildPrinterSettingsStartInfo_rejects_unsafe_printer_names(string printerName)
    {
        var act = () => BackendStartup.BuildPrinterSettingsStartInfo(printerName);

        act.Should().Throw<ArgumentException>();
    }
}
