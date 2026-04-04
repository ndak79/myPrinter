using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;
using Xunit;

namespace backend.Tests;

public class PageRotationIntegrationTests
{
    [Fact]
    public void CreateSimplexJob_WithPageRotations_AppliesRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.CW90 }
        };

        var job = service.CreateSimplexJob("test.pdf", "Printer1", pageRotations: rotations);

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.Is<Dictionary<int, RotationDirection>>(d => d.ContainsKey(2) && d[2] == RotationDirection.CW90)), Times.Once);
        job.TempPdfPath.Should().Be(@"C:\fake\rotated.pdf");
    }

    [Fact]
    public void CreateSimplexJob_WithPageRotationsAndPageRange_RemapsRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(5, false));
        mockWord.Setup(w => w.CreatePdfSubset(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int[]>()));
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 4, Rotation = RotationDirection.Rotate180 }
        };

        // Page range "3-5" -> selected pages: 3, 4, 5
        // Page 4 is at index 1 -> 1-based index 2
        var job = service.CreateSimplexJob("test.pdf", "Printer1", pageRange: "3-5", pageRotations: rotations);

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.Is<Dictionary<int, RotationDirection>>(d => d.ContainsKey(2) && d[2] == RotationDirection.Rotate180)), Times.Once);
    }

    [Fact]
    public void CreateNormalDuplexJob_AutoDuplex_WithRotations_AppliesRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(2, false));
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Returns(@"C:\fake\rotated.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CCW90 }
        };

        var job = service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: true, pageRotations: rotations);

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.Is<Dictionary<int, RotationDirection>>(d => d.ContainsKey(1) && d[1] == RotationDirection.CCW90)), Times.Once);
        job.TempPdfPath.Should().Be(@"C:\fake\rotated.pdf");
    }

    [Fact]
    public void CreateNormalDuplexJob_ManualDuplex_WithRotations_AppliesRotationsBeforeMixedOrientation()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(2, false));
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
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
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.FlipVertical }
        };

        var job = service.CreateNormalDuplexJob("test.pdf", "Printer1", isDuplexPrinter: false, pageRotations: rotations);

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.Is<Dictionary<int, RotationDirection>>(d => d.ContainsKey(2) && d[2] == RotationDirection.FlipVertical)), Times.Once);
        mockWord.Verify(w => w.ProcessMixedOrientation(@"C:\fake\rotated.pdf", It.IsAny<int[]?>(), out pageInfos), Times.Once);
    }

    [Fact]
    public void CreateBookletJob_WithRotations_AppliesRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(4, false));
        mockWord.Setup(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()))
                .Returns(@"C:\fake\rotated.pdf");
        mockWord.Setup(w => w.GetPageCount(It.IsAny<string>())).Returns(4);
        
        var pageInfos = new List<ManualDuplexPageInfo>();
        mockWord.Setup(w => w.ProcessMixedOrientation(It.IsAny<string>(), It.IsAny<int[]?>(), out pageInfos))
                .Returns(@"C:\fake\processed.pdf");

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 3, Rotation = RotationDirection.CW90 }
        };

        // Note: CreateBookletJob calls CreateNormalDuplexJob internally, which might call GetPdfInfo again.
        // We just want to verify ApplyPageRotations is called.
        try
        {
            service.CreateBookletJob("test.pdf", "Printer1", isDuplexPrinter: true, pageRotations: rotations);
        }
        catch
        {
            // Ignore exceptions from further processing (like CreateBookletPdf which uses PdfSharp)
            // We just want to verify the rotation was applied before that.
        }

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.Is<Dictionary<int, RotationDirection>>(d => d.ContainsKey(3) && d[3] == RotationDirection.CW90)), Times.Once);
    }

    [Fact]
    public void CreateSimplexJob_EmptyRotations_DoesNotApplyRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));

        var service = new PrintAlgorithmService(mockWord.Object);
        var job = service.CreateSimplexJob("test.pdf", "Printer1", pageRotations: new List<PageRotation>());

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()), Times.Never);
    }

    [Fact]
    public void CreateSimplexJob_OnlyNoneRotations_DoesNotApplyRotations()
    {
        var mockWord = new Mock<IWordInteropService>();
        mockWord.Setup(w => w.GetPdfInfo(It.IsAny<string>())).Returns(new PdfInfo(3, false));

        var service = new PrintAlgorithmService(mockWord.Object);
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.None }
        };
        var job = service.CreateSimplexJob("test.pdf", "Printer1", pageRotations: rotations);

        mockWord.Verify(w => w.ApplyPageRotations(It.IsAny<string>(), It.IsAny<Dictionary<int, RotationDirection>>()), Times.Never);
    }
}
