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

    [Theory]
    [InlineData(1, 10, 5_100)]
    [InlineData(5, 20, 12_750)]
    [InlineData(1, 10_000, 4_000)]
    [InlineData(1_000, 1, 120_000)]
    public void CalculateManualDuplexEjectDelayMs_ReducesEstimatedWaitByFifteenPercent(int sheets, int ppm, int expectedDelayMs)
    {
        PrintAlgorithmService.CalculateManualDuplexEjectDelayMs(sheets, ppm)
            .Should().Be(expectedDelayMs);
    }

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

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()), Times.Never);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase1_MultipleCopies_PrintsOneContinuousFrontPass()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            Copies = 3,
            PpmEstimate = 10_000,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        string? subsetPath = null;
        _mockWord
            .Setup(w => w.CreatePdfSubset(@"C:\fake\processed.pdf", It.IsAny<string>(), It.IsAny<int[]>()))
            .Callback<string, string, int[]>((_, targetPath, _) => subsetPath = targetPath);

        _sut.ExecutePrintJob(job, firstPhase: true);

        subsetPath.Should().NotBeNullOrWhiteSpace();
        _mockWord.Verify(w => w.CreatePdfSubset(
            @"C:\fake\processed.pdf",
            It.IsAny<string>(),
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 1, 3, 1, 3, 1, 3 }))), Times.Once);
        _mockWord.Verify(w => w.PrintPdf(subsetPath!, "TestPrinter", null, null), Times.Once);
        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\processed.pdf", "TestPrinter", "1,3", null), Times.Never);
    }

    // ==========================================
    //  MANUAL DUPLEX - PHASE 2
    // ==========================================

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_CallsCreateSmartDuplexPdf()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
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

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(@"C:\fake\processed.pdf", new[] { 4, 2 }, null), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_PassesShortEdgeFlipDirection()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
                 .Returns(@"C:\fake\rotated.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            Instruction = new FlipInstruction { Direction = FlipDirection.ShortEdge }
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(
            @"C:\fake\processed.pdf",
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 4, 2 })),
            FlipDirection.ShortEdge), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_DoesNotForceLongEdgeOverride()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
                 .Returns(@"C:\fake\rotated.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            Instruction = new FlipInstruction { Direction = FlipDirection.LongEdge }
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(
            @"C:\fake\processed.pdf",
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 4, 2 })),
            null), Times.Once);
    }

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_PrintsRotatedPdf()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
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

    [Fact]
    public void ExecutePrintJob_ManualDuplex_Phase2_MultipleCopies_PrintsOneContinuousBackPass()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
                 .Returns(@"C:\fake\rotated.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            Copies = 2,
            OddPages = new[] { 1, 3 },
            RemainingPages = new[] { 4, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        _sut.ExecutePrintJob(job, firstPhase: false);

        _mockWord.Verify(w => w.CreateSmartDuplexPdf(
            @"C:\fake\processed.pdf",
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 4, 2, 4, 2 })),
            null), Times.Once);
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

        var act = () => _sut.ExecutePrintJob(job, firstPhase: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No odd pages*");
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

        var act = () => _sut.ExecutePrintJob(job, firstPhase: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Phase 2 has no pages*");
        _mockWord.Verify(w => w.PrintPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ExecutePrintJob_NullJobState_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExecutePrintJob(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReprintManualDuplexFrontSheets_PrintsFrontPagesForSelectedSheets()
    {
        var pages = Enumerable.Range(1, 6)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = false
            })
            .ToList();

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages)
        };

        var printedCount = _sut.ReprintManualDuplexFrontSheets(job, new[] { 2, 3 });

        printedCount.Should().Be(2);
        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\processed.pdf", "TestPrinter", "3,5"), Times.Once);
    }

    [Fact]
    public void ReprintManualDuplexFrontSheets_RequiresJobWaitingForFlip()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", new List<ManualDuplexPageInfo>
            {
                new() { ProcessedIndex = 1, OriginalPageNumber = 1 },
                new() { ProcessedIndex = 2, OriginalPageNumber = 2 }
            })
        };

        var act = () => _sut.ReprintManualDuplexFrontSheets(job, new[] { 1 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*waiting for flip*");
        _mockWord.Verify(w => w.PrintPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ReprintManualDuplexFrontSheets_AllowsDuplicateSheetsForMultiCopyJobs()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            Copies = 2,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", new List<ManualDuplexPageInfo>
            {
                new() { ProcessedIndex = 1, OriginalPageNumber = 1 },
                new() { ProcessedIndex = 2, OriginalPageNumber = 2 }
            })
        };

        string? subsetPath = null;
        _mockWord
            .Setup(w => w.CreatePdfSubset(@"C:\fake\processed.pdf", It.IsAny<string>(), It.IsAny<int[]>()))
            .Callback<string, string, int[]>((_, targetPath, _) => subsetPath = targetPath);

        var printedCount = _sut.ReprintManualDuplexFrontSheets(job, new[] { 1, 1 });

        printedCount.Should().Be(2);
        subsetPath.Should().NotBeNullOrWhiteSpace();
        _mockWord.Verify(w => w.CreatePdfSubset(
            @"C:\fake\processed.pdf",
            It.IsAny<string>(),
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 1, 1 }))), Times.Once);
        _mockWord.Verify(w => w.PrintPdf(subsetPath!, "TestPrinter", null, null), Times.Once);
    }

    [Fact]
    public void StartManualDuplexBackSheetRecovery_PrintsFrontPagesAndStoresBackPagesInPhase2Order()
    {
        var pages = Enumerable.Range(1, 6)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = false
            })
            .ToList();

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages)
        };

        var printedCount = _sut.StartManualDuplexBackSheetRecovery(job, new[] { 1, 3 });

        printedCount.Should().Be(2);
        job.WaitingForRecoveryFlip.Should().BeTrue();
        job.RecoverySheetIndices.Should().BeEquivalentTo(new[] { 1, 3 }, opts => opts.WithStrictOrdering());
        job.RecoveryBackPages.Should().BeEquivalentTo(new[] { 6, 2 }, opts => opts.WithStrictOrdering());
        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\processed.pdf", "TestPrinter", "1,5"), Times.Once);
        _mockWord.Verify(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()), Times.Never);
    }

    [Fact]
    public void ContinueManualDuplexBackSheetRecovery_PrintsStoredBackPagesAndClearsRecoveryState()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
                 .Returns(@"C:\fake\back-recovery.pdf");

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            WaitingForRecoveryFlip = true,
            RecoverySheetIndices = new[] { 1, 3 },
            RecoveryBackPages = new[] { 6, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        var printedCount = _sut.ContinueManualDuplexBackSheetRecovery(job);

        printedCount.Should().Be(2);
        job.WaitingForRecoveryFlip.Should().BeFalse();
        job.RecoverySheetIndices.Should().BeEmpty();
        job.RecoveryBackPages.Should().BeEmpty();
        _mockWord.Verify(w => w.CreateSmartDuplexPdf(@"C:\fake\processed.pdf", new[] { 6, 2 }, null), Times.Once);
        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\back-recovery.pdf", "TestPrinter", null), Times.Once);
    }

    [Fact]
    public void ContinueManualDuplexBackSheetRecovery_WhenBackPrintFails_KeepsRecoveryStateForRetry()
    {
        _mockWord.Setup(w => w.CreateSmartDuplexPdf(It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<FlipDirection?>()))
                 .Returns(@"C:\fake\back-recovery.pdf");
        _mockWord.Setup(w => w.PrintPdf(@"C:\fake\back-recovery.pdf", "TestPrinter", null, null))
                 .Throws(new InvalidOperationException("printer failed"));

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            WaitingForRecoveryFlip = true,
            RecoverySheetIndices = new[] { 1, 3 },
            RecoveryBackPages = new[] { 6, 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter"
        };

        var act = () => _sut.ContinueManualDuplexBackSheetRecovery(job);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("printer failed");
        job.WaitingForRecoveryFlip.Should().BeTrue();
        job.RecoverySheetIndices.Should().BeEquivalentTo(new[] { 1, 3 }, opts => opts.WithStrictOrdering());
        job.RecoveryBackPages.Should().BeEquivalentTo(new[] { 6, 2 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void StartManualDuplexBackSheetRecovery_WhenWaitingForRecoveryFlip_ReprintsFrontsForRetry()
    {
        var pages = Enumerable.Range(1, 6)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = false
            })
            .ToList();

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            WaitingForRecoveryFlip = true,
            RecoverySheetIndices = new[] { 1 },
            RecoveryBackPages = new[] { 2 },
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages)
        };

        var printedCount = _sut.StartManualDuplexBackSheetRecovery(job, new[] { 1, 3 });

        printedCount.Should().Be(2);
        job.WaitingForRecoveryFlip.Should().BeTrue();
        job.RecoverySheetIndices.Should().BeEquivalentTo(new[] { 1, 3 }, opts => opts.WithStrictOrdering());
        job.RecoveryBackPages.Should().BeEquivalentTo(new[] { 6, 2 }, opts => opts.WithStrictOrdering());
        _mockWord.Verify(w => w.PrintPdf(@"C:\fake\processed.pdf", "TestPrinter", "1,5"), Times.Once);
    }

    [Fact]
    public void StartManualDuplexBackSheetRecovery_RequiresBackPassSent()
    {
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = false,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", new List<ManualDuplexPageInfo>
            {
                new() { ProcessedIndex = 1, OriginalPageNumber = 1 },
                new() { ProcessedIndex = 2, OriginalPageNumber = 2 }
            })
        };

        var act = () => _sut.StartManualDuplexBackSheetRecovery(job, new[] { 1 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*back pass*");
        _mockWord.Verify(w => w.PrintPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void StartManualDuplexBackSheetRecovery_AllowsDuplicateSheetsForMultiCopyJobs()
    {
        var pages = Enumerable.Range(1, 6)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = false
            })
            .ToList();

        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            Copies = 2,
            TempPdfPath = @"C:\fake\processed.pdf",
            PrinterName = "TestPrinter",
            ManualPlan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages)
        };

        string? subsetPath = null;
        _mockWord
            .Setup(w => w.CreatePdfSubset(@"C:\fake\processed.pdf", It.IsAny<string>(), It.IsAny<int[]>()))
            .Callback<string, string, int[]>((_, targetPath, _) => subsetPath = targetPath);

        var printedCount = _sut.StartManualDuplexBackSheetRecovery(job, new[] { 1, 3, 3 });

        printedCount.Should().Be(3);
        job.WaitingForRecoveryFlip.Should().BeTrue();
        job.RecoverySheetIndices.Should().BeEquivalentTo(new[] { 1, 3, 3 }, opts => opts.WithStrictOrdering());
        job.RecoveryBackPages.Should().BeEquivalentTo(new[] { 6, 6, 2 }, opts => opts.WithStrictOrdering());
        subsetPath.Should().NotBeNullOrWhiteSpace();
        _mockWord.Verify(w => w.CreatePdfSubset(
            @"C:\fake\processed.pdf",
            It.IsAny<string>(),
            It.Is<int[]>(pages => pages.SequenceEqual(new[] { 1, 5, 5 }))), Times.Once);
        _mockWord.Verify(w => w.PrintPdf(subsetPath!, "TestPrinter", null, null), Times.Once);
    }
}
