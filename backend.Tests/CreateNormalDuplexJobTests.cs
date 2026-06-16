using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;

namespace backend.Tests;

public class CreateNormalDuplexJobTests
{
    private readonly Mock<IWordInteropService> _mockWord;
    private readonly PrintAlgorithmService _sut;
    private const string FakePdfPath = @"C:\fake\doc.pdf";
    private const string FakePrinter = "TestPrinter";

    public CreateNormalDuplexJobTests()
    {
        _mockWord = new Mock<IWordInteropService>();
        _mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>()))
                 .Returns(new PdfInfo(4, false));
        _mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        _sut = new PrintAlgorithmService(_mockWord.Object);
    }

    // ==========================================
    //  AUTO DUPLEX (isDuplexPrinter = true)
    // ==========================================

    [Fact]
    public void AutoDuplex_NoPageRange_ReturnsCorrectState()
    {
        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: true);

        result.WaitingForFlip.Should().BeFalse();
        result.IsManualDuplex.Should().BeFalse();
        result.TempPdfPath.Should().Be(FakePdfPath);
    }

    [Fact]
    public void AutoDuplex_PageRange_CreatesSubset()
    {
        _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: true, pageRange: "1-2");

        _mockWord.Verify(w => w.CreatePdfSubset(
            FakePdfPath,
            It.IsAny<string>(),
            It.Is<int[]>(p => p.SequenceEqual(new[] { 1, 2 }))),
            Times.Once);
    }

    [Fact]
    public void AutoDuplex_PageRangeCoversAll_NoSubset()
    {
        _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: true, pageRange: "1-4");

        _mockWord.Verify(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()), Times.Never);
    }

    [Fact]
    public void AutoDuplex_InvalidPageRange_ThrowsInvalidOperation()
    {
        var act = () => _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: true, pageRange: "99");

        act.Should().Throw<InvalidOperationException>();
    }

    // ==========================================
    //  MANUAL DUPLEX (isDuplexPrinter = false)
    // ==========================================

    private void SetupManualDuplexMocks(bool isLandscape = false, int pageCount = 2)
    {
        _mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>()))
                 .Returns(new PdfInfo(pageCount, isLandscape));

        var pageInfos = Enumerable.Range(1, pageCount)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = isLandscape
            })
            .ToList();

        // Pad to even if needed
        if (pageCount % 2 != 0)
        {
            pageInfos.Add(new ManualDuplexPageInfo
            {
                ProcessedIndex = pageCount + 1,
                OriginalPageNumber = -1,
                IsBlank = true,
                IsLandscape = isLandscape
            });
        }

        var outPageInfos = pageInfos;
        _mockWord.Setup(w => w.ProcessMixedOrientation(
            It.IsAny<string>(),
            It.IsAny<int[]?>(),
            out outPageInfos))
            .Returns(@"C:\fake\processed.pdf");
    }

    [Fact]
    public void ManualDuplex_2PagePortrait_IsManualDuplexTrue()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.IsManualDuplex.Should().BeTrue();
        result.WaitingForFlip.Should().BeTrue();
    }

    [Fact]
    public void ManualDuplex_Portrait_FlipDirectionIsLongEdge()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.Instruction.Should().NotBeNull();
        result.Instruction!.Direction.Should().Be(FlipDirection.LongEdge);
    }

    [Fact]
    public void ManualDuplex_Landscape_FlipDirectionIsShortEdge()
    {
        SetupManualDuplexMocks(isLandscape: true, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.Instruction.Should().NotBeNull();
        result.Instruction!.Direction.Should().Be(FlipDirection.ShortEdge);
    }

    [Fact]
    public void ManualDuplex_ManualFlipDirShortEdge_OverridesPortraitMetadata()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(
            FakePdfPath,
            FakePrinter,
            isDuplexPrinter: false,
            manualFlipDir: "ShortEdge");

        result.Instruction.Should().NotBeNull();
        result.Instruction!.Direction.Should().Be(FlipDirection.ShortEdge);
    }

    [Fact]
    public void ManualDuplex_2Pages_Phase1AndPhase2Correct()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.OddPages.Should().Contain(1);
        result.RemainingPages.Should().Contain(2);
    }

    [Fact]
    public void ManualDuplex_TempPdfPath_IsProcessedPath()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.TempPdfPath.Should().Be(@"C:\fake\processed.pdf");
    }

    [Fact]
    public void ManualDuplex_InstructionText_IsNotNullOrEmpty()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.Instruction!.Text.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ManualDuplex_ManualPlan_IsSet()
    {
        SetupManualDuplexMocks(isLandscape: false, pageCount: 2);

        var result = _sut.CreateNormalDuplexJob(FakePdfPath, FakePrinter, isDuplexPrinter: false);

        result.ManualPlan.Should().NotBeNull();
    }

}
