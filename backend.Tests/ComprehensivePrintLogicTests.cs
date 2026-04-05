using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;
using Xunit;

namespace backend.Tests;

/// <summary>
/// Comprehensive test suite covering all print logic combinations in PrintAlgorithmService.
/// Sections: A) Simplex, B) Auto Duplex, C) Manual Duplex, D) Booklet,
///           E) ApplyPageOrder, F) RemapRotations, G) BuildRotationMap, H) ParsePageRange.
/// </summary>
public class ComprehensivePrintLogicTests
{
    private readonly PrintAlgorithmService _sut;

    public ComprehensivePrintLogicTests()
    {
        var mockWord = new Mock<IWordInteropService>();
        _sut = new PrintAlgorithmService(mockWord.Object);
    }

    #region Helpers

    private static Mock<IWordInteropService> CreateMockWord(int pageCount, bool isLandscape = false)
    {
        var mock = new Mock<IWordInteropService>();
        mock.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(pageCount, isLandscape));
        return mock;
    }

    private static List<ManualDuplexPageInfo> MakePageInfos(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsBlank = false,
                IsLandscape = false
            })
            .ToList();

    #endregion

    // ================================================================
    //  A. SIMPLEX TESTS
    // ================================================================

    [Fact]
    public void Simplex_AllPages_NoOptions_NoSubsetCreated()
    {
        var mockWord = CreateMockWord(5);
        var service = new PrintAlgorithmService(mockWord.Object);

        service.CreateSimplexJob("test.pdf", "Printer1");

        mockWord.Verify(
            w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()),
            Times.Never);
    }

    [Fact]
    public void Simplex_PageRange_2To4_CreatesSubset234()
    {
        var mockWord = CreateMockWord(5);
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "2-4");

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 2, 3, 4 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Simplex_PageRange_WithCustomOrder_423_SubsetInExactOrder()
    {
        var mockWord = CreateMockWord(5);
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "2-4", pageOrder: new[] { 4, 2, 3 });

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 4, 2, 3 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Simplex_Watermark_AppliedBeforeSubsetCreation()
    {
        var mockWord = CreateMockWord(5);
        mockWord.Setup(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()))
                .Returns(@"C:\fake\watermarked.pdf");

        string? subsetSource = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => subsetSource = s);

        var service = new PrintAlgorithmService(mockWord.Object);
        var watermark = new WatermarkOptions { Text = "DRAFT" };

        service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "1-3", watermark: watermark);

        mockWord.Verify(w => w.AddWatermarkToPdf("test.pdf", watermark), Times.Once);
        subsetSource.Should().Be(@"C:\fake\watermarked.pdf");
    }

    [Fact]
    public void Simplex_RotationCW90_OnPage2_ApplyPageRotationsCalledCorrectly()
    {
        var mockWord = CreateMockWord(5);
        Dictionary<int, RotationDirection>? capturedRotations = null;
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Callback<string, Dictionary<int, RotationDirection>>((s, map) => capturedRotations = map)
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.CW90 }
        };

        service.CreateSimplexJob("test.pdf", "Printer1", pageRotations: rotations);

        capturedRotations.Should().NotBeNull();
        capturedRotations.Should().ContainKey(2);
        capturedRotations![2].Should().Be(RotationDirection.CW90);
    }

    // ================================================================
    //  B. AUTO DUPLEX TESTS (isDuplexPrinter=true)
    // ================================================================

    [Fact]
    public void AutoDuplex_AllPages_NoSubsetCreated()
    {
        var mockWord = CreateMockWord(5);
        var service = new PrintAlgorithmService(mockWord.Object);

        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: true);

        mockWord.Verify(
            w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()),
            Times.Never);
    }

    [Fact]
    public void AutoDuplex_PageRange_135_CreatesSubset135()
    {
        var mockWord = CreateMockWord(5);
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: true, pageRange: "1,3,5");

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 1, 3, 5 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void AutoDuplex_PageRange_WithRotation_RotationsRemappedToSubsetIndices()
    {
        var mockWord = CreateMockWord(5);
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));

        Dictionary<int, RotationDirection>? capturedRotations = null;
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Callback<string, Dictionary<int, RotationDirection>>((s, map) => capturedRotations = map)
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 3, Rotation = RotationDirection.CW90 }
        };

        // selectedPages = [1,3,5], page 3 at index 1 (0-based) => subset index 2
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: true,
            pageRange: "1,3,5", pageRotations: rotations);

        capturedRotations.Should().NotBeNull();
        capturedRotations.Should().ContainKey(2);
        capturedRotations![2].Should().Be(RotationDirection.CW90);
    }

    [Fact]
    public void AutoDuplex_Watermark_AppliedFirst()
    {
        var mockWord = CreateMockWord(3);
        mockWord.Setup(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()))
                .Returns(@"C:\fake\watermarked.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var watermark = new WatermarkOptions { Text = "CONFIDENTIAL" };

        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: true, watermark: watermark);

        mockWord.Verify(w => w.AddWatermarkToPdf("test.pdf", watermark), Times.Once);
    }

    // ================================================================
    //  C. MANUAL DUPLEX TESTS (isDuplexPrinter=false)
    // ================================================================

    [Fact]
    public void ManualDuplex_AllPages_NoSingleSided_ProcessMixedOrientationCalledWithEmpty()
    {
        var mockWord = CreateMockWord(4);
        var pageInfos = MakePageInfos(4);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false);

        mockWord.Verify(w => w.ProcessMixedOrientation(
            It.IsAny<string>(),
            It.Is<int[]?>(ss => ss != null && ss.Length == 0),
            out pageInfos), Times.Once);
    }

    [Fact]
    public void ManualDuplex_SingleSidedPage2_ProcessMixedOrientationCalledWithPage2()
    {
        var mockWord = CreateMockWord(4);
        var pageInfos = MakePageInfos(4);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false,
            singleSidedPages: new[] { 2 });

        mockWord.Verify(w => w.ProcessMixedOrientation(
            It.IsAny<string>(),
            It.Is<int[]?>(ss => ss != null && ss.Length == 1 && ss[0] == 2),
            out pageInfos), Times.Once);
    }

    [Fact]
    public void ManualDuplex_CustomOrder_312_SingleSidedRemappedToSubsetIndices()
    {
        var mockWord = CreateMockWord(3);
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));

        var pageInfos = MakePageInfos(4);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);

        // Order [3,1,2] with singleSidedPages=[1]
        // pagesToPrint after ApplyPageOrder: [3,1,2]
        // Original page 1 at index 1 (0-based) => subset index 2
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false,
            pageOrder: new[] { 3, 1, 2 }, singleSidedPages: new[] { 1 });

        mockWord.Verify(w => w.ProcessMixedOrientation(
            It.IsAny<string>(),
            It.Is<int[]?>(ss => ss != null && ss.Length == 1 && ss[0] == 2),
            out pageInfos), Times.Once);
    }

    [Fact]
    public void ManualDuplex_Rotation_AppliedBeforeProcessMixedOrientation()
    {
        var mockWord = CreateMockWord(2);
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Returns(@"C:\fake\rotated.pdf");

        var pageInfos = MakePageInfos(2);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.Rotate180 }
        };

        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false, pageRotations: rotations);

        mockWord.Verify(
            w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()),
            Times.Once);
        // ProcessMixedOrientation received the rotated PDF path, proving rotation came first
        mockWord.Verify(w => w.ProcessMixedOrientation(
            @"C:\fake\rotated.pdf",
            It.IsAny<int[]?>(),
            out pageInfos), Times.Once);
    }

    [Fact]
    public void ManualDuplex_WaitingForFlip_IsTrue_InReturnedJobState()
    {
        var mockWord = CreateMockWord(4);
        var pageInfos = MakePageInfos(4);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var result = service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false);

        result.WaitingForFlip.Should().BeTrue();
    }

    [Fact]
    public void ManualDuplex_ManualPlan_IsSet_InReturnedJobState()
    {
        var mockWord = CreateMockWord(4);
        var pageInfos = MakePageInfos(4);
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var result = service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false);

        result.ManualPlan.Should().NotBeNull();
        result.ManualPlan!.ProcessedPdfPath.Should().Be(@"C:\fake\processed.pdf");
    }

    // ================================================================
    //  D. BOOKLET PURE LOGIC (no mocking needed)
    // ================================================================

    [Fact]
    public void Booklet_CalculateBookletOrder_8Pages_Returns_81276345()
    {
        var result = _sut.CalculateBookletOrder(8);
        result.Should().BeEquivalentTo(new[] { 8, 1, 2, 7, 6, 3, 4, 5 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Booklet_CalculateBookletOrder_4Pages_Returns_4123()
    {
        var result = _sut.CalculateBookletOrder(4);
        result.Should().BeEquivalentTo(new[] { 4, 1, 2, 3 }, opts => opts.WithStrictOrdering());
    }

    [Theory]
    [InlineData(5, 8)]
    [InlineData(4, 4)]
    [InlineData(1, 4)]
    [InlineData(9, 12)]
    public void Booklet_RoundUpToMultipleOf4_ReturnsExpected(int input, int expected)
    {
        _sut.RoundUpToMultipleOf4(input).Should().Be(expected);
    }

    // ================================================================
    //  E. APPLY PAGE ORDER (static, no mock needed)
    // ================================================================

    [Fact]
    public void ApplyPageOrder_EmptyPageOrder_ReturnsSelectedPagesUnchanged()
    {
        var selected = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.ApplyPageOrder(selected, Array.Empty<int>());

        result.Should().BeEquivalentTo(selected, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_NullPageOrder_ReturnsSelectedPagesUnchanged()
    {
        var selected = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.ApplyPageOrder(selected, null);

        result.Should().BeEquivalentTo(selected, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_PagesNotInSelected_FilteredOut_RemainingAppended()
    {
        var selected = new[] { 1, 2, 3 };
        var order = new[] { 5, 3, 1 }; // 5 not in selected

        var result = PrintAlgorithmService.ApplyPageOrder(selected, order);

        // 5 filtered, [3,1] kept in order, 2 appended
        result.Should().BeEquivalentTo(new[] { 3, 1, 2 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_AllPagesInDifferentOrder_ReorderedCorrectly()
    {
        var selected = new[] { 1, 2, 3, 4 };
        var order = new[] { 4, 2, 3, 1 };

        var result = PrintAlgorithmService.ApplyPageOrder(selected, order);

        result.Should().BeEquivalentTo(new[] { 4, 2, 3, 1 }, opts => opts.WithStrictOrdering());
    }

    // ================================================================
    //  F. REMAP ROTATIONS (static, no mock needed)
    // ================================================================

    [Fact]
    public void RemapRotations_EmptyRotationMap_ReturnsEmpty()
    {
        var empty = new Dictionary<int, RotationDirection>();
        var selected = new[] { 1, 2, 3 };

        var result = PrintAlgorithmService.RemapRotations(empty, selected);

        result.Should().BeEmpty();
    }

    [Fact]
    public void RemapRotations_Page3CW90_Selected234_MapsToSubsetIndex2()
    {
        var rotationMap = new Dictionary<int, RotationDirection> { { 3, RotationDirection.CW90 } };
        var selected = new[] { 2, 3, 4 };

        var result = PrintAlgorithmService.RemapRotations(rotationMap, selected);

        // Page 3 is at index 1 (0-based) in [2,3,4] => 1-based subset index 2
        result.Should().HaveCount(1);
        result.Should().ContainKey(2);
        result[2].Should().Be(RotationDirection.CW90);
    }

    [Fact]
    public void RemapRotations_PageNotInSelected_DroppedFromRemappedMap()
    {
        var rotationMap = new Dictionary<int, RotationDirection> { { 5, RotationDirection.CW90 } };
        var selected = new[] { 1, 2, 3 };

        var result = PrintAlgorithmService.RemapRotations(rotationMap, selected);

        result.Should().BeEmpty();
    }

    // ================================================================
    //  G. BUILD ROTATION MAP (static, no mock needed)
    // ================================================================

    [Fact]
    public void BuildRotationMap_NullInput_ReturnsEmptyDict()
    {
        var result = PrintAlgorithmService.BuildRotationMap(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildRotationMap_ListWithNoneRotation_NotIncludedInDict()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.None }
        };

        var result = PrintAlgorithmService.BuildRotationMap(rotations);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildRotationMap_CW90AndCCW90_BothInDictWithCorrectKeys()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 },
            new PageRotation { PageNumber = 3, Rotation = RotationDirection.CCW90 }
        };

        var result = PrintAlgorithmService.BuildRotationMap(rotations);

        result.Should().HaveCount(2);
        result[1].Should().Be(RotationDirection.CW90);
        result[3].Should().Be(RotationDirection.CCW90);
    }

    // ================================================================
    //  H. PARSE PAGE RANGE EDGE CASES (instance method, needs service)
    // ================================================================

    [Fact]
    public void ParsePageRange_ComplexRange_1To3_5_7To8_ReturnsCorrectSet()
    {
        var result = _sut.ParsePageRange("1-3,5,7-8", 10);

        result.Should().BeEquivalentTo(new[] { 1, 2, 3, 5, 7, 8 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ParsePageRange_ReversedRange_5To3_Returns345()
    {
        var result = _sut.ParsePageRange("5-3", 10);

        result.Should().BeEquivalentTo(new[] { 3, 4, 5 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ParsePageRange_OutOfRange_99On5PageDoc_ReturnsEmpty()
    {
        var result = _sut.ParsePageRange("99", 5);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePageRange_Zero_ReturnsEmpty()
    {
        var result = _sut.ParsePageRange("0", 5);

        result.Should().BeEmpty();
    }
}
