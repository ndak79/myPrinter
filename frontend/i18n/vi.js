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
