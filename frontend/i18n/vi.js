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
    recovery: 'Cứu lỗi In',
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
    deselectPage: 'Bỏ in trang này',
    reselectPage: 'In lại trang này',
    page: (n) => `Trang ${n}`,
  },

  // Flip modal (manual duplex instructions)
  flip: {
    title: '📄 Hướng Dẫn Đặt Giấy',
    check1: '1. Chờ máy in xong — đèn ngừng nhấp nháy',
    check2: '2. Lấy chồng giấy ra — theo đúng hướng mũi tên',
    check3: '3. Đặt lại vào khay — mặt trắng ngửa lên',
    continue: '✓ Đã Đặt Giấy - Tiếp Tục In',
    // SVG animation text (used in _showFlipModal)
    paperPrinted: 'Giấy đã in',
    side1Done: 'mặt 1 ✓',
    paperTray: 'Khay giấy',
    horizontalSteps: 'Lấy ra → Lật ngang → Đặt lại',
    verticalSteps: 'Lấy ra → Lật → Đặt lại',
    defaultInstruction: 'Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.',
  },

  recovery: {
    open: 'Giấy bị kẹt / hỏng',
    unavailable: 'Chưa có lệnh in thủ công nào có thể cứu lỗi',
    availablePhase1: 'Cứu lỗi trước khi lật giấy',
    availablePhase2: 'Cứu lỗi sau khi in mặt sau',
    noContext: 'Chưa có lệnh in nào có thể cứu lỗi.',
    selectJob: 'Chọn lệnh in cần khôi phục',
    title: 'Khôi phục sau kẹt giấy',
    intro: 'Chọn các tờ bị kẹt, bị rách hoặc chưa ra khỏi máy. Ứng dụng sẽ in lại mặt trước của các tờ đó để bạn thay vào xấp giấy trước khi lật.',
    rangePlaceholder: 'Ví dụ: 5-7, 10',
    applyRange: 'Chọn dải',
    clear: 'Bỏ chọn',
    submit: 'In lại tờ đã chọn',
    retrySubmit: 'In lại lần nữa',
    noPlan: 'Không có kế hoạch in để khôi phục.',
    noSelection: 'Chọn ít nhất một tờ bị lỗi.',
    multiCopyUnsupported: 'Nếu in nhiều bản copy, hãy chọn đúng bản copy trước khi chọn các tờ lỗi.',
    copyLabel: 'Bản copy',
    copyOption: (copy) => `Bản ${copy}`,
    sheet: (n) => `Tờ ${n}`,
    copySheet: (copy, sheet) => `Bản ${copy} · Tờ ${sheet}`,
    front: (n) => n ? `Mặt trước: Trang ${n}` : 'Mặt trước: Trắng',
    back: (n) => n ? `Mặt sau: Trang ${n}` : 'Mặt sau: Trắng',
    printed: (n) => `Đã in lại ${n} tờ. Thay các tờ hỏng vào đúng vị trí rồi mới bấm tiếp tục.`,
    retryHint: 'Nếu lần in lại vẫn lỗi, giữ lựa chọn hiện tại và bấm In lại lần nữa.',
    failed: (msg) => `Không thể in lại tờ lỗi: ${msg}`,
    selected: (n) => `Đã chọn ${n} tờ lỗi`,
  },

  phase2Recovery: {
    title: 'Cứu lỗi sau khi in mặt sau',
    backSent: 'Đã gửi lượt in mặt sau. Nếu phát hiện tờ lỗi, dùng Cứu lỗi In trên thanh điều khiển.',
    selectIntro: 'Chọn các tờ bị hỏng hoặc chưa chắc đã in xong. Ứng dụng sẽ in lại cả hai mặt trên tờ thay thế.',
    passRangePlaceholder: 'Ví dụ: tờ 1-3, 5',
    passSheet: (_pass, sheet) => `Tờ ${sheet}`,
    passCopySheet: (copy, sheet) => `Bản ${copy} · Tờ ${sheet}`,
    noMatchingSheets: 'Không tìm thấy tờ nào trong dải đã nhập.',
    printReplacementFronts: 'In mặt trước tờ thay thế',
    frontPrinted: (n) => `Đã in mặt trước cho ${n} tờ thay thế. Tiếp theo chỉ lật các tờ thay thế này.`,
    retryFronts: 'In lại mặt trước lần nữa',
    frontRetried: (n) => `Đã in lại mặt trước cho ${n} tờ thay thế. Nếu ổn, đặt lại các tờ này để in mặt sau.`,
    flipIntro: 'Lấy các tờ thay thế vừa in mặt trước, đặt lại vào khay theo cùng hướng dẫn lật giấy, rồi in mặt sau cho mini stack này.',
    printReplacementBacks: 'Đã đặt giấy - in mặt sau',
    backPrinted: (n) => `Đã in mặt sau cho ${n} tờ thay thế. Thay các tờ hỏng trong xấp giấy.`,
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
    cancelFromFlip: 'Huỷ in',
    reopenFlip: '↩ Mở hướng dẫn lật giấy',
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
    previewNotReady: 'Bản xem trước PDF chưa sẵn sàng',
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
      start:    'Bắt đầu',
      modes:    'Chế độ in',
      custom:   'Tùy chỉnh',
      flip:     'Lật giấy',
      tips:     'Mẹo hay',
    },
    start: { content: `
      <h3>Bắt đầu</h3>

      <h4>① Chọn máy in</h4>
      <p>Chọn máy in từ danh sách ở thanh trên cùng. Nếu không thấy máy in, hãy kiểm tra kết nối và đảm bảo máy in đang bật.</p>

      <h4>② Thêm tài liệu</h4>
      <ul>
        <li>Kéo thả file vào vùng xem trước, hoặc nhấn <strong>+ Thêm file</strong></li>
        <li>Hỗ trợ: PDF, Word, Excel, PowerPoint — các định dạng Office được chuyển đổi tự động</li>
        <li>Thêm nhiều file cùng lúc — mỗi file có một tab riêng</li>
      </ul>

      <h4>③ Chọn chế độ in</h4>
      <p>Sử dụng bộ chọn chế độ ở góc trên bên phải:</p>
      <ul>
        <li>🔄 <strong>In thông minh (Duplex)</strong> — In 2 mặt thủ công trên máy in 1 mặt</li>
        <li>📖 <strong>In Sách</strong> — In sách A5 gấp đôi từ giấy A4</li>
      </ul>

      <h4>④ Bắt đầu in!</h4>
      <p>Nhấn nút <strong>🖨 In</strong> ở góc dưới bên phải. App sẽ in mặt trước trước, sau đó hướng dẫn bạn lật giấy để in mặt sau. Xem tab <em>"Lật giấy"</em> để biết chi tiết.</p>

      <h4>📋 Xem lịch sử in</h4>
      <p>Mọi lệnh in đều được ghi lại. Nhấn <strong>📋 Lịch sử in</strong> trên thanh tiêu đề để xem lại — tiện lợi để kiểm tra file nào đã in.</p>
    ` },
    modes: { content: `
      <h3>🖨 Chế độ in</h3>

      <h4>🔄 In thông minh (Duplex)</h4>
      <p>Chế độ mặc định — biến máy in 1 mặt thành "máy in 2 mặt" bằng cách:</p>
      <ol>
        <li><strong>Bước 1:</strong> App gửi tất cả <strong>mặt trước</strong> (trang 1, 3, 5…) đến máy in</li>
        <li><strong>Bước 2:</strong> In xong → hướng dẫn lật giấy xuất hiện trên màn hình</li>
        <li><strong>Bước 3:</strong> Bạn lật giấy theo hướng dẫn, đặt lại vào khay và nhấn "Tiếp tục"</li>
        <li><strong>Bước 4:</strong> App in <strong>mặt sau</strong> (trang 2, 4, 6…) — xong!</li>
      </ol>
      <p>💡 <em>Kết quả: mỗi tờ giấy có nội dung cả 2 mặt, tiết kiệm 50% giấy!</em></p>

      <h4>📖 In Sách (Booklet)</h4>
      <p>Chế độ đặc biệt — in 4 trang A5 trên 2 mặt giấy A4; gấp đôi lại thành sách nhỏ.</p>
      <p>App tự động tính thứ tự trang để khi gấp đôi, các trang nằm đúng vị trí:</p>
      <table style="border-collapse:collapse; margin:10px 0; font-size:13px; width:100%">
        <tr style="background:#e8f0fe">
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:left">Tờ</th>
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:center">Mặt trước</th>
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:center">Mặt sau</th>
        </tr>
        <tr>
          <td style="padding:6px 12px; border:1px solid #e2e8f0">Tờ 1</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Trang 8 | Trang 1</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Trang 2 | Trang 7</td>
        </tr>
        <tr style="background:#f8fafc">
          <td style="padding:6px 12px; border:1px solid #e2e8f0">Tờ 2</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Trang 6 | Trang 3</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Trang 4 | Trang 5</td>
        </tr>
      </table>
      <p>💡 <em>Sau khi in, gấp đôi chồng giấy A4 → bạn có một cuốn sách A5 hoàn chỉnh!</em></p>

      <h4>🔍 Xem trước khi in</h4>
      <p>Nhấn <strong>🖨 Xem trước khi in</strong> trên thanh tiêu đề để xem bố cục thực tế trên tờ giấy. Bạn sẽ thấy chính xác mặt trước và mặt sau của mỗi tờ trước khi gửi đến máy in.</p>
    ` },
    custom: { content: `
      <h3>⚙ Tùy chỉnh</h3>

      <h4>📄 Chọn / Bỏ chọn trang</h4>
      <p><strong>Click phải</strong> vào bất kỳ trang nào ở khung xem trước bên trái → menu xuất hiện với các tùy chọn:</p>
      <ul>
        <li>✅ <strong>Chọn / Bỏ chọn trang</strong> — trang bị bỏ chọn sẽ không được in</li>
        <li>🔄 <strong>Mặt in → 1 mặt</strong> — trang đó chỉ in 1 mặt (mặt sau để trắng)</li>
        <li>🔄 <strong>Mặt in → 2 mặt</strong> — khôi phục in 2 mặt bình thường cho trang đó</li>
      </ul>

      <h4>📝 Nhập khoảng trang</h4>
      <p>Sử dụng ô nhập trang trên thanh tiêu đề để chọn nhanh. Ví dụ:</p>
      <ul>
        <li><code>1-5</code> — in trang 1 đến 5</li>
        <li><code>1,3,7</code> — chỉ in trang 1, 3 và 7</li>
        <li><code>2-8,12</code> — in trang 2–8 và trang 12</li>
      </ul>

      <h4>📋 Số bản & Ghép bộ</h4>
      <ul>
        <li><strong>Số bản</strong> — Chọn số bản in cho mỗi file (1–99)</li>
        <li><strong>Ghép bộ</strong> — Bật: in từng bộ hoàn chỉnh. Tắt: in tất cả trang 1, rồi tất cả trang 2…</li>
      </ul>

      <h4>🔄 Xoay trang</h4>
      <p>Click phải vào trang → xoay 90° theo chiều kim đồng hồ hoặc ngược lại. Hữu ích khi trang PDF bị xoay sai hướng.</p>

      <h4>🌄 Trang ngang</h4>
      <p>App <strong>tự động nhận diện</strong> hướng trang. Khi chuyển sang Xem trước khi in:</p>
      <ul>
        <li>📃 File có <strong>toàn trang dọc</strong> hoặc <strong>hướng hỗn hợp</strong> → trang ngang tự động xoay để chia sẻ tờ giấy với trang dọc</li>
        <li>🌄 File có <strong>toàn trang ngang</strong> → mỗi trang ngang in trên tờ riêng (giữ nguyên hướng ngang)</li>
      </ul>
    ` },
    flip: { content: `
      <h3>🔄 Hướng dẫn lật giấy</h3>
      <p>Đây là bước quan trọng nhất! Sau khi máy in xong mặt trước, bạn cần lật giấy đúng cách để in mặt sau.</p>

      <h4>📺 Hướng dẫn trực quan</h4>
      <p>App hiển thị <strong>hướng dẫn animation</strong> ngay trên màn hình — chỉ cần làm theo! Animation cho thấy:</p>
      <ul>
        <li>📍 Nơi lấy giấy ra (khay đầu ra)</li>
        <li>🔄 Hướng lật giấy</li>
        <li>📥 Khay đặt giấy vào, và mặt nào hướng lên</li>
      </ul>

      <h4>↕ Trang dọc</h4>
      <p>Lật theo <strong>cạnh dài</strong> — lật lên/xuống (như đọc sách).</p>

      <h4>↔ Trang ngang</h4>
      <p>Lật theo <strong>cạnh ngắn</strong> — lật trái/phải (như lịch để bàn).</p>

      <h4>✅ Từng bước thực hiện</h4>
      <ol>
        <li>⏳ <strong>Chờ máy in xong hoàn toàn</strong> — đèn ngừng nhấp nháy, tất cả trang đã ra</li>
        <li>📤 <strong>Lấy chồng giấy ra</strong> — giữ nguyên thứ tự, không xáo trộn</li>
        <li>🔄 <strong>Lật theo hướng dẫn animation</strong> — mặt trắng phải hướng lên</li>
        <li>📥 <strong>Đặt lại vào khay giấy</strong> — đúng chiều, không thêm giấy mới</li>
        <li>👆 Nhấn <strong>"✓ Đã Đặt Giấy - Tiếp Tục In"</strong></li>
      </ol>

      <h4>⏱ Tự động tiếp tục</h4>
      <p>Bật <strong>"Tự động tiếp tục sau 30 giây"</strong> → app đếm ngược và tự động in mặt sau. Rất tiện khi bạn đã quen thao tác!</p>

      <h4>⚠ In sai mặt?</h4>
      <p>Không sao! Thử <strong>lật giấy theo chiều ngược lại</strong>. Mỗi dòng máy in có bố trí khay khác nhau — thường chỉ cần thử 1–2 lần là quen.</p>
    ` },
    tips: { content: `
      <h3>💡 Mẹo hay</h3>

      <h4>⌨ Phím tắt</h4>
      <ul>
        <li><code>Ctrl + P</code> — In nhanh</li>
        <li><code>Esc</code> — Đóng hộp thoại đang mở (hướng dẫn, cài đặt…)</li>
      </ul>

      <h4>📑 Quản lý nhiều file</h4>
      <ul>
        <li>Kéo thả <strong>nhiều file</strong> cùng lúc</li>
        <li><strong>Kéo tab</strong> để sắp xếp lại thứ tự file</li>
        <li>Mỗi file giữ cài đặt riêng: chọn trang, số bản, xoay — không ảnh hưởng lẫn nhau</li>
        <li>Nhấn <strong>×</strong> trên tab để xóa file đó</li>
      </ul>

      <h4>🖨 Định dạng hỗ trợ</h4>
      <ul>
        <li>📄 <strong>PDF</strong> — mở trực tiếp, nhanh nhất</li>
        <li>📝 <strong>Word</strong> (.doc, .docx) — tự động chuyển đổi sang PDF</li>
        <li>📊 <strong>Excel</strong> (.xls, .xlsx) — tự động chuyển đổi</li>
        <li>💽 <strong>PowerPoint</strong> (.ppt, .pptx) — tự động chuyển đổi</li>
      </ul>

      <h4>🎯 Độ chính xác khi in</h4>
      <ul>
        <li>Luôn <strong>xem trước</strong> bằng chế độ "Xem trước khi in" trước khi nhấn In</li>
        <li>Dùng <strong>1 mặt</strong> cho trang bìa hoặc trang cuối</li>
        <li>In thử <strong>1–2 tờ</strong> trước khi in số lượng lớn</li>
      </ul>

      <h4>🌐 Ngôn ngữ</h4>
      <p>Nhấn nút <strong>🌐</strong> trên thanh tiêu đề để chuyển đổi giữa Tiếng Việt và Tiếng Anh.</p>
    ` },
  },
};

window.VI_STRINGS.guide = {
  title: 'Hướng dẫn in và xử lý kẹt giấy',
  tab: {
    start: 'Bắt đầu',
    modes: 'Kiểu in',
    custom: 'Chọn trang',
    flip: 'Lật giấy',
    recovery: 'Cứu lỗi In',
  },
  start: { content: `
    <h3>Bắt đầu in</h3>
    <ol>
      <li><strong>Chọn máy in</strong> ở thanh trên cùng. Nếu danh sách trống, kiểm tra máy in trong Windows rồi mở lại app.</li>
      <li><strong>Thêm file</strong> bằng cách kéo thả hoặc bấm <strong>+ Thêm file</strong>. App hỗ trợ PDF, Word và ảnh; file Word/ảnh sẽ được chuyển sang PDF trước khi in.</li>
      <li><strong>Kiểm tra trang cần in</strong>. Ô <strong>Trang</strong> để trống nghĩa là in tất cả.</li>
      <li><strong>Bấm In</strong>. Nếu máy in chỉ in 1 mặt, app sẽ in mặt trước trước rồi hiện hướng dẫn lật giấy.</li>
    </ol>
    <p>Luôn giữ nguyên thứ tự xấp giấy khi lấy ra khỏi máy. Đây là điều quan trọng nhất để mặt sau khớp đúng trang.</p>
  ` },
  modes: { content: `
    <h3>Chọn kiểu in</h3>
    <h4>In thông minh</h4>
    <p>Dùng cho tài liệu thông thường. App chia lệnh in thành hai lượt: mặt trước, sau đó mặt sau. Đây là lựa chọn mặc định cho máy in 1 mặt.</p>
    <h4>In Sách</h4>
    <p>Dùng khi muốn gấp giấy A4 thành sách A5. App tự sắp thứ tự trang để sau khi gấp, trang đọc theo đúng thứ tự.</p>
    <h4>Xem trước khi in</h4>
    <p>Bấm <strong>Xem trước khi in</strong> để kiểm tra từng tờ vật lý trước khi gửi lệnh. Nếu tài liệu có trang ngang, hãy xem trước để chắc bố cục đúng ý.</p>
  ` },
  custom: { content: `
    <h3>Chọn trang và chỉnh từng file</h3>
    <h4>Chọn nhanh bằng ô Trang</h4>
    <ul>
      <li><code>1-5</code>: in trang 1 đến 5</li>
      <li><code>1,3,7</code>: chỉ in trang 1, 3 và 7</li>
      <li><code>2-8,12</code>: in trang 2 đến 8 và trang 12</li>
    </ul>
    <h4>Chọn trực tiếp trên preview</h4>
    <p>Click phải vào trang để bỏ in, chọn in 1 mặt, xoay trang, khôi phục về in 2 mặt, hoặc chèn trang trắng trước/sau trang đó trong chế độ Xem trước khi in. Trang trắng có thể xóa ngay trong chế độ xem theo tờ.</p>
    <h4>Nhiều file</h4>
    <p>Mỗi file có tab riêng và giữ cài đặt riêng: trang được chọn, số bản, xoay trang và chế độ trang ngang.</p>
  ` },
  flip: { content: `
    <h3>Lật giấy khi in 2 mặt thủ công</h3>
    <ol>
      <li>Chờ máy in xong hoàn toàn lượt mặt trước.</li>
      <li>Lấy nguyên xấp giấy ra, không đảo thứ tự và không trộn thêm giấy mới.</li>
      <li>Làm đúng theo hình hướng dẫn trên màn hình: lấy từ khay ra, lật đúng chiều, đặt lại vào khay nạp.</li>
      <li>Bấm <strong>Đã đặt giấy - Tiếp tục in</strong> để in mặt sau.</li>
    </ol>
    <p>Nếu lỡ đóng hướng dẫn, bấm nút <strong>Mở hướng dẫn lật giấy</strong> để mở lại. Nếu muốn dừng lệnh, dùng <strong>Huỷ In</strong>.</p>
  ` },
  recovery: { content: `
      <h3>Cứu lỗi in khi kẹt giấy hoặc hỏng tờ</h3>
      <p>Nút <strong>Cứu lỗi In</strong> luôn nằm trên thanh điều khiển chính và tự bật khi app còn nhớ một lệnh in hai mặt thủ công có thể khôi phục. Nếu không có lỗi, bạn không cần bấm thêm nút nào sau khi máy in chạy xong.</p>
      <h4>Kẹt giấy trước khi lật giấy</h4>
    <ol>
      <li>Bấm <strong>Cứu lỗi In</strong> khi đang ở màn hướng dẫn lật giấy.</li>
      <li>Chọn các <strong>tờ vật lý</strong> bị kẹt, rách hoặc chưa ra khỏi máy. Ví dụ: <code>tờ 1-2</code>.</li>
      <li>App in lại mặt trước của các tờ đó.</li>
      <li>Thay các tờ lỗi vào đúng vị trí trong xấp giấy, rồi mới tiếp tục in mặt sau.</li>
    </ol>
    <h4>Lỗi sau khi đã in mặt sau</h4>
    <ol>
      <li>Sau khi máy in gửi lượt mặt sau, kiểm tra xấp giấy. Nếu có tờ hỏng, thiếu hoặc chưa chắc đúng, bấm <strong>Cứu lỗi In</strong> trên thanh điều khiển chính.</li>
      <li>Chọn các tờ bị hỏng theo số tờ đang hiển thị: <strong>Tờ 1</strong>, <strong>Tờ 2</strong>, ...</li>
      <li>In mặt trước cho tờ thay thế, lật riêng các tờ thay thế đó, rồi in mặt sau.</li>
      <li>Thay tờ hỏng trong xấp giấy bằng tờ thay thế. Nếu không có lỗi, lệnh in đã xong ở phía người dùng.</li>
    </ol>
    <p>Nếu in nhiều bản copy, hãy chọn đúng bản copy trước khi chọn tờ lỗi để app in đúng các tờ thay thế.</p>
  ` },
};
