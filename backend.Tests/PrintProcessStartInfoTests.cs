using FluentAssertions;
using PrinterApp.Services;

namespace backend.Tests;

public sealed class PrintProcessStartInfoTests
{
    [Fact]
    public void BuildSumatraPrintStartInfo_passes_printer_and_pdf_as_argument_list_items()
    {
        const string printerName = "Office \"Duplex\" /unexpected";
        const string pdfPath = @"C:\Temp\print job.pdf";

        var startInfo = WordInteropService.BuildSumatraPrintStartInfo(
            @"C:\Tools\SumatraPDF.exe",
            pdfPath,
            printerName,
            "ShortEdge");

        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.CreateNoWindow.Should().BeTrue();
        startInfo.Arguments.Should().BeEmpty();
        startInfo.ArgumentList.Should().Equal(
            "-print-to",
            printerName,
            "-print-settings",
            "duplexshort",
            pdfPath);
    }

    [Fact]
    public void BuildShellPrintStartInfo_uses_shell_verb_without_powershell_command()
    {
        const string pdfPath = @"C:\Temp\print job.pdf";

        var startInfo = WordInteropService.BuildShellPrintStartInfo(pdfPath);

        startInfo.FileName.Should().Be(pdfPath);
        startInfo.Verb.Should().Be("print");
        startInfo.UseShellExecute.Should().BeTrue();
        startInfo.Arguments.Should().BeEmpty();
    }
}
