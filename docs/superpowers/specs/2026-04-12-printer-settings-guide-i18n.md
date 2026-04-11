# Spec: Printer Settings Button, User Guide Modal, and Language Toggle (i18n)

**Date:** 2026-04-12  
**Status:** Draft  
**Approach:** A — i18n JSON + `data-i18n` attributes

---

## 1. Overview

Add three buttons to the header bar (next to the Print button):

| Button | Icon | Label (VI) | Label (EN) |
|--------|------|-----------|-----------|
| Printer Settings | ⚙️ | Cài đặt | Settings |
| User Guide | ❓ | Hướng dẫn | Help |
| Language Toggle | 🌐 | VI / EN | VI / EN |

Header layout after change:
```
[🖨 Chọn máy in] [Chế độ in ▾] [Trang: 1-∞]  [spacer]  [📄 Xem] [🖨 Xem trước] [landscape bar]  | ⚙️ Cài đặt | ❓ Hướng dẫn | 🌐 VI |  [🖨 In]
```

---

## 2. Feature 1 — Printer Settings Button (⚙️)

### 2.1 Frontend

- Button `id="printer-settings-btn"` added after landscape bar, before print button
- Only enabled when a printer is selected (`printer-select` has a non-empty value)
- On click: `POST /api/printer/settings` with body `{ "printerName": "<selected printer name>" }`
- On success: show toast "Đã mở cài đặt máy in" / "Printer settings opened"
- On error: show toast with error message from response

### 2.2 Backend — new endpoint

```
POST /api/printer/settings
Body: { "printerName": string }
Response: { "success": bool, "message": string }
```

Implementation in `BackendStartup.cs`:

```csharp
app.MapPost("/api/printer/settings", (PrinterSettingsRequest req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.PrinterName))
            return Results.BadRequest(new { success = false, message = "Printer name is required" });

        // Open Windows Printer Properties dialog via rundll32
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"printui.dll,PrintUIEntry /p /n \"{req.PrinterName}\"",
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);

        return Results.Ok(new { success = true, message = "Printer settings dialog opened" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to open printer settings: {ex.Message}");
    }
});
```

New model in `PrintModels.cs`:
```csharp
public record PrinterSettingsRequest(string PrinterName);
```

### 2.3 Button states

| State | Condition | Appearance |
|-------|-----------|-----------|
| Disabled | No printer selected | opacity 0.35, cursor not-allowed |
| Enabled | Printer selected | normal |
| Loading | Request in flight | spinner icon, disabled |

---

## 3. Feature 2 — User Guide Modal (❓)

### 3.1 Frontend

- Button `id="guide-btn"` always enabled
- Opens modal `id="guide-modal"` — full overlay, scrollable content
- Modal has tab navigation for sections:
  1. **Tổng quan** / Overview
  2. **In thông minh** / Smart Print
  3. **In Sách** / Booklet
  4. **In thủ công** / Manual Duplex
- Close via ✕ button or clicking overlay
- All content strings stored in i18n keys (see Section 4)

### 3.2 Modal HTML structure

```html
<div id="guide-modal" class="modal hidden">
  <div class="modal-overlay" id="guide-modal-overlay"></div>
  <div class="modal-content modal-guide">
    <div class="modal-header">
      <h2 data-i18n="guide.title">Hướng dẫn sử dụng</h2>
      <button class="modal-close" id="guide-modal-close">✕</button>
    </div>
    <div class="guide-tabs">
      <button class="guide-tab active" data-tab="overview" data-i18n="guide.tab.overview">Tổng quan</button>
      <button class="guide-tab" data-tab="smart" data-i18n="guide.tab.smart">In thông minh</button>
      <button class="guide-tab" data-tab="booklet" data-i18n="guide.tab.booklet">In Sách</button>
      <button class="guide-tab" data-tab="manual" data-i18n="guide.tab.manual">In thủ công</button>
    </div>
    <div class="modal-body guide-body" id="guide-body">
      <!-- Content injected by GuideModule.render(tab, lang) -->
    </div>
  </div>
</div>
```

### 3.3 Content source

Guide content lives in `frontend/i18n/vi.js` and `frontend/i18n/en.js` as structured strings under the `guide.*` namespace. Content is injected as HTML by `GuideModule.render(tab)` — uses `innerHTML` with pre-sanitized static strings (no user input, safe).

---

## 4. Feature 3 — Language Toggle (🌐)

### 4.1 i18n Architecture

**app.js is a classic script (not ES module).** i18n files are loaded as plain `<script>` tags that assign globals before `app.js` runs.

**File structure:**
```
frontend/
  i18n/
    vi.js     — window.VI_STRINGS = { ... }
    en.js     — window.EN_STRINGS = { ... }
```

**Loading in `index.html`** (before `app.js`):
```html
<script src="i18n/vi.js"></script>
<script src="i18n/en.js"></script>
<script src="app.js?v=20"></script>
```

**`I18nModule`** in `app.js`:
```js
const I18nModule = {
  _lang: localStorage.getItem('lang') || 'vi',
  _strings: {},          // populated on init
  
  init() { this.setLang(this._lang); },
  
  t(key) {
    return key.split('.').reduce((o, k) => o?.[k], this._strings) ?? key;
  },
  
  setLang(lang) {
    this._lang = lang;
    this._strings = lang === 'en' ? EN_STRINGS : VI_STRINGS;
    localStorage.setItem('lang', lang);
    this.applyAll();
    this._updateToggleBtn();
  },
  
  applyAll() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
      const key = el.dataset.i18n;
      const text = this.t(key);
      if (text !== key) el.textContent = text;
    });
    document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
      const key = el.dataset.i18nPlaceholder;
      const text = this.t(key);
      if (text !== key) el.placeholder = text;
    });
    document.querySelectorAll('[data-i18n-title]').forEach(el => {
      const key = el.dataset.i18nTitle;
      const text = this.t(key);
      if (text !== key) el.title = text;
    });
    // Re-render guide if open
    if (!document.getElementById('guide-modal')?.classList.contains('hidden')) {
      GuideModule.renderCurrentTab();
    }
  },
  
  _updateToggleBtn() {
    const btn = document.getElementById('lang-toggle-btn');
    if (btn) btn.textContent = `🌐 ${this._lang.toUpperCase()}`;
  }
};
```

### 4.2 i18n key coverage

All UI strings must have keys. Scope:

| Category | Examples |
|----------|---------|
| Header controls | `header.printer.placeholder`, `header.mode.duplex`, `header.mode.booklet`, `header.pageRange.placeholder` |
| Buttons | `btn.print`, `btn.settings`, `btn.guide`, `btn.addFile` |
| View toggle | `view.page`, `view.sheet` |
| Landscape bar | `landscape.label`, `landscape.together`, `landscape.separate` |
| File tabs | `tab.add`, `tab.copies`, `tab.collate` |
| Preview empty | `preview.empty`, `preview.dropHint` |
| History | `history.title`, `history.clear` |
| Toasts | `toast.printStarted`, `toast.uploadError`, `toast.settingsOpened`, ... |
| Guide content | `guide.title`, `guide.tab.*`, `guide.overview.*`, `guide.smart.*`, `guide.booklet.*`, `guide.manual.*` |
| Modals | `modal.zoomTitle`, `modal.flip.*` |
| Zoom toolbar | `zoom.allDouble`, `zoom.allSingle`, `zoom.deselectAll`, `zoom.hint` |

### 4.3 HTML attribute migration

All hardcoded text in `index.html` gets `data-i18n="key"` attribute. Dynamic strings generated in `app.js` use `I18nModule.t('key')` instead of string literals.

**Example:**
```html
<!-- Before -->
<button id="print-btn" class="btn-print" disabled>🖨 In</button>

<!-- After -->
<button id="print-btn" class="btn-print" disabled data-i18n="btn.print">🖨 In</button>
```

### 4.4 Language toggle button

```html
<button id="lang-toggle-btn" class="btn-icon-sm" title="Switch language">🌐 VI</button>
```

On click: toggles between `vi` and `en` by calling `I18nModule.setLang(newLang)`.

### 4.5 Persistence

Language preference saved to `localStorage` under key `'lang'`. Restored on `I18nModule.init()` at app startup.

---

## 5. CSS

### 5.1 New button styles

New class `btn-icon-sm` for the small icon buttons (Settings, Guide, Language):
```css
.btn-icon-sm {
  height: 34px;
  padding: 0 12px;
  border: 1.5px solid #93c5fd;
  border-radius: 8px;
  background: #ffffff;
  color: #1e3a5f;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  transition: background 0.15s, border-color 0.15s;
}
.btn-icon-sm:hover { background: #eff6ff; border-color: #3b82f6; }
.btn-icon-sm:disabled { opacity: 0.35; cursor: not-allowed; }
```

### 5.2 Guide modal

```css
.modal-guide { max-width: 720px; width: 90vw; max-height: 85vh; display: flex; flex-direction: column; }
.guide-tabs { display: flex; gap: 4px; padding: 0 20px; border-bottom: 1px solid #e2e8f0; }
.guide-tab { padding: 8px 16px; border: none; background: none; cursor: pointer; color: #64748b; font-weight: 500; border-bottom: 2px solid transparent; }
.guide-tab.active { color: #2563eb; border-bottom-color: #2563eb; }
.guide-body { flex: 1; overflow-y: auto; padding: 20px; line-height: 1.7; }
```

---

## 6. Wiring (JavaScript)

### 6.1 Initialization order in `app.js`

```js
// At DOMContentLoaded, after other modules:
I18nModule.init();          // Must be last — applies translations to all wired elements
GuideModule.init();
PrinterSettingsModule.init();
LangToggleModule.init();
```

### 6.2 `PrinterSettingsModule`

```js
const PrinterSettingsModule = {
  init() {
    const btn = document.getElementById('printer-settings-btn');
    if (!btn) return;
    btn.addEventListener('click', async () => {
      const printerName = document.getElementById('printer-select')?.value;
      if (!printerName) return;
      btn.disabled = true;
      try {
        const res = await fetch('http://localhost:8787/api/printer/settings', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ printerName }),
        });
        const data = await res.json();
        ToastModule.show(data.message || I18nModule.t('toast.settingsOpened'));
      } catch (e) {
        ToastModule.show(I18nModule.t('toast.settingsError'), 'error');
      } finally {
        btn.disabled = false;
      }
    });
    // Sync enabled state with printer selection
    document.getElementById('printer-select')?.addEventListener('change', () => {
      btn.disabled = !document.getElementById('printer-select').value;
    });
    btn.disabled = !document.getElementById('printer-select')?.value;
  }
};
```

### 6.3 `GuideModule`

```js
const GuideModule = {
  _activeTab: 'overview',
  init() {
    document.getElementById('guide-btn')?.addEventListener('click', () => this.open());
    document.getElementById('guide-modal-close')?.addEventListener('click', () => this.close());
    document.getElementById('guide-modal-overlay')?.addEventListener('click', () => this.close());
    document.querySelectorAll('.guide-tab').forEach(btn => {
      btn.addEventListener('click', () => {
        this._activeTab = btn.dataset.tab;
        document.querySelectorAll('.guide-tab').forEach(b => b.classList.toggle('active', b === btn));
        this.renderCurrentTab();
      });
    });
  },
  open() {
    document.getElementById('guide-modal')?.classList.remove('hidden');
    this.renderCurrentTab();
  },
  close() { document.getElementById('guide-modal')?.classList.add('hidden'); },
  renderCurrentTab() {
    const body = document.getElementById('guide-body');
    if (!body) return;
    body.innerHTML = I18nModule.t(`guide.${this._activeTab}.content`);
  }
};
```

### 6.4 `LangToggleModule`

```js
const LangToggleModule = {
  init() {
    document.getElementById('lang-toggle-btn')?.addEventListener('click', () => {
      const next = I18nModule._lang === 'vi' ? 'en' : 'vi';
      I18nModule.setLang(next);
    });
  }
};
```

--- (sample — not exhaustive)

### `frontend/i18n/vi.js` (excerpt)
```js
window.VI_STRINGS = {
  btn: { print: '🖨 In', settings: '⚙️ Cài đặt', guide: '❓ Hướng dẫn', addFile: '+ Thêm file' },
  header: {
    printer: { placeholder: '🖨 Chọn máy in…' },
    mode: { duplex: 'In thông minh', booklet: 'In Sách' },
    pageRange: { placeholder: 'Trang: 1-∞' },
  },
  view: { page: '📄 Xem nội dung', sheet: '🖨 Xem trước khi in' },
  landscape: { label: 'Trang ngang:', together: '🔀 In cùng trang dọc', separate: '⬜ In tờ riêng' },
  toast: {
    settingsOpened: 'Đã mở cài đặt máy in',
    settingsError: 'Không thể mở cài đặt máy in',
  },
  guide: {
    title: 'Hướng dẫn sử dụng',
    tab: { overview: 'Tổng quan', smart: 'In thông minh', booklet: 'In Sách', manual: 'In thủ công' },
    overview: { content: '<h3>Tổng quan</h3><p>Ứng dụng in ấn thông minh hỗ trợ máy in 1 mặt...</p>' },
    smart:    { content: '<h3>In thông minh</h3><p>Chế độ in 2 mặt thủ công tự động hướng dẫn...</p>' },
    booklet:  { content: '<h3>In Sách</h3><p>In 4 trang A5 trên 2 mặt giấy A4...</p>' },
    manual:   { content: '<h3>In thủ công</h3><p>Làm theo hướng dẫn animation khi lật giấy...</p>' },
  },
  // ... all other keys
};
```

### `frontend/i18n/en.js` (excerpt)
```js
window.EN_STRINGS = {
  btn: { print: '🖨 Print', settings: '⚙️ Settings', guide: '❓ Help', addFile: '+ Add File' },
  header: {
    printer: { placeholder: '🖨 Select printer…' },
    mode: { duplex: 'Smart Print', booklet: 'Booklet' },
    pageRange: { placeholder: 'Pages: 1-∞' },
  },
  view: { page: '📄 Page View', sheet: '🖨 Print Preview' },
  landscape: { label: 'Landscape pages:', together: '🔀 Together with portrait', separate: '⬜ Separate sheet' },
  toast: {
    settingsOpened: 'Printer settings opened',
    settingsError: 'Could not open printer settings',
  },
  guide: {
    title: 'User Guide',
    tab: { overview: 'Overview', smart: 'Smart Print', booklet: 'Booklet', manual: 'Manual Duplex' },
    overview: { content: '<h3>Overview</h3><p>Smart printing app for single-sided printers...</p>' },
    smart:    { content: '<h3>Smart Print</h3><p>Automatic manual duplex with visual guidance...</p>' },
    booklet:  { content: '<h3>Booklet</h3><p>Print 4 A5 pages on 2 sides of A4 paper...</p>' },
    manual:   { content: '<h3>Manual Duplex</h3><p>Follow the animation instructions when flipping paper...</p>' },
  },
  // ... all other keys
};
```

---

## 8. Out of Scope

- Translation of PDF/document content (not possible — binary files)
- RTL language support
- More than 2 languages (VI/EN only)
- Backend i18n (server responses stay in English)
- Printer settings beyond what Windows dialog provides

---

## 9. Implementation Order

1. **Backend:** Add `PrinterSettingsRequest` model + `POST /api/printer/settings` endpoint
2. **i18n files:** Create `frontend/i18n/vi.js` and `frontend/i18n/en.js` with all keys
3. **`I18nModule`:** Add to `app.js`, wire `init()` at startup
4. **HTML migration:** Add `data-i18n` attributes to all static text in `index.html`
5. **JS string migration:** Replace hardcoded strings in `app.js` with `I18nModule.t('key')`
6. **3 new buttons:** Add HTML to header, wire JS modules (`PrinterSettingsModule`, `GuideModule`, `LangToggleModule`)
7. **Guide modal:** Add HTML, CSS, content
8. **CSS:** Add `btn-icon-sm`, `modal-guide`, `guide-tabs`, `guide-tab`, `guide-body`
9. **Verify:** All strings show correctly in both languages, Windows dialog opens, guide modal renders

---

## 10. Acceptance Criteria

- [ ] ⚙️ button disabled when no printer selected; clicking opens Windows Printer Properties dialog
- [ ] ❓ button always enabled; clicking opens guide modal with 4 tabs; content changes with language
- [ ] 🌐 button toggles VI↔EN; ALL visible text in app changes immediately
- [ ] Language preference persists across page refresh (localStorage)
- [ ] No hardcoded Vietnamese/English strings remain in `index.html` or `app.js`
- [ ] `node --check frontend/app.js` passes
- [ ] App works correctly in both languages
