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
        _mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        _sut = new PrintAlgorithmService(_mockWord.Object);
    }

    [Fact]
    public void CreateSimplexJob_NoPageRange_ReturnsCorrectState()
    {
        var result = _sut.CreateSimplexJob(FakePdfPath, FakePrinter);

        result.IsManualDuplex.Should().BeFalse();
        result.WaitingForFlip.Should().BeFalse();
        result.TempPdfPath.Should().Be(FakePdfPath);
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

}
