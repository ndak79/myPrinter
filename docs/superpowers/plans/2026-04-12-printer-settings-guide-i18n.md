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
| `frontend/app.js` | **Modify** | Add `I18nModule`, `PrinterSettingsModule`, `GuideModule`, `LangToggleModule`; migrate dynamic strings; fix race condition in `_renderPrinters` |
| `frontend/styles.css` | **Modify** | Add `.btn-icon-sm`, `.modal-guide`, `.guide-tabs`, `.guide-tab`, `.guide-body` |
| `backend/Models/PrintModels.cs` | **Modify** | Add `PrinterSettingsRequest` record |
| `backend/BackendStartup.cs` | **Modify** | Add `POST /api/printer/settings` endpoint (uses `System.Diagnostics`) |
| `backend/Program.cs` | **Modify** | Add console log for new endpoint |

---

## Task 1: Backend — Printer Settings Endpoint

**Files:**
- Modify: `backend/Models/PrintModels.cs`
- Modify: `backend/BackendStartup.cs`
- Modify: `backend/Program.cs`

- [ ] **Step 1.1: Add `PrinterSettingsRequest` model to `PrintModels.cs`**

  Open `backend/Models/PrintModels.cs`. Add at the end of the file (before the last `}`):

  ```csharp
  public record PrinterSettingsRequest(string PrinterName);
  ```

- [ ] **Step 1.2: Add `using System.Diagnostics;` to `BackendStartup.cs`**

  Open `backend/BackendStartup.cs`. Check the top of the file for existing `using` directives. If `using System.Diagnostics;` is not already present, add it with the other `using` statements at the top:

  ```csharp
  using System.Diagnostics;
  ```

- [ ] **Step 1.3: Add `POST /api/printer/settings` endpoint to `BackendStartup.cs`**

  In `backend/BackendStartup.cs`, after the `app.MapDelete("/api/print/cancel", ...)` block (around line 349) and before `return app;`, add:

  ```csharp
  app.MapPost("/api/printer/settings", (PrinterSettingsRequest req) =>
  {
      try
      {
          if (string.IsNullOrWhiteSpace(req.PrinterName))
              return Results.BadRequest(new { success = false, message = "Printer name is required" });

          var psi = new ProcessStartInfo
          {
              FileName = "rundll32.exe",
              Arguments = $"printui.dll,PrintUIEntry /p /n \"{req.PrinterName}\"",
              UseShellExecute = true,
          };
          Process.Start(psi);

          return Results.Ok(new { success = true, message = "Printer settings dialog opened" });
      }
      catch (Exception ex)
      {
          return Results.Problem($"Failed to open printer settings: {ex.Message}");
      }
  });
  ```

- [ ] **Step 1.4: Add console log to `Program.cs`**

  Open `backend/Program.cs`. Find the block where other endpoints are logged (e.g., `Console.WriteLine("  POST /api/print")` or similar). Add alongside them:

  ```csharp
  Console.WriteLine("  POST /api/printer/settings - Open Windows printer properties dialog");
  ```

- [ ] **Step 1.5: Build to verify no compile errors**

  ```powershell
  cd backend
  dotnet build
  ```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 1.6: Commit**

  ```powershell
  git add backend/Models/PrintModels.cs backend/BackendStartup.cs backend/Program.cs
  git commit -m "feat(backend): add POST /api/printer/settings endpoint"
  ```

---

## Task 2: Create i18n String Files

**Files:**
- Create: `frontend/i18n/vi.js`
- Create: `frontend/i18n/en.js`

- [ ] **Step 2.1: Create `frontend/i18n/vi.js`**

  Create directory `frontend/i18n/` if it doesn't exist, then create `frontend/i18n/vi.js`:

  ```js
  window.VI_STRINGS = {
    // App
    app: { title: 'Máy In Thông Minh' },

    // Header
    header: {
      printer: { placeholder: '🖨 Chọn máy in…', ariaLabel: 'Chọn máy in' },
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
      // SVG animation text (used in _showFlipModal)
      paperPrinted: 'Giấy đã in',
      side1Done: 'mặt 1 ✓',
      paperTray: 'Khay giấy',
      horizontalSteps: 'Lấy ra → Lật ngang → Đặt lại',
      verticalSteps: 'Lấy ra → Lật → Đặt lại',
      defaultInstruction: 'Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.',
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
      cancel: '✕ Huỷ In',
      printing: (i, total) => `⏳ Đang in file ${i}/${total}...`,
      sendingSingle: '⏳ Đang gửi lệnh in...',
      done: (n) => n > 1 ? `✓ Đã in ${n} file!` : '✓ Đã gửi lệnh in!',
    },

    // Summary bar
    summary: {
      pages: (n) => `📄 ${n} trang`,
      sheets: (n) => `🗒️ ${n} tờ`,
      copies: (n) => `· ${n} bản`,
      files: (n) => `· ${n} file`,
      allSelected: 'Tất cả được chọn',
      selected: (sel, total) => `${sel}/${total} trang được chọn`,
      total: (total) => `${total} trang`,
      lessThanMinute: '< 1 phút',
      minutes: (n) => `~${n} phút`,
      hoursMinutes: (h, m) => `~${h}h ${m}m`,
    },

    // History
    history: {
      empty: 'Chưa có lịch sử in',
    },

    // Sheet view labels
    sheet: {
      bookletLabel: (i, total) => `Tờ ${i}/${total} · Booklet`,
      label: (i, total) => `Tờ ${i}/${total}`,
      front: 'Mặt trước',
      back: 'Mặt sau',
      noprint: 'Không in',
      page: (n) => `Mặt trước · Trang ${n}`,
      backPage: (n) => `Mặt sau · Trang ${n}`,
      bookletFacePage: (n) => `Trang ${n ?? '—'}`,
      backSimplex: 'Mặt sau · (in 1 mặt)',
      backBlank: 'Mặt sau · Trang trắng',
      deleteBlank: 'Xóa trang trắng',
      ejectHint: (n) => `Trang ${n} — nhấn để thêm vào bản in`,
      ejectLabel: (n) => `Trang ${n}`,
    },

    // Toasts / messages
    toast: {
      settingsOpened: 'Đã mở cài đặt máy in',
      settingsError: 'Không thể mở cài đặt máy in',
      printerSelected: (name) => `Đã chọn máy in: ${name}`,
      printerLoadError: (msg) => `Lỗi tải danh sách máy in: ${msg}`,
      printerLost: 'Máy in đã chọn không còn khả dụng. Vui lòng chọn lại.',
      fileTypeUnsupported: (name) => `Loại file không được hỗ trợ: ${name}`,
      fileTooLarge: (name) => `File quá lớn (tối đa 100MB): ${name}`,
      uploadError: (msg) => `Lỗi: ${msg}`,
      converting: (name) => `Đang chuyển đổi ${name}...`,
      convertError: (name) => `Lỗi chuyển đổi: ${name}`,
      uploadSuccess: (name, pages) => `Đã tải: ${name} (${pages} trang)`,
      pdfLoaded: (pages) => `Đã tải ${pages} trang`,
      allDouble: 'Đã chọn tất cả in 2 mặt',
      allSingle: 'Đã chọn tất cả in 1 mặt',
      deselectAll: 'Đã bỏ chọn tất cả. Chọn trang để in.',
      pageDuplex: (n) => `Trang ${n} sẽ in 2 mặt`,
      pageSimplex: (n) => `Trang ${n} sẽ in 1 mặt`,
      blankInserted: 'Đã chèn trang trắng',
      rotateReset: (n) => `Trang ${n}: đã reset xoay`,
      rotated: (n, label) => `Trang ${n}: ${label}`,
      historyCleared: 'Đã xóa lịch sử',
      historyItemRemoved: 'Đã xóa mục lịch sử',
      printCancelled: 'Đã hủy lệnh in',
      printCancelFailed: 'Không thể hủy lệnh in',
      selectPrinterFirst: 'Chọn máy in trước',
      noPagesSelected: 'Không có trang nào được chọn để in',
      sendingFile: (name) => `Đang gửi lệnh in: ${name}...`,
      printFileError: (name, msg) => `Lỗi in file ${name}: ${msg}`,
      frontDone: 'Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.',
      printSuccess: (n) => n > 1 ? `In thành công ${n} file!` : 'In thành công!',
      printError: (msg) => `Lỗi khi in: ${msg}`,
      printingBack: 'Đang in mặt chẵn...',
      printComplete: 'In hoàn tất!',
      continueError: (msg) => `Lỗi khi tiếp tục in: ${msg}`,
      pageReordered: 'Đã đổi thứ tự trang',
      fileLoadError: (msg) => `Lỗi khi tải file: ${msg}`,
      reprintSuccess: (file) => `Đã khôi phục cài đặt in "${file}"`,
      reprintSuccessMixed: (file) => `Đã khôi phục cài đặt in "${file}" (bỏ qua dải trang vì file đang mở khác)`,
    },

    // Screen reader announcements
    sr: {
      printerSelected: (name) => `Đã chọn máy in: ${name}`,
      fileLoaded: (name, pages) => `Đã tải file ${name}, ${pages} trang`,
      allSelected: 'Đã chọn tất cả trang',
      selected: (n) => `Đã chọn ${n} trang`,
      printingFile: (name) => `Đang in file ${name}`,
      printCancelled: 'Đã hủy lệnh in',
      printSuccess: 'In thành công!',
    },

    // Error panel
    error: {
      uploadFailed: (name, msg) => `Không thể tải lên <b>${name}</b>: ${msg}`,
      convertFailed: (name, msg) => `Không thể chuyển đổi <b>${name}</b>: ${msg}`,
      printFailed: (msg) => `Lệnh in thất bại: ${msg}`,
      retry: 'Thử lại',
    },

    // Print mode labels
    mode: {
      smart: 'In thông minh',
      booklet: 'Sách A5',
      bookletFull: 'Sách A5 (Booklet)',
    },

    // History item labels
    historyItem: {
      reprint: 'In lại',
      delete: 'Xóa',
      pages: (n) => `${n} trang`,
      copies: (n) => `${n} bản`,
    },

    // Tab tooltips (TabsModule)
    tab: {
      landscapeBadge: 'File này toàn trang ngang — tự động lật theo cạnh ngắn khi in 2 mặt',
      close: 'Đóng file',
      addTitle: 'Thêm file',
    },

    // Confirm print modal row labels
    confirmRow: {
      file: 'File:',
      printer: 'Máy in:',
      mode: 'Chế độ:',
      pagesLabel: 'Trang:',
      pages: (n, range) => `${n} trang (${range})`,
      rangeAll: 'Tất cả',
      rangeAllFiles: 'Tất cả các file',
      sheetsLabel: 'Số tờ:',
      sheets: (s, c) => `${s} tờ × ${c} bản`,
      sheetsMixed: (s) => `${s} tờ (mỗi file khác nhau)`,
      time: 'Thời gian:',
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

  ```js
  window.EN_STRINGS = {
    app: { title: 'Smart Printer' },

    header: {
      printer: { placeholder: '🖨 Select printer…', ariaLabel: 'Select printer' },
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
      // SVG animation text
      paperPrinted: 'Printed paper',
      side1Done: 'side 1 ✓',
      paperTray: 'Paper tray',
      horizontalSteps: 'Remove → Flip sideways → Replace',
      verticalSteps: 'Remove → Flip → Replace',
      defaultInstruction: 'Remove paper and place straight back in tray (printed side down). NO rotation needed.',
    },

    confirm: {
      title: '🖨️ Confirm Print Job',
      cancel: 'Cancel',
      ok: '✓ Confirm Print',
      noPages: 'No pages to print.',
    },

    print: {
      start: '🖨 Start Printing',
      cancel: '✕ Cancel Print',
      printing: (i, total) => `⏳ Printing file ${i}/${total}...`,
      sendingSingle: '⏳ Sending print job...',
      done: (n) => n > 1 ? `✓ Printed ${n} files!` : '✓ Print job sent!',
    },

    summary: {
      pages: (n) => `📄 ${n} pages`,
      sheets: (n) => `🗒️ ${n} sheets`,
      copies: (n) => `· ${n} copies`,
      files: (n) => `· ${n} files`,
      allSelected: 'All selected',
      selected: (sel, total) => `${sel}/${total} pages selected`,
      total: (total) => `${total} pages`,
      lessThanMinute: '< 1 min',
      minutes: (n) => `~${n} min`,
      hoursMinutes: (h, m) => `~${h}h ${m}m`,
    },

    history: {
      empty: 'No print history yet',
    },

    sheet: {
      bookletLabel: (i, total) => `Sheet ${i}/${total} · Booklet`,
      label: (i, total) => `Sheet ${i}/${total}`,
      front: 'Front',
      back: 'Back',
      noprint: 'Not printed',
      page: (n) => `Front · Page ${n}`,
      backPage: (n) => `Back · Page ${n}`,
      bookletFacePage: (n) => `Page ${n ?? '—'}`,
      backSimplex: 'Back · (single-sided)',
      backBlank: 'Back · Blank page',
      deleteBlank: 'Delete blank page',
      ejectHint: (n) => `Page ${n} — click to add to print`,
      ejectLabel: (n) => `Page ${n}`,
    },

    toast: {
      settingsOpened: 'Printer settings opened',
      settingsError: 'Could not open printer settings',
      printerSelected: (name) => `Printer selected: ${name}`,
      printerLoadError: (msg) => `Error loading printers: ${msg}`,
      printerLost: 'Selected printer is no longer available. Please select again.',
      fileTypeUnsupported: (name) => `Unsupported file type: ${name}`,
      fileTooLarge: (name) => `File too large (max 100MB): ${name}`,
      uploadError: (msg) => `Error: ${msg}`,
      converting: (name) => `Converting ${name}...`,
      convertError: (name) => `Conversion error: ${name}`,
      uploadSuccess: (name, pages) => `Loaded: ${name} (${pages} pages)`,
      pdfLoaded: (pages) => `Loaded ${pages} pages`,
      allDouble: 'All pages set to double-sided',
      allSingle: 'All pages set to single-sided',
      deselectAll: 'All deselected. Select pages to print.',
      pageDuplex: (n) => `Page ${n} will print double-sided`,
      pageSimplex: (n) => `Page ${n} will print single-sided`,
      blankInserted: 'Blank page inserted',
      rotateReset: (n) => `Page ${n}: rotation reset`,
      rotated: (n, label) => `Page ${n}: ${label}`,
      historyCleared: 'History cleared',
      historyItemRemoved: 'History item removed',
      printCancelled: 'Print job cancelled',
      printCancelFailed: 'Could not cancel print job',
      selectPrinterFirst: 'Please select a printer first',
      noPagesSelected: 'No pages selected to print',
      sendingFile: (name) => `Sending print job: ${name}...`,
      printFileError: (name, msg) => `Print error for ${name}: ${msg}`,
      frontDone: 'Front side printed! Please follow the instructions.',
      printSuccess: (n) => n > 1 ? `Successfully printed ${n} files!` : 'Print job sent!',
      printError: (msg) => `Print error: ${msg}`,
      printingBack: 'Printing back side...',
      printComplete: 'Printing complete!',
      continueError: (msg) => `Error continuing print: ${msg}`,
      pageReordered: 'Page order changed',
      fileLoadError: (msg) => `Error loading file: ${msg}`,
      reprintSuccess: (file) => `Print settings restored for "${file}"`,
      reprintSuccessMixed: (file) => `Print settings restored for "${file}" (page range skipped — different file active)`,
    },

    sr: {
      printerSelected: (name) => `Printer selected: ${name}`,
      fileLoaded: (name, pages) => `File loaded: ${name}, ${pages} pages`,
      allSelected: 'All pages selected',
      selected: (n) => `${n} pages selected`,
      printingFile: (name) => `Printing file ${name}`,
      printCancelled: 'Print job cancelled',
      printSuccess: 'Print successful!',
    },

    error: {
      uploadFailed: (name, msg) => `Could not upload <b>${name}</b>: ${msg}`,
      convertFailed: (name, msg) => `Could not convert <b>${name}</b>: ${msg}`,
      printFailed: (msg) => `Print job failed: ${msg}`,
      retry: 'Retry',
    },

    // Print mode labels
    mode: {
      smart: 'Smart Print',
      booklet: 'Booklet',
      bookletFull: 'Booklet A5',
    },

    // History item labels
    historyItem: {
      reprint: 'Reprint',
      delete: 'Delete',
      pages: (n) => `${n} pages`,
      copies: (n) => `${n} copies`,
    },

    // Tab tooltips (TabsModule)
    tab: {
      landscapeBadge: 'This file is all landscape — automatically flips on short edge when printing double-sided',
      close: 'Close file',
      addTitle: 'Add file',
    },

    // Confirm print modal row labels
    confirmRow: {
      file: 'File:',
      printer: 'Printer:',
      mode: 'Mode:',
      pagesLabel: 'Pages:',
      pages: (n, range) => `${n} pages (${range})`,
      rangeAll: 'All',
      rangeAllFiles: 'All files',
      sheetsLabel: 'Sheets:',
      sheets: (s, c) => `${s} sheets × ${c} copies`,
      sheetsMixed: (s) => `${s} sheets (varies per file)`,
      time: 'Time:',
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
  git commit -m "feat(i18n): add VI and EN string files with full coverage"
  ```

---

## Task 3: Add `I18nModule` to `app.js`

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 3.1: Add `I18nModule` constant near the top of `app.js`**

  Find `const AppState` near the top of `app.js`. Add the following block **immediately before** it:

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
          // Update document title
          document.title = this.t('app.title');
          // Static text nodes (auto-detect HTML to use innerHTML vs textContent)
          document.querySelectorAll('[data-i18n]').forEach(el => {
              const key = el.dataset.i18n;
              const text = this.t(key);
              if (text !== key) {
                  if (typeof text === 'string' && text.includes('<')) {
                      el.innerHTML = text;
                  } else {
                      el.textContent = text;
                  }
              }
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
          // Aria-label attributes
          document.querySelectorAll('[data-i18n-aria]').forEach(el => {
              const key = el.dataset.i18nAria;
              const text = this.t(key);
              if (text !== key) el.setAttribute('aria-label', text);
          });
          // Re-render guide body if guide modal is open
          if (!document.getElementById('guide-modal')?.classList.contains('hidden')) {
              GuideModule.renderCurrentTab();
          }
          // Update printer select placeholder option
          const printerSel = document.getElementById('printer-select');
          if (printerSel) {
              const placeholderOpt = printerSel.querySelector('option[value=""]');
              if (placeholderOpt) placeholderOpt.textContent = this.t('header.printer.placeholder');
          }
          // Update mode select options
          const modeSel = document.getElementById('mode-select');
          if (modeSel) {
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

  Open `frontend/styles.css`. Find the `.btn-print:disabled { ... }` block (around line 2230). Add the following block **immediately after** it:

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
    overflow-x:       auto;
    white-space:      nowrap;
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
    white-space:      nowrap;
    flex-shrink:      0;
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

  > **Note:** If current version is not `v=19`, replace whatever version string exists.

- [ ] **Step 5.2: Add `data-i18n` to static text elements in header**

  Replace the header section (lines 26–63, containing `<header class="header-settings">`) entirely with:

  ```html
  <!-- ── Row 1: Settings bar ────────────────────────────── -->
  <header class="header-settings">
    <!-- Printer selector -->
    <select id="printer-select" class="header-select" aria-label="Chọn máy in" data-i18n-aria="header.printer.ariaLabel">
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

  > **Note:** Add `data-i18n-aria="header.printer.ariaLabel"` to printer-select, and add `ariaLabel` key to both `vi.js` and `en.js` under `header.printer`: `ariaLabel: 'Chọn máy in'` / `ariaLabel: 'Select printer'`.

- [ ] **Step 5.3: Add `data-i18n` to file tabs row**

  Replace the file tabs row (lines 65–81, the `<div id="file-tabs">` block) with:

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

  > **Note:** The existing HTML at line 78 has a bare text node ` Ghép bộ` (no `<span>`). This step adds the `<span data-i18n>` wrapper — replace the entire `<label>` element.

- [ ] **Step 5.4: Add `data-i18n` to preview panel and history**

  Replace lines 83–114 (the `<div class="app-body">` block) with:

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
  <!-- ── Manual Duplex Instruction Modal ── -->
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

              <!-- Checklist -->
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

              <!-- Auto-continue timer -->
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
  <!-- ── Print Confirmation Modal ───────────────────── -->
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

  Replace the drop hint (lines 125–129):
  ```html
  <div class="upload-drop-hint" id="drop-hint">
    <span style="font-size:32px">📄</span>
    <span style="font-size:18px; color:var(--color-accent); margin-left:12px" data-i18n="preview.dropHint">Thả file vào đây</span>
  </div>
  ```

  For the context menu (lines 199–296), **DO NOT replace the entire block**. The existing menu uses `data-action` and `data-submenu` attributes that JS event listeners depend on. Only add `data-i18n` to the `<span>` elements that contain visible text. Replace the context menu block with this exact version (same structure, only adds `data-i18n`):

  ```html
  <!-- ── Page Context Menu ──────────────────────────────── -->
  <div id="page-context-menu" class="context-menu hidden">
      <div class="context-menu-header" id="context-menu-header">Trang 1</div>

      <!-- Mặt in: 2 mặt / 1 mặt -->
      <div class="context-menu-item cm-has-sub" data-submenu="sub-sides">
          <span class="context-menu-icon">📄</span>
          <span data-i18n="ctx.sides">Mặt in</span>
          <span class="context-menu-arrow">›</span>
          <div class="context-submenu hidden" id="sub-sides">
              <div class="context-menu-item" data-action="double-sided">
                  <span class="context-menu-icon">📄</span>
                  <span data-i18n="ctx.double">2 mặt</span>
                  <span class="context-menu-check" id="check-double">✓</span>
              </div>
              <div class="context-menu-item" data-action="single-sided">
                  <span class="context-menu-icon">📃</span>
                  <span data-i18n="ctx.single">1 mặt</span>
                  <span class="context-menu-check" id="check-single"></span>
              </div>
              <div class="context-menu-separator"></div>
              <div class="context-menu-item" data-action="all-double-sided">
                  <span class="context-menu-icon">📑</span>
                  <span data-i18n="ctx.allDouble">Tất cả: 2 mặt</span>
              </div>
              <div class="context-menu-item" data-action="all-single-sided">
                  <span class="context-menu-icon">📋</span>
                  <span data-i18n="ctx.allSingle">Tất cả: 1 mặt</span>
              </div>
              <div class="context-menu-separator"></div>
              <div class="context-menu-item" data-action="deselect-all">
                  <span class="context-menu-icon">✕</span>
                  <span data-i18n="ctx.deselectAll">Bỏ chọn tất cả</span>
              </div>
          </div>
      </div>

      <!-- Xoay -->
      <div class="context-menu-item cm-has-sub" data-submenu="sub-rotate">
          <span class="context-menu-icon">🔄</span>
          <span data-i18n="ctx.rotate">Xoay trang</span>
          <span class="context-menu-arrow">›</span>
          <div class="context-submenu hidden" id="sub-rotate">
              <div class="context-menu-item" data-action="rotate-cw90">
                  <span class="context-menu-icon">↻</span>
                  <span data-i18n="ctx.rotateCW">Xoay phải 90°</span>
              </div>
              <div class="context-menu-item" data-action="rotate-ccw90">
                  <span class="context-menu-icon">↺</span>
                  <span data-i18n="ctx.rotateCCW">Xoay trái 90°</span>
              </div>
              <div class="context-menu-item" data-action="rotate-180">
                  <span class="context-menu-icon">🔃</span>
                  <span data-i18n="ctx.rotate180">Xoay 180°</span>
              </div>
              <div class="context-menu-separator"></div>
              <div class="context-menu-item" data-action="rotate-fliph">
                  <span class="context-menu-icon">↔</span>
                  <span data-i18n="ctx.flipH">Lật ngang</span>
              </div>
              <div class="context-menu-item" data-action="rotate-flipv">
                  <span class="context-menu-icon">↕</span>
                  <span data-i18n="ctx.flipV">Lật dọc</span>
              </div>
              <div class="context-menu-separator"></div>
              <div class="context-menu-item" data-action="rotate-reset">
                  <span class="context-menu-icon">↩</span>
                  <span data-i18n="ctx.rotateReset">Reset về gốc</span>
              </div>
          </div>
      </div>

      <!-- Chèn — chỉ hiện trong sheet view -->
      <div class="context-menu-item cm-has-sub cm-sheet-only" data-submenu="sub-insert">
          <span class="context-menu-icon">➕</span>
          <span data-i18n="ctx.insert">Chèn</span>
          <span class="context-menu-arrow">›</span>
          <div class="context-submenu hidden" id="sub-insert">
              <div class="context-menu-item" data-action="insert-blank-before">
                  <span class="context-menu-icon">📄</span>
                  <span data-i18n="ctx.blankBefore">Trang trắng trước</span>
              </div>
              <div class="context-menu-item" data-action="insert-blank-after">
                  <span class="context-menu-icon">📄</span>
                  <span data-i18n="ctx.blankAfter">Trang trắng sau</span>
              </div>
              <div class="context-menu-separator"></div>
              <div class="context-menu-item" data-action="insert-image-before">
                  <span class="context-menu-icon">🖼</span>
                  <span data-i18n="ctx.imageBefore">Ảnh trước</span>
              </div>
              <div class="context-menu-item" data-action="insert-image-after">
                  <span class="context-menu-icon">🖼</span>
                  <span data-i18n="ctx.imageAfter">Ảnh sau</span>
              </div>
          </div>
      </div>
  </div>
  ```

  > **KEY RULE:** All `data-action`, `data-submenu`, `id`, `class` attributes are **preserved exactly** from the original. Only `data-i18n` attributes are added to `<span>` text nodes. The `context-menu-header` ID and `context-menu-check` IDs (`check-double`, `check-single`) are preserved — JS uses them.

- [ ] **Step 5.8: Commit**

  ```powershell
  git add frontend/index.html
  git commit -m "feat(i18n): add data-i18n attributes and new buttons to index.html"
  ```

---

## Task 6: Add New JS Modules to `app.js` + Fix Race Condition

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 6.1: Add `GuideModule` to `app.js`**

  Near the end of `app.js`, **before** the `DOMContentLoaded` event handler, add:

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

- [ ] **Step 6.4: Fix race condition in `_renderPrinters`**

  Find the `_renderPrinters` function in `app.js` at **line 1126**. Inside it, find the `if (def && forceDefault)` block (lines 1138–1143). The block currently reads:

  ```js
  if (def && forceDefault) {
      sel.value = def.name;
      AppState.selectedPrinter = def;
      PrintModule.updateButton();
      StepIndicatorModule.update();
  }
  ```

  Add `sel.dispatchEvent(new Event('change'))` as the **last line** inside the `if` block:

  ```js
  if (def && forceDefault) {
      sel.value = def.name;
      AppState.selectedPrinter = def;
      PrintModule.updateButton();
      StepIndicatorModule.update();
      sel.dispatchEvent(new Event('change')); // Fix: notify PrinterSettingsModule of auto-selection
  }
  ```

  > **Why:** `PrinterSettingsModule.init()` attaches a `'change'` listener to `#printer-select`. When printers load and `_renderPrinters` auto-selects the default, it sets `sel.value` directly — which does NOT fire a native `'change'` event. The dispatched event tells `PrinterSettingsModule` to enable the settings button. Insertion point: after `StepIndicatorModule.update()` (line 1142), still inside the `forceDefault` block.

- [ ] **Step 6.5: Wire all new modules in `DOMContentLoaded`**

  Find the `DOMContentLoaded` event handler at the bottom of `app.js`. It ends around line 5331 with `StepIndicatorModule.update();`. Add these lines **after** `StepIndicatorModule.update();` and **before** the `document.getElementById('mode-select')?.addEventListener` line (around line 5334):

  ```js
      GuideModule.init();
      PrinterSettingsModule.init();
      LangToggleModule.init();
      I18nModule.init(); // Must be last — applies translations after all modules are wired
  ```

- [ ] **Step 6.6: Verify syntax**

  ```powershell
  node --check frontend/app.js
  ```

  Expected: no output (success)

- [ ] **Step 6.7: Commit**

  ```powershell
  git add frontend/app.js
  git commit -m "feat(ui): add GuideModule, PrinterSettingsModule, LangToggleModule; fix printer auto-select race condition"
  ```

---

## Task 7: Migrate Dynamic Strings in `app.js` to i18n

**Files:**
- Modify: `frontend/app.js`

This task migrates all user-visible dynamic strings. Replace every hardcoded Vietnamese string with the corresponding `I18nModule.t('key')` call.

### 7A — Toast messages

- [ ] **Step 7A.1: Printer selection toast (line ~1115)**

  Find:
  ```js
  SRModule.announce(`Đã chọn máy in: ${name}`);
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.printerSelected')(name));
  ```

  Find (near same location):
  ```js
  showToast(`Đã chọn máy in: ${name}`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printerSelected')(name));
  ```

- [ ] **Step 7A.2: Printer load error toast (line ~1121)**

  Find:
  ```js
  showToast(`Lỗi tải danh sách máy in: ${err.message}`, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printerLoadError')(err.message), 'error');
  ```

- [ ] **Step 7A.3: Printer lost toast (line ~1175)**

  Find:
  ```js
  showToast('Máy in đã chọn không còn khả dụng. Vui lòng chọn lại.', 'warning');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printerLost'), 'warning');
  ```

- [ ] **Step 7A.4: File type unsupported toast (line ~1218)**

  Find:
  ```js
  showToast('Loại file không được hỗ trợ: ' + file.name, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.fileTypeUnsupported')(file.name), 'error');
  ```

- [ ] **Step 7A.5: File too large toast (line ~1224)**

  Find:
  ```js
  showToast(`File quá lớn (tối đa 100MB): ${file.name}`, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.fileTooLarge')(file.name), 'error');
  ```

- [ ] **Step 7A.6: Upload error toast (line ~1263)**

  Find:
  ```js
  showToast('Lỗi: ' + result.message, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.uploadError')(result.message), 'error');
  ```

- [ ] **Step 7A.7: Converting toast (line ~1271)**

  Find:
  ```js
  showToast(`Đang chuyển đổi ${entry.name}...`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.converting')(entry.name));
  ```

- [ ] **Step 7A.8: Convert error toast (line ~1278)**

  Find:
  ```js
  showToast(`Lỗi chuyển đổi: ${entry.name}`, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.convertError')(entry.name), 'error');
  ```

- [ ] **Step 7A.9: Upload success toast (line ~1312)**

  Find:
  ```js
  showToast(`Đã tải: ${entry.name} (${entry.totalPageCount} trang)`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.uploadSuccess')(entry.name, entry.totalPageCount));
  ```

  Find nearby SRModule announce:
  ```js
  SRModule.announce(`Đã tải file ${entry.name}, ${entry.totalPageCount} trang`);
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.fileLoaded')(entry.name, entry.totalPageCount));
  ```

- [ ] **Step 7A.10: PDF loaded toast (line ~1611)**

  Find:
  ```js
  showToast(`Đã tải ${AppState.totalPageCount} trang`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.pdfLoaded')(AppState.totalPageCount));
  ```

- [ ] **Step 7A.11: Select all / deselect toasts (lines ~521, ~527, ~534, ~1869, ~1878, ~1889, ~2125, ~2128, ~2132)**

  Find all occurrences of:
  ```js
  showToast('Đã chọn tất cả in 2 mặt');
  ```
  Replace ALL with:
  ```js
  showToast(I18nModule.t('toast.allDouble'));
  ```

  Find all occurrences of:
  ```js
  showToast('Đã chọn tất cả in 1 mặt');
  ```
  Replace ALL with:
  ```js
  showToast(I18nModule.t('toast.allSingle'));
  ```

  Find all occurrences of:
  ```js
  showToast('Đã bỏ chọn tất cả');
  // or
  showToast('Đã bỏ chọn tất cả. Chọn trang để in.');
  ```
  Replace ALL with:
  ```js
  showToast(I18nModule.t('toast.deselectAll'));
  ```

  Find SRModule announces:
  ```js
  SRModule.announce('Đã chọn tất cả trang');
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.allSelected'));
  ```

  Find:
  ```js
  SRModule.announce(`Đã chọn ${n} trang`);
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.selected')(n));
  ```

- [ ] **Step 7A.12: Per-page duplex/simplex toasts (lines ~2134, ~2136)**

  Find:
  ```js
  showToast(`Trang ${n} sẽ in 2 mặt`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.pageDuplex')(n));
  ```

  Find:
  ```js
  showToast(`Trang ${n} sẽ in 1 mặt`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.pageSimplex')(n));
  ```

- [ ] **Step 7A.13: Blank page inserted toast (line ~2199)**

  Find:
  ```js
  showToast('Đã chèn trang trắng');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.blankInserted'));
  ```

- [ ] **Step 7A.14: Rotation toasts (lines ~2218, ~2222)**

  Find:
  ```js
  showToast(`Trang ${pageNum}: đã reset xoay`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.rotateReset')(pageNum));
  ```

  Find the rotation labels object (around line 2221):
  ```js
  const labels = { CW90: 'Xoay phải 90°', CCW90: 'Xoay trái 90°', Rotate180: 'Xoay 180°', FlipHorizontal: 'Lật ngang', FlipVertical: 'Lật dọc' };
  ```
  Replace with:
  ```js
  const labels = {
      CW90: I18nModule.t('ctx.rotateCW'),
      CCW90: I18nModule.t('ctx.rotateCCW'),
      Rotate180: I18nModule.t('ctx.rotate180'),
      FlipHorizontal: I18nModule.t('ctx.flipH'),
      FlipVertical: I18nModule.t('ctx.flipV'),
  };
  ```

  Find (near line 2222):
  ```js
  showToast(`Trang ${pageNum}: ${labels[rotation]}`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.rotated')(pageNum, labels[rotation]));
  ```

- [ ] **Step 7A.15: History toasts (lines ~2338, ~2357)**

  Find:
  ```js
  showToast('Đã xóa lịch sử');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.historyCleared'));
  ```

  Find:
  ```js
  showToast('Đã xóa mục lịch sử');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.historyItemRemoved'));
  ```

- [ ] **Step 7A.16: Print cancel toasts + SR announce (lines ~2471–2474)**

  Find:
  ```js
  showToast('Đã hủy lệnh in');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printCancelled'));
  ```

  Find (immediately after the toast, line ~2472):
  ```js
  SRModule.announce('Đã hủy lệnh in');
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.printCancelled'));
  ```

  Find:
  ```js
  showToast('Không thể hủy lệnh in', 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printCancelFailed'), 'error');
  ```

- [ ] **Step 7A.17: No printer / no pages toasts (lines ~2518, ~2523)**

  Find:
  ```js
  showToast('Chọn máy in trước', 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.selectPrinterFirst'), 'error');
  ```

  Find:
  ```js
  showToast('Không có trang nào được chọn để in', 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.noPagesSelected'), 'error');
  ```

- [ ] **Step 7A.18: Sending file / print error toasts (lines ~2634, ~2641)**

  Find:
  ```js
  showToast(`Đang gửi lệnh in: ${file.name}...`);
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.sendingFile')(file.name));
  ```

  Find:
  ```js
  showToast(`Lỗi in file ${file.name}: ${result.message}`, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printFileError')(file.name, result.message), 'error');
  ```

- [ ] **Step 7A.19: Front done / print success toasts + SR announce (lines ~2676, ~2705, ~2706, ~2902)**

  Find all occurrences of:
  ```js
  showToast('Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.');
  ```
  Replace ALL with:
  ```js
  showToast(I18nModule.t('toast.frontDone'));
  ```

  Find:
  ```js
  showToast(`In thành công ${fileCount} file!`);
  // or
  showToast('In thành công!');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printSuccess')(fileCount));
  ```

  Find (line ~2706, immediately after the print success toast):
  ```js
  SRModule.announce('In thành công!');
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.printSuccess'));
  ```

  Find:
  ```js
  showToast('Lỗi khi in: ' + err.message, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printError')(err.message), 'error');
  ```

- [ ] **Step 7A.20: Printing back / complete toasts (lines ~2730, ~2744)**

  Find:
  ```js
  showToast('Đang in mặt chẵn...');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printingBack'));
  ```

  Find:
  ```js
  showToast('In hoàn tất!');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.printComplete'));
  ```

- [ ] **Step 7A.21: Continue print error toasts (lines ~2765, ~2771)**

  Find:
  ```js
  showToast('Lỗi: ' + result.message, 'error');
  ```
  (The one inside the continue-print handler) Replace with:
  ```js
  showToast(I18nModule.t('toast.uploadError')(result.message), 'error');
  ```

  Find:
  ```js
  showToast('Lỗi khi tiếp tục in: ' + err.message, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.continueError')(err.message), 'error');
  ```

- [ ] **Step 7A.22: Page reorder toast (line ~3635)**

  Find:
  ```js
  showToast('Đã đổi thứ tự trang');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.pageReordered'));
  ```

- [ ] **Step 7A.23: File load error toast (line ~1318)**

  Find:
  ```js
  showToast('Lỗi khi tải file: ' + err.message, 'error');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.fileLoadError')(err.message), 'error');
  ```

- [ ] **Step 7A.24: Reprint success toasts (lines ~2140, ~2411)**

  Find (line ~2140, inside `HistoryModule._reprint`):
  ```js
  showToast(`Đã khôi phục cài đặt in "${item.file}"`, 'info');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.reprintSuccess')(item.file), 'info');
  ```

  Find (line ~2411, same function, different branch):
  ```js
  showToast(`Đã khôi phục cài đặt in "${item.file}" (bỏ qua dải trang vì file đang mở khác)`, 'info');
  ```
  Replace with:
  ```js
  showToast(I18nModule.t('toast.reprintSuccessMixed')(item.file), 'info');
  ```

- [ ] **Step 7A.25: SR announce printing file (line ~2635)**

  Find (line ~2635, inside `PrintModule._startPrint` loop):
  ```js
  SRModule.announce(`Đang in file ${file.name}`);
  ```
  Replace with:
  ```js
  SRModule.announce(I18nModule.t('sr.printingFile')(file.name));
  ```

### 7B — Summary bar strings

- [ ] **Step 7B.1: Migrate time estimate strings (around lines 3160–3163 in SummaryModule AND lines 3338–3339 in ConfirmPrintModal)**

  There are TWO places with time estimate strings — both must be migrated.

  **In SummaryModule** (around lines 3160–3163):

  Find:
  ```js
  timeStr = '< 1 phút';
  ```
  Replace with:
  ```js
  timeStr = I18nModule.t('summary.lessThanMinute');
  ```

  Find:
  ```js
  timeStr = `~${Math.ceil(totalSec / 60)} phút`;
  ```
  Replace with:
  ```js
  timeStr = I18nModule.t('summary.minutes')(Math.ceil(totalSec / 60));
  ```

  Find:
  ```js
  timeStr = `~${Math.floor(totalSec / 3600)}h ${Math.ceil((totalSec % 3600) / 60)}m`;
  ```
  Replace with:
  ```js
  timeStr = I18nModule.t('summary.hoursMinutes')(Math.floor(totalSec / 3600), Math.ceil((totalSec % 3600) / 60));
  ```

  **In ConfirmPrintModal** (around lines 3338–3339):

  Find:
  ```js
  const timeStr = totalSec < 60 ? '< 1 phút'
      : `~${Math.ceil(totalSec / 60)} phút`;
  ```
  Replace with:
  ```js
  const timeStr = totalSec < 60
      ? I18nModule.t('summary.lessThanMinute')
      : I18nModule.t('summary.minutes')(Math.ceil(totalSec / 60));
  ```

  Also migrate page/sheet/copies/files labels in the summary bar `innerHTML` (lines ~3167–3174). The actual code uses `🗒️` (not `🖨`) and `·` separator (not `×`):

  Find (line ~3167):
  ```js
  <span>📄 ${totalPages} trang</span>
  ```
  Replace with:
  ```js
  <span>${I18nModule.t('summary.pages')(totalPages)}</span>
  ```

  Find (line ~3169):
  ```js
  <span>🗒️ ${totalSheets} tờ</span>
  ```
  Replace with:
  ```js
  <span>${I18nModule.t('summary.sheets')(totalSheets)}</span>
  ```

  Find (line ~3172):
  ```js
  ${copies !== null && copies > 1 ? `<span>· ${copies} bản</span>` : ''}
  ```
  Replace with:
  ```js
  ${copies !== null && copies > 1 ? `<span>${I18nModule.t('summary.copies')(copies)}</span>` : ''}
  ```

  Find (line ~3173):
  ```js
  ${multiFile ? `<span>· ${activeFiles.length} file</span>` : ''}
  ```
  Replace with:
  ```js
  ${multiFile ? `<span>${I18nModule.t('summary.files')(activeFiles.length)}</span>` : ''}
  ```

### 7C — SVG flip animation text

- [ ] **Step 7C.1: Migrate SVG text in `_showFlipModal` (around lines 2958–2984)**

  Find the `_showFlipModal` function (or equivalent) where SVG text elements are created with hardcoded Vietnamese strings. Replace each hardcoded string:

  | Find | Replace with |
  |------|-------------|
  | `'Giấy đã in'` | `I18nModule.t('flip.paperPrinted')` |
  | `'mặt 1 ✓'` | `I18nModule.t('flip.side1Done')` |
  | `'Khay giấy'` | `I18nModule.t('flip.paperTray')` |
  | `'Lấy ra → Lật ngang → Đặt lại'` | `I18nModule.t('flip.horizontalSteps')` |
  | `'Lấy ra → Lật → Đặt lại'` | `I18nModule.t('flip.verticalSteps')` |
  | `'Lấy giấy ra và đặt thẳng lại vào khay...'` (default instruction) | `I18nModule.t('flip.defaultInstruction')` |

### 7D — Other dynamic strings

- [ ] **Step 7D.1: History empty message (line ~2424)**

  Find:
  ```js
  container.innerHTML = '<div class="history-empty">Chưa có lịch sử in</div>';
  ```
  Replace with:
  ```js
  container.innerHTML = `<div class="history-empty">${I18nModule.t('history.empty')}</div>`;
  ```

- [ ] **Step 7D.2: Print button states**

  Find (around line 2480) — note: emoji has variation selector `\ufe0f`:
  ```js
  btn.innerHTML = '<span class="btn-icon">🖨️</span> Bắt Đầu In';
  ```
  Replace with:
  ```js
  btn.innerHTML = `<span class="btn-icon">🖨️</span> ${I18nModule.t('print.start').replace('🖨 ', '')}`;
  ```

  Find (around lines 2673, 2898):
  ```js
  btn.textContent = '✕ Huỷ In';
  ```
  Replace ALL with:
  ```js
  btn.textContent = I18nModule.t('print.cancel');
  ```

  Find (around line 2552) — the ternary has **two branches**, both must be replaced:
  ```js
  btn.textContent = filesToPrint.length > 1
      ? `⏳ Đang in file ${i + 1}/${filesToPrint.length}...`
      : '⏳ Đang gửi lệnh in...';
  ```
  Replace with:
  ```js
  btn.textContent = filesToPrint.length > 1
      ? I18nModule.t('print.printing')(i + 1, filesToPrint.length)
      : I18nModule.t('print.sendingSingle');
  ```

  Find (around line 2702):
  ```js
  btn.textContent = fileCount > 1 ? `✓ Đã in ${fileCount} file!` : '✓ Đã gửi lệnh in!';
  ```
  Replace with:
  ```js
  btn.textContent = I18nModule.t('print.done')(fileCount);
  ```

- [ ] **Step 7D.3: Orientation badge (line ~1699)**

  Find:
  ```js
  badge.textContent = isLandscape ? '↔ Ngang' : '↕ Dọc';
  ```
  Replace with:
  ```js
  badge.textContent = isLandscape ? I18nModule.t('orientation.landscape') : I18nModule.t('orientation.portrait');
  ```

- [ ] **Step 7D.4: File ready status (lines ~1287, ~1353, ~1542 — all occurrences)**

  Find all occurrences of:
  ```js
  if (fsEl) fsEl.textContent = 'Đã sẵn sàng';
  ```
  Replace ALL with:
  ```js
  if (fsEl) fsEl.textContent = I18nModule.t('file.ready');
  ```

- [ ] **Step 7D.5: Preview loading text (line ~564)**

  Find:
  ```js
  mainContainer.innerHTML = '<div class="loading" ...>Đang tải...</div>';
  ```
  Replace with:
  ```js
  mainContainer.innerHTML = `<div class="loading" style="padding:2rem;text-align:center;color:var(--text-muted)">${I18nModule.t('preview.loading')}</div>`;
  ```

- [ ] **Step 7D.6: Sheet/booklet view labels (around lines 4282–4467)**

  Find:
  ```js
  label.textContent = `Tờ ${sheet.sheetIndex}/${totalSheets} – Booklet`;
  ```
  Replace with:
  ```js
  label.textContent = I18nModule.t('sheet.bookletLabel')(sheet.sheetIndex, totalSheets);
  ```

  Find:
  ```js
  label.textContent = `Tờ ${sheet.sheetIndex}/${totalSheets}`;
  ```
  Replace with:
  ```js
  label.textContent = I18nModule.t('sheet.label')(sheet.sheetIndex, totalSheets);
  ```

  Find:
  ```js
  frontLabel.textContent = 'Mặt trước';
  ```
  Replace with:
  ```js
  frontLabel.textContent = I18nModule.t('sheet.front');
  ```

  Find:
  ```js
  backLabel2.textContent = 'Mặt sau';
  ```
  Replace with:
  ```js
  backLabel2.textContent = I18nModule.t('sheet.back');
  ```

  Find (around line 4439):
  ```js
  `Mặt trước · Trang ${sheet.front}`
  ```
  Replace with:
  ```js
  I18nModule.t('sheet.page')(sheet.front)
  ```

  Find (line ~4448 — the complete back-face label ternary):
  ```js
  const backFaceLabel = backPageNum
      ? `Mặt sau · Trang ${backPageNum}`
      : sheet.isSingleForced ? 'Mặt sau · (in 1 mặt)' : 'Mặt sau · Trang trắng';
  ```
  Replace with:
  ```js
  const backFaceLabel = backPageNum
      ? I18nModule.t('sheet.backPage')(backPageNum)
      : sheet.isSingleForced ? I18nModule.t('sheet.backSimplex') : I18nModule.t('sheet.backBlank');
  ```

  Find (booklet branch — lines ~4416–4429, makeFace calls with `` `Trang ${n ?? '—'}` ``):
  ```js
  frontRow.appendChild(makeFace(sheet.front, `Trang ${sheet.front ?? '—'}`));
  frontRow.appendChild(makeFace(sheet.front2, `Trang ${sheet.front2 ?? '—'}`));
  ```
  Replace with:
  ```js
  frontRow.appendChild(makeFace(sheet.front, I18nModule.t('sheet.bookletFacePage')(sheet.front)));
  frontRow.appendChild(makeFace(sheet.front2, I18nModule.t('sheet.bookletFacePage')(sheet.front2)));
  ```

  Find:
  ```js
  backRow.appendChild(makeFace(sheet.back, `Trang ${sheet.back ?? '—'}`));
  backRow.appendChild(makeFace(sheet.back2, `Trang ${sheet.back2 ?? '—'}`));
  ```
  Replace with:
  ```js
  backRow.appendChild(makeFace(sheet.back, I18nModule.t('sheet.bookletFacePage')(sheet.back)));
  backRow.appendChild(makeFace(sheet.back2, I18nModule.t('sheet.bookletFacePage')(sheet.back2)));
  ```

  Find (blank page delete button title, line ~4319):
  ```js
  xBtn.title = 'Xóa trang trắng';
  ```
  Replace with:
  ```js
  xBtn.title = I18nModule.t('sheet.deleteBlank');
  ```

  Find (ejected card title, line ~4475):
  ```js
  card.title = `Trang ${pageNum} — nhấn để thêm vào bản in`;
  ```
  Replace with:
  ```js
  card.title = I18nModule.t('sheet.ejectHint')(pageNum);
  ```

  Find (ejected card label text, line ~4479):
  ```js
  lbl.textContent = `Trang ${pageNum}`;
  ```
  Replace with:
  ```js
  lbl.textContent = I18nModule.t('sheet.ejectLabel')(pageNum);
  ```

  Find:
  ```js
  header.textContent = 'Không in';
  ```
  Replace with:
  ```js
  header.textContent = I18nModule.t('sheet.noprint');
  ```

- [ ] **Step 7D.7: Printer select placeholder in JS (line ~1129)**

  Find:
  ```js
  sel.innerHTML = '<option value="">🖨 Chọn máy in…</option>';
  ```
  Replace with:
  ```js
  sel.innerHTML = `<option value="">${I18nModule.t('header.printer.placeholder')}</option>`;
  ```

- [ ] **Step 7D.8: Badge text migration (lines ~721, ~866, ~1955)**

  There are 3 occurrences but line ~1956 has a typo in the source (`'MAT'` without diacritics). Handle separately:

  **Occurrences at lines ~721 and ~866** (correct Vietnamese with diacritics):
  Find:
  ```js
  badge.textContent = isSingle ? '1 MẶT' : '2 MẶT';
  ```
  Replace with:
  ```js
  badge.textContent = isSingle ? I18nModule.t('zoom.singleSided') : I18nModule.t('zoom.doubleSided');
  ```

  **Occurrence at line ~1956** (typo: `'MAT'` without diacritics — inside `ZoomModal._renderPage`):
  Find:
  ```js
  badge.textContent = isSingle ? '1 MAT' : '2 MAT';
  ```
  Replace with:
  ```js
  badge.textContent = isSingle ? I18nModule.t('zoom.singleSided') : I18nModule.t('zoom.doubleSided');
  ```

  Keys `zoom.singleSided` and `zoom.doubleSided` are already defined in vi.js and en.js (Task 2).

- [ ] **Step 7D.9: Context menu header dynamic text (line ~2068)**

  Find (inside the context-menu header update block, line ~2068):
  ```js
  header.textContent = (pageNum === 0) ? 'Trang trắng' : `Trang ${pageNum}`;
  ```
  Replace with:
  ```js
  header.textContent = (pageNum === 0) ? I18nModule.t('ctx.blankPage') : I18nModule.t('ctx.page')(pageNum);
  ```

  Keys `ctx.blankPage` and `ctx.page` are already defined in vi.js and en.js (Task 2).

- [ ] **Step 7D.10: Verify syntax**

  ```powershell
  node --check frontend/app.js
  ```

  Expected: no output (success)

- [ ] **Step 7D.11: Commit**

  ```powershell
  git add frontend/app.js
  git commit -m "feat(i18n): migrate all dynamic strings in app.js to I18nModule"
  ```

---

## Task 7E: Remaining Dynamic Strings Migration

**Files:** `frontend/app.js`

### 7E — Mode labels, history, confirm modal, error panel, preview empties

- [ ] **Step 7E.1: Mode label maps (HistoryModule line ~2427 + ConfirmPrintModal line ~3311)**

  There are two `modeLabel` maps in app.js. Both must be migrated.

  **In HistoryModule._render()** (line ~2427):
  Find:
  ```js
  const modeLabel = { normal: 'In thông minh', duplex: 'In thông minh', booklet: 'Sách A5' };
  ```
  Replace with:
  ```js
  const modeLabel = {
    normal: I18nModule.t('mode.smart'),
    duplex: I18nModule.t('mode.smart'),
    booklet: I18nModule.t('mode.booklet'),
  };
  ```

  **In ConfirmPrintModal._populate()** (line ~3311):
  Find:
  ```js
  const modeLabel = { duplex: 'In thông minh', normal: 'In thông minh', booklet: 'Sách A5 (Booklet)' };
  ```
  Replace with:
  ```js
  const modeLabel = {
    duplex: I18nModule.t('mode.smart'),
    normal: I18nModule.t('mode.smart'),
    booklet: I18nModule.t('mode.bookletFull'),
  };
  ```

- [ ] **Step 7E.2: History item HTML — tooltips and meta labels (lines ~2431–2435)**

  In `HistoryModule._render()`, find the innerHTML template:
  ```js
  <button class="history-action-btn history-reprint-btn" data-idx="${idx}" title="In lại">🔁</button>
  <button class="history-action-btn" data-delete="${idx}" title="Xóa">✕</button>
  ```
  Replace with:
  ```js
  <button class="history-action-btn history-reprint-btn" data-idx="${idx}" title="${I18nModule.t('historyItem.reprint')}">🔁</button>
  <button class="history-action-btn" data-delete="${idx}" title="${I18nModule.t('historyItem.delete')}">✕</button>
  ```

  Find the meta line:
  ```js
  <div class="history-meta">🖨️ ${item.printer} · ${item.pages} trang · ${modeLabel[item.mode] || item.mode} · ${item.copies} bản</div>
  ```
  Replace with:
  ```js
  <div class="history-meta">🖨️ ${item.printer} · ${I18nModule.t('historyItem.pages')(item.pages)} · ${modeLabel[item.mode] || item.mode} · ${I18nModule.t('historyItem.copies')(item.copies)}</div>
  ```

- [ ] **Step 7E.3: ConfirmPrintModal row labels (lines ~3342–3385)**

  In `ConfirmPrintModal._populate()`, the `fileLabel` already produces `N file` (English "file" is universal) — **no change needed** for `fileLabel`.

  Find the range string (3 branches — all must be replaced):
  ```js
  const rangeStr = multiFile ? 'Tất cả các file'
      : sel?.length === filesToPrint[0]?.totalPageCount ? 'Tất cả'
      : (PageSelectModule._formatRange(sel) || 'Tất cả');
  ```
  Replace with:
  ```js
  const rangeStr = multiFile ? I18nModule.t('confirmRow.rangeAllFiles')
      : sel?.length === filesToPrint[0]?.totalPageCount ? I18nModule.t('confirmRow.rangeAll')
      : (PageSelectModule._formatRange(sel) || I18nModule.t('confirmRow.rangeAll'));
  ```

  Find the copiesStr:
  ```js
  const copiesStr = copies !== null ? `${sheets} tờ × ${copies} bản` : `${sheets} tờ (mỗi file khác nhau)`;
  ```
  Replace with:
  ```js
  const copiesStr = copies !== null
      ? I18nModule.t('confirmRow.sheets')(sheets, copies)
      : I18nModule.t('confirmRow.sheetsMixed')(sheets);
  ```

  Find the innerHTML row labels (all 6 label spans):
  ```js
  <span class="confirm-row-label">File:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.file')}</span>
  ```

  ```js
  <span class="confirm-row-label">Máy in:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.printer')}</span>
  ```

  ```js
  <span class="confirm-row-label">Chế độ:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.mode')}</span>
  ```

  Find (pages row value):
  ```js
  <span class="confirm-row-value">${pages} trang (${rangeStr})</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-value">${I18nModule.t('confirmRow.pages')(pages, rangeStr)}</span>
  ```

  ```js
  <span class="confirm-row-label">Số tờ:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.sheetsLabel')}</span>
  ```

  ```js
  <span class="confirm-row-label">Thời gian:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.time')}</span>
  ```

- [ ] **Step 7E.3a: Add `confirmRow.sheetsLabel` and `confirmRow.pagesLabel` keys to vi.js and en.js**

  In vi.js `confirmRow` object, add:
  ```js
  sheetsLabel: 'Số tờ:',
  pagesLabel: 'Trang:',
  ```

  In en.js `confirmRow` object, add:
  ```js
  sheetsLabel: 'Sheets:',
  pagesLabel: 'Pages:',
  ```

  Then update the `Số tờ:` and `Trang:` row replacements in Step 7E.3:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.sheetsLabel')}</span>
  // and
  <span class="confirm-row-label">${I18nModule.t('confirmRow.pagesLabel')}</span>
  ```

  Specifically, in the ConfirmPrintModal innerHTML template, find:
  ```js
  <span class="confirm-row-label">Trang:</span>
  ```
  Replace with:
  ```js
  <span class="confirm-row-label">${I18nModule.t('confirmRow.pagesLabel')}</span>
  ```

- [ ] **Step 7E.4: Inline card error message (lines ~2719, ~3252)**

  Find (line ~2719, in `PrintModule._startPrint` catch block):
  ```js
  showCardError(actionSec, `Lệnh in thất bại: ${err.message}`, () => ...);
  ```
  Replace with:
  ```js
  showCardError(actionSec, I18nModule.t('error.printFailed')(err.message), () => ...);
  ```

  Find (line ~3252, in `showCardError` function):
  ```js
  ${retryFn ? '<button class="card-error-retry">Thử lại</button>' : ''}
  ```
  Replace with:
  ```js
  ${retryFn ? `<button class="card-error-retry">${I18nModule.t('error.retry')}</button>` : ''}
  ```

- [ ] **Step 7E.4a: ConfirmPrintModal no-pages message (line ~3314)**

  In `ConfirmPrintModal._populate()`, find (line ~3314):
  ```js
  container.innerHTML = '<div class="confirm-row"><span>Không có trang nào để in.</span></div>';
  ```
  Replace with:
  ```js
  container.innerHTML = `<div class="confirm-row"><span>${I18nModule.t('confirm.noPages')}</span></div>`;
  ```

  Key `confirm.noPages` is already defined in vi.js (`'Không có trang nào để in.'`) and en.js (`'No pages to print.'`) ✅

- [ ] **Step 7E.5: Preview panel empty state (line ~4893)**

  The `PreviewPanelModule.clear()` method sets innerHTML with an embedded `<strong>` tag — use `innerHTML` (not `textContent`) for this element.

  Find (line ~4893):
  ```js
  this._container.innerHTML = `
      <div class="preview-empty">
          <span class="preview-empty-icon">🖨</span>
          <span>Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong></span>
      </div>`;
  ```
  Replace with:
  ```js
  this._container.innerHTML = `
      <div class="preview-empty">
          <span class="preview-empty-icon">🖨</span>
          <span>${I18nModule.t('preview.empty')}</span>
      </div>`;
  ```

  Note: `preview.empty` in vi.js/en.js already contains the `<strong>` tag (e.g. `'Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong>'`). This is safe because we assign to `innerHTML` of the outer `<span>`, not `textContent`.

- [ ] **Step 7E.6: ThumbStrip empty state (line ~4991)**

  Find (line ~4991, in `ThumbStripModule.render()`):
  ```js
  this._container.innerHTML = `
      <div class="preview-empty" style="padding:16px;text-align:center">
          <span class="preview-empty-icon">📄</span>
          <span style="font-size:12px">Chưa có file</span>
      </div>`;
  ```
  Replace with:
  ```js
  this._container.innerHTML = `
      <div class="preview-empty" style="padding:16px;text-align:center">
          <span class="preview-empty-icon">📄</span>
          <span style="font-size:12px">${I18nModule.t('preview.thumbEmpty')}</span>
      </div>`;
  ```

- [ ] **Step 7E.7: 7A.3 severity fix — printerLost toast uses 'warning' not 'error'**

  In Step 7A.3 migration, the plan shows `'error'` severity but the actual code (line 1175) uses `'warning'`:
  ```js
  showToast('Máy in đã chọn không còn khả dụng. Vui lòng chọn lại.', 'warning');
  ```
  Correct replacement:
  ```js
  showToast(I18nModule.t('toast.printerLost'), 'warning');
  ```
  (Step 7A.3 in the plan must preserve `'warning'` not change to `'error'`.)

- [ ] **Step 7E.8: Fix `flip.autoTimer` innerHTML risk in `I18nModule.applyAll()`**

  The key `flip.autoTimer` (VI: `'Tự động tiếp tục sau <strong>30 giây</strong>'`) contains HTML tags.
  If `applyAll()` uses `el.textContent = t(key)`, the `<strong>` will render as literal text.

  In Task 3's `I18nModule.applyAll()` implementation, ensure that elements with `data-i18n` that contain HTML are assigned via `innerHTML`:

  Option A — Element-specific check (recommended):
  ```js
  // In applyAll(), replace the generic textContent assignment with:
  document.querySelectorAll('[data-i18n]').forEach(el => {
      const key = el.dataset.i18n;
      const val = I18nModule.t(key);
      if (typeof val === 'string') {
          // Use innerHTML only for keys known to contain HTML tags
          const htmlKeys = new Set(['flip.autoTimer', 'preview.empty', 'error.uploadFailed', 'error.convertFailed']);
          if (htmlKeys.has(key)) {
              el.innerHTML = val;
          } else {
              el.textContent = val;
          }
      }
  });
  ```

  Option B — Auto-detect HTML (simpler):
  ```js
  if (val.includes('<')) {
      el.innerHTML = val;
  } else {
      el.textContent = val;
  }
  ```

  **Use Option B** — it's simpler and handles future keys automatically. Update Task 3's `applyAll()` accordingly.

- [ ] **Step 7E.9: Tab tooltips and `#file-tab-add-btn` title (app.js lines ~1451, ~1459; index.html line 68)**

  **Landscape badge tooltip** (line ~1451, in `TabsModule.render()`):
  Find:
  ```js
  badge.title = 'File này toàn trang ngang — tự động lật theo cạnh ngắn khi in 2 mặt';
  ```
  Replace with:
  ```js
  badge.title = I18nModule.t('tab.landscapeBadge');
  ```

  **Tab close button tooltip** (line ~1459, in `TabsModule.render()`):
  Find:
  ```js
  closeBtn.title = 'Đóng file';
  ```
  Replace with:
  ```js
  closeBtn.title = I18nModule.t('tab.close');
  ```

  **`#file-tab-add-btn` title attribute** (index.html line 68):
  Task 5's replacement block must preserve the `title` attribute using `data-i18n-title`. In the Step 5.2 HTML block, ensure the add-file button is:
  ```html
  <button id="file-tab-add-btn" class="file-tab-add" type="button"
      data-i18n="btn.addFile"
      data-i18n-title="tab.addTitle">+ Thêm file</button>
  ```
  And `I18nModule.applyAll()` already handles `[data-i18n-title]` elements (Task 3, title attributes section).

- [ ] **Step 7E.10: Verify syntax**

  ```powershell
  node --check frontend/app.js
  ```
  Expected: no output (success)

- [ ] **Step 7E.11: Commit**

  ```powershell
  git add frontend/app.js
  git commit -m "feat(i18n): migrate remaining dynamic strings — history, confirm modal, error panel, preview empties"
  ```

---

## Task 8: End-to-End Verification

- [ ] **Step 8.1: Build backend**

  ```powershell
  cd backend
  dotnet build
  ```
  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8.2: Syntax check frontend**

  ```powershell
  node --check frontend/app.js
  ```
  Expected: no output

- [ ] **Step 8.3: Open app in browser**

  Start backend:
  ```powershell
  cd backend; dotnet run
  ```
  Serve frontend:
  ```powershell
  cd frontend; python -m http.server 8080
  ```
  Navigate to `http://localhost:8080`

- [ ] **Step 8.4: Verify default VI language**

  - All header text in Vietnamese: "🖨 Chọn máy in…", "In thông minh", "In Sách", "Trang: 1-∞"
  - 3 new buttons visible: "⚙️ Cài đặt", "❓ Hướng dẫn", "🌐 VI"
  - "⚙️ Cài đặt" button is **disabled** (no printer selected)
  - `document.title` shows "Máy In Thông Minh"

- [ ] **Step 8.5: Verify printer auto-select enables Settings button**

  - Wait for printers to load (app auto-selects default printer)
  - "⚙️ Cài đặt" button should become **enabled** automatically (race condition fix)

- [ ] **Step 8.6: Verify language toggle**

  Click "🌐 VI" → changes to "🌐 EN", ALL text switches to English:
  - "🖨 Select printer…", "Smart Print", "Booklet", "Pages: 1-∞"
  - "📄 Page View", "🖨 Print Preview"
  - "⚙️ Settings", "❓ Help"
  - `document.title` shows "Smart Printer"

  Refresh page → stays in EN (localStorage persistence)

  Click "🌐 EN" → switches back to VI

- [ ] **Step 8.7: Verify Guide modal**

  Click "❓ Hướng dẫn":
  - Modal opens, title "Hướng dẫn sử dụng"
  - 4 tabs visible and scrollable on small screens
  - Each tab shows correct content
  - Click ✕ or overlay → closes; Escape key → closes

  Switch to EN → click "❓ Help":
  - Title: "User Guide"; tabs in English; content in English

- [ ] **Step 8.8: Verify Printer Settings button**

  Select a printer → "⚙️ Cài đặt" becomes enabled.
  Click it → Windows Printer Properties dialog opens.
  Toast: "Đã mở cài đặt máy in" (VI) / "Printer settings opened" (EN)

- [ ] **Step 8.9: Final commit**

  ```powershell
  git add -A
  git commit -m "feat: printer settings, user guide modal, and full i18n VI/EN support"
  ```

---

## Self-Review Checklist

- [x] **Spec coverage:** All acceptance criteria have corresponding tasks
- [x] **No placeholders:** All code blocks are complete and runnable
- [x] **Type consistency:** `I18nModule.t()` used consistently; function-valued strings called as `I18nModule.t('key')(args)`
- [x] **Backend model:** `PrinterSettingsRequest` defined in Task 1 and used in same task
- [x] **`using System.Diagnostics;`** added to `BackendStartup.cs` (Step 1.2)
- [x] **Program.cs log** added for new endpoint (Step 1.4)
- [x] **Script load order:** `vi.js` → `en.js` → `app.js` — globals available before `I18nModule.init()`
- [x] **`I18nModule.init()` called last** in DOMContentLoaded — after all modules wired
- [x] **GuideModule ref in I18nModule.applyAll()** — defined before `I18nModule.init()` runs
- [x] **Race condition fixed:** `sel.dispatchEvent(new Event('change'))` added as last line inside `if (def && forceDefault)` block in `_renderPrinters` (line ~1143, Step 6.4)
- [x] **Exact insertion point:** New module inits go after `StepIndicatorModule.update()`, before `mode-select` handler (line ~5332)
- [x] **`overflow-x: auto` on `.guide-tabs`** for small screens (Task 4)
- [x] **`document.title` updated** in `I18nModule.applyAll()` (Task 3)
- [x] **`header.printer.ariaLabel`** present in both vi.js and en.js (Step 2.1, 2.2)
- [x] **Collate label** bare text node wrapped in `<span data-i18n>` (Step 5.3 note)
- [x] **Context menu HTML** preserves all `data-action`, `data-submenu`, `class`, `id` attributes exactly — only adds `data-i18n` to `<span>` text nodes (Step 5.7)
- [x] **50+ toast/dynamic strings** fully covered in vi.js, en.js, and Task 7 (7A.1–7A.25)
- [x] **`toast.reprintSuccess` / `toast.reprintSuccessMixed`** added (line ~2140, ~2411) — Step 7A.24
- [x] **`sr.printingFile`** added (line ~2635) — Step 7A.25
- [x] **Sheet labels use middle dot `·`** not en dash `–` — matches actual code (lines 4282, 4439, 4447)
- [x] **Print button cancel uses `✕`** not `⏹` — matches actual code; `done` uses `✓` not `✅`
- [x] **ConfirmPrintModal time strings** migrated in Step 7B.1 (lines ~3338–3339)
- [x] **SVG flip text** migrated via `flip.*` keys (Step 7C.1)
- [x] **Rotation labels object** migrated to use `I18nModule.t()` (Step 7A.14)
- [x] **Summary time strings** migrated (Step 7B.1)
- [x] **SRModule announce strings** migrated (Steps 7A.1, 7A.9, 7A.11)
- [x] **Badge text** migrated for all 3 occurrences (lines ~721, ~866, ~1955) — `'1 MẶT'`/`'2 MẶT'` → `zoom.singleSided`/`zoom.doubleSided` (Step 7D.8)
- [x] **Context menu header JS** migrated (line ~2068) — `'Trang trắng'`/`` `Trang ${n}` `` → `ctx.blankPage`/`ctx.page(n)` (Step 7D.9)
- [x] **`mode.*` keys** added to vi.js/en.js — covers `modeLabel` maps in HistoryModule + ConfirmPrintModal (Step 7E.1)
- [x] **`historyItem.*` keys** added — reprint/delete tooltips, pages/copies unit labels (Step 7E.2)
- [x] **`confirmRow.*` keys** added — all 6 row labels + range strings + sheets/copies format (Step 7E.3)
- [x] **`error.printFailed` + `error.retry`** added — inline card error message (Step 7E.4)
- [x] **`preview.empty` uses innerHTML** — contains `<strong>` tag, assigned via `innerHTML` not `textContent` (Step 7E.5)
- [x] **`preview.thumbEmpty`** migrated — ThumbStrip empty state (Step 7E.6)
- [x] **`toast.printerLost` severity** — uses `'warning'` not `'error'` (Step 7E.7)
- [x] **`flip.autoTimer` innerHTML risk** fixed — `applyAll()` uses `val.includes('<')` check to assign via `innerHTML` (Step 7E.8)
- [x] **`applyAll()` innerHTML fix applied in Task 3** — not deferred to 7E, directly in the implementation code block
- [x] **Summary bar emoji/separator match code** — `summary.sheets` uses `🗒️` (not `🖨`); `summary.copies` uses `·` prefix (not `×`); `summary.files` key added (Step 7B.1)
- [x] **`confirm.noPages` string** migrated (line ~3314) — Step 7E.4a
- [x] **`fileLabel` in ConfirmPrintModal** — `N file` is universal, no translation needed (Step 7E.3 note)
- [x] **`Trang:` label** explicitly replaced with `confirmRow.pagesLabel` (Step 7E.3a)
- [x] **`toast.printerLost` severity** — uses `'warning'` in BOTH 7A.3 and 7E.7 (consistent)
- [x] **Print button emoji** — `🖨️` (with variation selector U+FE0F) in Step 7D.2 find string
- [x] **Badge text typo** — line ~1956 has `'1 MAT'`/`'2 MAT'` (no diacritics); Step 7D.8 handles separately from lines ~721/~866
- [x] **Tab tooltips** migrated — landscape badge + close button via `tab.*` keys (Step 7E.9)
- [x] **`#file-tab-add-btn` title** preserved via `data-i18n-title="tab.addTitle"` (Step 7E.9 + Step 5.2 note)
- [x] **`sr.printCancelled` + `sr.printSuccess`** added to vi.js/en.js `sr` section; migrated in Steps 7A.16 and 7A.19
- [x] **`print.sendingSingle`** added to vi.js/en.js; Step 7D.2 covers both branches of the print-button ternary (multi-file and single-file)
- [x] **rangeStr 3rd branch** (`|| 'Tất cả'`) migrated in Step 7E.3 — all 3 branches of rangeStr ternary use `I18nModule.t()`
- [x] **Booklet face page labels** (`` `Trang ${n ?? '—'}` `` × 4 calls) — `sheet.bookletFacePage` key added; migrated in Step 7D.6
- [x] **`backSimplex` + `backBlank`** — `sheet.backSimplex`/`sheet.backBlank` keys added; back-face ternary fully migrated in Step 7D.6
- [x] **`sheet.deleteBlank`** — `'Xóa trang trắng'` (line ~4319 blank-delete button title) migrated in Step 7D.6
- [x] **`sheet.ejectHint` + `sheet.ejectLabel`** — ejected card title and label text (lines ~4475, ~4479) migrated in Step 7D.6
