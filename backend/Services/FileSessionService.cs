using System.Collections.Concurrent;
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

    public FileSessionService()
    {
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

    public void AddJob(string jobId, PrintJobState state) => _jobs[jobId] = state;

    public PrintJobState? GetJob(string jobId)
        => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public void RemoveJob(string jobId)
    {
        if (_jobs.TryRemove(jobId, out var job))
            DeleteFileSafe(job.TempPdfPath);
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;

        foreach (var (id, session) in _files)
        {
            if (session.CreatedAt < cutoff && _files.TryRemove(id, out _))
            {
                DeleteFileSafe(session.FilePath);
                Console.WriteLine($"[FileSessionService] Cleaned up expired file session: {id}");
            }
        }

        foreach (var (jobId, job) in _jobs)
        {
            if (!File.Exists(job.TempPdfPath))
            {
                _jobs.TryRemove(jobId, out _);
                Console.WriteLine($"[FileSessionService] Cleaned up orphaned job: {jobId}");
            }
        }
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

    public void Dispose() => _cleanupTimer.Dispose();
}
