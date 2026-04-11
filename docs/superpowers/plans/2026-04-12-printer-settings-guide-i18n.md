# Printer Settings, User Guide & i18n Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three header buttons — ⚙️ Printer Settings (opens Windows dialog), ❓ User Guide (modal with 4 tabs), 🌐 Language Toggle (VI/EN full-app i18n) — plus a complete i18n infrastructure covering all visible text in the app.

**Architecture:** i18n uses `window.VI_STRINGS` / `window.EN_STRINGS` globals (classic script pattern, not ES modules). HTML static text uses `data-i18n="key"` attributes; dynamic JS strings call `I18nModule.t('key')`. Language persists in `localStorage`.

**Tech Stack:** Vanilla JS, HTML5, CSS3, .NET 10 backend (Minimal API), `rundll32.exe printui.dll` for Windows printer dialog.

---

## File Map

| File | Action | What changes |
|------|--------|-------------|
| `frontend/i18n/vi.js` | **Create** | `window.VI_STRINGS` — all UI strings in Vietnamese |
| `frontend/i18n/en.js` | **Create** | `window.EN_STRINGS` — all UI strings in English |
| `frontend/index.html` | **Modify** | Add 3 buttons, guide modal HTML, `data-i18n` attributes, i18n script tags |
| `frontend/app.js` | **Modify** | Add `I18nModule`, `PrinterSettingsModule`, `GuideModule`, `LangToggleModule`; migrate dynamic strings |
| `frontend/styles.css` | **Modify** | Add `.btn-icon-sm`, `.modal-guide`, `.guide-tabs`, `.guide-tab`, `.guide-body` |
| `backend/Models/PrintModels.cs` | **Modify** | Add `PrinterSettingsRequest` record |
| `backend/BackendStartup.cs` | **Modify** | Add `POST /api/printer/settings` endpoint |

---

## Task 1: Backend — Printer Settings Endpoint

**Files:**
- Modify: `backend/Models/PrintModels.cs`
- Modify: `backend/BackendStartup.cs`

- [ ] **Step 1.1: Add `PrinterSettingsRequest` model to `PrintModels.cs`**

Open `backend/Models/PrintModels.cs`. Add at the end of the file (before the last `}`):

```csharp
public record PrinterSettingsRequest(string PrinterName);
```

- [ ] **Step 1.2: Add `POST /api/printer/settings` endpoint to `BackendStartup.cs`**

In `backend/BackendStartup.cs`, after the `app.MapDelete("/api/print/cancel", ...)` block (around line 349) and before `return app;`, add:

```csharp
app.MapPost("/api/printer/settings", (PrinterSettingsRequest req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.PrinterName))
            return Results.BadRequest(new { success = false, message = "Printer name is required" });

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

- [ ] **Step 1.3: Build to verify no compile errors**

```powershell
cd backend
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 1.4: Commit**

```powershell
git add backend/Models/PrintModels.cs backend/BackendStartup.cs
git commit -m "feat(backend): add POST /api/printer/settings endpoint"
```

---

## Task 2: Create i18n String Files

**Files:**
- Create: `frontend/i18n/vi.js`
- Create: `frontend/i18n/en.js`

- [ ] **Step 2.1: Create `frontend/i18n/vi.js`**

Create file `frontend/i18n/vi.js` with ALL Vietnamese strings:

```js
window.VI_STRINGS = {
  // App title
  app: { title: 'Máy In Thông Minh' },

  // Header
  header: {
    printer: { placeholder: '🖨 Chọn máy in…' },
    mode: { duplex: 'In thông minh', booklet: 'In Sách' },
    pageRange: { placeholder: 'Trang: 1-∞', ariaLabel: 'Chọn trang' },
    viewGroup: { title: 'Chế độ xem' },
  },

  // Buttons
  btn: {
    print: '🖨 In',
    settings: '⚙️ Cài đặt',
    guide: '❓ Hướng dẫn',
    addFile: '+ Thêm file',
    historyClear: 'Xóa',
    historyToggle: '📋 Lịch sử in',
  },

  // View toggle
  view: {
    page: '📄 Xem nội dung',
    sheet: '🖨 Xem trước khi in',
  },

  // Landscape modebar
  landscape: {
    label: 'Trang ngang:',
    together: '🔀 In cùng trang dọc',
    separate: '⬜ In tờ riêng',
  },

  // Copies widget
  copies: {
    label: 'Số bản:',
    collate: ' Ghép bộ',
    decTitle: 'Giảm số bản',
    incTitle: 'Tăng số bản',
  },

  // Preview panel
  preview: {
    empty: 'Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong>',
    thumbEmpty: 'Chưa có file',
    dropHint: 'Thả file vào đây',
    loading: 'Đang tải...',
  },

  // Orientation badge
  orientation: {
    landscape: '↔ Ngang',
    portrait: '↕ Dọc',
  },

  // File status
  file: {
    ready: 'Đã sẵn sàng',
  },

  // Zoom modal
  zoom: {
    allPages: 'Tất cả trang',
    allDouble: 'Tất cả 2 mặt',
    allSingle: 'Tất cả 1 mặt',
    deselectAll: '✕ Bỏ chọn tất cả',
    hint: 'Click phải vào trang để chọn in 1 mặt',
    scrollHint: 'Cuộn để xem tất cả',
    page: (n) => `Trang ${n}`,
    doubleSided: '2 MẶT',
    singleSided: '1 MẶT',
  },

  // Context menu
  ctx: {
    sides: 'Mặt in',
    double: '2 mặt',
    single: '1 mặt',
    allDouble: 'Tất cả: 2 mặt',
    allSingle: 'Tất cả: 1 mặt',
    deselectAll: 'Bỏ chọn tất cả',
    rotate: 'Xoay trang',
    rotateCW: 'Xoay phải 90°',
    rotateCCW: 'Xoay trái 90°',
    rotate180: 'Xoay 180°',
    flipH: 'Lật ngang',
    flipV: 'Lật dọc',
    rotateReset: 'Reset về gốc',
    insert: 'Chèn',
    blankBefore: 'Trang trắng trước',
    blankAfter: 'Trang trắng sau',
    imageBefore: 'Ảnh trước',
    imageAfter: 'Ảnh sau',
    blankPage: 'Trang trắng',
    page: (n) => `Trang ${n}`,
  },

  // Flip modal (manual duplex instructions)
  flip: {
    title: '📄 Hướng Dẫn Đặt Giấy',
    check1: '1. Chờ máy in xong — đèn ngừng nhấp nháy',
    check2: '2. Lấy chồng giấy ra — theo đúng hướng mũi tên',
    check3: '3. Đặt lại vào khay — mặt trắng ngửa lên',
    autoTimer: 'Tự động tiếp tục sau <strong>30 giây</strong>',
    continue: '✓ Đã Đặt Giấy - Tiếp Tục In',
  },

  // Confirm print modal
  confirm: {
    title: '🖨️ Xác Nhận Lệnh In',
    cancel: 'Huỷ',
    ok: '✓ Xác Nhận In',
    noPages: 'Không có trang nào để in.',
  },

  // Print button states
  print: {
    start: '🖨 Bắt Đầu In',
    cancel: '⏹ Huỷ In',
    printing: (i, total) => `⏳ Đang in file ${i}/${total}...`,
    done: (n) => n > 1 ? `✅ Đã in ${n} file!` : '✅ Đã gửi lệnh in!',
  },

  // Summary bar
  summary: {
    pages: (n) => `📄 ${n} trang`,
    sheets: (n) => `🖨 ${n} tờ`,
    copies: (n) => `× ${n} bản`,
    allSelected: 'Tất cả được chọn',
    selected: (sel, total) => `${sel}/${total} trang được chọn`,
    total: (total) => `${total} trang`,
  },

  // History
  history: {
    empty: 'Chưa có lịch sử in',
  },

  // Sheet view labels
  sheet: {
    bookletLabel: (i, total) => `Tờ ${i}/${total} – Booklet`,
    label: (i, total) => `Tờ ${i}/${total}`,
    front: 'Mặt trước',
    back: 'Mặt sau',
    noprint: 'Không in',
    page: (n) => `Mặt trước – Trang ${n}`,
    backPage: (n) => `Mặt sau – Trang ${n}`,
  },

  // Toasts / messages
  toast: {
    settingsOpened: 'Đã mở cài đặt máy in',
    settingsError: 'Không thể mở cài đặt máy in',
    uploadError: 'Lỗi tải file',
    convertError: 'Lỗi chuyển đổi file',
  },

  // Error panel
  error: {
    uploadFailed: (name, msg) => `Không thể tải lên <b>${name}</b>: ${msg}`,
    convertFailed: (name, msg) => `Không thể chuyển đổi <b>${name}</b>: ${msg}`,
  },

  // Guide modal
  guide: {
    title: 'Hướng dẫn sử dụng',
    tab: {
      overview: 'Tổng quan',
      smart: 'In thông minh',
      booklet: 'In Sách',
      manual: 'In thủ công',
    },
    overview: { content: `
      <h3>Tổng quan</h3>
      <p>Ứng dụng <strong>Máy In Thông Minh</strong> được thiết kế dành riêng cho máy in 1 mặt, giúp bạn in 2 mặt thủ công hoặc in sách booklet A5 một cách dễ dàng.</p>
      <h4>Các tính năng chính</h4>
      <ul>
        <li>📄 <strong>Xem nội dung</strong> — xem từng trang tài liệu</li>
        <li>🖨 <strong>Xem trước khi in</strong> — xem bố cục thực tế trên tờ giấy</li>
        <li>🔀 <strong>Chọn trang in</strong> — click phải vào trang để in 1 mặt hoặc 2 mặt</li>
        <li>📋 <strong>Lịch sử in</strong> — xem lại các lần in trước</li>
      </ul>
      <h4>Quy trình cơ bản</h4>
      <ol>
        <li>Chọn máy in từ danh sách</li>
        <li>Kéo thả file vào hoặc nhấn <strong>+ Thêm file</strong></li>
        <li>Chọn chế độ in (In thông minh hoặc In Sách)</li>
        <li>Nhấn <strong>🖨 In</strong> và làm theo hướng dẫn</li>
      </ol>
    ` },
    smart: { content: `
      <h3>In thông minh</h3>
      <p>Chế độ <strong>In thông minh</strong> giúp bạn in 2 mặt thủ công trên máy in 1 mặt.</p>
      <h4>Cách hoạt động</h4>
      <ol>
        <li>App in tất cả <strong>mặt trước</strong> trước (các trang lẻ)</li>
        <li>Hướng dẫn animation xuất hiện — làm theo để lật giấy đúng cách</li>
        <li>App tự động in <strong>mặt sau</strong> (các trang chẵn)</li>
      </ol>
      <h4>Lưu ý lật giấy</h4>
      <ul>
        <li>↕ <strong>Trang dọc (Portrait)</strong>: Lật theo cạnh dài (lật lên/xuống)</li>
        <li>↔ <strong>Trang ngang (Landscape)</strong>: Lật theo cạnh ngắn (lật trái/phải)</li>
      </ul>
      <h4>Chọn trang in 1 mặt</h4>
      <p>Click phải vào trang bất kỳ → chọn <em>Mặt in → 1 mặt</em> để bỏ qua trang đó khỏi in 2 mặt.</p>
    ` },
    booklet: { content: `
      <h3>In Sách (Booklet)</h3>
      <p>Chế độ <strong>In Sách</strong> in 4 trang A5 trên 2 mặt giấy A4 — sau khi gấp đôi sẽ thành sách nhỏ.</p>
      <h4>Thứ tự trang tự động</h4>
      <p>App tự tính thứ tự trang để khi gấp đôi giấy A4, các trang theo đúng thứ tự 1, 2, 3, 4...</p>
      <p><em>Ví dụ 8 trang:</em></p>
      <ul>
        <li>Tờ 1 Mặt trước: Trang 8 | Trang 1</li>
        <li>Tờ 1 Mặt sau: Trang 2 | Trang 7</li>
        <li>Tờ 2 Mặt trước: Trang 6 | Trang 3</li>
        <li>Tờ 2 Mặt sau: Trang 4 | Trang 5</li>
      </ul>
      <h4>Trang ngang trong booklet</h4>
      <p>Chọn <strong>🔀 In cùng trang dọc</strong> để tự động xoay trang ngang, hiển thị cùng chiều với trang dọc trên cùng một tờ.</p>
    ` },
    manual: { content: `
      <h3>In thủ công (Manual Duplex)</h3>
      <p>Sau khi máy in xong mặt trước, app sẽ hiển thị hướng dẫn để bạn lật giấy đúng cách trước khi in mặt sau.</p>
      <h4>Các bước thực hiện</h4>
      <ol>
        <li>✅ <strong>Chờ máy in xong</strong> — đèn ngừng nhấp nháy</li>
        <li>✅ <strong>Lấy chồng giấy ra</strong> — theo đúng hướng mũi tên trong animation</li>
        <li>✅ <strong>Đặt lại vào khay</strong> — mặt trắng ngửa lên, đúng chiều</li>
        <li>Nhấn <strong>✓ Đã Đặt Giấy - Tiếp Tục In</strong></li>
      </ol>
      <h4>Tự động tiếp tục</h4>
      <p>Bật checkbox <em>"Tự động tiếp tục sau 30 giây"</em> để app tự động in mặt sau mà không cần nhấn nút.</p>
      <h4>Lưu ý quan trọng</h4>
      <ul>
        <li>Không đặt thêm giấy mới — chỉ dùng tờ vừa in</li>
        <li>Đảm bảo chiều giấy đúng theo hướng dẫn animation</li>
        <li>Nếu in sai mặt, thử đổi chiều lật giấy</li>
      </ul>
    ` },
  },
};
```

- [ ] **Step 2.2: Create `frontend/i18n/en.js`**

Create file `frontend/i18n/en.js`:

```js
window.EN_STRINGS = {
  app: { title: 'Smart Printer' },

  header: {
    printer: { placeholder: '🖨 Select printer…' },
    mode: { duplex: 'Smart Print', booklet: 'Booklet' },
    pageRange: { placeholder: 'Pages: 1-∞', ariaLabel: 'Select pages' },
    viewGroup: { title: 'View mode' },
  },

  btn: {
    print: '🖨 Print',
    settings: '⚙️ Settings',
    guide: '❓ Help',
    addFile: '+ Add File',
    historyClear: 'Clear',
    historyToggle: '📋 Print History',
  },

  view: {
    page: '📄 Page View',
    sheet: '🖨 Print Preview',
  },

  landscape: {
    label: 'Landscape pages:',
    together: '🔀 Together with portrait',
    separate: '⬜ Separate sheet',
  },

  copies: {
    label: 'Copies:',
    collate: ' Collate',
    decTitle: 'Decrease copies',
    incTitle: 'Increase copies',
  },

  preview: {
    empty: 'Drag files here or click <strong>+ Add File</strong>',
    thumbEmpty: 'No file yet',
    dropHint: 'Drop file here',
    loading: 'Loading...',
  },

  orientation: {
    landscape: '↔ Landscape',
    portrait: '↕ Portrait',
  },

  file: {
    ready: 'Ready',
  },

  zoom: {
    allPages: 'All pages',
    allDouble: 'All double-sided',
    allSingle: 'All single-sided',
    deselectAll: '✕ Deselect all',
    hint: 'Right-click a page to set single-sided',
    scrollHint: 'Scroll to see all',
    page: (n) => `Page ${n}`,
    doubleSided: '2-SIDED',
    singleSided: '1-SIDED',
  },

  ctx: {
    sides: 'Print sides',
    double: '2-sided',
    single: '1-sided',
    allDouble: 'All: 2-sided',
    allSingle: 'All: 1-sided',
    deselectAll: 'Deselect all',
    rotate: 'Rotate page',
    rotateCW: 'Rotate right 90°',
    rotateCCW: 'Rotate left 90°',
    rotate180: 'Rotate 180°',
    flipH: 'Flip horizontal',
    flipV: 'Flip vertical',
    rotateReset: 'Reset rotation',
    insert: 'Insert',
    blankBefore: 'Blank page before',
    blankAfter: 'Blank page after',
    imageBefore: 'Image before',
    imageAfter: 'Image after',
    blankPage: 'Blank page',
    page: (n) => `Page ${n}`,
  },

  flip: {
    title: '📄 Paper Placement Guide',
    check1: '1. Wait for printing to finish — light stops blinking',
    check2: '2. Remove the paper stack — follow the arrow direction',
    check3: '3. Place back in tray — blank side facing up',
    autoTimer: 'Auto-continue after <strong>30 seconds</strong>',
    continue: '✓ Paper Placed - Continue Printing',
  },

  confirm: {
    title: '🖨️ Confirm Print Job',
    cancel: 'Cancel',
    ok: '✓ Confirm Print',
    noPages: 'No pages to print.',
  },

  print: {
    start: '🖨 Start Printing',
    cancel: '⏹ Cancel Print',
    printing: (i, total) => `⏳ Printing file ${i}/${total}...`,
    done: (n) => n > 1 ? `✅ Printed ${n} files!` : '✅ Print job sent!',
  },

  summary: {
    pages: (n) => `📄 ${n} pages`,
    sheets: (n) => `🖨 ${n} sheets`,
    copies: (n) => `× ${n} copies`,
    allSelected: 'All selected',
    selected: (sel, total) => `${sel}/${total} pages selected`,
    total: (total) => `${total} pages`,
  },

  history: {
    empty: 'No print history yet',
  },

  sheet: {
    bookletLabel: (i, total) => `Sheet ${i}/${total} – Booklet`,
    label: (i, total) => `Sheet ${i}/${total}`,
    front: 'Front',
    back: 'Back',
    noprint: 'Not printed',
    page: (n) => `Front – Page ${n}`,
    backPage: (n) => `Back – Page ${n}`,
  },

  toast: {
    settingsOpened: 'Printer settings opened',
    settingsError: 'Could not open printer settings',
    uploadError: 'File upload error',
    convertError: 'File conversion error',
  },

  error: {
    uploadFailed: (name, msg) => `Could not upload <b>${name}</b>: ${msg}`,
    convertFailed: (name, msg) => `Could not convert <b>${name}</b>: ${msg}`,
  },

  guide: {
    title: 'User Guide',
    tab: {
      overview: 'Overview',
      smart: 'Smart Print',
      booklet: 'Booklet',
      manual: 'Manual Duplex',
    },
    overview: { content: `
      <h3>Overview</h3>
      <p><strong>Smart Printer</strong> is designed for single-sided printers, enabling manual duplex printing and booklet A5 creation.</p>
      <h4>Key Features</h4>
      <ul>
        <li>📄 <strong>Page View</strong> — view document pages</li>
        <li>🖨 <strong>Print Preview</strong> — see actual layout on paper</li>
        <li>🔀 <strong>Page Selection</strong> — right-click pages to set 1-sided or 2-sided</li>
        <li>📋 <strong>Print History</strong> — review past print jobs</li>
      </ul>
      <h4>Basic Workflow</h4>
      <ol>
        <li>Select a printer from the list</li>
        <li>Drag & drop files or click <strong>+ Add File</strong></li>
        <li>Choose print mode (Smart Print or Booklet)</li>
        <li>Click <strong>🖨 Print</strong> and follow instructions</li>
      </ol>
    ` },
    smart: { content: `
      <h3>Smart Print</h3>
      <p><strong>Smart Print</strong> enables manual duplex on a single-sided printer.</p>
      <h4>How It Works</h4>
      <ol>
        <li>App prints all <strong>front sides</strong> first (odd pages)</li>
        <li>Animation guide appears — follow it to flip paper correctly</li>
        <li>App automatically prints <strong>back sides</strong> (even pages)</li>
      </ol>
      <h4>Paper Flip Direction</h4>
      <ul>
        <li>↕ <strong>Portrait pages</strong>: Flip along the long edge (flip up/down)</li>
        <li>↔ <strong>Landscape pages</strong>: Flip along the short edge (flip left/right)</li>
      </ul>
      <h4>Single-Sided Pages</h4>
      <p>Right-click any page → <em>Print sides → 1-sided</em> to exclude it from duplex printing.</p>
    ` },
    booklet: { content: `
      <h3>Booklet Printing</h3>
      <p><strong>Booklet</strong> mode prints 4 A5 pages on 2 sides of A4 paper — fold in half to get a small book.</p>
      <h4>Automatic Page Order</h4>
      <p>The app automatically calculates page order so that after folding, pages are in correct sequence 1, 2, 3, 4...</p>
      <p><em>Example with 8 pages:</em></p>
      <ul>
        <li>Sheet 1 Front: Page 8 | Page 1</li>
        <li>Sheet 1 Back: Page 2 | Page 7</li>
        <li>Sheet 2 Front: Page 6 | Page 3</li>
        <li>Sheet 2 Back: Page 4 | Page 5</li>
      </ul>
      <h4>Landscape Pages in Booklet</h4>
      <p>Choose <strong>🔀 Together with portrait</strong> to auto-rotate landscape pages so they appear correctly alongside portrait pages on the same sheet.</p>
    ` },
    manual: { content: `
      <h3>Manual Duplex</h3>
      <p>After printing the front side, the app shows instructions for flipping paper correctly before printing the back.</p>
      <h4>Steps</h4>
      <ol>
        <li>✅ <strong>Wait for printing to finish</strong> — light stops blinking</li>
        <li>✅ <strong>Remove paper stack</strong> — follow the arrow direction in the animation</li>
        <li>✅ <strong>Place back in tray</strong> — blank side facing up, correct orientation</li>
        <li>Click <strong>✓ Paper Placed - Continue Printing</strong></li>
      </ol>
      <h4>Auto-Continue</h4>
      <p>Enable <em>"Auto-continue after 30 seconds"</em> to automatically start back-side printing without clicking.</p>
      <h4>Important Notes</h4>
      <ul>
        <li>Do not add new paper — use only the just-printed sheets</li>
        <li>Ensure paper orientation matches the animation guide</li>
        <li>If printing on wrong side, try flipping in the opposite direction</li>
      </ul>
    ` },
  },
};
```

- [ ] **Step 2.3: Commit**

```powershell
git add frontend/i18n/vi.js frontend/i18n/en.js
git commit -m "feat(i18n): add VI and EN string files"
```

---

## Task 3: Add `I18nModule` to `app.js`

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 3.1: Add `I18nModule` constant near the top of `app.js`**

Find the line that starts with `// ═══` (the first section divider, around line 1). Add the following block just before the first `const` module definition in `app.js` (search for `const AppState` at the top):

```js
// ── i18n ─────────────────────────────────────────────────────────
const I18nModule = {
    _lang: localStorage.getItem('lang') || 'vi',
    _strings: {},

    init() {
        this.setLang(this._lang);
    },

    t(key) {
        // Support dot-notation keys like 'btn.print'
        const val = key.split('.').reduce((o, k) => o?.[k], this._strings);
        return (val !== undefined && val !== null) ? val : key;
    },

    setLang(lang) {
        this._lang = lang;
        this._strings = (lang === 'en' ? window.EN_STRINGS : window.VI_STRINGS) || {};
        localStorage.setItem('lang', lang);
        this.applyAll();
        this._updateToggleBtn();
    },

    applyAll() {
        // Static text nodes
        document.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.dataset.i18n;
            const text = this.t(key);
            if (text !== key) el.textContent = text;
        });
        // Placeholders
        document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            const key = el.dataset.i18nPlaceholder;
            const text = this.t(key);
            if (text !== key) el.placeholder = text;
        });
        // Title attributes
        document.querySelectorAll('[data-i18n-title]').forEach(el => {
            const key = el.dataset.i18nTitle;
            const text = this.t(key);
            if (text !== key) el.title = text;
        });
        // Re-render guide body if guide modal is open
        if (!document.getElementById('guide-modal')?.classList.contains('hidden')) {
            GuideModule.renderCurrentTab();
        }
        // Update printer select placeholder
        const printerSel = document.getElementById('printer-select');
        if (printerSel) {
            const placeholderOpt = printerSel.querySelector('option[value=""]');
            if (placeholderOpt) placeholderOpt.textContent = this.t('header.printer.placeholder');
        }
        // Update mode select options
        const modeSel = document.getElementById('mode-select');
        if (modeSel) {
            modeSel.querySelector('option[value="duplex"]')?.setAttribute('_i18n_applied', '');
            modeSel.querySelectorAll('option').forEach(opt => {
                if (opt.value === 'duplex') opt.textContent = this.t('header.mode.duplex');
                if (opt.value === 'booklet') opt.textContent = this.t('header.mode.booklet');
            });
        }
    },

    _updateToggleBtn() {
        const btn = document.getElementById('lang-toggle-btn');
        if (btn) btn.textContent = `🌐 ${this._lang.toUpperCase()}`;
    },
};
```

- [ ] **Step 3.2: Verify syntax**

```powershell
node --check frontend/app.js
```

Expected: no output (success)

- [ ] **Step 3.3: Commit**

```powershell
git add frontend/app.js
git commit -m "feat(i18n): add I18nModule to app.js"
```

---

## Task 4: Add CSS for New Buttons and Guide Modal

**Files:**
- Modify: `frontend/styles.css`

- [ ] **Step 4.1: Add CSS for `btn-icon-sm` and guide modal**

Open `frontend/styles.css`. Find the `.btn-print` block (around line 2209) and add the following block immediately after `.btn-print:disabled { ... }` (around line 2230):

```css
/* ── Icon-style small buttons (Settings, Guide, Language) ── */
.btn-icon-sm {
  height:           34px;
  padding:          0 12px;
  border:           1.5px solid #93c5fd;
  border-radius:    8px;
  background:       #ffffff;
  color:            #1e3a5f;
  font-family:      var(--font-system);
  font-size:        13px;
  font-weight:      500;
  cursor:           pointer;
  white-space:      nowrap;
  transition:       background 0.15s, border-color 0.15s;
  flex-shrink:      0;
}
.btn-icon-sm:hover          { background: #eff6ff; border-color: #3b82f6; }
.btn-icon-sm:active         { background: #dbeafe; }
.btn-icon-sm:disabled       { opacity: 0.35; cursor: not-allowed; }

/* ── Guide modal ───────────────────────────────────────────── */
.modal-guide {
  max-width:        720px;
  width:            90vw;
  max-height:       85vh;
  display:          flex;
  flex-direction:   column;
}
.guide-tabs {
  display:          flex;
  gap:              4px;
  padding:          0 20px;
  border-bottom:    1px solid #e2e8f0;
  flex-shrink:      0;
}
.guide-tab {
  padding:          10px 16px;
  border:           none;
  background:       none;
  cursor:           pointer;
  color:            #64748b;
  font-family:      var(--font-system);
  font-size:        13px;
  font-weight:      500;
  border-bottom:    2px solid transparent;
  transition:       color 0.15s, border-color 0.15s;
}
.guide-tab:hover            { color: #2563eb; }
.guide-tab.active           { color: #2563eb; border-bottom-color: #2563eb; }
.guide-body {
  flex:             1;
  overflow-y:       auto;
  padding:          20px 24px;
  line-height:      1.75;
  color:            #1e3a5f;
}
.guide-body h3              { margin: 0 0 12px; font-size: 16px; color: #1e40af; }
.guide-body h4              { margin: 16px 0 8px; font-size: 14px; color: #1e3a5f; }
.guide-body p               { margin: 0 0 10px; }
.guide-body ul, .guide-body ol { margin: 0 0 10px; padding-left: 24px; }
.guide-body li              { margin-bottom: 6px; }
```

- [ ] **Step 4.2: Commit**

```powershell
git add frontend/styles.css
git commit -m "feat(ui): add btn-icon-sm and guide modal CSS"
```

---

## Task 5: Update `index.html` — New Buttons, Guide Modal, i18n Attributes, Script Tags

**Files:**
- Modify: `frontend/index.html`

- [ ] **Step 5.1: Add i18n script tags and update app.js version**

In `frontend/index.html`, replace:
```html
<script src="app.js?v=19"></script>
```
with:
```html
<script src="i18n/vi.js"></script>
<script src="i18n/en.js"></script>
<script src="app.js?v=20"></script>
```

- [ ] **Step 5.2: Add `data-i18n` to static text elements in header**

Replace the header section (lines 26–63) entirely with:

```html
  <!-- ── Row 1: Settings bar ────────────────────────────── -->
  <header class="header-settings">
    <!-- Printer selector -->
    <select id="printer-select" class="header-select" aria-label="Chọn máy in">
      <option value="" data-i18n="header.printer.placeholder">🖨 Chọn máy in…</option>
    </select>

    <!-- Print mode selector -->
    <select id="mode-select" class="header-select" aria-label="Chế độ in">
      <option value="duplex" data-i18n="header.mode.duplex">In thông minh</option>
      <option value="booklet" data-i18n="header.mode.booklet">In Sách</option>
    </select>

    <!-- Page range -->
    <input id="page-range-input"
           class="header-select"
           type="text"
           placeholder="Trang: 1-∞"
           data-i18n-placeholder="header.pageRange.placeholder"
           aria-label="Chọn trang"
           style="width:110px; padding-right:8px; background-image:none;">

    <div class="header-spacer"></div>

    <!-- View mode toggle -->
    <div class="view-toggle-group" id="view-toggle-group" data-i18n-title="header.viewGroup.title" title="Chế độ xem">
      <button class="view-toggle-btn active" id="view-btn-page" data-view="page" data-i18n="view.page">📄 Xem nội dung</button>
      <button class="view-toggle-btn" id="view-btn-sheet" data-view="sheet" data-i18n="view.sheet">🖨 Xem trước khi in</button>
    </div>

    <!-- Landscape mode toggle (only visible in sheet view) -->
    <div class="sheet-view-modebar" id="sheet-view-modebar" style="display:none">
      <span class="sheet-modebar-label" data-i18n="landscape.label">Trang ngang:</span>
      <button class="sheet-modebar-btn active" data-lsmode="together" data-i18n="landscape.together">🔀 In cùng trang dọc</button>
      <button class="sheet-modebar-btn" data-lsmode="separate" data-i18n="landscape.separate">⬜ In tờ riêng</button>
    </div>

    <!-- Settings, Guide, Language buttons -->
    <button id="printer-settings-btn" class="btn-icon-sm" disabled data-i18n="btn.settings">⚙️ Cài đặt</button>
    <button id="guide-btn" class="btn-icon-sm" data-i18n="btn.guide">❓ Hướng dẫn</button>
    <button id="lang-toggle-btn" class="btn-icon-sm">🌐 VI</button>

    <!-- Print button -->
    <button id="print-btn" class="btn-print" disabled data-i18n="btn.print">🖨 In</button>
  </header>
```

- [ ] **Step 5.3: Add `data-i18n` to file tabs row**

Replace lines 65–81 with:

```html
  <!-- ── Row 2: File tabs ───────────────────────────────── -->
  <div id="file-tabs" class="file-tabs-bar" role="tablist" aria-label="Các file đang mở">
    <div id="file-tab-list" class="file-tab-list"></div>
    <button id="file-tab-add-btn" class="file-tab-add" type="button" data-i18n="btn.addFile">+ Thêm file</button>
    <!-- Copies widget (per-file) -->
    <div class="copies-widget" id="copies-widget">
      <span class="copies-label" data-i18n="copies.label">Số bản:</span>
      <div class="copies-control">
        <button class="copies-btn" id="copies-dec" data-i18n-title="copies.decTitle" title="Giảm số bản">−</button>
        <span class="copies-val" id="copies-display">1</span>
        <button class="copies-btn" id="copies-inc" data-i18n-title="copies.incTitle" title="Tăng số bản">+</button>
      </div>
      <label class="collate-label" id="collate-label" style="display:none">
        <input type="checkbox" id="collate-check" checked><span data-i18n="copies.collate"> Ghép bộ</span>
      </label>
    </div>
  </div>
```

- [ ] **Step 5.4: Add `data-i18n` to preview panel and history**

Replace lines 83–114 with:

```html
  <!-- ── Body ──────────────────────────────────────────── -->
  <div class="app-body">

    <!-- Left: thumbnail strip -->
    <nav class="thumb-panel" aria-label="Danh sách trang">
      <div id="thumb-strip" class="thumb-panel-inner">
        <!-- Rendered by ThumbStripModule -->
        <div class="preview-empty" style="padding:16px;text-align:center">
          <span class="preview-empty-icon">📄</span>
          <span data-i18n="preview.thumbEmpty">Chưa có file</span>
        </div>
      </div>
    </nav>

    <!-- Right: preview area -->
    <main id="preview-panel" class="preview-panel" aria-label="Xem trước tài liệu">
      <div class="preview-empty">
        <span class="preview-empty-icon">🖨</span>
        <span data-i18n="preview.empty">Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong></span>
      </div>

      <!-- Print History (collapsible at bottom of preview) -->
      <div class="history-section" style="margin-top:auto;">
        <div class="history-header">
          <button class="btn-outline" id="history-toggle-btn" type="button" data-i18n="btn.historyToggle">📋 Lịch sử in</button>
          <button class="btn-outline" id="history-clear-btn" type="button" data-i18n="btn.historyClear" style="color:#ef4444;border-color:rgba(239,68,68,0.4)">Xóa</button>
        </div>
        <div id="history-panel" class="history-panel hidden">
          <div id="history-list"></div>
        </div>
      </div>
    </main>

  </div><!-- /.app-body -->
```

- [ ] **Step 5.5: Add `data-i18n` to zoom modal and flip modal static text**

Replace the zoom modal section (lines 134–154) with:

```html
<!-- ── Page Zoom Modal ────────────────────────────────── -->
<div id="page-zoom-modal" class="modal hidden">
    <div class="modal-overlay" id="zoom-modal-overlay"></div>
    <div class="modal-content modal-zoom">
        <div class="modal-header">
            <h2 id="zoom-page-title" data-i18n="zoom.allPages">Tất cả trang</h2>
            <button class="modal-close" id="zoom-modal-close">✕</button>
        </div>
        <div class="modal-body modal-zoom-body">
            <div class="zoom-toolbar">
                <button class="zoom-action-btn" id="all-double-btn" data-i18n="zoom.allDouble">Tất cả 2 mặt</button>
                <button class="zoom-action-btn" id="all-single-btn" data-i18n="zoom.allSingle">Tất cả 1 mặt</button>
                <button class="zoom-action-btn" id="deselect-all-btn" data-i18n="zoom.deselectAll">✕ Bỏ chọn tất cả</button>
                <span class="zoom-page-info" id="zoom-page-info" data-i18n="zoom.hint">Click phải vào trang để chọn in 1 mặt</span>
            </div>
            <div class="zoom-canvas-container">
                <canvas id="zoom-canvas"></canvas>
            </div>
        </div>
    </div>
</div>
```

Replace the flip modal section (lines 156–197) with:

```html
<!-- ── Manual Duplex Instruction Modal (upgraded) (3) ── -->
<div id="flip-modal" class="modal hidden">
    <div class="modal-overlay"></div>
    <div class="modal-content modal-flip">
        <div class="modal-header">
            <h2 data-i18n="flip.title">📄 Hướng Dẫn Đặt Giấy</h2>
        </div>
        <div class="modal-body">
            <!-- Animated flip diagram -->
            <div class="flip-diagram" id="instruction-visual"></div>
            <p class="instruction-text" id="instruction-text"></p>

            <!-- Checklist (3) -->
            <div class="flip-checklist" id="flip-checklist">
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-1">
                    <span class="flip-check-label" data-i18n="flip.check1">1. Chờ máy in xong — đèn ngừng nhấp nháy</span>
                </label>
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-2">
                    <span class="flip-check-label" data-i18n="flip.check2">2. Lấy chồng giấy ra — theo đúng hướng mũi tên</span>
                </label>
                <label class="flip-check-item">
                    <input type="checkbox" class="flip-checkbox" id="flip-check-3">
                    <span class="flip-check-label" data-i18n="flip.check3">3. Đặt lại vào khay — mặt trắng ngửa lên</span>
                </label>
            </div>

            <!-- Auto-continue timer (optional) -->
            <label class="flip-timer-toggle">
                <input type="checkbox" id="flip-timer-enable">
                <span data-i18n="flip.autoTimer">Tự động tiếp tục sau <strong>30 giây</strong></span>
            </label>
            <div class="flip-timer-bar hidden" id="flip-timer-bar">
                <div class="flip-timer-fill" id="flip-timer-fill"></div>
            </div>
        </div>
        <div class="modal-footer">
            <button id="continue-btn" class="btn-primary flip-continue-btn" data-i18n="flip.continue">✓ Đã Đặt Giấy - Tiếp Tục In</button>
        </div>
    </div>
</div>
```

Replace the confirm print modal section (lines 304–320) with:

```html
<!-- ── Print Confirmation Modal (7) ───────────────────── -->
<div id="confirm-print-modal" class="modal hidden">
    <div class="modal-overlay" id="confirm-modal-overlay"></div>
    <div class="modal-content modal-confirm">
        <div class="modal-header">
            <h2 data-i18n="confirm.title">🖨️ Xác Nhận Lệnh In</h2>
            <button class="modal-close" id="confirm-modal-close">✕</button>
        </div>
        <div class="modal-body">
            <div id="confirm-print-summary" class="confirm-print-summary"></div>
        </div>
        <div class="modal-footer">
            <button id="confirm-print-cancel-btn" class="btn-outline" data-i18n="confirm.cancel">Huỷ</button>
            <button id="confirm-print-ok-btn" class="btn-primary" data-i18n="confirm.ok">✓ Xác Nhận In</button>
        </div>
    </div>
</div>
```

- [ ] **Step 5.6: Add Guide modal HTML (before closing `</body>`)**

Before `<script src="i18n/vi.js">` (which you added in Step 5.1), add the guide modal:

```html
<!-- ── User Guide Modal ───────────────────────────────── -->
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
            <!-- Content injected by GuideModule.renderCurrentTab() -->
        </div>
    </div>
</div>
```

- [ ] **Step 5.7: Add `data-i18n` to drop hint and context menu**

Replace lines 125–129:
```html
<div class="upload-drop-hint" id="drop-hint">
  <span style="font-size:32px">📄</span>
  <span style="font-size:18px; color:var(--color-accent); margin-left:12px" data-i18n="preview.dropHint">Thả file vào đây</span>
</div>
```

In the context menu (lines 199–296), add `data-i18n` to each `<span>` that contains text:
- `Trang 1` → keep dynamic (set by JS), no `data-i18n`
- `Mặt in` → `data-i18n="ctx.sides"`
- `2 mặt` → `data-i18n="ctx.double"`
- `1 mặt` → `data-i18n="ctx.single"`
- `Tất cả: 2 mặt` → `data-i18n="ctx.allDouble"`
- `Tất cả: 1 mặt` → `data-i18n="ctx.allSingle"`
- `Bỏ chọn tất cả` → `data-i18n="ctx.deselectAll"`
- `Xoay trang` → `data-i18n="ctx.rotate"`
- `Xoay phải 90°` → `data-i18n="ctx.rotateCW"`
- `Xoay trái 90°` → `data-i18n="ctx.rotateCCW"`
- `Xoay 180°` → `data-i18n="ctx.rotate180"`
- `Lật ngang` → `data-i18n="ctx.flipH"`
- `Lật dọc` → `data-i18n="ctx.flipV"`
- `Reset về gốc` → `data-i18n="ctx.rotateReset"`
- `Chèn` → `data-i18n="ctx.insert"`
- `Trang trắng trước` → `data-i18n="ctx.blankBefore"`
- `Trang trắng sau` → `data-i18n="ctx.blankAfter"`
- `Ảnh trước` → `data-i18n="ctx.imageBefore"`
- `Ảnh sau` → `data-i18n="ctx.imageAfter"`

- [ ] **Step 5.8: Commit**

```powershell
git add frontend/index.html
git commit -m "feat(i18n): add data-i18n attributes and new buttons to index.html"
```

---

## Task 6: Add New JS Modules (`GuideModule`, `PrinterSettingsModule`, `LangToggleModule`) to `app.js`

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 6.1: Add `GuideModule` to `app.js`**

Near the end of `app.js`, before the `DOMContentLoaded` event handler, add:

```js
// ── GuideModule ────────────────────────────────────────────────
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
        // Close on Escape key
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && !document.getElementById('guide-modal')?.classList.contains('hidden')) {
                this.close();
            }
        });
    },

    open() {
        document.getElementById('guide-modal')?.classList.remove('hidden');
        this.renderCurrentTab();
    },

    close() {
        document.getElementById('guide-modal')?.classList.add('hidden');
    },

    renderCurrentTab() {
        const body = document.getElementById('guide-body');
        if (!body) return;
        const content = I18nModule.t(`guide.${this._activeTab}.content`);
        body.innerHTML = typeof content === 'string' ? content : '';
    },
};
```

- [ ] **Step 6.2: Add `PrinterSettingsModule` to `app.js`**

Immediately after `GuideModule`, add:

```js
// ── PrinterSettingsModule ──────────────────────────────────────
const PrinterSettingsModule = {
    init() {
        const btn = document.getElementById('printer-settings-btn');
        if (!btn) return;

        // Initial state — sync with current printer select value
        const printerSel = document.getElementById('printer-select');
        btn.disabled = !printerSel?.value;

        btn.addEventListener('click', async () => {
            const printerName = document.getElementById('printer-select')?.value;
            if (!printerName) return;
            btn.disabled = true;
            const originalText = btn.textContent;
            btn.textContent = '⏳';
            try {
                const res = await fetch('http://localhost:8787/api/printer/settings', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ printerName }),
                });
                const data = await res.json();
                if (data.success) {
                    showToast(I18nModule.t('toast.settingsOpened'));
                } else {
                    showToast(data.message || I18nModule.t('toast.settingsError'), 'error');
                }
            } catch (e) {
                showToast(I18nModule.t('toast.settingsError'), 'error');
            } finally {
                btn.disabled = !document.getElementById('printer-select')?.value;
                btn.textContent = originalText;
            }
        });

        // Sync enabled state when printer selection changes
        document.getElementById('printer-select')?.addEventListener('change', e => {
            btn.disabled = !e.target.value;
        });
    },
};
```

- [ ] **Step 6.3: Add `LangToggleModule` to `app.js`**

Immediately after `PrinterSettingsModule`, add:

```js
// ── LangToggleModule ───────────────────────────────────────────
const LangToggleModule = {
    init() {
        document.getElementById('lang-toggle-btn')?.addEventListener('click', () => {
            const next = I18nModule._lang === 'vi' ? 'en' : 'vi';
            I18nModule.setLang(next);
        });
    },
};
```

- [ ] **Step 6.4: Wire all new modules in DOMContentLoaded**

Find the `DOMContentLoaded` event handler at the bottom of `app.js`. It starts with something like:

```js
document.addEventListener('DOMContentLoaded', () => {
```

Add these calls inside it, **after** all other module `init()` calls and as the very last items:

```js
    GuideModule.init();
    PrinterSettingsModule.init();
    LangToggleModule.init();
    I18nModule.init(); // Must be last — applies translations to all already-wired elements
```

- [ ] **Step 6.5: Verify syntax**

```powershell
node --check frontend/app.js
```

Expected: no output (success)

- [ ] **Step 6.6: Commit**

```powershell
git add frontend/app.js
git commit -m "feat(ui): add GuideModule, PrinterSettingsModule, LangToggleModule to app.js"
```

---

## Task 7: Migrate Key Dynamic Strings in `app.js` to i18n

**Files:**
- Modify: `frontend/app.js`

This task migrates the most user-visible dynamic strings. Non-exhaustive — focus on strings that change meaning between VI/EN.

- [ ] **Step 7.1: Migrate history empty message**

Find (around line 2424):
```js
container.innerHTML = '<div class="history-empty">Chưa có lịch sử in</div>';
```
Replace with:
```js
container.innerHTML = `<div class="history-empty">${I18nModule.t('history.empty')}</div>`;
```

- [ ] **Step 7.2: Migrate print button states**

Find the print button text assignments. There are several patterns. Replace each:

Find (around line 2480):
```js
btn.innerHTML = '<span class="btn-icon">🖨</span> Bắt Đầu In';
```
Replace with:
```js
btn.innerHTML = `<span class="btn-icon">🖨</span> ${I18nModule.t('print.start').replace('🖨 ', '')}`;
```

Find (around line 2552):
```js
btn.textContent = filesToPrint.length > 1
```
(This sets printing-in-progress text — update the template string to use `I18nModule.t('print.printing')` as a function call):

Find the line that sets `btn.textContent` to something like `⏳ Đang in file ...`:
```js
btn.textContent = `⏳ Đang in file ${i + 1}/${files.length}...`;
```
Replace with:
```js
btn.textContent = I18nModule.t('print.printing')(i + 1, files.length);
```

Find (around line 2702):
```js
btn.textContent = fileCount > 1 ? `✅ Đã in ${fileCount} file!` : '✅ Đã gửi lệnh in!';
```
Replace with:
```js
btn.textContent = I18nModule.t('print.done')(fileCount);
```

- [ ] **Step 7.3: Migrate orientation badge**

Find (around line 1699):
```js
badge.textContent = isLandscape ? '↔ Ngang' : '↕ Dọc';
```
Replace with:
```js
badge.textContent = isLandscape ? I18nModule.t('orientation.landscape') : I18nModule.t('orientation.portrait');
```

- [ ] **Step 7.4: Migrate file ready status**

Find occurrences of `'Đã sẵn sàng'` (around lines 1287, 1353, 1542):
```js
if (fsEl) fsEl.textContent = 'Đã sẵn sàng';
```
Replace all 3 occurrences with:
```js
if (fsEl) fsEl.textContent = I18nModule.t('file.ready');
```

- [ ] **Step 7.5: Migrate preview loading text**

Find (around line 564):
```js
mainContainer.innerHTML = '<div class="loading" ...>Đang tải...</div>';
```
Replace with:
```js
mainContainer.innerHTML = `<div class="loading" style="padding:2rem;text-align:center;color:var(--text-muted)">${I18nModule.t('preview.loading')}</div>`;
```

- [ ] **Step 7.6: Migrate page/sheet labels in sheet view**

Find (around line 4282):
```js
label.textContent = `Tờ ${sheet.sheetIndex}/${totalSheets} – Booklet`;
```
Replace with:
```js
label.textContent = I18nModule.t('sheet.bookletLabel')(sheet.sheetIndex, totalSheets);
```

Find (around line 4284):
```js
label.textContent = `Tờ ${sheet.sheetIndex}/${totalSheets}`;
```
Replace with:
```js
label.textContent = I18nModule.t('sheet.label')(sheet.sheetIndex, totalSheets);
```

Find (around line 4412):
```js
frontLabel.textContent = 'Mặt trước';
```
Replace with:
```js
frontLabel.textContent = I18nModule.t('sheet.front');
```

Find (around line 4424):
```js
backLabel2.textContent = 'Mặt sau';
```
Replace with:
```js
backLabel2.textContent = I18nModule.t('sheet.back');
```

Find (around line 4467):
```js
header.textContent = 'Không in';
```
Replace with:
```js
header.textContent = I18nModule.t('sheet.noprint');
```

- [ ] **Step 7.7: Migrate printer select placeholder in JS**

Find (around line 1129):
```js
sel.innerHTML = '<option value="">🖨 Chọn máy in…</option>';
```
Replace with:
```js
sel.innerHTML = `<option value="">${I18nModule.t('header.printer.placeholder')}</option>`;
```

- [ ] **Step 7.8: Verify syntax**

```powershell
node --check frontend/app.js
```

Expected: no output (success)

- [ ] **Step 7.9: Commit**

```powershell
git add frontend/app.js
git commit -m "feat(i18n): migrate dynamic strings in app.js to I18nModule"
```

---

## Task 8: End-to-End Verification

- [ ] **Step 8.1: Open app in browser**

Open `frontend/index.html` directly in Chrome or start a local server:
```powershell
cd frontend
python -m http.server 8080
```
Navigate to `http://localhost:8080`

- [ ] **Step 8.2: Verify default VI language**

- All header text in Vietnamese: "🖨 Chọn máy in…", "In thông minh", "In Sách", "Trang: 1-∞", "📄 Xem nội dung", "🖨 Xem trước khi in"
- 3 new buttons visible: "⚙️ Cài đặt", "❓ Hướng dẫn", "🌐 VI"
- "⚙️ Cài đặt" button is disabled (no printer selected yet)

- [ ] **Step 8.3: Verify language toggle**

Click "🌐 VI" → should change to "🌐 EN" and ALL text switches to English:
- "🖨 Select printer…", "Smart Print", "Booklet", "Pages: 1-∞"
- "📄 Page View", "🖨 Print Preview"
- "⚙️ Settings", "❓ Help"

Refresh page → should stay in EN (localStorage persistence)

Click "🌐 EN" → switches back to VI.

- [ ] **Step 8.4: Verify Guide modal**

Click "❓ Hướng dẫn":
- Modal opens with title "Hướng dẫn sử dụng"
- 4 tabs: Tổng quan, In thông minh, In Sách, In thủ công
- Each tab shows correct content
- Click ✕ or overlay → closes

Switch language to EN → click "❓ Help":
- Modal title: "User Guide"
- Tabs: Overview, Smart Print, Booklet, Manual Duplex
- Content in English

- [ ] **Step 8.5: Verify Printer Settings button**

Start backend: `cd backend; dotnet run`

Select a printer from the dropdown → "⚙️ Cài đặt" becomes enabled.
Click it → Windows Printer Properties dialog opens for selected printer.
Toast appears: "Đã mở cài đặt máy in"

In EN mode: Toast shows "Printer settings opened"

- [ ] **Step 8.6: Verify no hardcoded Vietnamese in static HTML**

Check that all static text in `index.html` has `data-i18n` attributes and no bare Vietnamese text remains outside of attribute defaults.

- [ ] **Step 8.7: Final syntax check**

```powershell
node --check frontend/app.js
```

Expected: no output (success)

- [ ] **Step 8.8: Final commit and push**

```powershell
git add -A
git commit -m "feat: printer settings, user guide modal, and full i18n VI/EN support"
git push origin main
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All 10 acceptance criteria have corresponding tasks
- [x] **No placeholders:** All code blocks are complete and runnable
- [x] **Type consistency:** `I18nModule.t()` used consistently; function-valued strings called as `I18nModule.t('key')(args)`
- [x] **Backend model:** `PrinterSettingsRequest` defined in Task 1 and used in same task
- [x] **Script load order:** `vi.js` → `en.js` → `app.js` — globals available before `I18nModule.init()`
- [x] **`I18nModule.init()` called last** in DOMContentLoaded — all elements already in DOM
- [x] **GuideModule ref in I18nModule.applyAll()** — `GuideModule` is defined before `I18nModule` is initialized (it's defined earlier in the file)
