# Layout Redesign — Option A + Multi-File Tabs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current dark-theme 5-step wizard + modal layout with a persistent 3-zone app shell: 2-row header (settings + file tabs), left vertical thumbnail strip, right main preview area. Light mode macOS visual style.

**Architecture:** `index.html` restructured into `.app-shell` with `#header` (2 rows), `#thumb-panel` (left, fixed 180px), `#preview-panel` (right, flex-grow). The 5-step wizard controls are inlined into the header Row 1 as compact controls. The print preview modal is eliminated — preview is always visible. CSS rewritten in `styles.css` and `sidebar-styles.css` to macOS light theme. Dark theme variables are replaced.

**Tech Stack:** Vanilla JS, CSS custom properties, Flexbox/Grid, `-apple-system` font stack, macOS color palette

**Prerequisite:** Multi-file AppState plan (`2026-04-05-multi-file-state.md`) must be complete — this plan assumes `TabsModule`, `AppState.files[]`, and `PrintPreviewModule.jumpToFile()` exist.

---

## Visual Specification

```
┌─────────────────────────────────────────────────────────────────────┐
│ ROW 1 (44px): [🖨 Máy in ▼]  [⚙ Chế độ ▼]  [📋 Trang ▼]  spacer  [🖨 In] │
├─────────────────────────────────────────────────────────────────────┤
│ ROW 2 (36px): [📄 file1.pdf ×] [📄 file2.docx ×] [+ Thêm file]          │
├──────────────┬──────────────────────────────────────────────────────┤
│  THUMB PANEL │  PREVIEW PANEL                                       │
│  (180px)     │  (flex: 1, scrollable)                               │
│              │                                                      │
│  ┌────────┐  │   ┌──────────────────────────────┐                  │
│  │  [1]   │  │   │                              │                  │
│  └────────┘  │   │    Page 1 — full width       │                  │
│  ┌────────┐  │   │                              │                  │
│  │  [2]   │  │   └──────────────────────────────┘                  │
│  └────────┘  │                                                      │
│   ···        │   ┌──────────────────────────────┐                  │
│              │   │    Page 2                    │                  │
│              │   └──────────────────────────────┘                  │
└──────────────┴──────────────────────────────────────────────────────┘
```

## macOS Light Color Palette

```css
--color-bg:           #F5F5F7;   /* macOS window background */
--color-surface:      #FFFFFF;   /* panels, cards */
--color-surface-alt:  #F2F2F7;   /* alternate rows, hover */
--color-border:       #D1D1D6;   /* separator lines */
--color-border-strong:#C7C7CC;   /* active borders */
--color-accent:       #0071E3;   /* macOS blue */
--color-accent-hover: #0077ED;
--color-accent-light: #E8F1FD;   /* selected page tint */
--color-text-primary: #1D1D1F;   /* main text */
--color-text-secondary:#6E6E73;  /* secondary labels */
--color-text-muted:   #AEAEB2;   /* placeholder, disabled */
--color-danger:       #FF3B30;   /* macOS red */
--color-success:      #34C759;   /* macOS green */
--color-warning:      #FF9500;   /* macOS orange */
--header-height-r1:   44px;
--header-height-r2:   36px;
--thumb-panel-width:  180px;
--font-system:        -apple-system, "SF Pro Display", "Segoe UI", sans-serif;
--shadow-sm:          0 1px 3px rgba(0,0,0,.08), 0 1px 2px rgba(0,0,0,.06);
--shadow-md:          0 4px 12px rgba(0,0,0,.10), 0 2px 4px rgba(0,0,0,.06);
--radius-sm:          6px;
--radius-md:          10px;
--radius-lg:          14px;
```

---

## File Map

| File | Change |
|------|--------|
| `frontend/index.html` | Full restructure: app-shell layout, inline controls in header, remove modal overlay |
| `frontend/styles.css` | Replace dark glassmorphism with macOS light palette and app-shell layout rules |
| `frontend/sidebar-styles.css` | Remove modal-specific CSS; add thumb-panel and preview-panel persistent layout |
| `frontend/app.js` | Remove `PrintPreviewModule.open()/close()` modal behavior; preview is always shown |
| `frontend/app.js` | Move printer/mode/pages selectors into header DOM; update all `getElementById` references |

---

## Task 1: CSS foundations — macOS light theme variables

**Files:**
- Modify: `frontend/styles.css` (replace :root variables block)

- [ ] **Step 1: Replace CSS custom properties in :root**

Find the `:root` block at the top of `frontend/styles.css`. Replace the entire `:root { }` block with:

```css
:root {
  /* Colors */
  --color-bg:            #F5F5F7;
  --color-surface:       #FFFFFF;
  --color-surface-alt:   #F2F2F7;
  --color-border:        #D1D1D6;
  --color-border-strong: #C7C7CC;
  --color-accent:        #0071E3;
  --color-accent-hover:  #0077ED;
  --color-accent-light:  #E8F1FD;
  --color-text-primary:  #1D1D1F;
  --color-text-secondary:#6E6E73;
  --color-text-muted:    #AEAEB2;
  --color-danger:        #FF3B30;
  --color-success:       #34C759;
  --color-warning:       #FF9500;

  /* Layout */
  --header-height-r1:    44px;
  --header-height-r2:    36px;
  --thumb-panel-width:   180px;
  --header-total-height: calc(var(--header-height-r1) + var(--header-height-r2));

  /* Typography */
  --font-system: -apple-system, "SF Pro Display", "Segoe UI", system-ui, sans-serif;
  --font-mono:   "SF Mono", "Cascadia Code", monospace;

  /* Elevation */
  --shadow-sm: 0 1px 3px rgba(0,0,0,.08), 0 1px 2px rgba(0,0,0,.06);
  --shadow-md: 0 4px 12px rgba(0,0,0,.10), 0 2px 4px rgba(0,0,0,.06);

  /* Shape */
  --radius-sm: 6px;
  --radius-md: 10px;
  --radius-lg: 14px;
}

/* Global reset */
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: var(--font-system);
  background:  var(--color-bg);
  color:       var(--color-text-primary);
  height:      100vh;
  overflow:    hidden; /* app shell — no body scroll */
  -webkit-font-smoothing: antialiased;
}
```

- [ ] **Step 2: Add app-shell layout rules to styles.css**

Append to `frontend/styles.css`:

```css
/* ── App Shell ────────────────────────────────────────────── */
.app-shell {
  display:        flex;
  flex-direction: column;
  height:         100vh;
  overflow:       hidden;
}

/* ── Header Row 1: Settings bar ────────────────────────────── */
.header-settings {
  display:          flex;
  align-items:      center;
  gap:              8px;
  padding:          0 16px;
  height:           var(--header-height-r1);
  background:       rgba(255,255,255,0.85);
  backdrop-filter:  blur(20px) saturate(180%);
  -webkit-backdrop-filter: blur(20px) saturate(180%);
  border-bottom:    1px solid var(--color-border);
  flex-shrink:      0;
  z-index:          100;
}

.header-spacer { flex: 1; }

/* Compact select controls in settings row */
.header-select {
  height:           28px;
  padding:          0 8px;
  border:           1px solid var(--color-border);
  border-radius:    var(--radius-sm);
  background:       var(--color-surface);
  color:            var(--color-text-primary);
  font-family:      var(--font-system);
  font-size:        13px;
  cursor:           pointer;
  max-width:        180px;
  appearance:       none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='6'%3E%3Cpath d='M0 0l5 6 5-6z' fill='%236E6E73'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 8px center;
  padding-right:    24px;
}
.header-select:focus {
  outline:          2px solid var(--color-accent);
  outline-offset:   -1px;
}

/* Print button */
.btn-print {
  height:           28px;
  padding:          0 14px;
  background:       var(--color-accent);
  color:            #fff;
  border:           none;
  border-radius:    var(--radius-sm);
  font-size:        13px;
  font-weight:      500;
  cursor:           pointer;
  white-space:      nowrap;
  font-family:      var(--font-system);
  transition:       background 0.15s;
}
.btn-print:hover:not(:disabled)  { background: var(--color-accent-hover); }
.btn-print:disabled               { opacity: 0.4; cursor: not-allowed; }

/* ── Header Row 2: File tabs ────────────────────────────────── */
.file-tabs-bar {
  display:          flex;
  align-items:      center;
  gap:              2px;
  padding:          0 12px;
  height:           var(--header-height-r2);
  background:       var(--color-surface-alt);
  border-bottom:    1px solid var(--color-border);
  overflow-x:       auto;
  overflow-y:       hidden;
  flex-shrink:      0;
  scrollbar-width:  none;
}
.file-tabs-bar::-webkit-scrollbar { display: none; }

.file-tab {
  display:          flex;
  align-items:      center;
  gap:              4px;
  height:           26px;
  padding:          0 10px 0 8px;
  border:           1px solid transparent;
  border-radius:    var(--radius-sm);
  background:       transparent;
  color:            var(--color-text-secondary);
  font-size:        12px;
  font-family:      var(--font-system);
  cursor:           pointer;
  white-space:      nowrap;
  max-width:        180px;
  transition:       background 0.1s, color 0.1s;
  position:         relative;
}
.file-tab:hover {
  background:       var(--color-surface);
  color:            var(--color-text-primary);
}
.file-tab.active {
  background:       var(--color-surface);
  color:            var(--color-text-primary);
  border-color:     var(--color-border);
  box-shadow:       var(--shadow-sm);
}
.file-tab-icon  { font-size: 11px; flex-shrink: 0; }
.file-tab-label { overflow: hidden; text-overflow: ellipsis; }
.file-tab-close {
  display:          flex;
  align-items:      center;
  justify-content:  center;
  width:            14px;
  height:           14px;
  border:           none;
  background:       transparent;
  border-radius:    50%;
  font-size:        12px;
  line-height:      1;
  color:            var(--color-text-muted);
  cursor:           pointer;
  flex-shrink:      0;
  padding:          0;
  margin-left:      2px;
}
.file-tab-close:hover { background: var(--color-border); color: var(--color-text-primary); }

.file-tab-add {
  height:           24px;
  padding:          0 10px;
  border:           1px dashed var(--color-border);
  border-radius:    var(--radius-sm);
  background:       transparent;
  color:            var(--color-text-secondary);
  font-size:        12px;
  font-family:      var(--font-system);
  cursor:           pointer;
  white-space:      nowrap;
  margin-left:      4px;
  transition:       all 0.15s;
}
.file-tab-add:hover {
  background:       var(--color-surface);
  border-color:     var(--color-accent);
  color:            var(--color-accent);
}

/* ── Body: thumb panel + preview panel ─────────────────────── */
.app-body {
  display:          flex;
  flex:             1;
  overflow:         hidden;
  min-height:       0;
}

/* ── Thumbnail Panel (left, vertical) ──────────────────────── */
.thumb-panel {
  width:            var(--thumb-panel-width);
  flex-shrink:      0;
  background:       var(--color-surface);
  border-right:     1px solid var(--color-border);
  display:          flex;
  flex-direction:   column;
  overflow:         hidden;
}
.thumb-panel-inner {
  flex:             1;
  overflow-y:       auto;
  padding:          8px 6px;
  display:          flex;
  flex-direction:   column;
  gap:              4px;
  scrollbar-width:  thin;
  scrollbar-color:  var(--color-border) transparent;
}

/* File section divider inside thumb strip */
.thumb-file-divider {
  font-size:        10px;
  font-weight:      600;
  color:            var(--color-text-muted);
  text-transform:   uppercase;
  letter-spacing:   0.04em;
  padding:          8px 4px 2px;
  border-top:       1px solid var(--color-border);
  margin-top:       4px;
  overflow:         hidden;
  text-overflow:    ellipsis;
  white-space:      nowrap;
}
.thumb-file-divider:first-child { border-top: none; margin-top: 0; padding-top: 2px; }

/* Individual thumbnail */
.thumb-item {
  position:         relative;
  border-radius:    var(--radius-sm);
  overflow:         hidden;
  border:           2px solid transparent;
  cursor:           pointer;
  background:       var(--color-surface-alt);
  transition:       border-color 0.12s, box-shadow 0.12s;
  aspect-ratio:     0.707; /* A4 ratio */
}
.thumb-item canvas {
  display:          block;
  width:            100%;
  height:           auto;
}
.thumb-item:hover  { border-color: var(--color-border-strong); }
.thumb-item.active { border-color: var(--color-accent); box-shadow: 0 0 0 1px var(--color-accent); }
.thumb-item.selected-for-print {
  background:       var(--color-accent-light);
  border-color:     var(--color-accent);
}
.thumb-item-label {
  position:         absolute;
  bottom:           3px;
  right:            4px;
  background:       rgba(0,0,0,0.55);
  color:            #fff;
  font-size:        10px;
  font-weight:      600;
  padding:          1px 5px;
  border-radius:    3px;
  pointer-events:   none;
}

/* ── Preview Panel (right, scrollable) ─────────────────────── */
.preview-panel {
  flex:             1;
  overflow-y:       auto;
  background:       var(--color-bg);
  padding:          24px;
  display:          flex;
  flex-direction:   column;
  gap:              20px;
  scrollbar-width:  thin;
  scrollbar-color:  var(--color-border) transparent;
}

.preview-page-card {
  background:       var(--color-surface);
  border-radius:    var(--radius-md);
  box-shadow:       var(--shadow-sm);
  overflow:         hidden;
  margin:           0 auto;
  max-width:        800px;
  width:            100%;
}
.preview-page-card canvas {
  display:          block;
  width:            100%;
  height:           auto;
}

/* Empty state */
.preview-empty {
  flex:             1;
  display:          flex;
  flex-direction:   column;
  align-items:      center;
  justify-content:  center;
  color:            var(--color-text-muted);
  gap:              12px;
  font-size:        15px;
}
.preview-empty-icon { font-size: 48px; }

/* ── Utility: upload drop overlay ───────────────────────────── */
.upload-drop-hint {
  position:         fixed;
  inset:            0;
  background:       rgba(0,113,227,.08);
  border:           2px dashed var(--color-accent);
  border-radius:    var(--radius-lg);
  z-index:          200;
  pointer-events:   none;
  display:          none;
}
.upload-drop-hint.visible { display: flex; align-items: center; justify-content: center; }

/* ── Toast notifications ────────────────────────────────────── */
.toast-container {
  position:         fixed;
  bottom:           24px;
  right:            24px;
  display:          flex;
  flex-direction:   column;
  gap:              8px;
  z-index:          9999;
}
.toast {
  padding:          10px 16px;
  border-radius:    var(--radius-md);
  font-size:        13px;
  font-weight:      500;
  box-shadow:       var(--shadow-md);
  animation:        toast-in 0.2s ease;
  max-width:        320px;
  background:       var(--color-surface);
  color:            var(--color-text-primary);
  border-left:      3px solid var(--color-accent);
}
.toast.success { border-color: var(--color-success); }
.toast.error   { border-color: var(--color-danger); }
.toast.info    { border-color: var(--color-accent); }
@keyframes toast-in {
  from { transform: translateX(20px); opacity: 0; }
  to   { transform: translateX(0);    opacity: 1; }
}

/* ── Modals (manual flip, zoom, confirm print) ──────────────── */
.modal-overlay {
  position:         fixed;
  inset:            0;
  background:       rgba(0,0,0,0.3);
  backdrop-filter:  blur(4px);
  display:          flex;
  align-items:      center;
  justify-content:  center;
  z-index:          500;
}
.modal-overlay.hidden { display: none; }

.modal-box {
  background:       var(--color-surface);
  border-radius:    var(--radius-lg);
  box-shadow:       var(--shadow-md);
  padding:          28px;
  max-width:        480px;
  width:            90%;
}
```

- [ ] **Step 3: Verify no dark-mode :root leaks**

Open the app in browser. Confirm the background is light (`#F5F5F7`), not dark. Console: `getComputedStyle(document.documentElement).getPropertyValue('--color-bg').trim()` should return `#F5F5F7`.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css
git commit -m "style: macOS light theme variables and app-shell CSS layout"
```

---

## Task 2: Restructure index.html to app-shell

**Files:**
- Modify: `frontend/index.html` — full restructure

- [ ] **Step 1: Replace body content with app-shell structure**

Replace the entire `<body>` content of `frontend/index.html` with:

```html
<body>
<div class="app-shell">

  <!-- ── Row 1: Settings bar ────────────────────────────── -->
  <header class="header-settings">
    <!-- Printer selector -->
    <select id="printer-select" class="header-select" aria-label="Chọn máy in">
      <option value="">🖨 Chọn máy in…</option>
    </select>

    <!-- Print mode selector -->
    <select id="mode-select" class="header-select" aria-label="Chế độ in">
      <option value="duplex">2 mặt thường</option>
      <option value="simplex">1 mặt</option>
      <option value="booklet">Booklet A5</option>
    </select>

    <!-- Page range -->
    <input id="page-range-input"
           class="header-select"
           type="text"
           placeholder="Trang: 1-∞"
           aria-label="Chọn trang"
           style="width:110px; padding-right:8px; background-image:none;">

    <div class="header-spacer"></div>

    <!-- Print button -->
    <button id="print-btn" class="btn-print" disabled>🖨 In</button>
  </header>

  <!-- ── Row 2: File tabs ───────────────────────────────── -->
  <div id="file-tabs" class="file-tabs-bar" role="tablist" aria-label="Các file đang mở">
    <!-- Rendered by TabsModule -->
  </div>

  <!-- ── Body ──────────────────────────────────────────── -->
  <div class="app-body">

    <!-- Left: thumbnail strip -->
    <nav class="thumb-panel" aria-label="Danh sách trang">
      <div id="thumb-strip" class="thumb-panel-inner">
        <!-- Rendered by ThumbStripModule -->
        <div class="preview-empty">
          <span class="preview-empty-icon">📄</span>
          <span>Chưa có file</span>
        </div>
      </div>
    </nav>

    <!-- Right: preview area -->
    <main id="preview-panel" class="preview-panel" aria-label="Xem trước tài liệu">
      <div class="preview-empty">
        <span class="preview-empty-icon">🖨</span>
        <span>Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong></span>
      </div>
    </main>

  </div><!-- /.app-body -->

</div><!-- /.app-shell -->

<!-- ── Hidden file input ──────────────────────────────── -->
<input type="file" id="file-input" multiple
       accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
       style="display:none">

<!-- ── Upload drop overlay ───────────────────────────── -->
<div class="upload-drop-hint" id="drop-hint">
  <span style="font-size:32px">📄</span>
  <span style="font-size:18px; color:var(--color-accent); margin-left:12px">Thả file vào đây</span>
</div>

<!-- ── Toast container ───────────────────────────────── -->
<div id="toast-container" class="toast-container"></div>

<!-- ── Modals (flip guide, zoom, confirm) ────────────── -->
<div id="flip-modal"    class="modal-overlay hidden"><!-- existing flip modal content --></div>
<div id="zoom-modal"    class="modal-overlay hidden"><!-- existing zoom modal content --></div>
<div id="confirm-modal" class="modal-overlay hidden"><!-- existing confirm modal content --></div>

<!-- ── Scripts ───────────────────────────────────────── -->
<script type="module">
  import * as pdfjsLib from 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.min.mjs';
  window.pdfjsLib = pdfjsLib;
</script>
<script src="app.js"></script>
</body>
```

**Note:** Copy the existing flip-modal, zoom-modal, and confirm-modal inner HTML from the old `index.html` into the placeholders above. Do NOT remove them — they are still needed.

- [ ] **Step 2: Open in browser and verify layout**

Open the app. Verify:
- 2-row header visible at top (settings row + empty tabs row)
- Left panel visible (180px, light background, "Chưa có file" placeholder)
- Right panel fills remaining space with empty state message
- No horizontal scroll on the body

- [ ] **Step 3: Commit**

```bash
git add frontend/index.html
git commit -m "feat(layout): app-shell HTML structure — 2-row header + thumb panel + preview panel"
```

---

## Task 3: Wire up header controls to existing modules

**Files:**
- Modify: `frontend/app.js` — update `getElementById` references in PrinterModule, PageSelectModule, PrintModule

- [ ] **Step 1: Update PrinterModule to populate #printer-select**

Find where printers are populated in app.js (search for `printer-select` or `printer-list`). The old code likely rendered a custom list UI. Replace with:

```javascript
// In PrinterModule (or wherever printers are loaded):
_renderPrinters(printers) {
    const sel = document.getElementById('printer-select');
    if (!sel) return;
    sel.innerHTML = '<option value="">🖨 Chọn máy in…</option>';
    printers.forEach(p => {
        const opt = document.createElement('option');
        opt.value       = p.name;
        opt.textContent = p.name + (p.supportsDuplex ? ' ✦' : '');
        sel.appendChild(opt);
    });
    // Auto-select default
    if (printers[0]) {
        sel.value = printers[0].name;
        AppState.selectedPrinter = printers[0].name;
    }
},

// Add change listener in init():
document.getElementById('printer-select')?.addEventListener('change', e => {
    AppState.selectedPrinter = e.target.value || null;
    PrintModule.updateButton();
});
```

- [ ] **Step 2: Update PageSelectModule to read #page-range-input**

Find `PageSelectModule` in app.js. Ensure it reads from `#page-range-input` (the header input). The existing code likely already uses this ID — verify it still works. If it referenced a different element, update the ID reference.

- [ ] **Step 3: Update mode-select change handler**

Add to `DOMContentLoaded` or a `PrintModeModule.init()`:

```javascript
document.getElementById('mode-select')?.addEventListener('change', e => {
    AppState.printMode = e.target.value; // 'duplex' | 'simplex' | 'booklet'
    PrintModule.updateButton();
});
```

If `AppState.printMode` doesn't exist yet, add it to the AppState definition (default `'duplex'`).

- [ ] **Step 4: Update print-btn handler**

Ensure `#print-btn` triggers the existing print flow:

```javascript
document.getElementById('print-btn')?.addEventListener('click', () => {
    PrintModule.startPrint();
});
```

Where `PrintModule.startPrint()` is the existing print initiation function (rename from whatever it's called currently).

- [ ] **Step 5: Test controls work**

- Load app → printer dropdown populated
- Select printer → AppState.selectedPrinter updated (verify in console)
- Upload a file → print button becomes enabled
- Change mode-select → reflected in AppState.printMode

- [ ] **Step 6: Commit**

```bash
git add frontend/app.js
git commit -m "feat(layout): wire header controls — printer select, mode, page range, print btn"
```

---

## Task 4: ThumbStripModule — persistent left thumbnail panel

**Files:**
- Modify: `frontend/app.js` — add `ThumbStripModule`, update existing thumbnail logic

- [ ] **Step 1: Add ThumbStripModule**

Add before `DOMContentLoaded`:

```javascript
// ═══════════════════════════════════════════════════════════════════
// ThumbStripModule — Persistent left thumbnail panel
// Renders thumbnails for ALL loaded files with file dividers.
// Click thumbnail → scroll preview panel to that page.
// ═══════════════════════════════════════════════════════════════════
const ThumbStripModule = {
    _container:    null,
    _observer:     null,       // IntersectionObserver for lazy render
    _renderTasks:  new Map(),  // 'fileId-pageNum' → RenderTask
    _cache:        new Map(),  // 'fileId-pageNum' → offscreen canvas

    init() {
        this._container = document.getElementById('thumb-strip');
    },

    // Full re-render: called on file add/remove/switch
    render() {
        if (!this._container) return;

        // Cancel all in-flight renders
        for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
        this._renderTasks.clear();

        if (this._observer) { this._observer.disconnect(); this._observer = null; }
        this._container.innerHTML = '';

        if (AppState.files.length === 0) {
            this._container.innerHTML = `
                <div class="preview-empty" style="padding:16px;text-align:center">
                    <span class="preview-empty-icon">📄</span>
                    <span style="font-size:12px">Chưa có file</span>
                </div>`;
            return;
        }

        // Build IntersectionObserver for lazy canvas rendering
        this._observer = new IntersectionObserver(entries => {
            entries.forEach(e => {
                if (e.isIntersecting) {
                    const el      = e.target;
                    const fileId  = el.dataset.fileId;
                    const pageNum = parseInt(el.dataset.page);
                    this._renderThumb(fileId, pageNum, el);
                    this._observer.unobserve(el);
                }
            });
        }, { rootMargin: '150px' });

        // Render all files
        AppState.files.forEach((f, fileIndex) => {
            // File section divider
            const divider = document.createElement('div');
            divider.className   = 'thumb-file-divider';
            divider.textContent = f.name;
            divider.title       = f.name;
            this._container.appendChild(divider);

            // Thumbnails for this file
            for (let p = 1; p <= f.totalPageCount; p++) {
                const item = document.createElement('div');
                item.className        = 'thumb-item';
                item.dataset.fileId   = f.id;
                item.dataset.page     = p;
                item.dataset.fileIndex = fileIndex;
                item.setAttribute('tabindex', '0');
                item.setAttribute('role', 'button');
                item.setAttribute('aria-label', `File ${f.name}, trang ${p}`);

                const canvas  = document.createElement('canvas');
                const label   = document.createElement('div');
                label.className   = 'thumb-item-label';
                label.textContent = p;

                item.appendChild(canvas);
                item.appendChild(label);

                // Active file + active page highlight
                if (fileIndex === AppState.activeFileIndex && p === 1) {
                    item.classList.add('active');
                }

                item.addEventListener('click', () => this._onThumbClick(fileIndex, p));
                this._container.appendChild(item);
                this._observer.observe(item);
            }
        });
    },

    async _renderThumb(fileId, pageNum, el) {
        const key    = `${fileId}-${pageNum}`;
        const canvas = el.querySelector('canvas');
        if (!canvas) return;

        if (this._cache.has(key)) {
            const cached = this._cache.get(key);
            canvas.width  = cached.width;
            canvas.height = cached.height;
            canvas.getContext('2d').drawImage(cached, 0, 0);
            return;
        }

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const existing = this._renderTasks.get(key);
        if (existing) { try { existing.cancel(); } catch(_){} }

        const page = await fileEntry.pdfDoc.getPage(pageNum);
        const vp   = page.getViewport({ scale: 0.2 });
        const off  = document.createElement('canvas');
        off.width  = vp.width;
        off.height = vp.height;

        const task = page.render({
            canvasContext: off.getContext('2d', { alpha: false }),
            viewport:      vp,
            intent:        'display',
        });
        this._renderTasks.set(key, task);

        try {
            await task.promise;
            this._cache.set(key, off);
            canvas.width  = vp.width;
            canvas.height = vp.height;
            canvas.getContext('2d').drawImage(off, 0, 0);
        } catch(err) {
            if (err?.name !== 'RenderingCancelledException') console.warn(err);
        } finally {
            page.cleanup();
            this._renderTasks.delete(key);
        }
    },

    _onThumbClick(fileIndex, pageNum) {
        // Switch active file if needed
        if (fileIndex !== AppState.activeFileIndex) {
            TabsModule.setActive(fileIndex);
        }
        // Scroll preview panel to this page
        PreviewPanelModule.scrollToPage(pageNum);
        // Update active highlight
        this._setActiveHighlight(fileIndex, pageNum);
    },

    _setActiveHighlight(fileIndex, pageNum) {
        this._container.querySelectorAll('.thumb-item.active')
            .forEach(el => el.classList.remove('active'));
        const target = this._container.querySelector(
            `.thumb-item[data-file-index="${fileIndex}"][data-page="${pageNum}"]`
        );
        target?.classList.add('active');
        target?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },

    // Called when preview panel scrolls — update active thumb highlight
    onPreviewScroll(fileIndex, pageNum) {
        this._setActiveHighlight(fileIndex, pageNum);
    },
},
```

- [ ] **Step 2: Add ThumbStripModule.init() to DOMContentLoaded**

```javascript
ThumbStripModule.init();
```

- [ ] **Step 3: Call ThumbStripModule.render() after each file upload**

In `UploadModule._upload()`, after `AppState.addFile(entry)` and loading the PDF:

```javascript
ThumbStripModule.render();
```

Also call it from `TabsModule.setActive()` after switching files (so dividers stay correct).

- [ ] **Step 4: Test thumbnails appear in left panel**

Upload a PDF → thumbnails appear in left panel. Upload a 2nd PDF → 2 file sections with dividers. Click a thumbnail → preview scrolls to that page.

- [ ] **Step 5: Commit**

```bash
git add frontend/app.js
git commit -m "feat(thumbstrip): persistent left thumbnail panel with multi-file sections"
```

---

## Task 5: PreviewPanelModule — persistent main preview

**Files:**
- Modify: `frontend/app.js` — add `PreviewPanelModule`, replaces `PrintPreviewModule` main view logic

- [ ] **Step 1: Add PreviewPanelModule**

```javascript
// ═══════════════════════════════════════════════════════════════════
// PreviewPanelModule — Persistent right-panel PDF preview
// Replaces the old modal main view.
// ═══════════════════════════════════════════════════════════════════
const PreviewPanelModule = {
    _container:   null,
    _observer:    null,
    _renderTasks: new Map(), // 'fileId-pageNum' → RenderTask
    _cache:       new Map(), // 'fileId-pageNum' → offscreen canvas
    _pageEls:     new Map(), // 'fileId-pageNum' → .preview-page-card el

    init() {
        this._container = document.getElementById('preview-panel');
        // Scroll listener → update thumb highlight
        this._container?.addEventListener('scroll', () => this._onScroll(), { passive: true });
    },

    // Render all pages of active file
    render(fileEntry) {
        if (!this._container || !fileEntry?.pdfDoc) return;

        // Disconnect old observer
        if (this._observer) { this._observer.disconnect(); this._observer = null; }

        // Clear old page elements for this file (keep other files' cached canvases)
        this._pageEls.forEach((el, key) => {
            if (key.startsWith(fileEntry.id + '-')) {
                el.remove();
                this._pageEls.delete(key);
            }
        });
        this._container.innerHTML = '';

        // Build IntersectionObserver
        this._observer = new IntersectionObserver(entries => {
            entries.forEach(e => {
                if (!e.isIntersecting) return;
                const el      = e.target;
                const fileId  = el.dataset.fileId;
                const pageNum = parseInt(el.dataset.page);
                this._renderPage(fileId, pageNum, el);
                this._observer.unobserve(el);
            });
        }, { rootMargin: '200px' });

        // Create placeholder cards
        for (let p = 1; p <= fileEntry.totalPageCount; p++) {
            const card       = document.createElement('div');
            card.className   = 'preview-page-card';
            card.dataset.fileId = fileEntry.id;
            card.dataset.page   = p;
            card.style.minHeight = '400px'; // prevent layout thrash before render

            const canvas = document.createElement('canvas');
            card.appendChild(canvas);

            const key = `${fileEntry.id}-${p}`;
            this._pageEls.set(key, card);
            this._container.appendChild(card);
            this._observer.observe(card);
        }
    },

    async _renderPage(fileId, pageNum, el) {
        const key    = `${fileId}-${pageNum}`;
        const canvas = el.querySelector('canvas');
        if (!canvas) return;

        if (this._cache.has(key)) {
            const cached = this._cache.get(key);
            canvas.width  = cached.width;
            canvas.height = cached.height;
            canvas.getContext('2d').drawImage(cached, 0, 0);
            el.style.minHeight = '';
            return;
        }

        const existing = this._renderTasks.get(key);
        if (existing) { try { existing.cancel(); } catch(_){} }

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const page = await fileEntry.pdfDoc.getPage(pageNum);
        const vp   = page.getViewport({ scale: 1.5 });
        const off  = document.createElement('canvas');
        off.width  = vp.width;
        off.height = vp.height;

        const task = page.render({
            canvasContext: off.getContext('2d', { alpha: false }),
            viewport:      vp,
            intent:        'display',
        });
        this._renderTasks.set(key, task);

        try {
            await task.promise;
            this._cache.set(key, off);
            canvas.width  = vp.width;
            canvas.height = vp.height;
            canvas.getContext('2d').drawImage(off, 0, 0);
            el.style.minHeight = '';
        } catch(err) {
            if (err?.name !== 'RenderingCancelledException') console.warn(err);
        } finally {
            page.cleanup();
            this._renderTasks.delete(key);
        }
    },

    scrollToPage(pageNum) {
        const fileId = AppState.activeFile?.id;
        if (!fileId) return;
        const key = `${fileId}-${pageNum}`;
        const el  = this._pageEls.get(key);
        el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    },

    _onScroll() {
        // Find which page is most visible → update thumb highlight
        if (!this._container || !AppState.activeFile) return;
        const containerRect = this._container.getBoundingClientRect();
        let   bestPage      = 1;
        let   bestOverlap   = 0;

        this._pageEls.forEach((el, key) => {
            if (!key.startsWith(AppState.activeFile.id + '-')) return;
            const rect    = el.getBoundingClientRect();
            const overlap = Math.min(rect.bottom, containerRect.bottom)
                          - Math.max(rect.top,    containerRect.top);
            if (overlap > bestOverlap) {
                bestOverlap = overlap;
                bestPage    = parseInt(el.dataset.page);
            }
        });

        ThumbStripModule.onPreviewScroll(AppState.activeFileIndex, bestPage);
    },

    clear() {
        if (this._observer) { this._observer.disconnect(); this._observer = null; }
        if (this._container) {
            this._container.innerHTML = `
                <div class="preview-empty">
                    <span class="preview-empty-icon">🖨</span>
                    <span>Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong></span>
                </div>`;
        }
    },
},
```

- [ ] **Step 2: Add PreviewPanelModule.init() to DOMContentLoaded**

```javascript
PreviewPanelModule.init();
```

- [ ] **Step 3: Call PreviewPanelModule.render() in UploadModule._upload() after PDF is loaded**

Replace the old `await PreviewModule.render(entry.id)` call with:

```javascript
PreviewPanelModule.render(AppState.activeFile);
ThumbStripModule.render();
```

And `PreviewModule.render()` (which loads the PDF into pdfDoc) is still called first.

- [ ] **Step 4: Test full flow**

Upload PDF → pages appear in right panel, thumbnails in left panel. Scroll right panel → left thumbnail highlight follows. Click thumbnail → right panel scrolls to that page.

- [ ] **Step 5: Commit**

```bash
git add frontend/app.js
git commit -m "feat(preview): persistent PreviewPanelModule replaces modal main view"
```

---

## Task 6: Remove old modal & dead code cleanup

**Files:**
- Modify: `frontend/app.js`
- Modify: `frontend/sidebar-styles.css`

- [ ] **Step 1: Remove PrintPreviewModule open/close modal behavior from app.js**

The `PrintPreviewModule` was a full-screen modal. Since preview is now persistent:
- Remove `PrintPreviewModule.open()` and `PrintPreviewModule.close()` methods
- Remove `#print-preview-btn` click listener (the button no longer exists)
- Keep any context menu or lasso-select logic if still needed

- [ ] **Step 2: Remove old modal CSS from sidebar-styles.css**

Delete all CSS rules for:
- `.modal-print-preview` (the modal overlay container)
- `.preview-thumb-panel` (old left panel inside modal)
- `.preview-main-view` (old right panel inside modal)
- `.preview-thumb-grid` (old thumb grid inside modal)
- `.preview-main-canvas-container` (old scrollable main view inside modal)

Keep:
- `.lasso-rect` if lasso-select is preserved
- Any `.preview-thumb-item` states that are still referenced

- [ ] **Step 3: Build check**

```powershell
dotnet build desktop/MyPrinter.Desktop.csproj
```

Open app in browser. Verify no JS console errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js frontend/sidebar-styles.css
git commit -m "cleanup: remove modal print preview shell, dead CSS"
```

---

## Task 7: Global drag-drop file upload onto app window

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 1: Wire global drag-drop to show drop hint overlay**

Add to `DOMContentLoaded`:

```javascript
// Global drag-drop — anywhere on the window
document.addEventListener('dragover', e => {
    e.preventDefault();
    document.getElementById('drop-hint')?.classList.add('visible');
});
document.addEventListener('dragleave', e => {
    if (!e.relatedTarget) {
        document.getElementById('drop-hint')?.classList.remove('visible');
    }
});
document.addEventListener('drop', async e => {
    e.preventDefault();
    document.getElementById('drop-hint')?.classList.remove('visible');
    for (const file of e.dataTransfer.files) {
        await UploadModule._upload(file);
    }
});
```

- [ ] **Step 2: Test drag-drop**

Drag a PDF onto the app window → drop hint overlay appears → release → file tab and thumbnails appear.

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(upload): global drag-drop file upload with drop hint overlay"
```

---

## Verification Checklist

After all tasks complete:

- [ ] App opens in macOS light style (white/gray, `#F5F5F7` background)
- [ ] 2-row header visible (settings + tabs), compact and not dominating vertical space
- [ ] Upload 1 file → 1 tab, thumbs in left panel, pages in right panel
- [ ] Upload 2nd file → 2 tabs, both sections in thumb panel
- [ ] Click tab 1 → both thumbs and preview jump to file 1 page 1
- [ ] Click tab 2 → both thumbs and preview jump to file 2 page 1
- [ ] Click thumb page 3 of file 1 → preview scrolls to page 3
- [ ] Scroll preview → thumb highlight follows active page
- [ ] Printer dropdown populated and functional
- [ ] Print button enabled only when printer + pages selected
- [ ] Remove tab → app falls back to adjacent file or empty state
- [ ] Drag PDF onto window → upload works
- [ ] `dotnet build desktop/MyPrinter.Desktop.csproj` → 0 errors
- [ ] Flip modal, zoom modal, confirm modal still function correctly
