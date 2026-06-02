using System.Collections.Concurrent;
using System.Text.Json;
using PrinterApp.Models;

namespace PrinterApp.Services;

/// <summary>
/// Quản lý tập trung uploaded files và print jobs.
/// Thread-safe bằng ConcurrentDictionary.
/// Tự động dọn file temp + session cũ hơn 2 giờ mỗi 30 phút.
/// </summary>
public sealed class FileSessionService : IDisposable
{
    private static readonly TimeSpan SessionTtl      = TimeSpan.FromHours(2);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, FileSession>   _files = new();
    private readonly ConcurrentDictionary<string, PrintJobState> _jobs  = new();
    private readonly Timer _cleanupTimer;
    private readonly string _stateFilePath;
    private readonly object _stateLock = new();

    public FileSessionService(string? stateFilePath = null)
    {
        _stateFilePath = stateFilePath ?? GetDefaultStateFilePath();
        LoadPersistedJobs();
        _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupInterval, CleanupInterval);
    }

    // ─── Files ───────────────────────────────────────────────────────────────

    public void AddFile(string fileId, string filePath)
        => _files[fileId] = new FileSession { FileId = fileId, FilePath = filePath };

    public bool UpdateFilePath(string fileId, string newPath)
    {
        if (!_files.TryGetValue(fileId, out var session)) return false;
        session.FilePath = newPath;
        return true;
    }

    public string? GetFilePath(string fileId)
        => _files.TryGetValue(fileId, out var s) ? s.FilePath : null;

    public void RemoveFile(string fileId) => _files.TryRemove(fileId, out _);

    // ─── Jobs ────────────────────────────────────────────────────────────────

    public void AddJob(string jobId, PrintJobState state)
    {
        _jobs[jobId] = state;
        PersistJobs();
    }

    public PrintJobState? GetJob(string jobId)
        => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public PrintJobState? GetLatestRecoverableJob()
    {
        return _jobs.Values
            .Where(IsRecoverable)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Atomically removes and returns a job, without deleting its temp file.
    /// Used by /api/print/continue to prevent double-execution of phase 2
    /// if two requests arrive simultaneously for the same jobId (TOCTOU fix).
    /// </summary>
    public PrintJobState? ClaimJob(string jobId)
    {
        if (!_jobs.TryRemove(jobId, out var job)) return null;
        return job;
    }

    public void RemoveJob(string jobId)
    {
        if (_jobs.TryRemove(jobId, out var job))
        {
            DeleteFileSafe(job.TempPdfPath);
            // BUG-8-2 fix: also delete all intermediate files tracked during job construction
            foreach (var f in job.IntermediateFiles)
                DeleteFileSafe(f);
            PersistJobs();
        }
    }

    /// <summary>
    /// BUG-8-2 fix: delete all intermediate files for a completed non-manual-duplex job.
    /// Called from BackendStartup after ExecutePrintJob succeeds (no job stored in session).
    /// </summary>
    public static void DeleteIntermediateFiles(PrintJobState jobState)
    {
        foreach (var f in jobState.IntermediateFiles)
            DeleteFileSafe(f);
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;
        var jobsChanged = false;

        foreach (var (id, session) in _files)
        {
            // BE-16-1 fix: capture the removed session from TryRemove rather than reading
            // session.FilePath after removal. UpdateFilePath() mutates FilePath in-place on
            // the shared FileSession object, so reading session.FilePath after TryRemove
            // could see a new path written by a concurrent UpdateFilePath call, causing us
            // to delete the freshly-converted PDF instead of the original upload.
            // Using `removed` here snapshots FilePath atomically at the moment of removal.
            if (session.CreatedAt < cutoff && _files.TryRemove(id, out var removed))
            {
                DeleteFileSafe(removed.FilePath);
                Console.WriteLine($"[FileSessionService] Cleaned up expired file session: {id}");
            }
        }

        foreach (var (jobId, job) in _jobs)
        {
            // Remove jobs whose temp file has already been deleted externally.
            if (!File.Exists(job.TempPdfPath))
            {
                _jobs.TryRemove(jobId, out _);
                Console.WriteLine($"[FileSessionService] Cleaned up orphaned job: {jobId}");
                jobsChanged = true;
                continue;
            }

            // Remove abandoned jobs that have exceeded the session TTL (e.g. a
            // manual-duplex job where the user never clicked Continue or Cancel).
            // Use RemoveJob so TempPdfPath and IntermediateFiles are both deleted.
            if (job.CreatedAt < cutoff)
            {
                RemoveJob(jobId);
                Console.WriteLine($"[FileSessionService] Cleaned up expired job: {jobId}");
            }
        }

        if (jobsChanged) PersistJobs();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    public static void DeleteFileSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Console.WriteLine($"[FileSessionService] Deleted temp file: {path}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSessionService] WARNING: Could not delete {path}: {ex.Message}");
        }
    }

    private static bool IsRecoverable(PrintJobState job)
    {
        if (string.IsNullOrWhiteSpace(job.JobId)) return false;
        if (string.IsNullOrWhiteSpace(job.TempPdfPath) || !File.Exists(job.TempPdfPath)) return false;
        return job.WaitingForFlip || job.BackPassSent || job.WaitingForRecoveryFlip;
    }

    private static string GetDefaultStateFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "myPrinter");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "recovery-contexts.json");
    }

    private void LoadPersistedJobs()
    {
        try
        {
            if (!File.Exists(_stateFilePath)) return;

            var json = File.ReadAllText(_stateFilePath);
            var jobs = JsonSerializer.Deserialize<List<PrintJobState>>(json);
            if (jobs == null) return;

            var cutoff = DateTime.UtcNow - SessionTtl;
            var changed = false;
            foreach (var job in jobs)
            {
                if (job.CreatedAt < cutoff || !IsRecoverable(job))
                {
                    DeleteFileSafe(job.TempPdfPath);
                    foreach (var f in job.IntermediateFiles)
                        DeleteFileSafe(f);
                    changed = true;
                    continue;
                }

                _jobs[job.JobId] = job;
            }

            if (changed) PersistJobs();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSessionService] WARNING: Could not load recovery state: {ex.Message}");
        }
    }

    private void PersistJobs()
    {
        try
        {
            lock (_stateLock)
            {
                var dir = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                var jobs = _jobs.Values.Where(IsRecoverable).OrderBy(j => j.CreatedAt).ToList();
                var json = JsonSerializer.Serialize(jobs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFilePath, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSessionService] WARNING: Could not persist recovery state: {ex.Message}");
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
