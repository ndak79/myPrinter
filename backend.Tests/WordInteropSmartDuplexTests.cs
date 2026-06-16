using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrinterApp.Services;

namespace backend.Tests;

public sealed class WordInteropSmartDuplexTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void CreateSmartDuplexPdf_BackPassRotation_PreservesRawPageBoxAndUsesRotateMetadata()
    {
        var sourcePath = CreatePdf(widthPoints: 841.89, heightPoints: 595.28, rotate: 90);
        var service = new WordInteropService();

        var outputPath = service.CreateSmartDuplexPdf(sourcePath, new[] { 1 });
        _tempFiles.Add(outputPath);

        using var output = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        var page = output.Pages[0];
        page.Width.Point.Should().BeApproximately(841.89, 0.01);
        page.Height.Point.Should().BeApproximately(595.28, 0.01);
        page.Rotate.Should().Be(270);
    }

    private string CreatePdf(double widthPoints, double heightPoints, int rotate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"myprinter-smart-duplex-{Guid.NewGuid():N}.pdf");
        _tempFiles.Add(path);

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(widthPoints);
        page.Height = XUnit.FromPoint(heightPoints);
        page.Rotate = rotate;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(XBrushes.Black, 10, 10, 50, 20);
        }

        document.Save(path);
        return path;
    }
}
