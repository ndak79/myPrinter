# myPrinter UI/UX Improvements — Design Spec

**Date:** 2026-04-04  
**Status:** Approved for implementation  
**Scope:** 25 UI/UX improvements across 5 groups  
**App:** myPrinter — Windows manual duplex printer manager (plain HTML/JS/CSS frontend, .NET 8 backend)

---

## Constraints

- Frontend: plain HTML/JS/CSS, no framework, no bundler, `file://` protocol compatible
- Backend: .NET 8, PdfSharp, Word Interop, Windows-only
- App is **exclusively for manual duplex printers** (NOT auto-duplex)
- Shell: Windows PowerShell (no `&&`, use `;`)
- No ES modules, no npm packages on frontend

---

## Group 1 — Thumbnail & Preview

### 1. Orientation Badge on Sidebar Thumbnails

**Goal:** User can see at a glance if each page is portrait or landscape without opening ZoomModal.

**Design:**
- After canvas renders, compute `aspectRatio = canvas.width / canvas.height`
- If `aspectRatio < 1` → portrait badge (`▯ Portrait`), else landscape (`▭ Landscape`)
- Badge position: top-left corner of thumbnail, semi-transparent background, small font (10px)
- Color: `rgba(0,0,0,0.6)` bg, white text. Does not obstruct page content
- Added in `PreviewModule._createPlaceholder()` and updated after render in `_renderCanvas()`

**Files changed:** `frontend/index.html` (badge HTML), `frontend/app.js` (PreviewModule), `frontend/styles.css` (.orientation-badge)

---

### O. Thumbnail Pop Animation on Select/Deselect

**Goal:** Tactile feedback when user clicks to select/deselect a page.

**Design:**
- On click, add CSS class `.pop` to thumbnail for 200ms, then remove
- `.pop` keyframe: `scale(1) → scale(1.06) → scale(1)` with `ease-out` timing
- Also add brief border glow: `box-shadow` flash on `.selected` state transition
- No JS animation library — pure CSS `@keyframes` + JS class toggle

**Files changed:** `frontend/styles.css` (@keyframes pop), `frontend/app.js` (PageSelectModule.toggle())

---

### J. Hover Preview Popup

**Goal:** See a larger (300×400px) preview of a thumbnail without opening ZoomModal.

**Design:**
- Single shared `#hover-preview` div appended to `<body>`, `position: fixed`, `pointer-events: none`, hidden by default
- Contains a `<canvas id="hover-preview-canvas">` sized 300×400px
- On `mouseenter` of thumbnail: 150ms `setTimeout` → render upscaled copy via `drawImage(sourceCanvas)` into preview canvas
- Position: prefer right of thumbnail, flip left if would overflow viewport right edge; clamp vertically
- On `mouseleave`: clear timer, hide preview with CSS opacity transition (100ms)
- Cache: after first render per page, store offscreen canvas in `PreviewModule._previewCache[pageNum]` — reuse on subsequent hovers
- Keyboard: `tabindex="0"` already on thumbnails; `focus` → show preview, `blur` → hide
- Hide on `window.scroll`

**HTML added:** `<div id="hover-preview" hidden><canvas id="hover-preview-canvas"></canvas></div>`  
**Files changed:** `frontend/index.html`, `frontend/app.js` (new HoverPreviewModule), `frontend/styles.css` (#hover-preview)

---

### I. Drag-to-Reorder Pages

**Goal:** User can drag thumbnail cards to reorder pages before printing.

**Design:**

**Frontend:**
- Pointer events approach (mousedown/mousemove/mouseup + touch equivalents) — NOT HTML5 DnD API
- On `mousedown` on a thumbnail: create ghost clone (opacity 0.7, `position: fixed`) following cursor
- Blue placeholder `div.drop-placeholder` inserted between thumbnails to show drop target
- Target index computed via `getBoundingClientRect()` of each thumbnail vs cursor Y position
- On `mouseup`: insert dragged item at target index in DOM, update `AppState.pageOrder = [...new order]`
- `AppState.pageOrder`: new array `[1,2,3,...N]` initialized to natural order; reordering updates this
- `PreviewModule.updateThumbnails()` reads `AppState.pageOrder` for rendering
- Print payload: include `pageOrder: AppState.pageOrder` as array of 1-based page numbers in desired print order

**Backend (PrintRequest change):**
```csharp
// Add to PrintRequest in PrintModels.cs:
public int[]? PageOrder { get; set; }  // null = natural order
```
- `PrintAlgorithmService`: before building phase pages, if `PageOrder` is provided, reorder the page list accordingly
- Affects all modes: NormalDuplex, Simplex, BookletA5

**Files changed:** `frontend/app.js` (DragReorderModule, AppState), `frontend/styles.css` (.drop-placeholder, .drag-ghost), `backend/Models/PrintModels.cs`, `backend/Services/PrintAlgorithmService.cs`

---

### U. Per-page Rotation (CW90 / CCW90 / Flip H / Flip V / 180°)

**Goal:** User can rotate individual pages before printing to correct scan orientation errors.

**Design:**

**Frontend:**
- Context menu (right-click on thumbnail) gets new submenu: "Xoay trang ▶"
  - ↻ Xoay phải 90° (CW90)
  - ↺ Xoay trái 90° (CCW90)  
  - ↔ Lật ngang (FlipH)
  - ↕ Lật dọc (FlipV)
  - 🔄 Xoay 180°
  - ↩ Reset về gốc
- `AppState.pageRotations = new Map()` — key: pageNum (1-based), value: rotation string
- Thumbnail preview: apply `transform: rotate(Xdeg) scaleX(Y) scaleY(Z)` via CSS immediately for visual feedback
- ZoomModal also applies rotation transform to displayed page

**Backend:**
```csharp
// Add to PrintModels.cs:
public enum RotationDirection { None, CW90, CCW90, Rotate180, FlipHorizontal, FlipVertical }

public class PageRotation {
    public int PageNumber { get; set; }  // 1-based
    public RotationDirection Rotation { get; set; }
}

// Add to PrintRequest:
public List<PageRotation>? PageRotations { get; set; }
```
- `PrintAlgorithmService`: after page selection, apply rotations using `PdfSharp` page rotation API before assembling phases
- `WordInteropService.CreateRotatedPdfSubset()` already exists — extend to accept per-page rotation map

**Files changed:** `frontend/index.html` (context menu HTML), `frontend/app.js` (ContextMenu, AppState), `frontend/styles.css`, `backend/Models/PrintModels.cs`, `backend/Services/PrintAlgorithmService.cs`, `backend/Services/WordInteropService.cs`

---

## Group 2 — Workflow & Navigation

### 2. Step Indicator (1→2→3→4→5)

**Goal:** User sees which step they're on and overall progress at a glance.

**Design:**
- Horizontal progress bar at top of `.main-content`, above the step cards
- 5 steps: Chọn Máy In → Tải File → Chọn Trang → Chế Độ In → In
- Each step: circle with number + label below, connected by line
- Active step: gradient fill + glow. Completed steps: solid green checkmark. Future steps: muted
- Steps auto-advance: Step 1 ✓ when printer selected; Step 2 ✓ when file uploaded; Step 3 ✓ when page range valid; Step 4 always active after step 3; Step 5 active when print-ready
- New module: `StepIndicatorModule.update()` — called by PrinterModule, UploadModule, PageSelectModule, PrintModule after state changes

**Files changed:** `frontend/index.html` (step indicator HTML), `frontend/app.js` (StepIndicatorModule), `frontend/styles.css` (.step-indicator)

---

### 6. Page Range Live Preview

**Goal:** As user types page range, sidebar thumbnails highlight matching pages instantly (no need to press Enter).

**Design:**
- `PageSelectModule` already parses range on input; add `input` event listener (currently only `change`)
- Debounce: 200ms after last keystroke → call `_parseRange()` → update `AppState.selectedPages` → call `PreviewModule.updateThumbnails()`
- Show real-time count: `#inline-page-count` and `#inline-selected` update live
- Invalid range: input border turns red, count shows "—" (no crash)
- `AppState.isUserTypingPageRange` flag already exists — use it

**Files changed:** `frontend/app.js` (PageSelectModule.init() — add input event + debounce)

---

### B. Keyboard Navigation for Thumbnail Grid

**Goal:** Power users can navigate and select pages with keyboard only.

**Design:**
- Each thumbnail already has implicit focus via `tabindex` (to be ensured)
- Add `keydown` handlers on `.sidebar-preview-grid`:
  - `ArrowDown` / `ArrowUp` → move focus to next/prev thumbnail
  - `Space` / `Enter` → toggle selection (same as click)
  - `Shift+A` → select all (already mapped globally, ensure works when sidebar focused)
  - `Escape` → deselect all
- Visual focus ring: `outline: 2px solid #667eea; outline-offset: 2px` on focused thumbnail
- Grid uses `role="listbox"` + each thumbnail `role="option"` + `aria-selected`

**Files changed:** `frontend/app.js` (KeyboardModule + PreviewModule), `frontend/styles.css` (focus styles)

---

### C. aria-live Status Messages

**Goal:** Screen reader users get notified of state changes without interrupting workflow.

**Design:**
- Add single hidden `<div id="sr-status" aria-live="polite" aria-atomic="true" class="sr-only"></div>` to `index.html`
- `SRModule.announce(msg)` sets `textContent` then clears after 1s
- Call from: file upload complete, page selection change, printer selected, print job started/complete, flip modal open
- `sr-only` CSS: `position:absolute; width:1px; height:1px; overflow:hidden; clip:rect(0,0,0,0)`

**Files changed:** `frontend/index.html`, `frontend/app.js` (new SRModule), `frontend/styles.css` (.sr-only)

---

### T. Skeleton Loading State

**Goal:** Replace "Đang tải..." text with animated skeleton placeholders for better perceived performance.

**Design:**
- Printer list: while loading → show 2-3 skeleton printer rows (gray pulse rectangles)
- Thumbnail sidebar: while PDF rendering → show skeleton thumbnail cards (gray rectangles with pulse)
- CSS: `.skeleton` class with `background: linear-gradient(90deg, #1e293b 25%, #334155 50%, #1e293b 75%)` + `background-size: 200%` + `animation: skeleton-pulse 1.5s infinite`
- Replace `.printer-list` initial HTML with skeleton HTML; replace with real content when loaded
- Replace each thumbnail placeholder with skeleton card while canvas renders

**Files changed:** `frontend/index.html`, `frontend/app.js` (PrinterModule, PreviewModule), `frontend/styles.css` (.skeleton, @keyframes skeleton-pulse)

---

## Group 3 — Print Flow

### 3. Improved Flip Modal

**Goal:** Manual duplex flip instruction is clearer, more visual, with step checklist.

**Design:**
- Current flip modal has: instruction visual SVG, text, continue button
- Upgrade:
  - **3-step checklist** inside modal:
    1. ☐ Chờ máy in xong mặt trước
    2. ☐ Lấy giấy ra — theo đúng hướng mũi tên
    3. ☐ Đặt lại vào khay — mặt trắng ngửa lên
  - Checkboxes are tappable (user checks them off for confidence)
  - **Animated flip diagram**: CSS animation showing paper being flipped (simple SVG animation, not static)
  - **Optional countdown timer**: 30s countdown shown, with Skip button. Timer is off by default, user can enable via checkbox "Tự động tiếp tục sau 30 giây"
  - Continue button only fully-bright when all 3 checkboxes checked (still clickable without, but dim as reminder)
  
**Files changed:** `frontend/index.html` (flip modal HTML), `frontend/app.js` (PrintModule._showFlipModal()), `frontend/styles.css` (flip modal animations)

---

### 7. Print Confirmation Dialog

**Goal:** Prevent accidental prints by showing a summary before sending job.

**Design:**
- New modal `#confirm-print-modal` shown when user clicks "Bắt Đầu In"
- Summary shows:
  - 📄 File: `filename.pdf`
  - 🖨️ Máy in: `PrinterName`
  - 📋 Chế độ: `In 2 Mặt / In 1 Mặt / Sách A5`
  - 📖 Trang: `1-3, 5` (X trang được chọn)
  - 📃 Số tờ giấy: Y tờ
  - 🔢 Số bản: Z
  - ⏱️ Thời gian ước tính: ~N phút
- Two buttons: "Xác Nhận In" (primary) and "Huỷ" (secondary)
- Keyboard: Enter → confirm, Escape → cancel
- Focus trapped in modal when open

**Files changed:** `frontend/index.html`, `frontend/app.js` (PrintModule, new ConfirmPrintModal), `frontend/styles.css`

---

### G. Estimated Print Time in Summary

**Goal:** User knows roughly how long to wait before the job finishes.

**Design:**
- `SummaryModule.update()` already computes sheets and copies
- Add time estimate: `estimatedSeconds = sheets * copies * 15` (15s per sheet as default)
- Format: `< 1 min` / `~X phút` / `~X phút Y giây`
- Display in `#print-summary` inline with other stats
- Also shown in Confirmation Dialog (#7 above)

**Files changed:** `frontend/app.js` (SummaryModule.update())

---

### A. Cancel/Abort Print

**Goal:** User can cancel an ongoing print job without going to Windows Print Queue.

**Design:**
- While job is running (`AppState.currentJob` exists), "Bắt Đầu In" button changes to "Huỷ In" (red, with × icon)
- Click "Huỷ In" → call `DELETE /api/print/cancel` with `{ jobId: currentJob.jobId }`
- Backend: new endpoint `DELETE /api/print/cancel` — calls Windows `CancelPrintJob` via WMI or cancels the pending print job
- On success: reset `AppState.currentJob`, reset print button, show toast "Đã huỷ lệnh in"
- If job already completed: show toast "Lệnh in đã hoàn tất, không thể huỷ"

**Backend change:** New endpoint `DELETE /api/print/cancel` in `Program.cs` + cancel logic in `PrintAlgorithmService` or new `PrintJobManagerService`

**Files changed:** `frontend/app.js` (PrintModule), `backend/Program.cs`, `backend/Services/PrintAlgorithmService.cs` (or new service)

---

## Group 4 — Printer & Status

### 5. Clearer Printer Status

**Goal:** Printer selection is more prominent; status is unmistakable.

**Design:**
- Status indicator: replace small text badge with colored dot (16px circle) left of printer name
  - 🟢 Online, 🔴 Offline, 🟡 Unknown/Busy
- Selected printer card: larger left border (4px solid gradient) + subtle background glow
- Default printer badge: "⭐ Mặc định" moved to be more prominent
- Duplex support badge: show/hide based on `supportsDuplex` flag from API
- Auto-refresh every 30s already implemented — add animated "refreshing" state (spinner on refresh)

**Files changed:** `frontend/app.js` (PrinterModule._statusBadge(), render()), `frontend/styles.css` (.printer-status-dot, .printer-item.selected)

---

### N. Printer Selected Glow Animation

**Goal:** Selected printer card has an animated glowing border that feels premium.

**Design:**
- `.printer-item.selected`: animated gradient border using `@keyframes borderGlow`
- Technique: `border: 2px solid transparent` + `background-clip: padding-box` + `::after` pseudo-element with animated gradient + `border-radius` mask
- Animation: gradient rotates 360° over 3s, creating a "scanning" glow effect
- Subtle, not distracting — amplitude small

**Files changed:** `frontend/styles.css` (.printer-item.selected, @keyframes borderGlow)

---

### F. Tooltips for Printer Badges

**Goal:** Hovering printer status badge shows explanation + action hint.

**Design:**
- Pure CSS tooltips using `::after` pseudo-element on badge elements
- Badge `title` attributes replaced with custom CSS tooltips (better styling control)
- Tooltip content:
  - 🟢 Online: "Máy in đang hoạt động"
  - 🔴 Offline: "Máy in không kết nối. Kiểm tra dây cáp và bật máy."
  - "Hỗ trợ 2 mặt": "Máy in này có thể in 2 mặt tự động"
  - "Mặc định": "Máy in mặc định của Windows"
- Tooltip appears after 400ms hover, fades in 150ms
- `role="tooltip"` + `aria-describedby` for accessibility

**Files changed:** `frontend/styles.css` (.badge-tooltip), `frontend/app.js` (PrinterModule — add aria attributes)

---

### D. Inline Error Recovery

**Goal:** When a step fails, error is shown inline at that step — not just a fleeting toast.

**Design:**
- Step card gets `.error` state: left border turns red, error message appears inside card body
- Error messages with inline action:
  - Printer offline → "Máy in offline. [Thử lại]" (retry polls printer again)
  - Upload failed → "Upload thất bại. [Thử lại]" (retry upload)
  - Print failed → "In thất bại: [error message]. [Thử lại]" (retry print)
- `.card.error` CSS: `border-left: 4px solid var(--danger-color)` + red-tinted background
- `.card-error-message` component: icon + message + retry button
- Toast still shown for transient notifications; inline error for persistent failures requiring action

**Files changed:** `frontend/app.js` (PrinterModule, UploadModule, PrintModule), `frontend/styles.css` (.card.error, .card-error-message)

---

## Group 5 — History & UI Polish

### 4. Per-item History Delete

**Goal:** User can remove individual history entries without clearing all.

**Design:**
- Each history item row gets a `×` delete button (right side, appears on hover)
- `HistoryModule._render()`: adds `<button class="history-delete-btn" data-index="N">×</button>` per item
- Click handler: `HistoryModule.removeItem(index)` → splice from array → `_save()` → `_render()`
- Button hidden by default (`opacity: 0`), visible on row hover (`opacity: 1`), smooth transition
- "Xoá tất cả" button remains for bulk clear

**Files changed:** `frontend/app.js` (HistoryModule), `frontend/styles.css` (.history-delete-btn)

---

### E. Reprint from History

**Goal:** User can re-use a previous print job settings with one click.

**Design:**
- Each history item gets a 🔁 "In lại" button (left of the × button)
- Click → populate AppState with saved history data: printer, mode, copies, pageRange
- If file still exists in session (same `fileId`): restore fully and enable print button
- If file not found: show message "File không còn trong session. Vui lòng upload lại."
- History entry schema extended: add `fileId`, `printerName`, `mode`, `pageRange`, `copies` fields (already partially stored)

**Files changed:** `frontend/app.js` (HistoryModule, PrintModule, UploadModule), `frontend/styles.css` (.history-reprint-btn)

---

### K. Card Entrance Animation (Staggered)

**Goal:** App feels alive on load with cards animating in sequentially.

**Design:**
- On `DOMContentLoaded`, each `.card` gets `animation: cardEntrance 400ms ease-out forwards`
- Cards delayed: `animation-delay: N * 80ms` (card 1: 0ms, card 2: 80ms, ..., card 5: 320ms)
- `@keyframes cardEntrance`: `opacity: 0, translateY(20px)` → `opacity: 1, translateY(0)`
- Runs once on page load. No repeat on re-render.

**Files changed:** `frontend/styles.css` (@keyframes cardEntrance), `frontend/index.html` (data-animate-delay attributes)

---

### L. Print Button Ripple Effect

**Goal:** Premium tactile feedback when clicking the print button.

**Design:**
- On click: create `<span class="ripple">` element, position at click coordinates relative to button, animate `scale(0→3)` + `opacity(0.4→0)` over 500ms, then remove element
- CSS: `.ripple { position: absolute; border-radius: 50%; background: rgba(255,255,255,0.3); animation: ripple 500ms ease-out forwards }`
- Button needs `position: relative; overflow: hidden`
- During print (button shows spinner): ripple fires once then spinner takes over

**Files changed:** `frontend/app.js` (PrintModule — ripple handler), `frontend/styles.css` (.ripple, @keyframes ripple)

---

### M. Progress Bar Shimmer Animation

**Goal:** Upload progress bar looks active rather than static while processing.

**Design:**
- Add shimmer overlay on `#upload-progress-bar` during active upload
- CSS: `::after` pseudo-element with `background: linear-gradient(90deg, transparent 30%, rgba(255,255,255,0.2) 50%, transparent 70%)` + `animation: shimmer 1.5s infinite`
- `@keyframes shimmer`: `background-position: -200% 0` → `200% 0`
- Shimmer only active when `.uploading` class on progress bar; removed when complete

**Files changed:** `frontend/styles.css` (@keyframes shimmer, #upload-progress-bar.uploading::after), `frontend/app.js` (UploadModule — add/remove .uploading)

---

### P. Mode Card Selection Animation

**Goal:** Switching print mode has a visual transition, not just a color change.

**Design:**
- `.mode-card` (In 2 Mặt / In 1 Mặt / Sách A5) on select: scale up slightly + icon pulse
- `@keyframes modeSelect`: `scale(1) → scale(1.04) → scale(1)` over 200ms
- Icon inside selected card: brief rotation or pulse animation (`@keyframes iconPulse`)
- Deselected cards: fade slightly (`opacity: 0.7`)

**Files changed:** `frontend/styles.css` (@keyframes modeSelect, .mode-card.selected), `frontend/app.js` (mode selection handler)

---

### Q. Frosted Glass Sidebar Enhancement

**Goal:** Sidebar looks like premium macOS-style frosted glass.

**Design:**
- Current: `background: var(--bg-secondary)` (solid `#1e293b`)
- New: `background: rgba(15, 23, 42, 0.75); backdrop-filter: blur(20px) saturate(180%); -webkit-backdrop-filter: blur(20px) saturate(180%)`
- Add subtle noise texture overlay: `::before` with `background-image: url("data:image/svg+xml,...")` at 3% opacity
- Border-right: `border-right: 1px solid rgba(255,255,255,0.08)` (lighter, more glass-like)
- Only applies when `backdrop-filter` is supported (progressive enhancement)

**Files changed:** `frontend/styles.css` (.sidebar)

---

### S. Toast Notification Upgrade

**Goal:** Toast notifications are more polished with slide-in animation, countdown, and icons.

**Design:**
- Current toast: simple fade. New toast:
  - **Slide in** from right: `translateX(120%) → translateX(0)` over 250ms
  - **Icon**: animated checkmark (SVG stroke animation) for success, × for error, ℹ for info
  - **Countdown bar**: thin progress bar at bottom of toast depletes over duration (3s default)
  - Multiple toasts stack (max 3), older ones slide up
- New `ToastModule.show(message, type, duration)` replaces current implementation
- Each toast is a separate `div.toast-item` appended to `#toast-container`
- Auto-dismiss after `duration` ms; click to dismiss early

**Files changed:** `frontend/index.html` (toast container), `frontend/app.js` (ToastModule rewrite), `frontend/styles.css` (.toast-item, @keyframes toastSlideIn, .toast-countdown)

---

### H. Theme Transition Animation

**Goal:** Switching between dark/light/auto theme has a smooth fade instead of instant jump.

**Design:**
- Add `transition: background-color 300ms ease, color 300ms ease, border-color 300ms ease` to `:root` or `body`
- Also transition `background` on `.card`, `.sidebar`, `.printer-item`, `.modal-content`
- Exclude properties that would look odd with transition (e.g., `box-shadow` during transition may flash)
- For theme toggle buttons: add brief highlight animation on active button click

**Files changed:** `frontend/styles.css` (transition declarations on key elements)

---

## Backend Changes Summary

| Feature | Model Change | Endpoint Change | Service Change |
|---------|-------------|----------------|----------------|
| I. Drag-to-reorder | `PrintRequest.PageOrder: int[]?` | None (add to existing POST /api/print payload) | `PrintAlgorithmService` — reorder pages before phase building |
| U. Per-page rotation | `PrintRequest.PageRotations: List<PageRotation>?` + `PageRotation` class + `RotationDirection` enum | None | `PrintAlgorithmService` + `WordInteropService.CreateRotatedPdfSubset()` |
| A. Cancel print | None | NEW: `DELETE /api/print/cancel` | New cancel logic (WMI CancelPrintJob) |

---

## Implementation Order (Recommended)

1. **Group 5 first** (K, H, M, Q, S) — pure CSS/animation, no logic, easy wins, high visual impact
2. **Group 2** (2, 6, C, T) — navigation/workflow improvements, no backend needed
3. **Group 4** (5, N, F, D) — printer UI improvements, no backend needed  
4. **Group 3** (3, 7, G, A) — print flow improvements (A needs backend)
5. **Group 1** (1, O, J, B) — thumbnail improvements (I, U need backend)

Backend changes (I, U, A) can be developed in parallel with frontend Groups 2-4.

---

## Testing Checklist

- [ ] All 25 features work in Chrome on Windows (primary target)
- [ ] Dark and light themes both look correct after changes
- [ ] Keyboard navigation works for thumbnails, modals, step indicator
- [ ] Screen reader announces key state changes (aria-live)
- [ ] No console errors on file:// protocol
- [ ] Backend: `dotnet test` passes (existing 82 tests) + new tests for PageOrder/PageRotations/Cancel
- [ ] Print jobs still work correctly for all 3 modes after backend changes
