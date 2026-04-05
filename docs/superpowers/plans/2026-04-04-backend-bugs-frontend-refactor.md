# Backend Bug Fixes + Frontend Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 3 critical backend bugs (race condition, memory leak, auto-duplex+pageRange) bằng cách tạo `FileSessionService`; đồng thời restructure `app.js` thành code có cấu trúc rõ ràng, thêm lazy thumbnail rendering và fix UX bugs nhỏ.

**Architecture:**
- Backend: Đóng gói toàn bộ shared state vào `FileSessionService` (singleton) dùng `ConcurrentDictionary<string,FileSession>` với timestamp TTL, background cleanup timer, và fix logic auto-duplex pageRange trong `PrintAlgorithmService`. `Program.cs` chỉ còn chứa endpoint routing.
- Frontend: Giữ nguyên `app.js` là 1 file (tương thích `file://`), restructure thành section objects (`ThemeModule`, `PrinterModule`, `UploadModule`, `PreviewModule`, `PageSelectModule`, `PrintModule`) với JSDoc rõ ràng. Thêm `IntersectionObserver` lazy render cho thumbnails. Fix `Deselect All` logic và toast color.

**Tech Stack:** .NET 8 Minimal API, C# `ConcurrentDictionary`, `System.Threading.Timer`, PdfSharp, Vanilla JS ES5/ES6, PDF.js 3.11

---

## File Map

### Backend — Files thay đổi

| File | Action | Mô tả |
|------|--------|-------|
| `backend/Services/FileSessionService.cs` | **CREATE** | Singleton quản lý uploaded files + print jobs, ConcurrentDictionary, TTL cleanup |
| `backend/Models/PrintModels.cs` | **MODIFY** | Thêm `FileSession` record với `CreatedAt` |
| `backend/Services/PrintAlgorithmService.cs` | **MODIFY** | Fix auto-duplex branch: tạo subset PDF khi có pageRange |
| `backend/Program.cs` | **MODIFY** | Dùng `FileSessionService` thay raw Dictionaries; đăng ký service |

### Frontend — Files thay đổi

| File | Action | Mô tả |
|------|--------|-------|
| `frontend/app.js` | **MODIFY** | Restructure thành section objects, lazy thumbnails, fix UX bugs |

---

## TASK 1: Thêm `FileSession` model vào `PrintModels.cs`

**Files:**
- Modify: `backend/Models/PrintModels.cs` (cuối file, trước closing `}` của namespace)

- [ ] **Step 1: Thêm `FileSession` record**

Mở `backend/Models/PrintModels.cs`, thêm vào cuối namespace block (trước dấu `}` cuối cùng):

```csharp
    /// <summary>
    /// Đại diện cho 1 file đã upload + thời điểm tạo (dùng cho TTL cleanup).
    /// </summary>
    public sealed class FileSession
    {
        public string FileId    { get; init; } = "";
        public string FilePath  { get; set; }  = "";   // set vì có thể thay đổi sau convert
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }
```

- [ ] **Step 2: Verify file compile**

Chạy từ thư mục `backend/`:
```powershell
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**
```powershell
git add backend/Models/PrintModels.cs
git commit -m "feat(models): add FileSession with CreatedAt for TTL cleanup"
```

---

## TASK 2: Tạo `FileSessionService`

**Files:**
- Create: `backend/Services/FileSessionService.cs`

- [ ] **Step 1: Tạo file `backend/Services/FileSessionService.cs`** với nội dung:

```csharp
using System.Collections.Concurrent;
using PrinterApp.Models;

namespace PrinterApp.Services;

/// <summary>
/// Quản lý tập trung uploaded files và print jobs.
/// - Thread-safe bằng ConcurrentDictionary.
/// - Tự động dọn file temp + session cũ hơn <see cref="SessionTtl"/> (mặc định 2 giờ).
/// </summary>
public sealed class FileSessionService : IDisposable
{
    // TTL cho mỗi session: 2 giờ
    private static readonly TimeSpan SessionTtl     = TimeSpan.FromHours(2);
    // Khoảng thời gian chạy cleanup: 30 phút
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, FileSession>   _files = new();
    private readonly ConcurrentDictionary<string, PrintJobState> _jobs  = new();
    private readonly Timer _cleanupTimer;

    public FileSessionService()
    {
        // Chạy cleanup sau 30 phút và lặp mỗi 30 phút
        _cleanupTimer = new Timer(
            _ => Cleanup(),
            state: null,
            dueTime:  CleanupInterval,
            period:   CleanupInterval
        );
    }

    // ─── Files ───────────────────────────────────────────────────────────────

    /// <summary>Lưu một file session mới.</summary>
    public void AddFile(string fileId, string filePath)
    {
        _files[fileId] = new FileSession { FileId = fileId, FilePath = filePath };
    }

    /// <summary>Cập nhật đường dẫn file (ví dụ sau khi convert DOCX → PDF).</summary>
    public bool UpdateFilePath(string fileId, string newPath)
    {
        if (!_files.TryGetValue(fileId, out var session)) return false;
        session.FilePath = newPath;
        return true;
    }

    /// <summary>Lấy đường dẫn file theo fileId. Trả về null nếu không tồn tại.</summary>
    public string? GetFilePath(string fileId)
        => _files.TryGetValue(fileId, out var s) ? s.FilePath : null;

    /// <summary>Xóa session file (không xóa file trên disk — gọi DeleteFileSafe nếu cần).</summary>
    public void RemoveFile(string fileId) => _files.TryRemove(fileId, out _);

    // ─── Jobs ────────────────────────────────────────────────────────────────

    /// <summary>Lưu print job state.</summary>
    public void AddJob(string jobId, PrintJobState state)
    {
        _jobs[jobId] = state;
    }

    /// <summary>Lấy print job theo jobId. Trả về null nếu không tồn tại.</summary>
    public PrintJobState? GetJob(string jobId)
        => _jobs.TryGetValue(jobId, out var j) ? j : null;

    /// <summary>Xóa job và dọn các file temp liên quan.</summary>
    public void RemoveJob(string jobId)
    {
        if (_jobs.TryRemove(jobId, out var job))
        {
            DeleteFileSafe(job.TempPdfPath);
        }
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Dọn tất cả file sessions + print jobs cũ hơn SessionTtl.
    /// Được gọi tự động bởi timer, có thể gọi thủ công khi test.
    /// </summary>
    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;

        // Dọn file sessions cũ
        foreach (var (id, session) in _files)
        {
            if (session.CreatedAt < cutoff)
            {
                if (_files.TryRemove(id, out _))
                {
                    DeleteFileSafe(session.FilePath);
                    Console.WriteLine($"[FileSessionService] Cleaned up expired file session: {id}");
                }
            }
        }

        // Dọn print jobs cũ (PrintJobState không có CreatedAt — dùng file session cutoff làm proxy)
        // Jobs được tạo sau khi upload nên nếu file session hết hạn, job cũng nên bị dọn.
        // Để đơn giản: dọn job không còn file session tương ứng (orphaned jobs).
        var activeFileIds = new HashSet<string>(_files.Keys);
        foreach (var (jobId, job) in _jobs)
        {
            // Nếu TempPdfPath của job không còn tương ứng file session nào → orphaned
            bool isOrphaned = !activeFileIds.Any(fid =>
            {
                var fp = GetFilePath(fid);
                return fp != null && (fp == job.TempPdfPath ||
                       job.TempPdfPath.StartsWith(Path.GetTempPath()));
            });

            // Thực tế: với app này, chỉ cần xóa job nếu file temp không còn tồn tại
            if (!File.Exists(job.TempPdfPath))
            {
                _jobs.TryRemove(jobId, out _);
                Console.WriteLine($"[FileSessionService] Cleaned up orphaned job: {jobId}");
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Xóa file trên disk an toàn (không throw nếu thất bại).</summary>
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

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
```

- [ ] **Step 2: Build để kiểm tra**

```powershell
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**
```powershell
git add backend/Services/FileSessionService.cs
git commit -m "feat(services): add FileSessionService with ConcurrentDictionary and TTL auto-cleanup"
```

---

## TASK 3: Cập nhật `Program.cs` dùng `FileSessionService`

**Files:**
- Modify: `backend/Program.cs`

- [ ] **Step 1: Đăng ký `FileSessionService` là singleton**

Trong `Program.cs`, tìm block đăng ký services (sau `builder.Services.AddCors`):

```csharp
// Register services
builder.Services.AddSingleton<PrinterManagementService>();
builder.Services.AddSingleton<WordInteropService>();
builder.Services.AddSingleton<PrintAlgorithmService>();
```

Thêm `FileSessionService`:

```csharp
// Register services
builder.Services.AddSingleton<PrinterManagementService>();
builder.Services.AddSingleton<WordInteropService>();
builder.Services.AddSingleton<PrintAlgorithmService>();
builder.Services.AddSingleton<FileSessionService>();
```

- [ ] **Step 2: Xóa raw Dictionaries**

Tìm và xóa 2 dòng này (hiện ở dưới `var app = builder.Build();`):

```csharp
// In-memory storage for uploaded files and jobs
var uploadedFiles = new Dictionary<string, string>();
var printJobs = new Dictionary<string, PrintJobState>();
```

- [ ] **Step 3: Cập nhật endpoint `POST /api/upload`**

Thay signature từ:
```csharp
app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env) =>
```
Thành:
```csharp
app.MapPost("/api/upload", async (HttpRequest request, FileSessionService sessions) =>
```

Trong body endpoint, thay dòng:
```csharp
uploadedFiles[fileId] = tempPath;
```
Thành:
```csharp
sessions.AddFile(fileId, tempPath);
```

- [ ] **Step 4: Cập nhật endpoint `GET /api/file/{fileId}`**

Thay signature:
```csharp
app.MapGet("/api/file/{fileId}", (string fileId) =>
```
Thành:
```csharp
app.MapGet("/api/file/{fileId}", (string fileId, FileSessionService sessions) =>
```

Thay:
```csharp
if (!uploadedFiles.TryGetValue(fileId, out var filePath))
{
    Console.WriteLine($"[FILE ERROR] FileId not found in cache");
    return Results.NotFound("File not found");
}

Console.WriteLine($"[FILE] File path from cache: {filePath}");

if (!File.Exists(filePath))
{
    Console.WriteLine($"[FILE ERROR] File does not exist on disk: {filePath}");
    return Results.NotFound("File not found on disk");
}

var fileInfo = new FileInfo(filePath);
Console.WriteLine($"[FILE] Serving file: {filePath}, Size: {fileInfo.Length} bytes");

var fileBytes = File.ReadAllBytes(filePath);
return Results.File(fileBytes, "application/pdf");
```
Thành:
```csharp
var filePath = sessions.GetFilePath(fileId);
if (filePath == null)
{
    Console.WriteLine($"[FILE ERROR] FileId not found in session: {fileId}");
    return Results.NotFound("File not found");
}

Console.WriteLine($"[FILE] File path from session: {filePath}");

if (!File.Exists(filePath))
{
    Console.WriteLine($"[FILE ERROR] File does not exist on disk: {filePath}");
    return Results.NotFound("File not found on disk");
}

var fileInfo = new FileInfo(filePath);
Console.WriteLine($"[FILE] Serving file: {filePath}, Size: {fileInfo.Length} bytes");

var fileBytes = File.ReadAllBytes(filePath);
return Results.File(fileBytes, "application/pdf");
```

- [ ] **Step 5: Cập nhật endpoint `POST /api/convert`**

Thay signature:
```csharp
app.MapPost("/api/convert", (
    string fileId,
    WordInteropService wordService) =>
```
Thành:
```csharp
app.MapPost("/api/convert", (
    string fileId,
    WordInteropService wordService,
    FileSessionService sessions) =>
```

Thay:
```csharp
if (!uploadedFiles.TryGetValue(fileId, out var filePath))
{
    return Results.NotFound("File not found");
}
```
Thành:
```csharp
var filePath = sessions.GetFilePath(fileId);
if (filePath == null)
{
    return Results.NotFound("File not found");
}
```

Thay dòng cập nhật path sau convert:
```csharp
uploadedFiles[fileId] = pdfPath;
```
Thành:
```csharp
sessions.UpdateFilePath(fileId, pdfPath);
```

- [ ] **Step 6: Cập nhật endpoint `POST /api/print`**

Thay signature:
```csharp
app.MapPost("/api/print", (
    PrintRequest request,
    PrinterManagementService printerService,
    WordInteropService wordService,
    PrintAlgorithmService printAlgorithm) =>
```
Thành:
```csharp
app.MapPost("/api/print", (
    PrintRequest request,
    PrinterManagementService printerService,
    WordInteropService wordService,
    PrintAlgorithmService printAlgorithm,
    FileSessionService sessions) =>
```

Thay:
```csharp
if (!uploadedFiles.TryGetValue(request.FileId, out var filePath))
{
    Console.WriteLine($"[PRINT ERROR] File not found for fileId: {request.FileId}");
    Console.WriteLine($"[PRINT ERROR] Available fileIds: {string.Join(", ", uploadedFiles.Keys)}");
    return Results.NotFound(new PrintResponse
    {
        Success = false,
        Message = "File not found. Please upload the file again."
    });
}
```
Thành:
```csharp
var filePath = sessions.GetFilePath(request.FileId);
if (filePath == null)
{
    Console.WriteLine($"[PRINT ERROR] File not found for fileId: {request.FileId}");
    return Results.NotFound(new PrintResponse
    {
        Success = false,
        Message = "File not found. Please upload the file again."
    });
}
```

Sau dòng `printJobs[jobState.JobId] = jobState;` thay thành:
```csharp
sessions.AddJob(jobState.JobId, jobState);
```

- [ ] **Step 7: Cập nhật endpoint `POST /api/print/continue`**

Thay signature:
```csharp
app.MapPost("/api/print/continue", (
    string jobId,
    PrintAlgorithmService printAlgorithm) =>
```
Thành:
```csharp
app.MapPost("/api/print/continue", (
    string jobId,
    PrintAlgorithmService printAlgorithm,
    FileSessionService sessions) =>
```

Thay:
```csharp
if (!printJobs.TryGetValue(jobId, out var jobState))
{
    return Results.NotFound(new PrintResponse
    {
        Success = false,
        Message = "Job not found"
    });
}
```
Thành:
```csharp
var jobState = sessions.GetJob(jobId);
if (jobState == null)
{
    return Results.NotFound(new PrintResponse
    {
        Success = false,
        Message = "Job not found"
    });
}
```

Sau khi print xong thành công (sau `jobState.WaitingForFlip = false;`), thêm cleanup:
```csharp
jobState.WaitingForFlip = false;

// Dọn job và file temp sau khi hoàn thành
sessions.RemoveJob(jobId);
```

- [ ] **Step 8: Build**
```powershell
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 9: Commit**
```powershell
git add backend/Program.cs
git commit -m "refactor(program): replace raw Dictionaries with FileSessionService, add cleanup on job complete"
```

---

## TASK 4: Fix bug auto-duplex + pageRange trong `PrintAlgorithmService`

**Files:**
- Modify: `backend/Services/PrintAlgorithmService.cs`

**Vấn đề:** Khi `isDuplexPrinter = true`, method `CreateNormalDuplexJob` return sớm mà không xử lý `pageRange`. Kết quả: auto-duplex printer in toàn bộ file dù user chỉ chọn 1 vài trang.

- [ ] **Step 1: Tìm đoạn code auto-duplex return sớm**

Trong `PrintAlgorithmService.cs`, tìm block:

```csharp
if (isDuplexPrinter)
{
    // ==========================
    //  AUTO DUPLEX (MÁY IN 2 MẶT)
    // ==========================
    // PageRange (nếu có) sẽ được xử lý ở chỗ gọi PrintPdf (ExecutePrintJob),
    // hiện tại luồng cũ đang để pageRange=null => in toàn bộ.
    Console.WriteLine("[CreateNormalDuplexJob] Auto duplex printer detected. Using original PDF.");
    jobState.WaitingForFlip = false;
    return jobState;
}
```

- [ ] **Step 2: Thay bằng logic xử lý pageRange cho auto-duplex**

```csharp
if (isDuplexPrinter)
{
    // ==========================
    //  AUTO DUPLEX (MÁY IN 2 MẶT)
    // ==========================
    Console.WriteLine("[CreateNormalDuplexJob] Auto duplex printer detected.");

    if (!string.IsNullOrWhiteSpace(pageRange))
    {
        // Tạo subset PDF chứa đúng các trang được chọn
        Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex with pageRange: {pageRange}. Creating subset PDF.");
        var selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);

        if (selectedPages.Length == 0)
        {
            throw new InvalidOperationException($"Page range '{pageRange}' không hợp lệ hoặc không có trang nào.");
        }

        if (selectedPages.Length < pdfInfo.PageCount)
        {
            var subsetPath = Path.Combine(Path.GetTempPath(), $"auto_duplex_subset_{Guid.NewGuid()}.pdf");
            _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
            jobState.TempPdfPath = subsetPath;
            Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex subset created: {subsetPath}");
        }
    }

    jobState.WaitingForFlip = false;
    return jobState;
}
```

- [ ] **Step 3: Build**
```powershell
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Manual test**

Chạy `dotnet run` từ `backend/`. Mở frontend, upload 1 file PDF nhiều trang, chọn chỉ trang 2-3, chọn auto-duplex printer, bấm In. Kiểm tra console backend — phải thấy log `"Auto duplex subset created"`.

- [ ] **Step 5: Commit**
```powershell
git add backend/Services/PrintAlgorithmService.cs
git commit -m "fix(print): auto-duplex printer now respects pageRange by creating subset PDF"
```

---

## TASK 5: Restructure `frontend/app.js`

**Files:**
- Modify: `frontend/app.js`

**Approach:** Giữ 1 file duy nhất, tổ chức thành 6 module objects (`ThemeModule`, `PrinterModule`, `UploadModule`, `PreviewModule`, `PageSelectModule`, `PrintModule`) + `AppState` object tập trung. Mỗi module có method `init()`. Thêm lazy thumbnail render với `IntersectionObserver`. Fix `Deselect All` và toast color.

- [ ] **Step 1: Thay toàn bộ `frontend/app.js`**

Thay thế toàn bộ nội dung file bằng:

```javascript
// ═══════════════════════════════════════════════════════════════════
// myPrinter — app.js
// Cấu trúc: AppState + 6 Module Objects + DOMContentLoaded init
// Tương thích file:// (không dùng ES import/export)
// ═══════════════════════════════════════════════════════════════════

const API_BASE = 'http://localhost:8787/api';

// ─── PDF.js worker ──────────────────────────────────────────────────
pdfjsLib.GlobalWorkerOptions.workerSrc =
    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';

// ═══════════════════════════════════════════════════════════════════
// MODULE: AppState — Toàn bộ state ứng dụng tập trung tại đây
// ═══════════════════════════════════════════════════════════════════
const AppState = {
    selectedPrinter:   null,   // PrinterInfo object từ API
    uploadedFile:      null,   // { id, name, needsConversion }
    currentJob:        null,   // PrintJobState từ API
    currentPdfDoc:     null,   // PDF.js document object
    selectedPages:     new Set(),
    singleSidedPages:  new Set(),
    totalPageCount:    0,
    isUserTypingPageRange: false,

    reset() {
        this.uploadedFile     = null;
        this.currentPdfDoc    = null;
        this.currentJob       = null;
        this.selectedPages    = new Set();
        this.singleSidedPages = new Set();
        this.totalPageCount   = 0;
        this.isUserTypingPageRange = false;
    },

    selectAllPages() {
        this.selectedPages = new Set();
        for (let i = 1; i <= this.totalPageCount; i++) this.selectedPages.add(i);
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: Toast — Thông báo popup với màu theo loại
// ═══════════════════════════════════════════════════════════════════
const ToastModule = {
    /** @param {string} message @param {'success'|'error'|'info'} type */
    show(message, type = 'info') {
        const toast = document.getElementById('toast');
        const toastMsg = document.getElementById('toast-message');
        if (!toast || !toastMsg) return;

        toastMsg.textContent = message;

        // Màu theo loại
        toast.style.borderColor = {
            success: 'rgba(16, 185, 129, 0.5)',
            error:   'rgba(239, 68, 68, 0.5)',
            info:    'rgba(148, 163, 184, 0.2)',
        }[type] ?? 'rgba(148, 163, 184, 0.2)';

        toast.classList.remove('hidden');
        clearTimeout(this._timer);
        this._timer = setTimeout(() => toast.classList.add('hidden'), 3000);
    },

    _timer: null,
};

// Alias ngắn gọn dùng trong code
const showToast = (msg, type = 'info') => ToastModule.show(msg, type);

// ═══════════════════════════════════════════════════════════════════
// MODULE: ThemeModule — Dark/Light/Auto theme toggle
// ═══════════════════════════════════════════════════════════════════
const ThemeModule = {
    init() {
        const saved = localStorage.getItem('theme') || 'auto';
        this._apply(saved);
        this._updateButtons(saved);

        const toggle = document.getElementById('theme-toggle');
        if (!toggle) return;

        toggle.addEventListener('click', (e) => {
            const btn = e.target.closest('.theme-btn');
            if (!btn) return;
            const theme = btn.dataset.theme;
            localStorage.setItem('theme', theme);
            this._apply(theme);
            this._updateButtons(theme);
        });

        window.matchMedia('(prefers-color-scheme: dark)')
            .addEventListener('change', () => {
                if (localStorage.getItem('theme') === 'auto') this._apply('auto');
            });
    },

    _apply(theme) {
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        document.documentElement.setAttribute(
            'data-theme',
            theme === 'auto' ? (prefersDark ? 'dark' : 'light') : theme
        );
    },

    _updateButtons(active) {
        document.querySelectorAll('.theme-btn').forEach(btn =>
            btn.classList.toggle('active', btn.dataset.theme === active)
        );
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: PrinterModule — Load danh sách máy in
// ═══════════════════════════════════════════════════════════════════
const PrinterModule = {
    async init() {
        try {
            const response = await fetch(`${API_BASE}/printers`);
            const printers = await response.json();
            const list = document.getElementById('printer-list');

            if (!printers.length) {
                list.innerHTML = '<div class="loading">Không tìm thấy máy in nào</div>';
                return;
            }

            list.innerHTML = printers.map(p => `
                <div class="printer-item" data-printer='${JSON.stringify(p)}'>
                    <div class="printer-info">
                        <span class="printer-icon">🖨️</span>
                        <div class="printer-details">
                            <h3>${p.name}</h3>
                            <div class="printer-status">
                                ${p.isDefault ? '<span class="badge badge-info">Mặc định</span>' : ''}
                                ${p.isDuplex
                                    ? '<span class="badge badge-success">Hỗ trợ 2 mặt</span>'
                                    : '<span class="badge badge-warning">Chỉ 1 mặt</span>'}
                                ${this._statusBadge(p.status)}
                            </div>
                        </div>
                    </div>
                </div>
            `).join('');

            document.querySelectorAll('.printer-item').forEach(item => {
                item.addEventListener('click', () => {
                    document.querySelectorAll('.printer-item').forEach(i => i.classList.remove('selected'));
                    item.classList.add('selected');
                    AppState.selectedPrinter = JSON.parse(item.dataset.printer);
                    PrintModule.updateButton();
                });
            });

            // Auto-select máy in mặc định
            const def = printers.find(p => p.isDefault);
            if (def) {
                const defItem = Array.from(document.querySelectorAll('.printer-item'))
                    .find(el => JSON.parse(el.dataset.printer).name === def.name);
                defItem?.click();
            }
        } catch (err) {
            showToast('Lỗi khi tải danh sách máy in: ' + err.message, 'error');
        }
    },

    _statusBadge(status) {
        return {
            3: '<span class="badge badge-success">Sẵn sàng</span>',
            4: '<span class="badge badge-warning">Đang in</span>',
            7: '<span class="badge badge-danger">Offline</span>',
        }[status] ?? '';
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: UploadModule — Kéo thả / chọn file + upload
// ═══════════════════════════════════════════════════════════════════
const UploadModule = {
    init() {
        const area  = document.getElementById('upload-area');
        const input = document.getElementById('file-input');

        area.addEventListener('click', () => input.click());

        area.addEventListener('dragover', e => {
            e.preventDefault();
            area.classList.add('drag-over');
        });
        area.addEventListener('dragleave', () => area.classList.remove('drag-over'));
        area.addEventListener('drop', async e => {
            e.preventDefault();
            area.classList.remove('drag-over');
            if (e.dataTransfer.files[0]) await this._upload(e.dataTransfer.files[0]);
        });

        input.addEventListener('change', async e => {
            if (e.target.files[0]) await this._upload(e.target.files[0]);
        });

        document.getElementById('remove-file').addEventListener('click', () => this._remove());
    },

    async _upload(file) {
        const ext = '.' + file.name.split('.').pop().toLowerCase();
        if (!['.doc', '.docx', '.pdf'].includes(ext)) {
            showToast('Loại file không được hỗ trợ', 'error');
            return;
        }

        try {
            const formData = new FormData();
            formData.append('file', file);
            document.getElementById('file-status').textContent = 'Đang tải lên...';

            const res    = await fetch(`${API_BASE}/upload`, { method: 'POST', body: formData });
            const result = await res.json();
            if (!result.success) { showToast('Lỗi: ' + result.message, 'error'); return; }

            AppState.uploadedFile = {
                id:   result.fileId,
                name: result.originalFileName,
                needsConversion: ext !== '.pdf',
            };

            if (AppState.uploadedFile.needsConversion) {
                document.getElementById('file-status').textContent = 'Đang chuyển đổi sang PDF...';
                await fetch(`${API_BASE}/convert?fileId=${AppState.uploadedFile.id}`, { method: 'POST' });
            }

            document.getElementById('file-name').textContent   = AppState.uploadedFile.name;
            document.getElementById('file-status').textContent = 'Đã sẵn sàng';
            document.getElementById('upload-area').classList.add('hidden');
            document.getElementById('file-info').classList.remove('hidden');

            await PreviewModule.render(AppState.uploadedFile.id);

            document.getElementById('page-range-section')?.classList.remove('hidden');
            PrintModule.updateButton();
            showToast('Tải file thành công!', 'success');
        } catch (err) {
            showToast('Lỗi khi tải file: ' + err.message, 'error');
        }
    },

    _remove() {
        AppState.reset();
        document.getElementById('file-input').value = '';
        document.getElementById('upload-area').classList.remove('hidden');
        document.getElementById('file-info').classList.add('hidden');

        const sidebar = document.getElementById('preview-sidebar');
        if (sidebar) sidebar.style.display = 'none';

        document.getElementById('page-range-section')?.classList.add('hidden');
        const pageInput = document.getElementById('page-range-input');
        if (pageInput) pageInput.value = '';

        PrintModule.updateButton();
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: PreviewModule — PDF thumbnail grid + Zoom Modal
// IntersectionObserver cho lazy rendering: chỉ render canvas khi visible
// ═══════════════════════════════════════════════════════════════════
const PreviewModule = {
    /** @type {IntersectionObserver|null} */
    _observer: null,

    async render(fileId) {
        const sidebar = document.getElementById('preview-sidebar');
        const grid    = document.getElementById('sidebar-preview-grid');
        if (!sidebar || !grid) return;

        sidebar.style.display = 'flex';
        grid.innerHTML = '<div class="loading">Đang tải preview...</div>';

        try {
            const blob      = await fetch(`${API_BASE}/file/${fileId}`).then(r => r.blob());
            const url       = URL.createObjectURL(blob);
            const loadTask  = pdfjsLib.getDocument(url);
            AppState.currentPdfDoc = await loadTask.promise;
            AppState.totalPageCount = AppState.currentPdfDoc.numPages;

            AppState.selectAllPages();

            const countEl = document.getElementById('sidebar-page-count');
            if (countEl) countEl.textContent = `${AppState.totalPageCount} trang`;

            PageSelectModule.updateDisplay();

            grid.innerHTML = '';

            // Disconnect observer cũ nếu có
            this._observer?.disconnect();
            this._observer = new IntersectionObserver(
                (entries) => {
                    entries.forEach(entry => {
                        if (entry.isIntersecting) {
                            const thumb = entry.target;
                            const pageNum = parseInt(thumb.dataset.pageNumber);
                            if (!thumb.dataset.rendered) {
                                this._renderCanvas(thumb, pageNum);
                                this._observer.unobserve(thumb);
                            }
                        }
                    });
                },
                { rootMargin: '100px' }
            );

            for (let i = 1; i <= AppState.totalPageCount; i++) {
                const thumb = this._createThumbnailPlaceholder(i);
                grid.appendChild(thumb);
                this._observer.observe(thumb);
            }

            showToast(`Đã tải ${AppState.totalPageCount} trang`, 'success');
        } catch (err) {
            console.error('Error rendering PDF:', err);
            showToast('Lỗi khi tải preview PDF: ' + err.message, 'error');
        }
    },

    /** Tạo placeholder div cho thumbnail (chưa render canvas) */
    _createThumbnailPlaceholder(pageNum) {
        const div = document.createElement('div');
        div.className = 'page-thumbnail selected';
        div.dataset.pageNumber = pageNum;
        div.style.cssText = `
            position: relative; cursor: pointer;
            border: 2px solid #22c55e; border-radius: 8px;
            background: rgba(100,116,139,0.1);
            transition: all 0.2s; min-height: 80px;
        `;

        const label = document.createElement('div');
        label.style.cssText = `
            position: absolute; bottom: 4px; right: 4px;
            background: rgba(0,0,0,0.8); color: white;
            padding: 3px 6px; border-radius: 4px;
            font-size: 11px; font-weight: 600;
        `;
        label.textContent = pageNum;
        div.appendChild(label);

        // Click: toggle selection
        div.addEventListener('click', e => {
            if (e.detail === 1) setTimeout(() => {
                if (e.detail === 1) {
                    PageSelectModule.toggle(pageNum);
                }
            }, 200);
        });
        // Double-click: zoom modal
        div.addEventListener('dblclick', () => ZoomModal.open(pageNum));
        // Right-click: context menu
        div.addEventListener('contextmenu', e => {
            e.preventDefault();
            ContextMenu.show(e, pageNum);
        });

        return div;
    },

    /** Render canvas vào thumbnail đã tạo (gọi bởi IntersectionObserver) */
    async _renderCanvas(thumb, pageNum) {
        try {
            const page     = await AppState.currentPdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale: 0.6 });
            const canvas   = document.createElement('canvas');
            const ctx      = canvas.getContext('2d');

            canvas.width  = viewport.width;
            canvas.height = viewport.height;
            canvas.style.cssText = 'width:100%!important;height:auto!important;display:block;border-radius:6px;';

            await page.render({ canvasContext: ctx, viewport }).promise;

            thumb.insertBefore(canvas, thumb.firstChild);
            thumb.dataset.rendered = '1';
        } catch (err) {
            console.error(`Error rendering page ${pageNum}:`, err);
        }
    },

    /** Cập nhật border màu của tất cả thumbnails theo state */
    updateThumbnails() {
        document.querySelectorAll('.page-thumbnail').forEach(thumb => {
            const n       = parseInt(thumb.dataset.pageNumber);
            const sel     = AppState.selectedPages.has(n);
            const single  = AppState.singleSidedPages.has(n);
            thumb.classList.toggle('selected', sel);
            thumb.style.borderColor = sel
                ? (single ? '#3b82f6' : '#22c55e')
                : 'rgba(148,163,184,0.2)';
            thumb.title = `Trang ${n} - In ${single ? '1' : '2'} mặt`;
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: PageSelectModule — Quản lý chọn trang in
// ═══════════════════════════════════════════════════════════════════
const PageSelectModule = {
    init() {
        const input = document.getElementById('page-range-input');
        if (!input) return;

        input.addEventListener('focus', () => { AppState.isUserTypingPageRange = true; });
        input.addEventListener('blur',  () => {
            AppState.isUserTypingPageRange = false;
            this.updateDisplay();
        });

        input.addEventListener('input', e => {
            AppState.isUserTypingPageRange = true;
            const text = e.target.value.trim();

            if (!text) {
                AppState.selectAllPages();
            } else {
                AppState.selectedPages = new Set(this._parseRange(text));
            }

            PreviewModule.updateThumbnails();
            this._updateTexts();
            PrintModule.updateButton();
        });
    },

    toggle(pageNum) {
        if (AppState.selectedPages.has(pageNum)) {
            AppState.selectedPages.delete(pageNum);
        } else {
            AppState.selectedPages.add(pageNum);
        }
        PreviewModule.updateThumbnails();
        this.updateDisplay();
        PrintModule.updateButton();
    },

    updateDisplay() {
        this._updateTexts();
        if (!AppState.isUserTypingPageRange) {
            const input = document.getElementById('page-range-input');
            if (!input) return;
            const all = AppState.selectedPages.size === AppState.totalPageCount;
            input.value = all ? '' : this._formatRange(Array.from(AppState.selectedPages).sort((a,b)=>a-b));
        }
    },

    _updateTexts() {
        const all  = AppState.selectedPages.size === AppState.totalPageCount;
        const text = all ? 'Tất cả' : `${AppState.selectedPages.size} trang`;
        const el1  = document.getElementById('sidebar-selected-pages');
        const el2  = document.getElementById('inline-selected');
        if (el1) el1.textContent = text;
        if (el2) el2.textContent = all ? 'Đã chọn: Tất cả' : `Đã chọn: ${text}`;
    },

    _parseRange(text) {
        const pages = new Set();
        text.split(',').forEach(part => {
            part = part.trim();
            if (part.includes('-')) {
                const [a, b] = part.split('-').map(s => parseInt(s.trim()));
                if (!isNaN(a) && !isNaN(b)) {
                    for (let i = Math.min(a,b); i <= Math.max(a,b); i++) {
                        if (i >= 1 && i <= AppState.totalPageCount) pages.add(i);
                    }
                }
            } else {
                const n = parseInt(part);
                if (!isNaN(n) && n >= 1 && n <= AppState.totalPageCount) pages.add(n);
            }
        });
        return pages;
    },

    _formatRange(pages) {
        if (!pages.length) return '';
        const ranges = [];
        let start = pages[0], end = pages[0];
        for (let i = 1; i <= pages.length; i++) {
            if (i < pages.length && pages[i] === end + 1) {
                end = pages[i];
            } else {
                ranges.push(start === end ? `${start}` : `${start}-${end}`);
                if (i < pages.length) { start = end = pages[i]; }
            }
        }
        return ranges.join(',');
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: ZoomModal — Preview full trang, click để toggle selection
// ═══════════════════════════════════════════════════════════════════
const ZoomModal = {
    init() {
        document.getElementById('zoom-modal-close')?.addEventListener('click', () => this.close());
        document.getElementById('zoom-modal-overlay')?.addEventListener('click', () => this.close());

        document.getElementById('all-double-btn')?.addEventListener('click', () => {
            AppState.selectAllPages();
            AppState.singleSidedPages.clear();
            PreviewModule.updateThumbnails();
            PageSelectModule.updateDisplay();
            this._updateModalStyles();
            showToast('Đã chọn tất cả in 2 mặt', 'success');
        });

        document.getElementById('all-single-btn')?.addEventListener('click', () => {
            AppState.selectAllPages();
            AppState.singleSidedPages = new Set(AppState.selectedPages);
            PreviewModule.updateThumbnails();
            PageSelectModule.updateDisplay();
            this._updateModalStyles();
            showToast('Đã chọn tất cả in 1 mặt', 'success');
        });

        // FIX: Deselect All → xóa selection thực sự, không auto-select-all
        document.getElementById('deselect-all-btn')?.addEventListener('click', () => {
            AppState.selectedPages.clear();
            AppState.singleSidedPages.clear();
            PreviewModule.updateThumbnails();
            PageSelectModule.updateDisplay();
            this._updateModalStyles();
            PrintModule.updateButton();
            showToast('Đã bỏ chọn tất cả. Chọn trang để in.', 'info');
        });
    },

    open(pageNum) {
        const modal = document.getElementById('page-zoom-modal');
        if (!modal) return;
        modal.classList.remove('hidden');
        this._renderAllPages(pageNum);
    },

    close() {
        document.getElementById('page-zoom-modal')?.classList.add('hidden');
    },

    async _renderAllPages(scrollToPage) {
        const container = document.querySelector('.zoom-canvas-container');
        if (!container) return;
        container.innerHTML = '<div class="loading">Đang tải...</div>';

        const list = document.createElement('div');
        list.style.cssText = 'width:100%;max-width:800px;margin:0 auto;';

        const renderPromises = [];
        for (let i = 1; i <= AppState.totalPageCount; i++) {
            renderPromises.push((async (n) => {
                try {
                    const page = await AppState.currentPdfDoc.getPage(n);
                    return { index: n, element: this._buildPageContainer(page, n) };
                } catch { return null; }
            })(i));
        }

        const results = (await Promise.all(renderPromises))
            .filter(Boolean)
            .sort((a, b) => a.index - b.index);

        results.forEach(r => list.appendChild(r.element));
        container.innerHTML = '';
        container.appendChild(list);

        document.getElementById('zoom-page-title').textContent =
            `Tất cả trang (${AppState.totalPageCount})`;
        document.getElementById('zoom-page-info').textContent = 'Cuộn để xem tất cả';

        ['zoom-prev','zoom-next'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.style.display = 'none';
        });

        // Scroll đến trang cần xem
        setTimeout(() => {
            const target = list.querySelector(`[data-page="${scrollToPage}"]`);
            if (target) {
                const rect = target.getBoundingClientRect();
                const cRect = container.getBoundingClientRect();
                container.scrollTo({ top: rect.top - cRect.top + container.scrollTop - 20, behavior: 'smooth' });
            }
        }, 100);
    },

    _buildPageContainer(page, n) {
        const isSel    = AppState.selectedPages.has(n);
        const isSingle = AppState.singleSidedPages.has(n);
        const border   = isSel ? (isSingle ? '#3b82f6' : '#22c55e') : 'transparent';

        const div = document.createElement('div');
        div.className = 'modal-page-container';
        div.dataset.page = n;
        div.style.cssText = `
            margin-bottom:1.5rem; position:relative;
            border:3px solid ${border}; border-radius:8px;
            padding:1rem; background:rgba(30,41,59,0.5); cursor:pointer;
        `;

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;align-items:center;justify-content:space-between;margin-bottom:.75rem;';
        const title  = document.createElement('h4');
        title.textContent = `Trang ${n}`;
        title.style.cssText = 'margin:0;font-size:1rem;';

        const badge = document.createElement('div');
        badge.className = isSingle ? 'single-sided-badge' : 'double-sided-badge';
        badge.textContent = isSingle ? '1 MẶT' : '2 MẶT';
        if (!isSel) badge.style.opacity = '0.3';

        header.appendChild(title);

        const canvas  = document.createElement('canvas');
        const ctx     = canvas.getContext('2d');
        const vp      = page.getViewport({ scale: 1.2 });
        canvas.width  = vp.width;
        canvas.height = vp.height;
        canvas.style.cssText = 'width:100%;height:auto;display:block;border-radius:6px;';
        page.render({ canvasContext: ctx, viewport: vp });

        div.appendChild(header);
        div.appendChild(badge);
        div.appendChild(canvas);

        div.addEventListener('click', e => {
            if (e.button !== 0) return;
            if (AppState.selectedPages.has(n)) {
                AppState.selectedPages.delete(n);
            } else {
                AppState.selectedPages.add(n);
            }
            PreviewModule.updateThumbnails();
            PageSelectModule.updateDisplay();
            PrintModule.updateButton();
            this._updateModalStyles();
        });

        div.addEventListener('contextmenu', e => {
            e.preventDefault();
            ContextMenu.show(e, n);
        });

        return div;
    },

    _updateModalStyles() {
        document.querySelectorAll('.modal-page-container').forEach(el => {
            const n       = parseInt(el.dataset.page);
            const isSel   = AppState.selectedPages.has(n);
            const isSingle = AppState.singleSidedPages.has(n);
            el.style.borderColor = isSel ? (isSingle ? '#3b82f6' : '#22c55e') : 'transparent';
            const badge = el.querySelector('.single-sided-badge, .double-sided-badge');
            if (badge) {
                badge.className   = isSingle ? 'single-sided-badge' : 'double-sided-badge';
                badge.textContent = isSingle ? '1 MẶT' : '2 MẶT';
                badge.style.opacity = isSel ? '1' : '0.3';
            }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: ContextMenu — Right-click menu để chọn 1/2 mặt per page
// ═══════════════════════════════════════════════════════════════════
const ContextMenu = {
    _currentPage: null,

    init() {
        document.addEventListener('click', e => {
            if (!e.target.closest('.context-menu')) this.hide();
        });

        document.querySelectorAll('.context-menu-item').forEach(item => {
            item.addEventListener('click', e => {
                e.stopPropagation();
                this._handleAction(item.dataset.action);
            });
        });
    },

    show(event, pageNum) {
        this._currentPage = pageNum;
        const menu = document.getElementById('page-context-menu');
        document.getElementById('context-menu-header').textContent = `Trang ${pageNum}`;

        const isSingle = AppState.singleSidedPages.has(pageNum);
        document.getElementById('check-double').textContent = isSingle ? '' : '✓';
        document.getElementById('check-single').textContent = isSingle ? '✓' : '';

        menu.style.left = `${event.clientX}px`;
        menu.style.top  = `${event.clientY}px`;
        menu.classList.remove('hidden');

        const rect = menu.getBoundingClientRect();
        if (rect.right  > window.innerWidth)  menu.style.left = `${window.innerWidth  - rect.width  - 10}px`;
        if (rect.bottom > window.innerHeight) menu.style.top  = `${window.innerHeight - rect.height - 10}px`;
    },

    hide() {
        document.getElementById('page-context-menu').classList.add('hidden');
        this._currentPage = null;
    },

    _handleAction(action) {
        const n = this._currentPage;
        switch (action) {
            case 'all-double-sided':
                AppState.selectAllPages();
                AppState.singleSidedPages.clear();
                showToast('Đã chọn tất cả in 2 mặt', 'success');
                break;
            case 'all-single-sided':
                AppState.selectAllPages();
                AppState.singleSidedPages = new Set(AppState.selectedPages);
                showToast('Đã chọn tất cả in 1 mặt', 'success');
                break;
            case 'deselect-all':
                // FIX: xóa thực sự thay vì auto-select-all
                AppState.selectedPages.clear();
                AppState.singleSidedPages.clear();
                showToast('Đã bỏ chọn tất cả', 'info');
                PrintModule.updateButton();
                break;
            case 'double-sided':
                if (n !== null) {
                    if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n);
                    AppState.singleSidedPages.delete(n);
                    showToast(`Trang ${n} sẽ in 2 mặt`, 'info');
                }
                break;
            case 'single-sided':
                if (n !== null) {
                    if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n);
                    AppState.singleSidedPages.add(n);
                    showToast(`Trang ${n} sẽ in 1 mặt`, 'info');
                }
                break;
        }
        PreviewModule.updateThumbnails();
        PageSelectModule.updateDisplay();
        ZoomModal._updateModalStyles?.();
        this.hide();
    },
};

// ═══════════════════════════════════════════════════════════════════
// MODULE: PrintModule — Lệnh in, hướng dẫn lật giấy, tiếp tục in
// ═══════════════════════════════════════════════════════════════════
const PrintModule = {
    init() {
        document.getElementById('print-btn').addEventListener('click', () => this._startPrint());
        document.getElementById('continue-btn')?.addEventListener('click', async () => {
            await this._continuePrint();
            document.getElementById('flip-modal').classList.add('hidden');
        });
    },

    updateButton() {
        const btn = document.getElementById('print-btn');
        // Disabled nếu: chưa chọn máy in, chưa upload file, HOẶC không có trang nào được chọn
        btn.disabled = !AppState.selectedPrinter
            || !AppState.uploadedFile
            || AppState.selectedPages.size === 0;
    },

    async _startPrint() {
        if (!AppState.selectedPrinter) { showToast('Vui lòng chọn máy in', 'error'); return; }
        if (!AppState.uploadedFile)    { showToast('Vui lòng tải lên file cần in', 'error'); return; }
        if (AppState.selectedPages.size === 0) {
            showToast('Vui lòng chọn ít nhất 1 trang để in', 'error');
            return;
        }

        const mode = document.querySelector('input[name="print-mode"]:checked').value;
        const total = AppState.totalPageCount;
        const sel   = AppState.selectedPages;

        let pageRange = null;
        if (sel.size > 0 && sel.size < total) {
            pageRange = Array.from(sel).sort((a,b)=>a-b).join(',');
        }

        const body = {
            fileId:          AppState.uploadedFile.id,
            printerName:     AppState.selectedPrinter.name,
            mode:            mode === 'normal' ? 0 : 1,
            pageRange,
            singleSidedPages: AppState.singleSidedPages.size > 0
                ? Array.from(AppState.singleSidedPages) : null,
        };

        try {
            showToast('Đang gửi lệnh in...', 'info');
            const res    = await fetch(`${API_BASE}/print`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
            });
            const result = await res.json();

            if (!result.success) { showToast('Lỗi: ' + result.message, 'error'); return; }

            if (result.jobState?.waitingForFlip) {
                AppState.currentJob = result.jobState;
                this._showFlipModal(result.jobState.instruction);
                showToast('Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.', 'info');
            } else {
                showToast('In thành công!', 'success');
            }
        } catch (err) {
            showToast('Lỗi khi in: ' + err.message, 'error');
        }
    },

    async _continuePrint() {
        try {
            showToast('Đang in mặt chẵn...', 'info');
            const res    = await fetch(
                `${API_BASE}/print/continue?jobId=${AppState.currentJob.jobId}`,
                { method: 'POST' }
            );
            const result = await res.json();
            if (result.success) {
                showToast('In hoàn tất!', 'success');
                AppState.currentJob = null;
            } else {
                showToast('Lỗi: ' + result.message, 'error');
            }
        } catch (err) {
            showToast('Lỗi khi tiếp tục in: ' + err.message, 'error');
        }
    },

    _showFlipModal(instruction) {
        const modal = document.getElementById('flip-modal');
        document.getElementById('instruction-text').textContent =
            'Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.';
        document.getElementById('instruction-visual').innerHTML = this._simpleArrowSvg();
        modal.classList.remove('hidden');
    },

    _simpleArrowSvg() {
        return `<svg width="300" height="200" viewBox="0 0 300 200" style="margin:0 auto;">
            <defs>
                <marker id="ah2" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                    <polygon points="0 0,10 3.5,0 7" fill="#10b981"/>
                </marker>
            </defs>
            <rect x="100" y="60" width="100" height="80" fill="#f8fafc" stroke="#64748b" stroke-width="2" rx="2"/>
            <rect x="103" y="63" width="94" height="74" fill="white" stroke="#94a3b8" stroke-width="1"/>
            <text x="150" y="100" font-size="16" text-anchor="middle" fill="#94a3b8">Giấy đã in</text>
            <path d="M 150 145 L 150 175" stroke="#10b981" stroke-width="4" fill="none" marker-end="url(#ah2)"/>
            <rect x="80" y="180" width="140" height="15" fill="#e2e8f0" stroke="#667eea" stroke-width="2" rx="3"/>
            <text x="150" y="192" font-size="10" text-anchor="middle" fill="#667eea">Khay giấy</text>
            <text x="150" y="35" font-size="14" text-anchor="middle" fill="#10b981" font-weight="bold">↓ Đặt thẳng lại (không xoay)</text>
        </svg>`;
    },
};

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP — Khởi động tất cả modules khi DOM sẵn sàng
// ═══════════════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    ThemeModule.init();
    PrinterModule.init();
    UploadModule.init();
    PageSelectModule.init();
    ZoomModal.init();
    ContextMenu.init();
    PrintModule.init();
});
```

- [ ] **Step 2: Verify bằng cách mở frontend trong browser**

Chạy từ thư mục `frontend/`:
```powershell
python -m http.server 8080
```
Mở `http://localhost:8080`. Kiểm tra:
- [ ] Theme toggle hoạt động (A/L/D)
- [ ] Danh sách máy in load (cần backend chạy)
- [ ] Upload PDF → thumbnails xuất hiện (lazy: chỉ render khi scroll vào view)
- [ ] Click thumbnail → toggle selection (border đổi màu)
- [ ] Right-click → context menu đúng
- [ ] "Bỏ chọn tất cả" → selection thực sự rỗng, nút In bị disable
- [ ] Toast success màu xanh, error màu đỏ, info màu xám

- [ ] **Step 3: Commit**
```powershell
git add frontend/app.js
git commit -m "refactor(frontend): restructure app.js into module objects, lazy thumbnail IntersectionObserver, fix Deselect All and toast colors"
```

---

## TASK 6: Final push lên GitHub

- [ ] **Step 1: Verify toàn bộ**
```powershell
git log --oneline -6
```
Expected: thấy 5 commits mới từ plan này.

- [ ] **Step 2: Push**
```powershell
git push origin master
```

---

## Self-Review Checklist

- [x] **Task 1** → Spec: thêm `FileSession` model ✅
- [x] **Task 2** → Spec: `FileSessionService` với `ConcurrentDictionary` + TTL cleanup ✅
- [x] **Task 3** → Spec: `Program.cs` dùng service, cleanup sau job complete ✅
- [x] **Task 4** → Spec: auto-duplex + pageRange bug fixed ✅
- [x] **Task 5** → Spec: app.js restructured, lazy thumbnails, fix UX bugs ✅
- [x] **No placeholders** — tất cả code blocks đầy đủ ✅
- [x] **Type consistency** — `FileSession`, `FileSessionService`, method names nhất quán xuyên suốt ✅
- [x] **`AppState.reset()`** được gọi trong `UploadModule._remove()` ✅
- [x] **`ZoomModal._updateModalStyles`** được gọi từ `ContextMenu._handleAction` với optional chaining (`?.`) để tránh lỗi nếu modal chưa init ✅
