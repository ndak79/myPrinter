using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;

namespace backend.Tests;

public class CreateSimplexJobTests
{
    private readonly Mock<IWordInteropService> _mockWord;
    private readonly PrintAlgorithmService _sut;
    private const string FakePdfPath = @"C:\fake\doc.pdf";
    private const string FakePrinter = "TestPrinter";

    public CreateSimplexJobTests()
    {
        _mockWord = new Mock<IWordInteropService>();
        _mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>()))
                 .Returns(new PdfInfo(4, false));
        _mockWord.Setup(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()))
                 .Returns(@"C:\fake\watermarked.pdf");
        _mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        _sut = new PrintAlgorithmService(_mockWord.Object);
    }

    [Fact]
    public void CreateSimplexJob_NoPageRange_NoWatermark_ReturnsCorrectState()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter);

        result.IsManualDuplex.Should().BeFalse();
        result.WaitingForFlip.Should().BeFalse();
        result.TempPdfPath.Should().Be(FakePdfPath);
    }

    [Fact]
    public void CreateSimplexJob_NoWatermark_DoesNotCallAddWatermark()
    {
        _sut.CreateSimplexJob(FakePdfPath, FakePrinter);

        _mockWord.Verify(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()), Times.Never);
    }

    [Fact]
    public void CreateSimplexJob_WithWatermark_CallsAddWatermark()
    {
        var watermark = new WatermarkOptions { Text = "CONFIDENTIAL" };

        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter, watermark: watermark);

        _mockWord.Verify(w => w.AddWatermarkToPdf(It.IsAny<string>(), watermark), Times.Once);
    }

    [Fact]
    public void CreateSimplexJob_WithWatermark_TempPdfPathIsWatermarked()
    {
        var watermark = new WatermarkOptions { Text = "CONFIDENTIAL" };

        // GetPdfInfo will be called with watermarked path, return 4 pages
        _mockWord.Setup(w => w.GetPdfInfo(@"C:\fake\watermarked.pdf"))
                 .Returns(new PdfInfo(4, false));

        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter, watermark: watermark);

        // No page range, so TempPdfPath = watermarked path (no subset)
        result.TempPdfPath.Should().Be(@"C:\fake\watermarked.pdf");
    }

    [Fact]
    public void CreateSimplexJob_PageRange_CreatesPdfSubset()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter, pageRange: "1-2");

        _mockWord.Verify(w => w.CreatePdfSubset(FakePdfPath, It.IsAny<string>(), It.Is<int[]>(p => p.SequenceEqual(new[] { 1, 2 }))), Times.Once);
    }

    [Fact]
    public void CreateSimplexJob_PageRangeCoversAllPages_DoesNotCreateSubset()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter, pageRange: "1-4");

        _mockWord.Verify(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()), Times.Never);
    }

    [Fact]
    public void CreateSimplexJob_InvalidPageRange_ThrowsInvalidOperationException()
    {
        var act = () => _sut.CreateSimplexJob(FakePdfPath, FakePrinter, pageRange: "99");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateSimplexJob_PrinterNameSetCorrectly()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter);

        result.PrinterName.Should().Be(FakePrinter);
    }

    [Fact]
    public void CreateSimplexJob_JobIdIsNotEmpty()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter);

        result.JobId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateSimplexJob_WatermarkAppliedBeforeSubset()
    {
        var sequence = new MockSequence();
        _mockWord.InSequence(sequence)
                 .Setup(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()))
                 .Returns(@"C:\fake\watermarked.pdf");
        _mockWord.InSequence(sequence)
                 .Setup(w => w.GetPdfInfo(It.IsAny<string>()))
                 .Returns(new PdfInfo(4, false));
        _mockWord.InSequence(sequence)
                 .Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));

        var watermark = new WatermarkOptions { Text = "TEST" };
        _sut.CreateSimplexJob(FakePdfPath, FakePrinter, pageRange: "1-2", watermark: watermark);

        _mockWord.Verify(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()), Times.Once);
        _mockWord.Verify(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()), Times.Once);
    }
}
