using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;
using Xunit;

namespace backend.Tests;

public class MixedScenarioTests
{
    [Fact]
    public void PageRange_And_PageOrder_CreatesSubsetWithCustomOrder()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(5, false));
        
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        
        // Range "1-3" -> 1, 2, 3
        // Order [3, 1, 2]
        service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "1-3", pageOrder: new[] { 3, 1, 2 });

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 3, 1, 2 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void PageRange_PageOrder_And_Rotation_Combined()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(5, false));
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        
        Dictionary<int, RotationDirection>? capturedRotations = null;
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Callback<string, Dictionary<int, RotationDirection>>((s, map) => capturedRotations = map)
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        
        // Range "2-4" -> 2, 3, 4
        // Order [4, 2, 3]
        // Rotation on original page 2 -> CW90
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.CW90 }
        };

        service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "2-4", pageOrder: new[] { 4, 2, 3 }, pageRotations: rotations);

        // Original page 2 is at index 1 in the subset [4, 2, 3] -> 1-based index 2
        capturedRotations.Should().NotBeNull();
        capturedRotations.Should().ContainKey(2);
        capturedRotations![2].Should().Be(RotationDirection.CW90);
    }

    [Fact]
    public void PageOrder_And_SingleSidedPages_ManualDuplex_RemapsSingleSidedCorrectly()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(4, false));
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        
        var pageInfos = new List<ManualDuplexPageInfo>
        {
            new ManualDuplexPageInfo { ProcessedIndex = 1, OriginalPageNumber = 1, IsBlank = false, IsLandscape = false },
            new ManualDuplexPageInfo { ProcessedIndex = 2, OriginalPageNumber = 2, IsBlank = false, IsLandscape = false },
            new ManualDuplexPageInfo { ProcessedIndex = 3, OriginalPageNumber = 3, IsBlank = false, IsLandscape = false },
            new ManualDuplexPageInfo { ProcessedIndex = 4, OriginalPageNumber = 4, IsBlank = false, IsLandscape = false }
        };
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        
        // All pages: 1, 2, 3, 4
        // Order: [4, 3, 2, 1]
        // Single sided: [3] (original page 3)
        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false, pageOrder: new[] { 4, 3, 2, 1 }, singleSidedPages: new[] { 3 });

        // Original page 3 is at index 1 in [4, 3, 2, 1] -> 1-based index 2
        mockWord.Verify(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.Is<int[]?>(ss => ss != null && ss.Length == 1 && ss[0] == 2), out pageInfos), Times.Once);
    }

    [Fact]
    public void AllPagesSelected_CustomOrder_CreatesSubset()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));
        
        bool subsetCreated = false;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback(() => subsetCreated = true);

        var service = new PrintAlgorithmService(mockWord.Object);
        
        // All pages, but custom order [3, 2, 1]
        service.CreateSimplexJob("test.pdf", "Printer1", pageOrder: new[] { 3, 2, 1 });

        subsetCreated.Should().BeTrue();
    }

    [Fact]
    public void Simplex_WithPageOrderOnly_CreatesSubset()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));
        
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        
        service.CreateSimplexJob("test.pdf", "Printer1", pageOrder: new[] { 2, 1, 3 });

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 2, 1, 3 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ManualDuplex_WithPageRotations_NoPageRange_AppliesRotationsToFullDocument()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(2, false));
        
        Dictionary<int, RotationDirection>? capturedRotations = null;
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Callback<string, Dictionary<int, RotationDirection>>((s, map) => capturedRotations = map)
                .Returns(@"C:\fake\rotated.pdf");
                
        var pageInfos = new List<ManualDuplexPageInfo>
        {
            new ManualDuplexPageInfo { ProcessedIndex = 1, OriginalPageNumber = 1, IsBlank = false, IsLandscape = false },
            new ManualDuplexPageInfo { ProcessedIndex = 2, OriginalPageNumber = 2, IsBlank = false, IsLandscape = false }
        };
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.Rotate180 }
        };

        service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false, pageRotations: rotations);

        capturedRotations.Should().NotBeNull();
        capturedRotations.Should().ContainKey(2);
        capturedRotations![2].Should().Be(RotationDirection.Rotate180);
    }

    [Fact]
    public void PageOrder_WithPagesNotInDocument_GracefullyFiltered()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(2, false));
        
        int[]? capturedSubset = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => capturedSubset = pages);

        var service = new PrintAlgorithmService(mockWord.Object);
        
        // Document has pages 1, 2. Order requests 3, 2, 1. Page 3 should be ignored.
        service.CreateSimplexJob("test.pdf", "Printer1", pageOrder: new[] { 3, 2, 1 });

        capturedSubset.Should().NotBeNull();
        capturedSubset.Should().BeEquivalentTo(new[] { 2, 1 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Watermark_PageOrder_And_Rotations_Together()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.AddWatermarkToPdf(It.IsAny<string>(), It.IsAny<WatermarkOptions>()))
                .Returns(@"C:\fake\watermarked.pdf");
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));
        
        string? subsetSource = null;
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()))
                .Callback<string, string, int[]>((s, t, pages) => subsetSource = s);
                
        string? rotationSource = null;
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Callback<string, Dictionary<int, RotationDirection>>((s, map) => rotationSource = s)
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var watermark = new WatermarkOptions { Text = "TEST" };
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 }
        };

        service.CreateSimplexJob("test.pdf", "Printer1", watermark: watermark, pageOrder: new[] { 3, 2, 1 }, pageRotations: rotations);

        // Watermark should be applied first
        mockWord.Verify(w => w.AddWatermarkToPdf("test.pdf", watermark), Times.Once);
        
        // Subset should be created from watermarked PDF
        subsetSource.Should().Be(@"C:\fake\watermarked.pdf");
        
        // Rotations should be applied to the subset PDF
        rotationSource.Should().NotBeNull();
        rotationSource.Should().NotBe(@"C:\fake\watermarked.pdf"); // It should be the temp subset path
    }
}
