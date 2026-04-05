# Group 4 — Printer & Status — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make printer selection and status clearer — animated glow on selected printer, colored status dots, tooltips on badges, inline error recovery instead of just toast.

**Architecture:** Frontend-only. Modified: `PrinterModule` (status dot, glow, tooltips, error state). New CSS: `.printer-status-dot`, `@keyframes borderGlow`, `.badge-tooltip`, `.card.error`.

**Tech Stack:** Vanilla JS, CSS animations, `file://` protocol compatible.

---

## File Map

| File | Changes |
|------|---------|
| `frontend/styles.css` | Add `.printer-status-dot`; `@keyframes borderGlow` on `.printer-item.selected`; `.badge-tooltip`; `.card.error` + `.card-error-message` |
| `frontend/app.js` | Update `PrinterModule._statusBadge()` to return dot + text; update `PrinterModule.init()` render template; add inline error helpers to `PrinterModule`, `UploadModule`, `PrintModule` |

---

## Task 1: Printer Status Dot (5)

**Files:**
- Modify: `frontend/styles.css` — add `.printer-status-dot` styles
- Modify: `frontend/app.js` — update `PrinterModule._statusBadge()` and render template

- [ ] **Step 1: Add status dot CSS**

In `frontend/styles.css`, after `.printer-item.selected` styles, add:

```css
/* ── Printer Status Dot (5) ────────────────────────────────── */
.printer-status-dot {
    display: inline-block;
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
    margin-right: 0.25rem;
}

.printer-status-dot.online  { background: #10b981; box-shadow: 0 0 4px rgba(16, 185, 129, 0.5); }
.printer-status-dot.busy    { background: #f59e0b; box-shadow: 0 0 4px rgba(245, 158, 11, 0.5); }
.printer-status-dot.offline { background: #ef4444; box-shadow: 0 0 4px rgba(239, 68, 68, 0.5); }
.printer-status-dot.unknown { background: #94a3b8; }

.printer-item .printer-status {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.35rem;
    margin-top: 0.25rem;
}
```

- [ ] **Step 2: Update PrinterModule._statusBadge() to include dot**

In `frontend/app.js`, replace `PrinterModule._statusBadge()`:

```js
_statusBadge(status) {
    const map = {
        3: { cls: 'online',  label: 'Sẵn sàng' },
        4: { cls: 'busy',    label: 'Đang in' },
        7: { cls: 'offline', label: 'Offline' },
    };
    const s = map[status];
    if (!s) return '<span class="printer-status-dot unknown"></span>';
    return `<span class="printer-status-dot ${s.cls}"></span><span class="badge badge-${s.cls === 'online' ? 'success' : s.cls === 'busy' ? 'warning' : 'danger'}">${s.label}</span>`;
},
```

- [ ] **Step 3: Verify status dots**

Load the app. Printer list should show colored dots (green for online, red for offline, yellow for busy) before the status badge text. Polls every 30s and updates dots live.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): printer status dots (5)"
```

---

## Task 2: Animated Glow Border on Selected Printer (N)

**Files:**
- Modify: `frontend/styles.css` — add `@keyframes borderGlow` for `.printer-item.selected`

- [ ] **Step 1: Add animated glow border CSS**

In `frontend/styles.css`, find `.printer-item.selected`. Replace or extend with:

```css
/* ── Printer Selected Glow Animation (N) ──────────────────── */
@keyframes borderGlow {
    0%, 100% {
        border-color: #667eea;
        box-shadow: 0 0 0 1px rgba(102, 126, 234, 0.3),
                    inset 0 0 20px rgba(102, 126, 234, 0.05);
    }
    50% {
        border-color: #764ba2;
        box-shadow: 0 0 0 3px rgba(118, 75, 162, 0.25),
                    inset 0 0 20px rgba(118, 75, 162, 0.08);
    }
}

.printer-item.selected {
    background: rgba(102, 126, 234, 0.15);
    border-color: #667eea;
    animation: borderGlow 2.5s ease-in-out infinite;
}
```

- [ ] **Step 2: Verify animated glow**

Select a printer. The card border should gently pulse between purple-blue and violet hues. Subtle, not distracting.

- [ ] **Step 3: Commit**

```bash
git add frontend/styles.css
git commit -m "feat(ui): animated glow border on selected printer (N)"
```

---

## Task 3: Tooltips for Printer Badges (F)

**Files:**
- Modify: `frontend/styles.css` — add `.badge-tooltip` styles
- Modify: `frontend/app.js` — update `PrinterModule` render to add `data-tooltip` attributes on badges; update polling to maintain them

- [ ] **Step 1: Add tooltip CSS**

In `frontend/styles.css`, add:

```css
/* ── Printer Badge Tooltips (F) ────────────────────────────── */
[data-tooltip] {
    position: relative;
    cursor: help;
}

[data-tooltip]::after {
    content: attr(data-tooltip);
    position: absolute;
    bottom: calc(100% + 8px);
    left: 50%;
    transform: translateX(-50%);
    background: rgba(15, 23, 42, 0.95);
    color: #f1f5f9;
    font-size: 0.75rem;
    line-height: 1.4;
    padding: 0.4rem 0.65rem;
    border-radius: 6px;
    white-space: nowrap;
    max-width: 220px;
    white-space: normal;
    text-align: center;
    box-shadow: 0 4px 12px rgba(0,0,0,0.3);
    border: 1px solid rgba(255,255,255,0.1);
    opacity: 0;
    pointer-events: none;
    transition: opacity 150ms ease 400ms;
    z-index: 9000;
}

[data-tooltip]:hover::after {
    opacity: 1;
}

/* Arrow */
[data-tooltip]::before {
    content: '';
    position: absolute;
    bottom: calc(100% + 3px);
    left: 50%;
    transform: translateX(-50%);
    border: 5px solid transparent;
    border-top-color: rgba(15, 23, 42, 0.95);
    opacity: 0;
    pointer-events: none;
    transition: opacity 150ms ease 400ms;
    z-index: 9000;
}

[data-tooltip]:hover::before {
    opacity: 1;
}

[data-theme="light"] [data-tooltip]::after {
    background: rgba(15, 23, 42, 0.9);
}
```

- [ ] **Step 2: Update printer render template to include data-tooltip on badges**

In `frontend/app.js`, update the printer render template in `PrinterModule.init()`. Replace the badge HTML in the template literal (around lines 132-136):

```js
list.innerHTML = printers.map(p => `
    <div class="printer-item" data-printer='${JSON.stringify(p)}' data-name="${p.name.replace(/"/g, '&quot;')}">
        <div class="printer-info">
            <span class="printer-icon">🖨️</span>
            <div class="printer-details">
                <h3>${p.name}</h3>
                <div class="printer-status">
                    ${this._statusBadge(p.status)}
                    ${p.isDefault
                        ? '<span class="badge badge-info" data-tooltip="Máy in mặc định của Windows">⭐ Mặc định</span>'
                        : ''}
                    ${p.isDuplex
                        ? '<span class="badge badge-success" data-tooltip="Máy in này có thể in 2 mặt tự động">Hỗ trợ 2 mặt</span>'
                        : '<span class="badge badge-warning" data-tooltip="Máy in này chỉ in 1 mặt — dùng chế độ thủ công">Chỉ 1 mặt</span>'}
                </div>
            </div>
        </div>
    </div>
`).join('');
```

Also update the status dot/badge to include offline tooltip:

```js
_statusBadge(status) {
    const map = {
        3: { cls: 'online',  label: 'Sẵn sàng', tip: 'Máy in đang hoạt động bình thường' },
        4: { cls: 'busy',    label: 'Đang in',   tip: 'Máy in đang xử lý lệnh in khác' },
        7: { cls: 'offline', label: 'Offline',   tip: 'Máy in không kết nối. Kiểm tra dây cáp và bật máy.' },
    };
    const s = map[status];
    if (!s) return '<span class="printer-status-dot unknown" data-tooltip="Trạng thái không xác định"></span>';
    return `<span class="printer-status-dot ${s.cls}" data-tooltip="${s.tip}"></span><span class="badge badge-${s.cls === 'online' ? 'success' : s.cls === 'busy' ? 'warning' : 'danger'}" data-tooltip="${s.tip}">${s.label}</span>`;
},
```

- [ ] **Step 3: Verify tooltips**

Hover over "Hỗ trợ 2 mặt" badge — tooltip "Máy in này có thể in 2 mặt tự động" appears after 400ms. Hover "Offline" status dot — tooltip shows "Máy in không kết nối. Kiểm tra dây cáp và bật máy." Tooltip disappears on mouse leave.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): tooltips for printer badges (F)"
```

---

## Task 4: Inline Error Recovery (D)

**Files:**
- Modify: `frontend/styles.css` — add `.card.error` and `.card-error-message` styles
- Modify: `frontend/app.js` — add `showCardError(cardEl, msg, retryFn)` helper; use it in `PrinterModule.init()`, `UploadModule._upload()`, `PrintModule._startPrint()`

- [ ] **Step 1: Add error card CSS**

In `frontend/styles.css`, after `.card:hover` block, add:

```css
/* ── Inline Error Recovery (D) ─────────────────────────────── */
.card.error {
    border-left: 4px solid var(--danger-color);
    background: rgba(239, 68, 68, 0.05);
}

.card-error-message {
    display: flex;
    align-items: center;
    gap: 0.625rem;
    padding: 0.75rem 1rem;
    background: rgba(239, 68, 68, 0.08);
    border: 1px solid rgba(239, 68, 68, 0.25);
    border-radius: 8px;
    margin-top: 0.75rem;
    font-size: 0.875rem;
    color: #fca5a5;
}

.card-error-message .error-icon { font-size: 1rem; flex-shrink: 0; }
.card-error-message .error-text { flex: 1; }

.card-error-retry {
    background: rgba(239, 68, 68, 0.15);
    border: 1px solid rgba(239, 68, 68, 0.35);
    border-radius: 6px;
    color: #fca5a5;
    cursor: pointer;
    font-size: 0.8rem;
    padding: 0.25rem 0.625rem;
    transition: all 0.15s;
    white-space: nowrap;
}

.card-error-retry:hover {
    background: rgba(239, 68, 68, 0.25);
    color: white;
}
```

- [ ] **Step 2: Add showCardError() helper to app.js**

In `frontend/app.js`, add before the BOOTSTRAP section:

```js
// ─── Inline error helper (D) ─────────────────────────────────────
function showCardError(cardEl, message, retryFn) {
    if (!cardEl) return;
    cardEl.classList.add('error');
    // Remove previous error if any
    cardEl.querySelector('.card-error-message')?.remove();
    const errEl = document.createElement('div');
    errEl.className = 'card-error-message';
    errEl.innerHTML = `
        <span class="error-icon">⚠️</span>
        <span class="error-text">${message}</span>
        ${retryFn ? '<button class="card-error-retry">Thử lại</button>' : ''}
    `;
    if (retryFn) {
        errEl.querySelector('.card-error-retry').addEventListener('click', () => {
            clearCardError(cardEl);
            retryFn();
        });
    }
    cardEl.querySelector('.card-body').appendChild(errEl);
}

function clearCardError(cardEl) {
    if (!cardEl) return;
    cardEl.classList.remove('error');
    cardEl.querySelector('.card-error-message')?.remove();
}
```

- [ ] **Step 3: Use showCardError in PrinterModule.init()**

In `frontend/app.js`, in `PrinterModule.init()`, in the catch block (around line 160), replace `showToast(...)` with:

```js
} catch (err) {
    const card = document.querySelector('.card:has(#printer-list)') ||
                 document.getElementById('printer-list')?.closest('.card');
    showCardError(card, `Lỗi tải danh sách máy in: ${err.message}`, () => PrinterModule.init());
}
```

- [ ] **Step 4: Use showCardError in UploadModule._upload()**

In `frontend/app.js`, in `UploadModule._upload()`, in the catch block (around line 277), add inline error:

```js
} catch (err) {
    const card = document.getElementById('upload-area')?.closest('.card');
    showCardError(card, `Lỗi khi tải file: ${err.message}`, () => document.getElementById('file-input')?.click());
    showToast('Lỗi khi tải file: ' + err.message, 'error');
}
```

Also clear the error on successful upload. At the start of `_upload()`:
```js
const card = file ? document.getElementById('upload-area')?.closest('.card') : null;
if (card) clearCardError(card);
```

- [ ] **Step 5: Use showCardError in PrintModule._startPrint()**

In `frontend/app.js`, in `PrintModule._startPrint()`, after `showToast('Loi: ' + result.message, 'error')` in the error branch:

```js
const card = document.getElementById('print-btn')?.closest('.action-section') ||
             document.getElementById('print-btn')?.closest('.card');
// Toast still fires for transient errors; inline error for print failures
if (!result.success) {
    // existing toast
    showToast('Lỗi: ' + result.message, 'error');
    btn.disabled = false;
    btn.textContent = originalText;
    btn.style.opacity = '';
    return;
}
```

And in the catch block:
```js
} catch (err) {
    showToast('Lỗi khi in: ' + err.message, 'error');
    const actionSec = document.querySelector('.action-section');
    showCardError(actionSec, `Lệnh in thất bại: ${err.message}`, () => document.getElementById('print-btn')?.click());
    btn.disabled = false;
    btn.textContent = originalText;
    btn.style.opacity = '';
}
```

- [ ] **Step 6: Verify inline errors**

Simulate a network error (stop backend, try to load printers): Step 1 card should get a red left border and show "Lỗi tải danh sách máy in... [Thử lại]". Click "Thử lại" to retry. Toast no longer the only notification.

- [ ] **Step 7: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): inline error recovery with retry buttons (D)"
```

---

## Task 5: Final Verification

- [ ] **Step 1: Verify all 4 Group 4 features**

- [ ] Printer cards show colored status dot (green/yellow/red) + text badge
- [ ] Selected printer has animated gradient glow border (pulses every 2.5s)
- [ ] Hovering badges shows tooltip after 400ms
- [ ] Network errors on printer load / upload / print show inline card error with "Thử lại" button

- [ ] **Step 2: No regressions**

Verify printer polling still updates status every 30s. Verify print flow still works end-to-end.

- [ ] **Step 3: Commit count**

```bash
git log --oneline -8
```

Should show 4 feature commits from this plan.
