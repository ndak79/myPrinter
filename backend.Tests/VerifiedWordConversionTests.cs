using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PrinterApp.Services.WordConversion;
using System.IO.Compression;

namespace backend.Tests;

public sealed class VerifiedWordConversionTests : IDisposable
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
    public void InspectPdf_UsesRotateMetadataForEffectivePageSize()
    {
        var pdfPath = CreatePdf(841.89, 595.28, rotate: 90);

        var pages = PdfPageSizeVerifier.InspectPdf(pdfPath);

        pages.Should().ContainSingle();
        pages[0].EffectiveWidthPoints.Should().BeApproximately(595.28, 0.01);
        pages[0].EffectiveHeightPoints.Should().BeApproximately(841.89, 0.01);
        pages[0].PaperName.Should().Be("A4 portrait");
    }

    [Fact]
    public void ThrowIfMismatch_AllowsA4WithinTolerance()
    {
        var expected = new[]
        {
            new WordSourcePageSize(1, 595.05, 842.00, "A4 portrait")
        };
        var actual = new[]
        {
            new PdfPageSize(1, 595.08, 842.04, 595.08, 842.04, 0, "A4 portrait")
        };

        var act = () => PdfPageSizeVerifier.ThrowIfMismatch(expected, actual, "source.docx", "output.pdf");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfMismatch_RejectsA4SourceConvertedToLetterPdf()
    {
        var expected = new[]
        {
            new WordSourcePageSize(1, 595.05, 842.00, "A4 portrait")
        };
        var actual = new[]
        {
            new PdfPageSize(1, 612.00, 792.00, 612.00, 792.00, 0, "Letter portrait")
        };

        var act = () => PdfPageSizeVerifier.ThrowIfMismatch(expected, actual, "source.docx", "output.pdf");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*page 1*source.docx*A4 portrait*output.pdf*Letter portrait*");
    }

    [Fact]
    public void ConvertToPdf_DeletesOutputAndThrows_WhenWorkerProducesWrongPaperSize()
    {
        var inputPath = CreateTempPath(".docx");
        File.WriteAllText(inputPath, "fake source");
        var outputPath = CreatePdf(612.00, 792.00);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 1,
            HasExactPageSizes = true,
            SourcePageSizes = new List<WordSourcePageSize>
            {
                new(1, 595.05, 842.00, "A4 portrait")
            }
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*A4 portrait*Letter portrait*");
        File.Exists(outputPath).Should().BeFalse("a failed conversion must not leave a printable bad PDF behind");
    }

    [Fact]
    public void ConvertToPdf_UsesRawDocxPageSize_WhenWordWorkerReportsDifferentSize()
    {
        var inputPath = CreateDocxWithPageSize(widthTwips: 11907, heightTwips: 16840);
        var outputPath = CreatePdf(612.00, 792.00);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 1,
            HasExactPageSizes = true,
            SourcePageSizes = new List<WordSourcePageSize>
            {
                new(1, 612.00, 792.00, "Letter portrait")
            }
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*A4 portrait*Letter portrait*");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public void ConvertToPdf_AllowsRawUniformDocx_WhenWorkerSectionMappingIsAmbiguous()
    {
        var inputPath = CreateDocxWithPageSize(widthTwips: 11907, heightTwips: 16840);
        var outputPath = CreatePdf(595.08, 842.04);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 1,
            HasExactPageSizes = false
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().NotThrow();
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void ConvertToPdf_AllowsValidPdf_WhenWorkerSourceInspectionIsUnavailable()
    {
        var inputPath = CreateTempPath(".doc");
        File.WriteAllText(inputPath, "fake source");
        var outputPath = CreatePdf(595.08, 842.04);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 0,
            HasExactPageSizes = false,
            SourcePageInspectionWarning = "Word source inspection failed: COMException: The remote procedure call failed."
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().NotThrow("a transient Word page-inspection failure must not reject a valid exported PDF");
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void ConvertToPdf_UsesPdfPageCountForRawDocx_WhenWorkerPageInspectionIsUnavailable()
    {
        var inputPath = CreateDocxWithPageSize(widthTwips: 11907, heightTwips: 16840);
        var outputPath = CreatePdf(612.00, 792.00);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 0,
            HasExactPageSizes = false,
            SourcePageInspectionWarning = "Word source inspection failed: COMException: The remote procedure call failed."
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*A4 portrait*Letter portrait*");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public void ConvertToPdf_RejectsAmbiguousSource_WhenInspectionCompletedWithoutExactSizes()
    {
        var inputPath = CreateTempPath(".doc");
        File.WriteAllText(inputPath, "fake source");
        var outputPath = CreatePdf(595.08, 842.04);
        var worker = new FakeWorkerClient(new WordConversionWorkerResult
        {
            Success = true,
            SourcePageCount = 1,
            HasExactPageSizes = false
        });
        var converter = new VerifiedWordConverter(worker, TimeSpan.FromSeconds(10));

        var act = () => converter.ConvertToPdf(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mixed or ambiguous section page sizes*");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public void BuildStartInfo_PassesWorkerArgumentsAsSeparateItems()
    {
        const string inputPath = @"C:\Temp\source doc.docx";
        const string outputPath = @"C:\Temp\converted doc.pdf";
        const string resultPath = @"C:\Temp\worker result.json";

        var startInfo = WordConversionWorkerClient.BuildStartInfo(inputPath, outputPath, resultPath);

        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.CreateNoWindow.Should().BeTrue();
        startInfo.RedirectStandardOutput.Should().BeTrue();
        startInfo.RedirectStandardError.Should().BeTrue();
        startInfo.ArgumentList.Should().ContainInOrder(
            WordConversionWorkerCommand.CommandSwitch,
            "--input",
            inputPath,
            "--output",
            outputPath,
            "--result",
            resultPath);
    }

    private string CreatePdf(double widthPoints, double heightPoints, int rotate = 0)
    {
        var path = CreateTempPath(".pdf");
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(widthPoints);
        page.Height = XUnit.FromPoint(heightPoints);
        page.Rotate = rotate;
        document.Save(path);
        return path;
    }

    private string CreateTempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"myprinter-word-conversion-{Guid.NewGuid():N}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private string CreateDocxWithPageSize(int widthTwips, int heightTwips)
    {
        var path = CreateTempPath(".docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("word/document.xml");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write($$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>test</w:t></w:r></w:p>
                <w:sectPr>
                  <w:pgSz w:w="{{widthTwips}}" w:h="{{heightTwips}}"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
        return path;
    }

    private sealed class FakeWorkerClient : IWordConversionWorkerClient
    {
        private readonly WordConversionWorkerResult _result;

        public FakeWorkerClient(WordConversionWorkerResult result)
        {
            _result = result;
        }

        public WordConversionWorkerResult Convert(string inputPath, string outputPath, TimeSpan timeout)
        {
            return _result;
        }
    }
}
