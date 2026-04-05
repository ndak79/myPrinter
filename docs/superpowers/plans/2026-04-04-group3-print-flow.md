# Group 3 — Print Flow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the print flow with an upgraded flip modal (checklist + animation), a confirmation dialog before printing, estimated print time display, and a cancel print button.

**Architecture:** Frontend: new modal `#confirm-print-modal`, upgraded `#flip-modal`, updated `SummaryModule`, updated `PrintModule`. Backend: new `DELETE /api/print/cancel` endpoint.

**Tech Stack:** Vanilla JS, CSS animations, .NET 8 minimal API.

---

## File Map

| File | Changes |
|------|---------|
| `frontend/index.html` | Upgrade `#flip-modal` HTML; add `#confirm-print-modal` |
| `frontend/styles.css` | Add flip checklist styles; animated flip SVG; confirm modal styles; cancel button styles |
| `frontend/app.js` | Update `PrintModule._showFlipModal()`; add `ConfirmPrintModal`; update `PrintModule._startPrint()` to show confirm first; update `SummaryModule.update()` for time estimate; add cancel logic |
| `backend/Program.cs` | Add `DELETE /api/print/cancel` endpoint |
| `backend/Services/PrintAlgorithmService.cs` | Add cancel job support (or note if not feasible) |

---

## Task 1: Estimated Print Time in Summary (G)

**Files:**
- Modify: `frontend/app.js` — `SummaryModule.update()`: improve time estimate to use 15s/sheet

**Note:** The current code already shows time estimate using 4s/sheet (line 942). This task improves the estimate to 15s/sheet which is more realistic for manual duplex.

- [ ] **Step 1: Update time estimate in SummaryModule.update()**

In `frontend/app.js`, find `SummaryModule.update()` (around line 915). Find the lines:

```js
// Estimate time: ~4s per sheet
const totalSec = sheets * 4;
```

Replace with:

```js
// Estimate time: ~15s per sheet (realistic for manual duplex + processing)
const totalSec = sheets * 15;
```

Also improve the time string formatting:

```js
const timeStr = totalSec < 60
    ? `< 1 phút`
    : totalSec < 3600
        ? `~${Math.ceil(totalSec / 60)} phút`
        : `~${Math.floor(totalSec / 3600)}h ${Math.ceil((totalSec % 3600) / 60)}m`;
```

- [ ] **Step 2: Verify time estimate**

Select 10 pages, 2-sided mode. Summary should show ~2 phút (10 pages → 5 sheets → 75s → ~2 phút). Select simplex mode: 10 pages → 10 sheets → 150s → ~3 phút.

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(ui): improved print time estimate in summary (G)"
```

---

## Task 2: Print Confirmation Dialog (7)

**Files:**
- Modify: `frontend/index.html` — add `#confirm-print-modal`
- Modify: `frontend/styles.css` — add confirm modal styles
- Modify: `frontend/app.js` — add `ConfirmPrintModal`; update `PrintModule._startPrint()` to show confirm first

- [ ] **Step 1: Add confirm modal HTML to index.html**

In `frontend/index.html`, before the `</body>` tag, add:

```html
<!-- Print Confirmation Modal (7) -->
<div id="confirm-print-modal" class="modal hidden">
    <div class="modal-overlay" id="confirm-modal-overlay"></div>
    <div class="modal-content modal-confirm">
        <div class="modal-header">
            <h2>🖨️ Xác Nhận Lệnh In</h2>
            <button class="modal-close" id="confirm-modal-close">✕</button>
        </div>
        <div class="modal-body">
            <div id="confirm-print-summary" class="confirm-print-summary"></div>
        </div>
        <div class="modal-footer">
            <button id="confirm-print-cancel-btn" class="btn-outline">Huỷ</button>
            <button id="confirm-print-ok-btn" class="btn-primary">✓ Xác Nhận In</button>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Add confirm modal CSS to styles.css**

In `frontend/styles.css`, add:

```css
/* ── Print Confirmation Modal (7) ──────────────────────────── */
.modal-confirm {
    max-width: 480px;
    width: 90vw;
}

.confirm-print-summary {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
}

.confirm-row {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.625rem 0.75rem;
    background: rgba(100, 116, 139, 0.08);
    border-radius: 8px;
    font-size: 0.9rem;
}

.confirm-row-icon { font-size: 1.1rem; flex-shrink: 0; width: 1.5rem; text-align: center; }
.confirm-row-label { color: var(--text-muted); flex-shrink: 0; width: 6rem; }
.confirm-row-value { color: var(--text-primary); font-weight: 500; flex: 1; }

.confirm-row.highlight {
    background: rgba(102, 126, 234, 0.1);
    border: 1px solid rgba(102, 126, 234, 0.2);
}

.modal-footer {
    display: flex;
    gap: 0.75rem;
    justify-content: flex-end;
    padding-top: 1rem;
    border-top: 1px solid var(--border-color);
    margin-top: 1rem;
}
```

- [ ] **Step 3: Add ConfirmPrintModal module to app.js**

In `frontend/app.js`, add before the BOOTSTRAP section:

```js
// ═══════════════════════════════════════════════════════════════════
// ConfirmPrintModal — Summary before sending print job (7)
// ═══════════════════════════════════════════════════════════════════
const ConfirmPrintModal = {
    _resolve: null,

    init() {
        document.getElementById('confirm-modal-close')?.addEventListener('click',  () => this._close(false));
        document.getElementById('confirm-modal-overlay')?.addEventListener('click', () => this._close(false));
        document.getElementById('confirm-print-cancel-btn')?.addEventListener('click', () => this._close(false));
        document.getElementById('confirm-print-ok-btn')?.addEventListener('click',    () => this._close(true));

        document.addEventListener('keydown', e => {
            const modal = document.getElementById('confirm-print-modal');
            if (modal?.classList.contains('hidden')) return;
            if (e.key === 'Escape') this._close(false);
            if (e.key === 'Enter')  { e.preventDefault(); this._close(true); }
        });
    },

    // Returns a Promise<boolean>: true if user confirmed, false if cancelled
    show() {
        return new Promise(resolve => {
            this._resolve = resolve;
            this._populate();
            document.getElementById('confirm-print-modal')?.classList.remove('hidden');
            document.getElementById('confirm-print-ok-btn')?.focus();
        });
    },

    _close(confirmed) {
        document.getElementById('confirm-print-modal')?.classList.add('hidden');
        if (this._resolve) { this._resolve(confirmed); this._resolve = null; }
    },

    _populate() {
        const container = document.getElementById('confirm-print-summary');
        if (!container) return;

        const mode    = document.querySelector('input[name="print-mode"]:checked')?.value || 'normal';
        const pages   = AppState.selectedPages.size;
        const copies  = CopiesModule?.copies || 1;
        const printer = AppState.selectedPrinter;
        const modeLabel = { normal: 'In 2 Mặt Thường', booklet: 'Sách A5 (Booklet)', simplex: 'In 1 Mặt' };

        let sheets;
        if (mode === 'simplex') {
            sheets = pages * copies;
        } else if (mode === 'booklet') {
            sheets = Math.ceil(pages / 4) * copies;
        } else {
            const singleSided = AppState.singleSidedPages.size;
            sheets = (Math.ceil((pages - singleSided) / 2) + singleSided) * copies;
        }

        const totalSec = sheets * 15;
        const timeStr  = totalSec < 60 ? '< 1 phút'
            : `~${Math.ceil(totalSec / 60)} phút`;

        const sel = Array.from(AppState.selectedPages).sort((a,b)=>a-b);
        const rangeStr = sel.length === AppState.totalPageCount
            ? 'Tất cả'
            : sel.join(', ').replace(/,\s/g, ', ');

        container.innerHTML = `
            <div class="confirm-row">
                <span class="confirm-row-icon">📄</span>
                <span class="confirm-row-label">File:</span>
                <span class="confirm-row-value">${AppState.uploadedFile?.name || '—'}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">🖨️</span>
                <span class="confirm-row-label">Máy in:</span>
                <span class="confirm-row-value">${printer?.name || '—'}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📋</span>
                <span class="confirm-row-label">Chế độ:</span>
                <span class="confirm-row-value">${modeLabel[mode] || mode}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📖</span>
                <span class="confirm-row-label">Trang:</span>
                <span class="confirm-row-value">${pages} trang (${rangeStr})</span>
            </div>
            <div class="confirm-row highlight">
                <span class="confirm-row-icon">🗒️</span>
                <span class="confirm-row-label">Số tờ:</span>
                <span class="confirm-row-value">${sheets} tờ × ${copies} bản</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">⏱️</span>
                <span class="confirm-row-label">Thời gian:</span>
                <span class="confirm-row-value">${timeStr}</span>
            </div>
        `;
    },
};
```

- [ ] **Step 4: Update PrintModule._startPrint() to show confirm first**

In `frontend/app.js`, in `PrintModule._startPrint()` (around line 768), at the very beginning after the validation checks, add:

```js
async _startPrint() {
    if (!AppState.selectedPrinter) { showToast('Vui lòng chọn máy in', 'error'); return; }
    if (!AppState.uploadedFile)    { showToast('Vui lòng tải file cần in', 'error'); return; }
    if (AppState.selectedPages.size === 0) { showToast('Vui lòng chọn ít nhất 1 trang', 'error'); return; }

    // Show confirmation dialog (7)
    const confirmed = await ConfirmPrintModal.show();
    if (!confirmed) return;

    // ... rest of existing code unchanged ...
```

- [ ] **Step 5: Initialize ConfirmPrintModal in BOOTSTRAP**

In `frontend/app.js`, in the BOOTSTRAP section:
```js
ConfirmPrintModal.init();
```

- [ ] **Step 6: Verify confirmation dialog**

Click "Bắt Đầu In". Confirmation modal should appear with all print details. Press Escape or "Huỷ" to cancel — no print sent. Press Enter or "Xác Nhận In" — print proceeds normally.

- [ ] **Step 7: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): print confirmation dialog with summary (7)"
```

---

## Task 3: Improved Flip Modal (3)

**Files:**
- Modify: `frontend/index.html` — upgrade `#flip-modal` HTML with checklist + timer option
- Modify: `frontend/styles.css` — add checklist styles + SVG animation styles
- Modify: `frontend/app.js` — update `PrintModule._showFlipModal()` to use new HTML

- [ ] **Step 1: Replace flip modal HTML in index.html**

Find `#flip-modal` (around line 206). Replace entire modal content:

```html
<!-- Manual Duplex Instruction Modal (upgraded) (3) -->
<div id="flip-modal" class="modal hidden">
    <div class="modal-overlay"></div>
    <div class="modal-content modal-flip">
        <div class="modal-header">
            <h2>📄 Hướng Dẫn Đặt Giấy</h2>
        </div>
        <div class="modal-body">
            <!-- Animated flip diagram -->
            <div class="flip-diagram" id="instruction-visual"></div>
            <p class="instruction-text" id="instruction-text"></p>

            <!-- Checklist (3) -->
            <div class="flip-checklist" id="flip-checklist">
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-1">
                    <span class="flip-check-label">1. Chờ máy in xong — đèn ngừng nhấp nháy</span>
                </label>
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-2">
                    <span class="flip-check-label">2. Lấy chồng giấy ra — theo đúng hướng mũi tên</span>
                </label>
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-3">
                    <span class="flip-check-label">3. Đặt lại vào khay — mặt trắng ngửa lên</span>
                </label>
            </div>

            <!-- Auto-continue timer (optional) -->
            <label class="flip-timer-toggle">
                <input type="checkbox" id="flip-timer-enable">
                <span>Tự động tiếp tục sau <strong>30 giây</strong></span>
            </label>
            <div class="flip-timer-bar hidden" id="flip-timer-bar">
                <div class="flip-timer-fill" id="flip-timer-fill"></div>
            </div>
        </div>
        <div class="modal-footer">
            <button id="continue-btn" class="btn-primary flip-continue-btn">✓ Đã Đặt Giấy - Tiếp Tục In</button>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Add flip modal CSS to styles.css**

```css
/* ── Improved Flip Modal (3) ───────────────────────────────── */
.modal-flip {
    max-width: 500px;
    width: 90vw;
}

.flip-diagram {
    display: flex;
    justify-content: center;
    margin-bottom: 1rem;
}

.flip-checklist {
    display: flex;
    flex-direction: column;
    gap: 0.625rem;
    margin: 1rem 0;
}

.flip-check-item {
    display: flex;
    align-items: center;
    gap: 0.625rem;
    padding: 0.625rem 0.875rem;
    background: rgba(100, 116, 139, 0.08);
    border-radius: 8px;
    cursor: pointer;
    transition: background 0.15s;
    font-size: 0.9rem;
    border: 1px solid transparent;
}

.flip-check-item:has(.flip-checkbox:checked) {
    background: rgba(16, 185, 129, 0.08);
    border-color: rgba(16, 185, 129, 0.2);
}

.flip-checkbox {
    width: 18px;
    height: 18px;
    accent-color: #10b981;
    cursor: pointer;
    flex-shrink: 0;
}

.flip-check-label { flex: 1; color: var(--text-primary); }

.flip-check-item:has(.flip-checkbox:checked) .flip-check-label {
    color: #10b981;
}

.flip-continue-btn {
    opacity: 0.7;
    transition: opacity 0.3s;
}

.flip-continue-btn.all-checked {
    opacity: 1;
}

.flip-timer-toggle {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.8rem;
    color: var(--text-muted);
    cursor: pointer;
    margin-top: 0.75rem;
}

.flip-timer-bar {
    height: 4px;
    background: rgba(100, 116, 139, 0.2);
    border-radius: 2px;
    margin-top: 0.5rem;
    overflow: hidden;
}

.flip-timer-fill {
    height: 100%;
    background: #667eea;
    width: 100%;
    transform-origin: left;
    transition: none;
}

@keyframes flipTimerCountdown {
    from { width: 100%; }
    to   { width: 0%; }
}

/* Animated flip SVG */
@keyframes paperFlip {
    0%   { transform: rotateY(0deg) translateY(0); }
    40%  { transform: rotateY(90deg) translateY(-10px); }
    60%  { transform: rotateY(90deg) translateY(-10px); }
    100% { transform: rotateY(0deg) translateY(0); }
}

.flip-paper-anim {
    animation: paperFlip 2s ease-in-out infinite;
    transform-origin: center;
}
```

- [ ] **Step 3: Update PrintModule._showFlipModal() to wire up checklist**

In `frontend/app.js`, replace `PrintModule._showFlipModal(instruction)`:

```js
_showFlipModal(instruction) {
    // Animated SVG
    document.getElementById('instruction-visual').innerHTML = `
        <svg width="260" height="180" viewBox="0 0 260 180">
            <defs>
                <marker id="arrow-flip" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                    <polygon points="0 0,10 3.5,0 7" fill="#10b981"/>
                </marker>
            </defs>
            <!-- Paper group with animation -->
            <g class="flip-paper-anim">
                <rect x="80" y="40" width="100" height="80" fill="#f8fafc" stroke="#64748b" stroke-width="2" rx="2"/>
                <rect x="83" y="43" width="94" height="74" fill="white" stroke="#94a3b8" stroke-width="1"/>
                <text x="130" y="82" font-size="13" text-anchor="middle" fill="#94a3b8">Giấy đã in</text>
                <text x="130" y="98" font-size="11" text-anchor="middle" fill="#cbd5e1">mặt 1 ✓</text>
            </g>
            <!-- Arrow down -->
            <path d="M 130 125 L 130 155" stroke="#10b981" stroke-width="3" fill="none" marker-end="url(#arrow-flip)"/>
            <!-- Tray -->
            <rect x="60" y="158" width="140" height="14" fill="#e2e8f0" stroke="#667eea" stroke-width="2" rx="3"/>
            <text x="130" y="169" font-size="10" text-anchor="middle" fill="#667eea">Khay giấy</text>
            <!-- Label top -->
            <text x="130" y="25" font-size="12" text-anchor="middle" fill="#10b981" font-weight="bold">Lấy ra → Lật → Đặt lại</text>
        </svg>
    `;

    document.getElementById('instruction-text').textContent =
        instruction || 'Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.';

    // Reset checklist
    ['flip-check-1', 'flip-check-2', 'flip-check-3'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.checked = false;
    });
    const continueBtn = document.getElementById('continue-btn');
    if (continueBtn) continueBtn.classList.remove('all-checked');

    // Checklist → enable button when all checked
    const checkboxes = document.querySelectorAll('.flip-checkbox');
    const updateContinueBtn = () => {
        const allChecked = Array.from(checkboxes).every(cb => cb.checked);
        continueBtn?.classList.toggle('all-checked', allChecked);
    };
    checkboxes.forEach(cb => {
        cb.removeEventListener('change', updateContinueBtn);
        cb.addEventListener('change', updateContinueBtn);
    });

    // Optional timer
    let _timerInterval = null;
    const timerEnable = document.getElementById('flip-timer-enable');
    const timerBar    = document.getElementById('flip-timer-bar');
    const timerFill   = document.getElementById('flip-timer-fill');

    if (timerEnable) {
        timerEnable.checked = false;
        timerEnable.onchange = () => {
            if (timerEnable.checked) {
                timerBar?.classList.remove('hidden');
                let remaining = 30;
                if (timerFill) {
                    timerFill.style.transition = 'none';
                    timerFill.style.width = '100%';
                    setTimeout(() => {
                        timerFill.style.transition = 'width 30s linear';
                        timerFill.style.width = '0%';
                    }, 50);
                }
                _timerInterval = setInterval(() => {
                    remaining--;
                    if (remaining <= 0) {
                        clearInterval(_timerInterval);
                        document.getElementById('continue-btn')?.click();
                    }
                }, 1000);
            } else {
                clearInterval(_timerInterval);
                timerBar?.classList.add('hidden');
            }
        };
    }

    document.getElementById('flip-modal').classList.remove('hidden');
},
```

- [ ] **Step 4: Verify improved flip modal**

Trigger a duplex print job. The flip modal should show:
- Animated SVG (paper flipping loop)
- Instruction text
- 3 checkboxes (unchecked by default)
- Continue button dimmed until all 3 checked
- Optional 30s timer checkbox at bottom

Check all 3 boxes — button brightens. Enable timer — countdown bar fills from right to left, then auto-clicks continue.

- [ ] **Step 5: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): improved flip modal with checklist and optional timer (3)"
```

---

## Task 4: Cancel/Abort Print (A) — Backend

**Files:**
- Modify: `backend/Program.cs` — add `DELETE /api/print/cancel` endpoint
- Modify: `backend/Services/PrintAlgorithmService.cs` — add `CancelJob()` method (or note limitation)

- [ ] **Step 1: Read current PrintAlgorithmService to understand job state structure**

Read `D:\Pro\myPrinter\backend\Services\PrintAlgorithmService.cs` lines 1-80 to see `PrintJobState` and job storage.

The service uses `_jobs = new ConcurrentDictionary<string, PrintJobState>()`. The cancel endpoint needs to:
1. Look up the job by jobId
2. Remove it from `_jobs`
3. Cancel pending Windows print jobs via WMI (best-effort)

- [ ] **Step 2: Add CancelJob method to PrintAlgorithmService.cs**

In `backend/Services/PrintAlgorithmService.cs`, find the `PrintAlgorithmService` class. Add this method:

```csharp
public (bool success, string message) CancelJob(string jobId)
{
    if (!_jobs.TryRemove(jobId, out var job))
        return (false, "Job không tồn tại hoặc đã hoàn tất");

    // Best-effort: try to cancel Windows print job via WMI
    try
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT * FROM Win32_PrintJob WHERE Document LIKE '%{jobId}%'");
        foreach (ManagementObject obj in searcher.Get())
        {
            obj.InvokeMethod("Pause",  null);
            obj.Delete();
        }
    }
    catch
    {
        // WMI cancel failed — job may have already completed; ignore
    }

    return (true, "Đã hủy lệnh in");
}
```

Note: This requires `using System.Management;`. Add to the using list at top of file. Also ensure `System.Management` NuGet is referenced — check `backend/PrinterApp.csproj`:

```bash
dotnet add D:\Pro\myPrinter\backend\PrinterApp.csproj package System.Management
```

- [ ] **Step 3: Add DELETE /api/print/cancel endpoint to Program.cs**

In `backend/Program.cs`, after the `/api/print/continue` endpoint, add:

```csharp
// Cancel a pending print job (A)
app.MapDelete("/api/print/cancel", (string jobId, PrintAlgorithmService printService) =>
{
    if (string.IsNullOrWhiteSpace(jobId))
        return Results.BadRequest(new { success = false, message = "jobId is required" });

    var (success, message) = printService.CancelJob(jobId);
    return Results.Ok(new { success, message });
});
```

- [ ] **Step 4: Build backend to verify no compile errors**

```bash
dotnet build D:\Pro\myPrinter\backend\PrinterApp.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit backend changes**

```bash
git add backend/Program.cs backend/Services/PrintAlgorithmService.cs backend/PrinterApp.csproj
git commit -m "feat(backend): add DELETE /api/print/cancel endpoint (A)"
```

---

## Task 5: Cancel/Abort Print (A) — Frontend

**Files:**
- Modify: `frontend/styles.css` — add cancel button styles
- Modify: `frontend/app.js` — update `PrintModule._startPrint()` to track `AppState.currentJob`; add cancel button logic

- [ ] **Step 1: Add cancel button CSS to styles.css**

```css
/* ── Cancel Print Button (A) ───────────────────────────────── */
#print-btn.cancellable {
    background: linear-gradient(135deg, #ef4444, #b91c1c) !important;
}

#print-btn.cancellable:hover {
    background: linear-gradient(135deg, #dc2626, #991b1b) !important;
}
```

- [ ] **Step 2: Update PrintModule to show cancel button while printing**

In `frontend/app.js`, update `PrintModule._startPrint()`. After `btn.textContent = '⏳ Đang gửi lệnh in...'`, add:

```js
btn.dataset.mode = 'printing';
```

And add a cancel handler in `PrintModule.init()`:

```js
// Cancel print if job is in progress
document.getElementById('print-btn').addEventListener('click', async (e) => {
    const btn = document.getElementById('print-btn');
    if (btn.dataset.mode === 'cancellable' && AppState.currentJob?.jobId) {
        // Cancel mode
        try {
            await fetch(`${API_BASE}/print/cancel?jobId=${AppState.currentJob.jobId}`, { method: 'DELETE' });
            showToast('Đã hủy lệnh in', 'info');
            SRModule.announce('Đã hủy lệnh in');
        } catch {
            showToast('Không thể hủy lệnh in', 'error');
        }
        AppState.currentJob = null;
        btn.dataset.mode = '';
        btn.classList.remove('cancellable');
        btn.innerHTML = '<span class="btn-icon">🖨️</span> Bắt Đầu In';
        PrintModule.updateButton();
        return;
    }
    // Normal print flow
    PrintModule._startPrint();
});
```

Update `_startPrint()` — when `waitingForFlip`, set the button to cancellable mode:

```js
if (result.jobState?.waitingForFlip) {
    AppState.currentJob = result.jobState;
    this._showFlipModal(result.jobState.instruction);
    // Show cancel button
    btn.dataset.mode = 'cancellable';
    btn.classList.add('cancellable');
    btn.innerHTML = '<span class="btn-icon">✕</span> Huỷ In';
    btn.disabled = false;
    btn.style.opacity = '1';
    showToast('Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.', 'info');
}
```

And reset when flip complete (in `_continuePrint()` success path):
```js
btn.dataset.mode = '';
btn.classList.remove('cancellable');
btn.innerHTML = '<span class="btn-icon">🖨️</span> Bắt Đầu In';
```

- [ ] **Step 3: Verify cancel button**

Start a duplex print job. While flip modal is open, the print button should change to "✕ Huỷ In" (red). Clicking it sends DELETE to backend, resets the button, shows toast "Đã hủy lệnh in".

- [ ] **Step 4: Commit frontend cancel**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): cancel print button while job in progress (A)"
```

---

## Task 6: Final Verification

- [ ] **Step 1: Run backend tests**

```bash
dotnet test D:\Pro\myPrinter\backend.Tests
```

Expected: All 82 existing tests pass. No regressions from CancelJob addition.

- [ ] **Step 2: Verify all 4 Group 3 features**

- [ ] Summary shows realistic time estimate (~15s/sheet)
- [ ] Print button shows confirmation dialog with all details; Enter confirms, Escape cancels
- [ ] Flip modal shows animated SVG, checklist, timer option; continue button dims until all checked
- [ ] After print starts, button becomes "Huỷ In" (red); clicking cancels job

- [ ] **Step 3: Commit count**

```bash
git log --oneline -8
```

Should show 5 commits from this plan (1 backend, 4 frontend features).
