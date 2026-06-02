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
    recovery: 'Fix print issue',
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
    deselectPage: 'Skip this page',
    reselectPage: 'Print this page',
    page: (n) => `Page ${n}`,
  },

  flip: {
    title: '📄 Paper Placement Guide',
    check1: '1. Wait for printing to finish — light stops blinking',
    check2: '2. Remove the paper stack — follow the arrow direction',
    check3: '3. Place back in tray — blank side facing up',
    continue: '✓ Paper Placed - Continue Printing',
    paperPrinted: 'Printed paper',
    side1Done: 'side 1 ✓',
    paperTray: 'Paper tray',
    horizontalSteps: 'Remove → Flip sideways → Replace',
    verticalSteps: 'Remove → Flip → Replace',
    defaultInstruction: 'Remove paper and place straight back in tray (printed side down). NO rotation needed.',
  },

  recovery: {
    open: 'Paper jam / damaged',
    unavailable: 'No manual print job is available for recovery',
    availablePhase1: 'Recover before flipping paper',
    availablePhase2: 'Recover after back-side printing',
    noContext: 'No print job is available for recovery.',
    title: 'Recover after paper jam',
    intro: 'Select sheets that jammed, tore, or did not leave the printer. The app will reprint the front side of those sheets so you can replace them before flipping the stack.',
    rangePlaceholder: 'Example: 5-7, 10',
    applyRange: 'Select range',
    clear: 'Clear',
    submit: 'Reprint selected sheets',
    retrySubmit: 'Reprint again',
    noPlan: 'No print plan is available for recovery.',
    noSelection: 'Select at least one failed sheet.',
    multiCopyUnsupported: 'For multi-copy jobs, select the correct copy before choosing failed sheets.',
    copyLabel: 'Copy',
    copyOption: (copy) => `Copy ${copy}`,
    sheet: (n) => `Sheet ${n}`,
    copySheet: (copy, sheet) => `Copy ${copy} · Sheet ${sheet}`,
    front: (n) => n ? `Front: Page ${n}` : 'Front: Blank',
    back: (n) => n ? `Back: Page ${n}` : 'Back: Blank',
    printed: (n) => `Reprinted ${n} sheet(s). Replace the damaged sheets in the stack before continuing.`,
    retryHint: 'If the retry fails again, keep the current selection and click Reprint again.',
    failed: (msg) => `Could not reprint failed sheets: ${msg}`,
    selected: (n) => `${n} failed sheet(s) selected`,
  },

  phase2Recovery: {
    title: 'Fix back-side print issues',
    backSent: 'Back side sent. If a sheet is wrong, use Fix print issue on the toolbar.',
    selectIntro: 'Select damaged or uncertain sheets. The app will reprint both sides onto replacement sheets.',
    passRangePlaceholder: 'Example: sheet 1-3, 5',
    passSheet: (_pass, sheet) => `Sheet ${sheet}`,
    passCopySheet: (copy, sheet) => `Copy ${copy} · Sheet ${sheet}`,
    noMatchingSheets: 'No sheets matched that range.',
    printReplacementFronts: 'Print replacement fronts',
    frontPrinted: (n) => `Printed front side for ${n} replacement sheet(s). Flip only those replacement sheets next.`,
    retryFronts: 'Reprint fronts again',
    frontRetried: (n) => `Reprinted front side for ${n} replacement sheet(s). If they look good, place them back to print backs.`,
    flipIntro: 'Take the replacement sheets just printed on the front side, place them back in the tray using the same flip direction, then print the back side for this mini stack.',
    printReplacementBacks: 'Paper placed - print backs',
    backPrinted: (n) => `Printed back side for ${n} replacement sheet(s). Replace the damaged sheets in the stack.`,
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
    cancelFromFlip: 'Cancel print',
    reopenFlip: '↩ Reopen flip guide',
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

  mode: {
    smart: 'Smart Print',
    booklet: 'Booklet',
    bookletFull: 'Booklet A5',
  },

  historyItem: {
    reprint: 'Reprint',
    delete: 'Delete',
    pages: (n) => `${n} pages`,
    copies: (n) => `${n} copies`,
  },

  tab: {
    landscapeBadge: 'This file is all landscape — automatically flips on short edge when printing double-sided',
    close: 'Close file',
    addTitle: 'Add file',
  },

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
      start:    'Get Started',
      modes:    'Print Modes',
      custom:   'Customize',
      flip:     'Paper Flip',
      tips:     'Tips',
    },
    start: { content: `
      <h3>🚀 Getting Started</h3>
      <p><strong>Smart Printer</strong> turns your regular single-sided printer into a "duplex printer" — in just a few simple steps.</p>

      <h4>① Select a Printer</h4>
      <p>Open the app → pick your printer from the dropdown in the top-left corner. The app remembers your last selection.</p>

      <h4>② Add Files to Print</h4>
      <ul>
        <li>📂 <strong>Drag & drop</strong> — Drop PDF, Word, Excel, or PowerPoint files onto the center of the screen</li>
        <li>➕ <strong>"+ Add File" button</strong> — Click to open a traditional file picker dialog</li>
        <li>📑 <strong>Multiple files</strong> — Add as many files as you like; each appears on its own tab</li>
      </ul>

      <h4>③ Choose a Print Mode</h4>
      <p>Use the mode selector in the top-right:</p>
      <ul>
        <li>🔄 <strong>Smart Print (Duplex)</strong> — Manual two-sided printing on a single-sided printer</li>
        <li>📖 <strong>Booklet</strong> — Print a folded A5 booklet from A4 paper</li>
      </ul>

      <h4>④ Hit Print!</h4>
      <p>Click the <strong>🖨 Print</strong> button in the bottom-right. The app prints the front sides first, then guides you through flipping the paper for the back sides. See the <em>"Paper Flip"</em> tab for details.</p>

      <h4>📋 Review Print History</h4>
      <p>Every print job is logged. Click <strong>📋 Print History</strong> in the header bar to review past jobs — handy for checking which files you've already printed.</p>
    ` },
    modes: { content: `
      <h3>🖨 Print Modes</h3>

      <h4>🔄 Smart Print (Duplex)</h4>
      <p>The default mode — turns a single-sided printer into a "duplex printer" by:</p>
      <ol>
        <li><strong>Step 1:</strong> App sends all <strong>front sides</strong> (pages 1, 3, 5…) to the printer</li>
        <li><strong>Step 2:</strong> Printing finishes → a flip-guide animation appears on screen</li>
        <li><strong>Step 3:</strong> You flip the paper as shown, place it back in the tray, and click "Continue"</li>
        <li><strong>Step 4:</strong> App prints the <strong>back sides</strong> (pages 2, 4, 6…) — done!</li>
      </ol>
      <p>💡 <em>Result: every sheet has content on both sides, saving 50% paper!</em></p>

      <h4>📖 Booklet</h4>
      <p>A special mode — prints 4 A5 pages on 2 sides of A4 paper; fold in half to make a compact book.</p>
      <p>The app automatically calculates page ordering so everything falls in the right place after folding:</p>
      <table style="border-collapse:collapse; margin:10px 0; font-size:13px; width:100%">
        <tr style="background:#e8f0fe">
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:left">Sheet</th>
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:center">Front</th>
          <th style="padding:6px 12px; border:1px solid #c4d7f2; text-align:center">Back</th>
        </tr>
        <tr>
          <td style="padding:6px 12px; border:1px solid #e2e8f0">Sheet 1</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Page 8 | Page 1</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Page 2 | Page 7</td>
        </tr>
        <tr style="background:#f8fafc">
          <td style="padding:6px 12px; border:1px solid #e2e8f0">Sheet 2</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Page 6 | Page 3</td>
          <td style="padding:6px 12px; border:1px solid #e2e8f0; text-align:center">Page 4 | Page 5</td>
        </tr>
      </table>
      <p>💡 <em>After printing, fold the A4 stack in half → you get a complete A5 booklet!</em></p>

      <h4>🔍 Print Preview</h4>
      <p>Click <strong>🖨 Print Preview</strong> in the header to see the actual sheet layout. You'll see exactly what the front and back of each sheet look like before sending to the printer.</p>
    ` },
    custom: { content: `
      <h3>⚙ Customization</h3>

      <h4>📄 Select / Deselect Pages</h4>
      <p><strong>Right-click</strong> any page in the left preview pane → a menu appears with options:</p>
      <ul>
        <li>✅ <strong>Select / Deselect page</strong> — deselected pages won't be printed</li>
        <li>🔄 <strong>Print sides → 1-sided</strong> — that page prints on one side only (back left blank)</li>
        <li>🔄 <strong>Print sides → 2-sided</strong> — restore normal duplex for that page</li>
      </ul>

      <h4>📝 Page Range Input</h4>
      <p>Use the page input field in the header for quick selection. Examples:</p>
      <ul>
        <li><code>1-5</code> — print pages 1 through 5</li>
        <li><code>1,3,7</code> — print only pages 1, 3, and 7</li>
        <li><code>2-8,12</code> — print pages 2–8 and page 12</li>
      </ul>

      <h4>📋 Copies & Collation</h4>
      <ul>
        <li><strong>Copies</strong> — Choose how many copies per file (1–99)</li>
        <li><strong>Collate</strong> — On: prints complete sets one at a time. Off: prints all page 1s, then all page 2s…</li>
      </ul>

      <h4>🔄 Rotate Pages</h4>
      <p>Right-click a page → rotate 90° clockwise or counter-clockwise. Useful when a PDF page is oriented incorrectly.</p>

      <h4>🌄 Landscape Pages</h4>
      <p>The app <strong>auto-detects</strong> page orientation. When switching to Print Preview:</p>
      <ul>
        <li>📃 Files with <strong>all portrait</strong> or <strong>mixed orientation</strong> → landscape pages auto-rotate to share sheets with portrait pages</li>
        <li>🌄 Files with <strong>all landscape</strong> pages → each landscape page prints on its own sheet (preserving landscape orientation)</li>
      </ul>
    ` },
    flip: { content: `
      <h3>🔄 Paper Flip Guide</h3>
      <p>This is the most important step! After the printer finishes the front sides, you need to flip the paper correctly for the back sides.</p>

      <h4>📺 Visual Animation</h4>
      <p>The app shows an <strong>animated guide</strong> right on screen — just follow along! The animation shows:</p>
      <ul>
        <li>📍 Where to pick up the paper (output tray)</li>
        <li>🔄 Which direction to flip</li>
        <li>📥 Which tray to place it in, and which side faces up</li>
      </ul>

      <h4>↕ Portrait Pages</h4>
      <p>Flip along the <strong>long edge</strong> — flip up/down (like reading a book).</p>

      <h4>↔ Landscape Pages</h4>
      <p>Flip along the <strong>short edge</strong> — flip left/right (like a desk calendar).</p>

      <h4>✅ Step-by-Step</h4>
      <ol>
        <li>⏳ <strong>Wait for printing to finish completely</strong> — light stops blinking, all pages out</li>
        <li>📤 <strong>Remove the paper stack</strong> — keep the order intact, don't shuffle</li>
        <li>🔄 <strong>Flip following the animation</strong> — blank side must face up</li>
        <li>📥 <strong>Place back in the paper tray</strong> — correct orientation, no new paper added</li>
        <li>👆 Click <strong>"✓ Paper Placed - Continue Printing"</strong></li>
      </ol>

      <h4>⏱ Auto-Continue</h4>
      <p>Enable <strong>"Auto-continue after 30 seconds"</strong> → the app counts down and starts back-side printing automatically. Great once you've got the hang of it!</p>

      <h4>⚠ Printed on the Wrong Side?</h4>
      <p>No worries! Try <strong>flipping the paper the opposite way</strong>. Every printer model has a different tray layout — it usually takes 1–2 tries to figure out.</p>
    ` },
    tips: { content: `
      <h3>💡 Tips & Tricks</h3>

      <h4>⌨ Keyboard Shortcuts</h4>
      <ul>
        <li><code>Ctrl + P</code> — Quick print</li>
        <li><code>Esc</code> — Close open dialogs (guide, settings…)</li>
      </ul>

      <h4>📑 Managing Multiple Files</h4>
      <ul>
        <li>Drag & drop <strong>multiple files</strong> at once</li>
        <li><strong>Drag tabs</strong> to reorder files</li>
        <li>Each file remembers its own settings: page selection, copies, rotation — no cross-contamination</li>
        <li>Click <strong>×</strong> on a tab to remove that file</li>
      </ul>

      <h4>🖨 Supported Formats</h4>
      <ul>
        <li>📄 <strong>PDF</strong> — opens directly, fastest</li>
        <li>📝 <strong>Word</strong> (.doc, .docx) — auto-converted to PDF</li>
        <li>📊 <strong>Excel</strong> (.xls, .xlsx) — auto-converted</li>
        <li>💽 <strong>PowerPoint</strong> (.ppt, .pptx) — auto-converted</li>
      </ul>

      <h4>🎯 Printing Accuracy</h4>
      <ul>
        <li>Always <strong>preview</strong> using "Print Preview" mode before hitting Print</li>
        <li>Use <strong>1-sided</strong> for cover pages or the last page</li>
        <li>Test-print <strong>1–2 sheets</strong> before running a large job</li>
      </ul>

      <h4>🌐 Language</h4>
      <p>Click the <strong>🌐</strong> button in the header to switch between Vietnamese and English.</p>
    ` },
  },

};

window.EN_STRINGS.guide = {
  title: 'Printing and Paper-Jam Guide',
  tab: {
    start: 'Start',
    modes: 'Print Type',
    custom: 'Pages',
    flip: 'Flip Paper',
    recovery: 'Fix Print',
  },
  start: { content: `
    <h3>Start a print job</h3>
    <ol>
      <li><strong>Select a printer</strong> from the top bar. If the list is empty, check the printer in Windows and reopen the app.</li>
      <li><strong>Add files</strong> by dragging them in or clicking <strong>+ Add File</strong>. The app supports PDF, Word, and image files; Word/image files are converted to PDF before printing.</li>
      <li><strong>Check the pages</strong>. Leaving the <strong>Pages</strong> box empty prints everything.</li>
      <li><strong>Click Print</strong>. For single-sided printers, the app prints the front sides first, then shows the paper-flip guide.</li>
    </ol>
    <p>Keep the paper stack in the same order when removing it from the printer. This is the most important rule for matching the back sides correctly.</p>
  ` },
  modes: { content: `
    <h3>Choose a print type</h3>
    <h4>Smart Print</h4>
    <p>Use this for normal documents. The app splits printing into two passes: front sides first, then back sides. This is the default choice for single-sided printers.</p>
    <h4>Booklet</h4>
    <p>Use this when you want to fold A4 paper into an A5 booklet. The app arranges pages so the booklet reads in the correct order after folding.</p>
    <h4>Print Preview</h4>
    <p>Click <strong>Print Preview</strong> to inspect each physical sheet before sending the job. If the document has landscape pages, preview it first to confirm the layout.</p>
  ` },
  custom: { content: `
    <h3>Select pages and adjust each file</h3>
    <h4>Use the Pages box</h4>
    <ul>
      <li><code>1-5</code>: print pages 1 through 5</li>
      <li><code>1,3,7</code>: print only pages 1, 3, and 7</li>
      <li><code>2-8,12</code>: print pages 2 through 8 and page 12</li>
    </ul>
    <h4>Use the preview</h4>
    <p>Right-click a page to exclude it, make it one-sided, rotate it, restore normal two-sided printing, or insert a blank page before/after it in Print Preview. Blank pages can be removed from the sheet view.</p>
    <h4>Multiple files</h4>
    <p>Each file has its own tab and keeps its own settings: selected pages, copies, rotation, and landscape handling.</p>
  ` },
  flip: { content: `
    <h3>Flip paper for manual two-sided printing</h3>
    <ol>
      <li>Wait until the printer completely finishes the front-side pass.</li>
      <li>Remove the whole stack without changing order and without adding new paper.</li>
      <li>Follow the on-screen guide exactly: take the stack from the output tray, flip it in the shown direction, and place it back into the input tray.</li>
      <li>Click <strong>Paper placed - continue printing</strong> to print the back sides.</li>
    </ol>
    <p>If you close the guide by mistake, click <strong>Reopen flip guide</strong>. To stop the job, use <strong>Cancel Print</strong>.</p>
  ` },
  recovery: { content: `
      <h3>Fix a paper jam or damaged sheet</h3>
      <p>The <strong>Fix print issue</strong> button stays on the main toolbar and becomes active when the app still has a recoverable manual two-sided job. If nothing is wrong, no extra button is required after the printer finishes.</p>
      <h4>Jam before flipping the paper</h4>
    <ol>
      <li>Click <strong>Fix print issue</strong> while the flip guide is open.</li>
      <li>Select the <strong>physical sheets</strong> that jammed, tore, or did not leave the printer. Example: <code>sheet 1-2</code>.</li>
      <li>The app reprints the front side for those sheets.</li>
      <li>Replace the bad sheets in the correct positions, then continue to the back-side pass.</li>
    </ol>
    <h4>Problem after the back-side pass</h4>
    <ol>
      <li>After the printer sends the back-side pass, inspect the stack. If a sheet is damaged, missing, or uncertain, click <strong>Fix print issue</strong> on the main toolbar.</li>
      <li>Select damaged sheets by the displayed sheet number: <strong>Sheet 1</strong>, <strong>Sheet 2</strong>, ...</li>
      <li>Print replacement fronts, flip only those replacement sheets, then print their backs.</li>
      <li>Replace the damaged sheets in the stack. If nothing is wrong, the job is already finished from the user's side.</li>
    </ol>
    <p>For multi-copy jobs, select the correct copy before choosing failed sheets so the app reprints the right replacement sheets.</p>
  ` },
};
