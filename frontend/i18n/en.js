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
