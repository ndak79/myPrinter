using FluentAssertions;
using Moq;
using PrinterApp.Models;
using PrinterApp.Services;

namespace backend.Tests;

public class ExecutePrintJobTests
{
    private readonly Mock<IWordInteropService> _mockWord;
    private readonly PrintAlgorithmService _sut;

    public ExecutePrintJobTests()
    {
        _mockWord = new Mock<IWordInteropService>();
        _sut = new PrintAlgorithmService(_mockWord.Object);
    }

    // ==========================================
    //  AUTO DUPLEX
    // ==========================================

    [Fact]
    public void ExecutePrintJob_AutoDuplex_CallsPrintPdfOnce()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = false,
            WaitingForFlip = false,
            TempPdfPath = @"C:\fake\doc.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: true);

        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\doc.pdf", "TestPrinter", null), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_AutoDuplex_SecondPhase_StillPrintsNormally()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = false,
            WaitingForFlip = false,
            TempPdfPath = @"C:\fake\doc.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\doc.pdf", "TestPrinter", null), Times.Once);
    }

    // ==========================================
    //  MANUAL DUPLEX - PHASE 1
    // ==========================================

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase1_PrintsOddPages()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: true);

        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\processed.pdf", "TestPrinter", "1,3"), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase1_DoesNotCallCreateSmartDuplexPdf()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: true);

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>()), Times.Never);
    }

    // ==========================================
    //  MANUAL DUPLEX - PHASE 2
    // ==========================================

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_CallsCreateSmartDuplexPdf()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>()))
                 .Returns(@"C:\fake\rotated.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(@"C:\fake\processed.pdf", new[] { 4, 2 }), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_PrintsRotatedPdf()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>()))
                 .Returns(@"C:\fake\rotated.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\rotated.pdf", "TestPrinter", null), Times.Once);
    }

    // ==========================================
    //  EDGE CASES
    // ==========================================

    [Fact]
    public void ExecutePrintJob_Phase1_EmptyOddPages_DoesNotCallPrintPdf()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = Array.Empty<int>(),
            RemainingPages = new[] { 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: true);

        _mockWord.Verify(w => w.PrintPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ExecutePrintJob_Phase2_EmptyRemainingPages_DoesNotCallPrintPdf()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1 },
            RemainingPages = Array.Empty<int>(),
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.PrintPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ExecutePrintJob_NullJobState_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExecutePrintJob(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
