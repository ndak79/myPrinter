using FluentAssertions;
using PrinterApp.Models;
using PrinterApp.Services;

namespace backend.Tests;

/// <summary>
/// Tests that Cleanup() expires abandoned manual-duplex jobs by age (not only by missing file).
/// </summary>
public class FileSessionServiceCleanupTests : IDisposable
{
    private readonly FileSessionService _sut;
    private readonly string _stateFile;
    private readonly List<string> _tempFiles = new();

    public FileSessionServiceCleanupTests()
    {
        _stateFile = Path.Combine(Path.GetTempPath(), $"myprinter-session-{Guid.NewGuid():N}.json");
        _tempFiles.Add(_stateFile);
        _sut = new FileSessionService(_stateFile);
    }

    public void Dispose()
    {
        _sut.Dispose();
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string CreateTempFile()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        return path;
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PrintJobState with a back-dated CreatedAt so it looks expired.
    /// The temp PDF path points to a real file (so the "missing file" branch
    /// does NOT trigger — we want the age-based branch to fire).
    /// </summary>
    private PrintJobState MakeExpiredJob(string tempPdfPath, TimeSpan age)
    {
        // We can't set CreatedAt directly (it's `init`), so construct via object init.
        return new PrintJobState
        {
            IsManualDuplex   = true,
            WaitingForFlip   = true,
            TempPdfPath      = tempPdfPath,
            PrinterName      = "TestPrinter",
            CreatedAt        = DateTime.UtcNow - age
        };
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cleanup_ExpiredJob_WithExistingTempFile_IsRemovedAndFileDeleted()
    {
        // Arrange: a real temp file so the "missing file" branch won't fire.
        var tempPdf = CreateTempFile();
        var job = MakeExpiredJob(tempPdf, TimeSpan.FromHours(3)); // older than 2-hour TTL
        _sut.AddJob(job.JobId, job);

        // Act
        _sut.Cleanup();

        // Assert: job is gone from the session
        _sut.GetJob(job.JobId).Should().BeNull(
            "an expired job must be removed from the session by Cleanup()");

        // Assert: the temp PDF file was deleted via RemoveJob
        File.Exists(tempPdf).Should().BeFalse(
            "RemoveJob must delete TempPdfPath when expiring an abandoned job");
    }

    [Fact]
    public void Cleanup_ExpiredJob_IntermediateFilesAreDeleted()
    {
        // Arrange: job with intermediate files tracked (BUG-8-2 scenario)
        var tempPdf   = CreateTempFile();
        var intermed1 = CreateTempFile();
        var intermed2 = CreateTempFile();

        var job = MakeExpiredJob(tempPdf, TimeSpan.FromHours(3));
        job.IntermediateFiles.Add(intermed1);
        job.IntermediateFiles.Add(intermed2);
        _sut.AddJob(job.JobId, job);

        // Act
        _sut.Cleanup();

        // Assert
        _sut.GetJob(job.JobId).Should().BeNull();
        File.Exists(tempPdf).Should().BeFalse("main temp PDF must be deleted");
        File.Exists(intermed1).Should().BeFalse("intermediate file 1 must be deleted");
        File.Exists(intermed2).Should().BeFalse("intermediate file 2 must be deleted");
    }

    [Fact]
    public void Cleanup_FreshJob_WithExistingTempFile_IsNotRemoved()
    {
        // Arrange: a job created 10 minutes ago — well within the 2-hour TTL
        var tempPdf = CreateTempFile();
        var job = MakeExpiredJob(tempPdf, TimeSpan.FromMinutes(10));
        _sut.AddJob(job.JobId, job);

        // Act
        _sut.Cleanup();

        // Assert: fresh job stays
        _sut.GetJob(job.JobId).Should().NotBeNull(
            "a job within TTL must NOT be removed by Cleanup()");
        File.Exists(tempPdf).Should().BeTrue("the temp file of a fresh job must not be deleted");
    }

    [Fact]
    public void Cleanup_OrphanedJob_MissingTempFile_IsRemovedRegardlessOfAge()
    {
        // Arrange: a fresh job whose temp PDF has already been deleted externally
        // (original "missing file" cleanup path must still work).
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            TempPdfPath    = @"C:\does\not\exist\fake.pdf",
            PrinterName    = "TestPrinter"
            // CreatedAt defaults to UtcNow — well within TTL
        };
        _sut.AddJob(job.JobId, job);

        // Act
        _sut.Cleanup();

        // Assert: orphaned job removed even though it's young
        _sut.GetJob(job.JobId).Should().BeNull(
            "a job whose TempPdfPath is missing must be cleaned up regardless of age");
    }

    [Fact]
    public void AddJob_PersistsRecoveryContext_ForServiceRestart()
    {
        var tempPdf = CreateTempFile();
        var front = new ManualDuplexPageInfo
        {
            ProcessedIndex = 1,
            OriginalPageNumber = 1,
            IsBlank = false,
            IsLandscape = false
        };
        var back = new ManualDuplexPageInfo
        {
            ProcessedIndex = 2,
            OriginalPageNumber = 2,
            IsBlank = false,
            IsLandscape = false
        };
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = false,
            BackPassSent = true,
            TempPdfPath = tempPdf,
            PrinterName = "TestPrinter",
            ManualPlan = new ManualDuplexPlan
            {
                ProcessedPdfPath = tempPdf,
                ProcessedPages = new[] { front, back },
                Sheets = new[] { new ManualDuplexSheet { SheetIndex = 1, Front = front, Back = back } },
                Phase1Pages = new[] { 1 },
                Phase2Pages = new[] { 2 }
            }
        };

        _sut.AddJob(job.JobId, job);

        using var restarted = new FileSessionService(_stateFile);
        var restored = restarted.GetLatestRecoverableJob();

        restored.Should().NotBeNull();
        restored!.JobId.Should().Be(job.JobId);
        restored.BackPassSent.Should().BeTrue();
        restored.ManualPlan.Should().NotBeNull();
        restored.ManualPlan!.Sheets.Should().HaveCount(1);
        restored.ManualPlan.Phase2Pages.Should().Equal(2);
    }

    [Fact]
    public void RemoveJob_ClearsPersistedRecoveryContext()
    {
        var tempPdf = CreateTempFile();
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            TempPdfPath = tempPdf,
            PrinterName = "TestPrinter"
        };

        _sut.AddJob(job.JobId, job);
        _sut.RemoveJob(job.JobId);

        using var restarted = new FileSessionService(_stateFile);

        restarted.GetJob(job.JobId).Should().BeNull();
        restarted.GetLatestRecoverableJob().Should().BeNull();
    }

    [Fact]
    public void ClaimJob_DoesNotClearPersistedRecoveryContext()
    {
        var tempPdf = CreateTempFile();
        var job = new PrintJobState
        {
            IsManualDuplex = true,
            WaitingForFlip = true,
            TempPdfPath = tempPdf,
            PrinterName = "TestPrinter"
        };

        _sut.AddJob(job.JobId, job);

        _sut.ClaimJob(job.JobId).Should().NotBeNull();

        using var restarted = new FileSessionService(_stateFile);
        var restored = restarted.GetLatestRecoverableJob();

        restored.Should().NotBeNull(
            "claiming a job only protects the in-memory continue request; persisted recovery context must survive until success, cancel, or cleanup");
        restored!.JobId.Should().Be(job.JobId);
    }
}
