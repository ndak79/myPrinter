// ═══════════════════════════════════════════════════════════════════
// myPrinter — app.js
// Structure: AppState + 6 Module Objects + DOMContentLoaded init
// Compatible with file:// (no ES import/export)
// ═══════════════════════════════════════════════════════════════════

const API_BASE = (() => {
    // When running inside WinForms/WebView2, the port is passed as ?port=XXXX
    const params = new URLSearchParams(window.location.search);
    const port   = params.get('port') || '8787';
    return `http://localhost:${port}/api`;
})();

// ─── PDF.js worker (v5, ES module) ──────────────────────────────────
// Worker served locally to eliminate CDN latency (~200-500ms savings)
function _initPdfWorker() {
    if (typeof pdfjsLib !== 'undefined') {
        pdfjsLib.GlobalWorkerOptions.workerSrc = '/lib/pdf.worker.min.mjs';
    }
}
window.addEventListener('pdfjsReady', _initPdfWorker);
// Also try immediately in case script already loaded
_initPdfWorker();

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, ch => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;',
    }[ch]));
}

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
            if (el.id === 'print-btn' && el.dataset.mode) return;
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
        const printBtn = document.getElementById('print-btn');
        if (printBtn?.dataset.mode === 'flip-paused') {
            printBtn.textContent = this.t('print.reopenFlip');
        } else if (printBtn?.dataset.mode === 'cancellable' || printBtn?.dataset.mode === 'phase2-review') {
            printBtn.textContent = this.t('print.cancel');
        }
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

// ═══════════════════════════════════════════════════════════════════
// AppState — All application state centralized here
// ═══════════════════════════════════════════════════════════════════
const AppState = {
    selectedPrinter:       null,
    currentJob:            null,
    recoveryContext:       null,
    pendingPrintQueue:     null,  // B17-FE-2 fix: queue of remaining files after a manual-flip pause
    pendingManualBatch:    null,
    pendingManualReviewQueue: null,
    isUserTypingPageRange: false,
    printMode:             'duplex',   // 'duplex' | 'booklet'
    viewMode:              'page',     // 'page' | 'sheet'
    get landscapeMode() {
        return this.activeFile?.landscapeMode ?? 'together';
    },
    set landscapeMode(value) {
        if (this.activeFile) this.activeFile.landscapeMode = value;
    },

    // Multi-file
    files:           [],   // FileEntry[]
    activeFileIndex: -1,   // index into files[], -1 = no file loaded

    get activeFile() {
        return this.files[this.activeFileIndex] ?? null;
    },

    // Legacy shims — keep these so untouched code still works during migration
    get uploadedFile()     { return this.activeFile ? { id: this.activeFile.id, name: this.activeFile.name, needsConversion: this.activeFile.needsConversion } : null; },
    get currentPdfDoc()    { return this.activeFile?.pdfDoc ?? null; },
    set currentPdfDoc(v)   { if (this.activeFile) this.activeFile.pdfDoc = v; },
    get selectedPages()    { return this.activeFile?.selectedPages ?? new Set(); },
    set selectedPages(v)   { if (this.activeFile) this.activeFile.selectedPages = v; },
    get singleSidedPages() { return this.activeFile?.singleSidedPages ?? new Set(); },
    set singleSidedPages(v){ if (this.activeFile) this.activeFile.singleSidedPages = v; },
    get totalPageCount()   { return this.activeFile?.totalPageCount ?? 0; },
    set totalPageCount(v)  { if (this.activeFile) this.activeFile.totalPageCount = v; },
    get pageOrder()        { return this.activeFile?.pageOrder ?? []; },
    set pageOrder(v)       { if (this.activeFile) this.activeFile.pageOrder = v; },
    get pageRotations()    { return this.activeFile?.pageRotations ?? new Map(); },
    set pageRotations(v)   { if (this.activeFile) this.activeFile.pageRotations = v; },

    createFileEntry(id, name, needsConversion) {
        return {
            id, name, needsConversion,
            pdfDoc:           null,
            totalPageCount:   0,
            selectedPages:    new Set(),
            singleSidedPages: new Set(),
            pageOrder:        [],
            pageRotations:    new Map(),
            blankAbsorbedBy:  new Map(), // populated by buildSheetLayout; reset each render cycle
            _togetherRotations:      new Set(), // Set<pageNum> — pages auto-rotated by together mode
            _originalOrientationMap: null,      // Map<pageNum, bool> | null — pre-injection snapshot
            _pendingOrientationMap:  null,      // transient: set during async intrinsic detection, committed after guards (F1 fix)
            landscapeMode: 'together',   // 'separate' | 'together' — per-file, default matches current global default
            copies:        1,            // int 1–99 — per-file copy count
            collate:       true,         // bool — per-file collate setting
            _scrollPos: null,            // null = never rendered; { page, sheet, thumb } after first render
        };
    },

    addFile(entry) {
        this.files.push(entry);
        document.body.classList.add('has-files');
        this.activeFileIndex = this.files.length - 1;
    },

    removeFile(index) {
        const entry = this.files[index];
        // Release pdf.js worker memory for this document
        if (entry?.pdfDoc) {
            try { entry.pdfDoc.destroy(); } catch(_) {}
            entry.pdfDoc = null;
        }
        this.files.splice(index, 1);
        document.body.classList.toggle('has-files', this.files.length > 0);
        if (this.files.length === 0) {
            this.activeFileIndex = -1;
        } else if (index < this.activeFileIndex) {
            // Removed a file before the active file — shift index down
            this.activeFileIndex--;
        } else if (index === this.activeFileIndex) {
            // Removed the active file — stay at same position or clamp to last
            this.activeFileIndex = Math.min(index, this.files.length - 1);
        }
        // else: removed after active — no index change needed
    },

    setActiveFile(index) {
        if (index >= 0 && index < this.files.length) {
            this.activeFileIndex = index;
        }
    },

    selectAllPages() {
        const f = this.activeFile;
        if (!f) return;
        f.selectedPages = new Set();
        for (let i = 1; i <= f.totalPageCount; i++) f.selectedPages.add(i);
    },

    reset() {
        // B31-FE-7 fix: destroy pdf.js worker memory for all loaded documents before
        // clearing the files array. AppState.removeFile() does this individually, but
        // reset() previously dropped the array without calling destroy(), leaking the
        // pdf.js worker memory for every open document.
        for (const entry of this.files) {
            if (entry?.pdfDoc) {
                try { entry.pdfDoc.destroy(); } catch (_) {}
                entry.pdfDoc = null;
            }
        }
        this.files                = [];
        this.activeFileIndex      = -1;
        this.currentJob           = null;
        this.pendingPrintQueue    = null;  // B18-FE-1 fix: clear stale queue on full reset
        this.pendingManualBatch   = null;
        this.pendingManualReviewQueue = null;
        this.isUserTypingPageRange = false;
        this.printMode            = 'duplex'; // B24-FE-2 fix: reset to default so new session isn't contaminated
        this.viewMode             = 'page';   // B24-FE-2 fix: same
        // B29-FE-3 fix: keep the mode-select dropdown in sync with AppState.printMode.
        // Without this, the dropdown shows the previously selected mode (e.g. 'booklet')
        // after reset, while AppState.printMode is correctly 'duplex'. The dropdown's
        // change handler would then overwrite AppState.printMode with the stale display value.
        const modeSel = document.getElementById('mode-select');
        if (modeSel) modeSel.value = 'duplex';
    },
};

// ─── _teardownTogether ────────────────────────────────────────────
// Restores fileEntry to pre-together-mode state. Idempotent.
// Removes only auto-injected CCW90 rotations (tracked in _togetherRotations).
// User-set rotations (not in _togetherRotations) are never touched.
function _teardownTogether(fileEntry) {
    if (!fileEntry) return;
    for (const p of fileEntry._togetherRotations) {
        fileEntry.pageRotations.delete(p);
        fileEntry._orientationMap?.delete(p);  // force re-detect at original orientation
    }
    fileEntry._togetherRotations.clear();
    fileEntry._originalOrientationMap = null;
    fileEntry._pendingOrientationMap   = null; // discard any in-flight intrinsic detection (F1 fix)
}

// ─── lookAheadOrientation ──────────────────────────────────────────
// Determine effective orientation for a leading blank page (no group yet).
// Scans forward past the blank to find the first real page, returns its orientation.
function lookAheadOrientation(pages, blankIdx, orientationMap) {
    for (let i = blankIdx + 1; i < pages.length; i++) {
        if (pages[i] !== 0) return orientationMap.get(pages[i]) ?? false;
    }
    return false; // fallback: portrait
}

// ─── togglePageSelection ──────────────────────────────────────────
// Centralized toggle for page selection. Handles singleSidedPages cleanup (R8)
// and absorbed-blank cleanup (R7) when deselecting a single-sided page.
// CONTRACT: Caller MUST call PreviewPanelModule.render(entry) after this returns.
function togglePageSelection(entry, pageNum) {
    if (entry.selectedPages.has(pageNum)) {
        // B22-FE-8 fix: capture SS state BEFORE deleting it below, so the on-the-fly
        // absorption check (which replaces the blankAbsorbedBy map in page view) works.
        const wasSingleSided = entry.singleSidedPages.has(pageNum);

        // R8: deselect clears SS status
        entry.selectedPages.delete(pageNum);
        entry.singleSidedPages.delete(pageNum);

        // R7: if this page had absorbed a blank (R6), splice that blank out of pageOrder.
        // blankAbsorbedBy is populated by buildSheetLayout at last render.
        // Forward scan runs AFTER selectedPages.delete() so has(v) checks are accurate.
        // B22-FE-8 fix: blankAbsorbedBy is only populated in sheet view; in page view it
        // stays empty. Fall back to on-the-fly absorption check: a blank is absorbed iff
        // the page was single-sided (same semantic as blankAbsorbedBy, immune to view mode).
        const hasAbsorbedBlank = (entry.blankAbsorbedBy && entry.blankAbsorbedBy.has(pageNum))
            || wasSingleSided;
        if (hasAbsorbedBlank) {
            const rawIdx = entry.pageOrder.indexOf(pageNum);
            if (rawIdx >= 0) {
                for (let k = rawIdx + 1; k < entry.pageOrder.length; k++) {
                    const v = entry.pageOrder[k];
                    if (v === 0) {
                        entry.pageOrder.splice(k, 1);
                        break;
                    }
                    if (entry.selectedPages.has(v)) break; // selected page — stop
                    // deselected page — skip, continue forward
                }
            }
        }
    } else {
        entry.selectedPages.add(pageNum);
        // Do NOT auto-add to singleSidedPages — R11: re-select starts as duplex
    }
}

// ─── setSingleSided ───────────────────────────────────────────────
// Mark pageNum as single-sided. Invariant 5: blank (0) cannot be SS.
function setSingleSided(fileEntry, pageNum) {
    if (pageNum === 0) return;
    fileEntry.singleSidedPages.add(pageNum);
    PreviewPanelModule.render(fileEntry); // Invariant 7
}

// ─── unsetSingleSided ─────────────────────────────────────────────
// Remove single-sided status from one or more pages. If a page had absorbed
// a user blank (R6), removes that blank from pageOrder first (R7).
// pageNums: array of page numbers to unset.
function unsetSingleSided(fileEntry, pageNums) {
    // Phase 1: collect blank indices to remove (before splicing anything)
    const blankIndicesToRemove = [];
    for (const pageNum of pageNums) {
        // BUG FIX (B7b): previously guarded by fileEntry.blankAbsorbedBy.has(pageNum),
        // which is only populated during sheet-view renders. In page-view mode every
        // render() resets blankAbsorbedBy to new Map() without repopulating it, so the
        // guard was always false → absorbed blank never spliced from pageOrder → orphaned
        // blank survived → extra blank page printed.
        //
        // Fix: always run the forward scan — if a 0 exists after pageNum (skipping
        // deselected pages) it is the absorbed blank and must be removed. This is the
        // same on-the-fly derivation as buildEffectivePageOrder uses (B7 fix).
        const rawIdx = fileEntry.pageOrder.indexOf(pageNum);
        if (rawIdx >= 0) {
            for (let k = rawIdx + 1; k < fileEntry.pageOrder.length; k++) {
                const v = fileEntry.pageOrder[k];
                if (v === 0) {
                    blankIndicesToRemove.push(k);
                    break;
                }
                if (fileEntry.selectedPages.has(v)) {
                    break; // selected page encountered — blank not reachable
                }
                // deselected page — skip and continue forward
            }
        }
        fileEntry.singleSidedPages.delete(pageNum);
    }

    // Phase 2: splice in DESCENDING order to avoid index shift (Invariant 4)
    blankIndicesToRemove.sort((a, b) => b - a);
    for (const idx of blankIndicesToRemove) {
        fileEntry.pageOrder.splice(idx, 1);
    }

    PreviewPanelModule.render(fileEntry); // Invariant 7
}

// ─── buildEffectivePageOrder ──────────────────────────────────────
// Derive the page order to send to backend — strips absorbed blanks so backend
// does not double-blank (SS page already gets a system blank from ProcessMixedOrientation).
//
// Previously read fileEntry.blankAbsorbedBy, which is only populated during
// sheet-view renders. In page-view every render() resets it to new Map() without
// repopulating → stale empty Map → all blanks after SS pages were sent to backend
// → double-blank (Bug B7).
//
// Fix: derive absorption directly from fileEntry.singleSidedPages — a blank is
// "absorbed" iff its immediate predecessor in the selected+blank view is an SS page.
// This is exactly the same semantic blankAbsorbedBy encodes, but computed on-the-fly
// so the result is always correct regardless of view mode or render history.
function buildEffectivePageOrder(fileEntry) {
    // Derive pages[] — same filtered view buildSheetLayout uses (deselected pages excluded).
    // MUST use pages[] instead of raw pageOrder to correctly detect absorption adjacency
    // when a deselected page sits between a SS page and its absorbed blank.
    let pages = fileEntry.pageOrder.filter(
        p => p === 0 || fileEntry.selectedPages.has(p)
    );

    // Fallback: if pageOrder is empty but selectedPages is not (rare edge case),
    // derive from selectedPages to avoid mismatch between preview and print.
    if (!pages.length && fileEntry.selectedPages.size > 0) {
        pages = [...fileEntry.selectedPages].sort((a, b) => a - b);
    }

    // Strip blanks that were absorbed by SS pages; keep standalone blanks.
    // A blank is absorbed iff its immediate predecessor in the filtered view is
    // a single-sided page — the backend's ProcessMixedOrientation will re-add it.
    return pages.filter((p, i) => {
        if (p !== 0) return true;          // non-blank: always keep
        if (i === 0) return true;          // blank at front — no preceding SS page, never absorbed
        const prevPage = pages[i - 1];     // predecessor in filtered view
        // Derive absorption on-the-fly: absorbed iff predecessor is a selected SS page.
        // (Same semantics as blankAbsorbedBy but immune to stale cache state.)
        return !fileEntry.singleSidedPages.has(prevPage);
    });
}

// ═══════════════════════════════════════════════════════════════════
// buildSheetLayout — Compute physical sheet groups for sheet view
// Returns array of sheet objects: { sheetIndex, front, back, ... }
// ═══════════════════════════════════════════════════════════════════
function buildSheetLayout(fileEntry, printMode, orientationMap = null, landscapeMode = 'separate') {
    // pageOrder may contain 0 = user-inserted blank page
    const ordered = fileEntry.pageOrder.length
        ? fileEntry.pageOrder.filter(p => p === 0 || fileEntry.selectedPages.has(p))
        : Array.from(fileEntry.selectedPages).sort((a, b) => a - b);

    // If nothing selected, show all pages in order (blanks from pageOrder are preserved)
    const hasBlanks = fileEntry.pageOrder.some(p => p === 0);
    const pages = (ordered.length || hasBlanks)
        ? (ordered.length ? ordered : fileEntry.pageOrder.filter(p => p === 0))
        : Array.from({length: fileEntry.totalPageCount}, (_, i) => i + 1);

    const sheets = [];
    let blankAbsorbedBy = new Map(); // overwritten by duplex branch; empty for simplex/booklet

    if (printMode === 'simplex') {
        // Each page = its own sheet (front only)
        // BUG-6 fix: include isLandscape so _renderSheetView uses sheet-faces-col for landscape pages
        pages.forEach((p, i) => {
            sheets.push({ sheetIndex: i + 1, front: p, back: null, isLandscape: orientationMap?.get(p) ?? false });
        });

    } else if (printMode === 'booklet') {
        // Booklet: pad to multiple of 4, then fold order
        const padded = [...pages];
        while (padded.length % 4 !== 0) padded.push(null); // null = blank
        const n = padded.length;
        const sheetCount = n / 4;
        for (let s = 0; s < sheetCount; s++) {
            sheets.push({
                sheetIndex: s + 1,
                front: padded[n - 1 - s * 2],  // left side of front
                front2: padded[s * 2],           // right side of front
                back: padded[s * 2 + 1],         // left side of back
                back2: padded[n - 2 - s * 2],    // right side of back
                isBooklet: true,
            });
        }

    } else {
        // ── Duplex: spec §4.1 algorithm ──────────────────────────────────
        // ── Bước 1: Group pages by orientation ──────────────────────────
        const groups = [];
        let currentGroup = { isLandscape: null, pages: [] };

        for (let gi = 0; gi < pages.length; gi++) {
            const p = pages[gi];
            let effectiveOrientation;
            if (p === 0) {
                // Blank inherits orientation of current group;
                // if no group started yet, look ahead to first real page.
                effectiveOrientation = currentGroup.isLandscape !== null
                    ? currentGroup.isLandscape
                    : lookAheadOrientation(pages, gi, orientationMap ?? new Map());
            } else {
                effectiveOrientation = orientationMap ? (orientationMap.get(p) ?? false) : false;
            }

            if (currentGroup.isLandscape === null) {
                currentGroup.isLandscape = effectiveOrientation;
            }
            if (effectiveOrientation !== currentGroup.isLandscape) {
                groups.push(currentGroup);
                currentGroup = { isLandscape: effectiveOrientation, pages: [] };
            }
            currentGroup.pages.push({ pageNum: p });
        }
        groups.push(currentGroup);

        // Filter out ghost group from empty pages[] (isLandscape stays null, pages empty)
        const nonEmptyGroups = groups.filter(g => g.pages.length > 0);

        // ── Bước 2: Process each group, handle single-sided + blank absorption ──
        const logicalPages = []; // { pageNum: N|null|0, isLandscape: bool }

        for (const group of nonEmptyGroups) {
            const groupLogical = [];
            let i = 0;
            while (i < group.pages.length) {
                const p    = group.pages[i].pageNum;
                const next = group.pages[i + 1]?.pageNum; // undefined if last

                if (fileEntry.singleSidedPages.has(p)) {
                    // R2: close current sheet if in odd position
                    if (groupLogical.length % 2 === 1) {
                        groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
                    }
                    groupLogical.push({ pageNum: p, isLandscape: group.isLandscape });

                    if (next === 0) {
                        // R6: absorb the blank immediately after as back of SS sheet
                        groupLogical.push({ pageNum: 0, isLandscape: group.isLandscape });
                        blankAbsorbedBy.set(p, 0); // value 0 = sentinel for blank pageNum; map used as presence Set — only .has() matters
                        i += 2; // skip the blank
                    } else {
                        // No blank → auto-blank back
                        groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
                        i += 1;
                    }
                } else {
                    groupLogical.push({ pageNum: p, isLandscape: group.isLandscape });
                    i += 1;
                }
            }

            // R4: pad each orientation group to even count independently
            if (groupLogical.length % 2 === 1) {
                groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
            }
            logicalPages.push(...groupLogical);
        }

        // ── Bước 3: Pair logical pages into sheets ────────────────────
        let sheetIdx = 1;
        for (let j = 0; j < logicalPages.length; j += 2) {
            const f = logicalPages[j];
            const b = logicalPages[j + 1];

            if (!b) {
                // Should never happen — Bước 2 ensures even count per group
                console.error(`[buildSheetLayout] BUG: odd logicalPages at j=${j}. Bước 2 padding failed.`);
                break;
            }

            const isSingleForced = f.pageNum !== null
                && f.pageNum !== 0
                && (b.pageNum === null || b.pageNum === 0)
                && fileEntry.singleSidedPages.has(f.pageNum);

            sheets.push({
                sheetIndex:      sheetIdx++,
                front:           f.pageNum,
                back:            b.pageNum,
                isLandscape:     f.isLandscape,
                isSingleForced:  isSingleForced,
                backIsUserBlank: b.pageNum === 0,
            });
        }
    }
    // R15: deselectedPages = real pages that are not selected, in pageOrder sequence, no duplicates
    const seenDeselected = new Set();
    const deselectedPages = [];

    if (fileEntry.pageOrder.length > 0) {
        for (const p of fileEntry.pageOrder) {
            if (p !== 0 && !fileEntry.selectedPages.has(p) && !seenDeselected.has(p)) {
                seenDeselected.add(p);
                deselectedPages.push(p);
            }
        }
        // W3 fallback: pageOrder exists but contains only blanks (p===0)
        if (deselectedPages.length === 0 && fileEntry.selectedPages.size < fileEntry.totalPageCount) {
            for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                if (!fileEntry.selectedPages.has(p)) {
                    deselectedPages.push(p);
                }
            }
        }
    } else {
        // Fallback: pageOrder empty → derive from totalPageCount
        for (let p = 1; p <= fileEntry.totalPageCount; p++) {
            if (!fileEntry.selectedPages.has(p)) {
                deselectedPages.push(p);
            }
        }
    }

    return { sheets, blankAbsorbedBy, deselectedPages };
}

// ═══════════════════════════════════════════════════════════════════
// PrintPreviewModule — Full-screen preview modal with 2-panel layout
// Left: thumbnail panel (click→navigate, drag→multiselect, right-click→menu)
// Right: main view (all pages scrollable, synchronized with left panel)
// ═══════════════════════════════════════════════════════════════════
const PrintPreviewModule = {
    _observer:     null,    // IntersectionObserver for main view pages
    _thumbObserver: null,   // IntersectionObserver for thumbnail canvas rendering
    _activePage:   1,
    _isOpen:       false,
    _mainPageEls:  new Map(),   // pageNum → .preview-main-page element
    _thumbEls:     new Map(),   // pageNum → .preview-thumb-item element
    _thumbCache:   new Map(),   // pageNum → rendered offscreen canvas (thumb)
    _mainCache:    new Map(),   // pageNum → rendered offscreen canvas (main)
    _scrollTimer:  null,
    _thumbRenderTasks: new Map(), // pageNum → RenderTask (for cancel-and-replace)
    _mainRenderTasks:  new Map(), // pageNum → RenderTask (for cancel-and-replace)

    // ── Lasso state ───────────────────────────────────────────
    _lasso: {
        active:  false,
        startX:  0,
        startY:  0,
        el:      null,   // the .lasso-rect div
    },

    _cacheKey(pageNum) {
        return `${AppState.activeFile?.id ?? 'default'}-${pageNum}`;
    },

    init() {
        // Print Preview modal is replaced by persistent PreviewPanelModule.
        // Keep bulk action buttons, keyboard, lasso, and context-menu wiring.

        // Bulk action buttons inside modal (still work if elements exist)
        document.getElementById('preview-all-double-btn')
            ?.addEventListener('click', () => {
                AppState.selectAllPages(); AppState.singleSidedPages.clear();
                this._syncAll();
                showToast(I18nModule.t('toast.allDouble'));
            });
        document.getElementById('preview-all-single-btn')
            ?.addEventListener('click', () => {
                AppState.selectAllPages(); AppState.singleSidedPages = new Set(AppState.selectedPages);
                this._syncAll();
                showToast(I18nModule.t('toast.allSingle'));
            });
        document.getElementById('preview-deselect-all-btn')
            ?.addEventListener('click', () => {
                AppState.selectedPages.clear(); AppState.singleSidedPages.clear();
                this._syncAll();
                PrintModule.updateButton();
                showToast(I18nModule.t('toast.deselectAll'));
            });

        // Keyboard: Arrow keys navigate pages
        document.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown') { e.preventDefault(); this.setActivePage(Math.min(this._activePage + 1, AppState.totalPageCount)); }
            if (e.key === 'ArrowUp')   { e.preventDefault(); this.setActivePage(Math.max(this._activePage - 1, 1)); }
        });

        // Lasso on thumb panel
        const panel = document.getElementById('preview-thumb-panel');
        if (panel) {
            panel.addEventListener('pointerdown', (e) => this._lassoStart(e));
            document.addEventListener('pointermove', (e) => this._lassoMove(e));
            document.addEventListener('pointerup',   (e) => this._lassoEnd(e));
        }
    },

    // ── Render both panels ─────────────────────────────────────
    async _render() {
        this._thumbEls.clear();
        this._mainPageEls.clear();
        this._thumbCache.clear();
        this._mainCache.clear();

        const thumbGrid    = document.getElementById('preview-thumb-grid');
        const mainContainer = document.getElementById('preview-main-canvas-container');
        if (!thumbGrid || !mainContainer) return;

        thumbGrid.innerHTML    = '';
        mainContainer.innerHTML = `<div class="loading" style="padding:2rem;text-align:center;color:var(--text-muted)">${I18nModule.t('preview.loading')}</div>`;

        const order = AppState.pageOrder.length > 0 ? AppState.pageOrder : Array.from({ length: AppState.totalPageCount }, (_, i) => i + 1);

        // Build thumbnail items (lazy render via IntersectionObserver)
        this._thumbObserver = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting && !entry.target.dataset.rendered) {
                    const n = parseInt(entry.target.dataset.pageNumber);
                    this._renderThumb(entry.target, n);
                    this._thumbObserver?.unobserve(entry.target);
                }
            });
        }, { root: thumbGrid, rootMargin: '150px' });

        for (const pageNum of order) {
            const item = this._createThumbItem(pageNum);
            thumbGrid.appendChild(item);
            this._thumbEls.set(pageNum, item);
            this._thumbObserver.observe(item);
        }

        // Build main view pages (lazy render too)
        mainContainer.innerHTML = '';
        const list = document.createElement('div');
        list.style.cssText = 'width:100%;';

        this._observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting && !entry.target.dataset.rendered) {
                    const n = parseInt(entry.target.dataset.page);
                    this._renderMainPage(entry.target, n);
                    this._observer?.unobserve(entry.target);
                }
            });
        }, { root: mainContainer, rootMargin: '200px' });

        for (const pageNum of order) {
            const card = this._createMainCard(pageNum);
            list.appendChild(card);
            this._mainPageEls.set(pageNum, card);
            this._observer.observe(card);
        }
        mainContainer.appendChild(list);

        // Sync scroll: when user scrolls main view, update active thumb highlight
        mainContainer.addEventListener('scroll', () => {
            clearTimeout(this._scrollTimer);
            this._scrollTimer = setTimeout(() => this._syncActiveFromScroll(), 120);
        });

        // Bind drag-reorder to thumb grid
        const thumbGrid2 = document.getElementById('preview-thumb-grid');
        if (thumbGrid2) DragReorderModule.bindGrid(thumbGrid2);

        // Update header counts & footer summary
        this._updateCounts();
        this._updateFooterSummary();
        this._syncAllHighlights();

        // Scroll to first page active after a tick
        this._activePage = 1;
        this._highlightThumb(1);
    },

    // ── Thumbnail item ─────────────────────────────────────────
    _createThumbItem(pageNum) {
        const div = document.createElement('div');
        div.className = 'preview-thumb-item';
        div.dataset.pageNumber = pageNum;

        // Selection dot
        const dot = document.createElement('div');
        dot.className = 'thumb-sel-dot';
        div.appendChild(dot);

        // Page number label
        const label = document.createElement('div');
        label.className = 'thumb-page-num';
        label.textContent = pageNum;
        div.appendChild(label);

        // Click → navigate main view
        div.addEventListener('click', (e) => {
            if (e.button !== 0) return;
            this.setActivePage(pageNum);
        });

        // Right-click → context menu
        div.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            ContextMenu.show(e, pageNum);
        });

        return div;
    },

    async _renderThumb(item, pageNum) {
        try {
            let canvas;
            if (this._thumbCache.has(this._cacheKey(pageNum))) {
                canvas = this._thumbCache.get(this._cacheKey(pageNum));
            } else {
                // Cancel any in-flight render for this page
                const existing = this._thumbRenderTasks.get(pageNum);
                if (existing) { try { existing.cancel(); } catch {} }

                const page     = await AppState.currentPdfDoc.getPage(pageNum);
                const vp       = page.getViewport({ scale: 0.3 });
                const off      = document.createElement('canvas');
                off.width      = vp.width;
                off.height     = vp.height;
                const task     = page.render({ canvasContext: off.getContext('2d'), viewport: vp });
                this._thumbRenderTasks.set(pageNum, task);
                await task.promise;
                this._thumbRenderTasks.delete(pageNum);
                page.cleanup();
                this._thumbCache.set(this._cacheKey(pageNum), off);
                canvas = off;
            }

            const c = document.createElement('canvas');
            c.width  = canvas.width;
            c.height = canvas.height;
            c.getContext('2d').drawImage(canvas, 0, 0);
            item.insertBefore(c, item.firstChild);
            item.dataset.rendered = '1';

            // Apply rotation if any
            const rot = AppState.pageRotations.get(pageNum);
            if (rot) this._applyRotation(c, rot);
        } catch (err) {
            if (err?.name !== 'RenderingCancelledException') {
                console.error(`Thumb render error page ${pageNum}:`, err);
            }
        }
    },

    // ── Main view card ─────────────────────────────────────────
    _createMainCard(pageNum) {
        const div = document.createElement('div');
        div.className = 'preview-main-page';
        div.dataset.page = pageNum;

        const isSel    = AppState.selectedPages.has(pageNum);
        const isSingle = AppState.singleSidedPages.has(pageNum);
        div.classList.toggle('selected-for-print', isSel);
        div.classList.toggle('single-sided-print', isSingle && isSel);

        const header = document.createElement('div');
        header.className = 'preview-main-page-header';

        const title = document.createElement('h4');
        title.textContent = `Trang ${pageNum}`;
        header.appendChild(title);

        const badge = document.createElement('div');
        badge.className   = isSingle ? 'single-sided-badge' : 'double-sided-badge';
        badge.textContent = isSingle ? I18nModule.t('zoom.singleSided') : I18nModule.t('zoom.doubleSided');
        if (!isSel) badge.style.opacity = '0.3';
        header.appendChild(badge);

        div.appendChild(header);

        // Right-click
        div.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            ContextMenu.show(e, pageNum);
        });

        return div;
    },

    async _renderMainPage(card, pageNum) {
        try {
            let canvas;
            if (this._mainCache.has(this._cacheKey(pageNum))) {
                canvas = this._mainCache.get(this._cacheKey(pageNum));
            } else {
                // Cancel any in-flight render for this page
                const existing = this._mainRenderTasks.get(pageNum);
                if (existing) { try { existing.cancel(); } catch {} }

                const page = await AppState.currentPdfDoc.getPage(pageNum);
                const vp   = page.getViewport({ scale: 1.5 });
                const off  = document.createElement('canvas');
                off.width  = vp.width;
                off.height = vp.height;
                const task = page.render({ canvasContext: off.getContext('2d'), viewport: vp });
                this._mainRenderTasks.set(pageNum, task);
                await task.promise;
                this._mainRenderTasks.delete(pageNum);
                page.cleanup();
                this._mainCache.set(this._cacheKey(pageNum), off);
                canvas = off;
            }

            const c = document.createElement('canvas');
            c.width  = canvas.width;
            c.height = canvas.height;
            c.getContext('2d').drawImage(canvas, 0, 0);
            card.appendChild(c);
            card.dataset.rendered = '1';

            const rot = AppState.pageRotations.get(pageNum);
            if (rot) this._applyRotation(c, rot);
        } catch (err) {
            if (err?.name !== 'RenderingCancelledException') {
                console.error(`Main render error page ${pageNum}:`, err);
            }
        }
    },

    // ── Navigation ─────────────────────────────────────────────
    setActivePage(pageNum) {
        this._activePage = pageNum;
        this._highlightThumb(pageNum);
        this._scrollMainTo(pageNum);
    },

    jumpToFile(fileIndex) {
        AppState.setActiveFile(fileIndex);
        // Re-render preview with new active file's pages
        this._thumbCache.clear();
        this._mainCache.clear();
        this._render();
    },

    _scrollMainTo(pageNum) {
        const card      = this._mainPageEls.get(pageNum);
        const container = document.getElementById('preview-main-canvas-container');
        if (!card || !container) return;

        const cRect = container.getBoundingClientRect();
        const cTop  = card.getBoundingClientRect().top - cRect.top + container.scrollTop - 24;
        container.scrollTo({ top: cTop, behavior: 'smooth' });
    },

    _scrollThumbTo(pageNum) {
        const item = this._thumbEls.get(pageNum);
        const grid = document.getElementById('preview-thumb-grid');
        if (!item || !grid) return;
        const gRect = grid.getBoundingClientRect();
        const iRect = item.getBoundingClientRect();
        const top   = iRect.top - gRect.top + grid.scrollTop - grid.offsetHeight / 2 + item.offsetHeight / 2;
        grid.scrollTo({ top, behavior: 'smooth' });
    },

    _syncActiveFromScroll() {
        const container = document.getElementById('preview-main-canvas-container');
        if (!container) return;
        const cRect  = container.getBoundingClientRect();
        const center = cRect.top + cRect.height / 2;

        let nearest    = null;
        let minDist    = Infinity;
        this._mainPageEls.forEach((el, pageNum) => {
            const r    = el.getBoundingClientRect();
            const dist = Math.abs(r.top + r.height / 2 - center);
            if (dist < minDist) { minDist = dist; nearest = pageNum; }
        });
        if (nearest && nearest !== this._activePage) {
            this._activePage = nearest;
            this._highlightThumb(nearest);
            this._scrollThumbTo(nearest);
        }
    },

    // ── Highlight helpers ──────────────────────────────────────
    _highlightThumb(pageNum) {
        this._thumbEls.forEach((el, n) => el.classList.toggle('active-page', n === pageNum));
        this._mainPageEls.forEach((el, n) => el.classList.toggle('active-page', n === pageNum));
    },

    _syncAllHighlights() {
        this._thumbEls.forEach((el, n) => {
            const isSel    = AppState.selectedPages.has(n);
            const isSingle = isSel && AppState.singleSidedPages.has(n);
            el.classList.toggle('selected-for-print', isSel);
            el.classList.toggle('single-sided-print', isSingle);
            // sync rotation on thumbnail canvas
            const canvas = el.querySelector('canvas');
            if (canvas) this._applyRotation(canvas, AppState.pageRotations.get(n));
        });
        this._mainPageEls.forEach((el, n) => {
            const isSel    = AppState.selectedPages.has(n);
            const isSingle = isSel && AppState.singleSidedPages.has(n);
            el.classList.toggle('selected-for-print', isSel);
            el.classList.toggle('single-sided-print', isSingle);

            // update badge
            const badge = el.querySelector('.single-sided-badge, .double-sided-badge');
            if (badge) {
                badge.className   = isSingle ? 'single-sided-badge' : 'double-sided-badge';
                badge.textContent = isSingle ? I18nModule.t('zoom.singleSided') : I18nModule.t('zoom.doubleSided');
                badge.style.opacity = isSel ? '1' : '0.3';
            }
            // update rotation on canvas
            const canvas = el.querySelector('canvas');
            if (canvas) this._applyRotation(canvas, AppState.pageRotations.get(n));
        });
    },

    _syncAll() {
        this._syncAllHighlights();
        this._updateCounts();
        this._updateFooterSummary();
        // Also keep old thumbnail grid in sync (for other modules that reference it)
        PreviewModule.updateThumbnails();
        PageSelectModule.updateDisplay();
        // Rebuild SheetView immediately if active
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            PreviewPanelModule.render(AppState.activeFile);
        } else {
            PreviewPanelModule.onStateChanged();
        }
    },

    _updateCounts() {
        const total = AppState.totalPageCount;
        const sel   = AppState.selectedPages.size;
        const el1   = document.getElementById('preview-modal-page-count');
        const el2   = document.getElementById('preview-modal-selected-count');
        if (el1) el1.textContent = `${total} trang`;
        if (el2) el2.textContent = sel === total ? I18nModule.t('summary.allSelected') : I18nModule.t('summary.selected')(sel, total);
    },

    _updateFooterSummary() {
        const el = document.getElementById('preview-print-summary');
        if (!el) return;
        const pages   = AppState.selectedPages.size;
        const mode    = AppState.printMode || 'duplex'; // B26-FE-4: read from AppState not DOM
        const copies  = window.CopiesModule?.copies || 1;
        if (pages === 0) { el.innerHTML = ''; return; }

        let sheets;
        if (mode === 'booklet') { sheets = Math.ceil(pages / 4) * copies; }
        else { const s = AppState.singleSidedPages.size; sheets = (Math.ceil((pages - s) / 2) + s) * copies; }

        el.innerHTML = `<span>📄 ${pages} trang</span><span>·</span><span>🗒️ ${sheets} tờ</span>${copies > 1 ? `<span>· ${copies} bản</span>` : ''}`;
    },

    // ── Lasso (add-only brush select on thumbnail panel) ───────
    _lassoStart(e) {
        if (e.button !== 0) return;
        const panel = document.getElementById('preview-thumb-panel');
        const grid  = document.getElementById('preview-thumb-grid');
        if (!panel || !grid) return;

        // Only start lasso if NOT clicking on a thumbnail item directly
        if (e.target.closest('.preview-thumb-item')) return;

        this._lasso.active = true;
        const rect = panel.getBoundingClientRect();
        this._lasso.startX = e.clientX - rect.left;
        this._lasso.startY = e.clientY - rect.top + panel.scrollTop;

        const overlay = document.getElementById('lasso-overlay');
        if (overlay) {
            this._lasso.el = document.createElement('div');
            this._lasso.el.className = 'lasso-rect';
            overlay.appendChild(this._lasso.el);
        }

        panel.setPointerCapture(e.pointerId);
    },

    _lassoMove(e) {
        if (!this._lasso.active) return;
        const panel = document.getElementById('preview-thumb-panel');
        if (!panel) return;
        const rect = panel.getBoundingClientRect();
        const cx   = e.clientX - rect.left;
        const cy   = e.clientY - rect.top + panel.scrollTop;

        const x = Math.min(cx, this._lasso.startX);
        const y = Math.min(cy, this._lasso.startY);
        const w = Math.abs(cx - this._lasso.startX);
        const h = Math.abs(cy - this._lasso.startY);

        if (this._lasso.el) {
            this._lasso.el.style.cssText = `left:${x}px;top:${y}px;width:${w}px;height:${h}px;`;
        }

        // Highlight thumbnails that intersect with lasso rect
        this._thumbEls.forEach((el, pageNum) => {
            const elRect  = el.getBoundingClientRect();
            const panRect = panel.getBoundingClientRect();
            const elTop   = elRect.top - panRect.top + panel.scrollTop;
            const elBot   = elTop + elRect.height;
            const elLeft  = elRect.left - panRect.left;
            const elRight = elLeft + elRect.width;

            const lassoRight  = this._lasso.startX + (cx - this._lasso.startX);
            const lassoBottom = this._lasso.startY + (cy - this._lasso.startY);
            const lassoTop    = Math.min(this._lasso.startY, cy - rect.top + panel.scrollTop);
            const lassoLeft   = Math.min(this._lasso.startX, cx);
            const lassoR      = Math.max(this._lasso.startX, cx);
            const lassoB      = Math.max(this._lasso.startY, cy - rect.top + panel.scrollTop);

            const intersects = !(elRight < lassoLeft || elLeft > lassoR || elBot < lassoTop || elTop > lassoB);
            el.classList.toggle('lasso-hover', intersects);
        });
    },

    _lassoEnd(e) {
        if (!this._lasso.active) return;
        this._lasso.active = false;

        // Add all lasso-hover pages to selection
        this._thumbEls.forEach((el, pageNum) => {
            if (el.classList.contains('lasso-hover')) {
                AppState.selectedPages.add(pageNum);
                el.classList.remove('lasso-hover');
            }
        });

        // Cleanup lasso rect
        this._lasso.el?.remove();
        this._lasso.el = null;

        this._syncAll();
        PrintModule.updateButton();
    },

    // ── Rotation helper ────────────────────────────────────────
    _applyRotation(canvas, rotation) {
        const map = {
            CW90:          'rotate(90deg)',
            CCW90:         'rotate(-90deg)',
            Rotate180:     'rotate(180deg)',
            FlipHorizontal:'scaleX(-1)',
            FlipVertical:  'scaleY(-1)',
        };
        canvas.style.transform = (rotation && map[rotation]) ? map[rotation] : '';
    },

    // Called by DragReorderModule after reorder to sync main view DOM order
    _reorderMainView(order) {
        const container = document.getElementById('preview-main-canvas-container');
        const list = container?.querySelector('div');
        if (!list) return;
        order.forEach(pageNum => {
            const card = this._mainPageEls.get(pageNum);
            if (card) list.appendChild(card);
        });
    },

    // Called by ContextMenu and other modules after state changes
    onStateChanged() {
        this._syncAll();
        PrintModule.updateButton();
    },
};

// ═══════════════════════════════════════════════════════════════════
// ═══════════════════════════════════════════════════════════════════
// ToastModule — Upgraded sliding toast notifications (S)
// ═══════════════════════════════════════════════════════════════════
const ToastModule = {
    _MAX: 3,

    show(message, type = 'info', duration = 3000) {
        return this._create(message, type, duration);
    },

    showPersistent(message, type = 'info') {
        return this._create(message, type, null);
    },

    _create(message, type, duration) {
        const container = document.getElementById('toast-container');
        if (!container) return { dismiss() {} };
        const safeType = ['success', 'error', 'info'].includes(type) ? type : 'info';
        const isPersistent = duration === null;

        // Enforce max stack
        const existing = container.querySelectorAll('.toast-item:not(.dismissing)');
        if (existing.length >= this._MAX) {
            this._dismiss(existing[0]);
        }

        const icons = { success: '✅', error: '❌', info: 'ℹ️' };
        const icon  = icons[safeType] || 'ℹ️';

        const item = document.createElement('div');
        item.className = `toast-item toast-item-border-${safeType}`;
        item.innerHTML = `
            <div class="toast-item-body">
                <span class="toast-item-icon">${icon}</span>
                <span class="toast-item-msg">${escapeHtml(message)}</span>
            </div>
            <div class="toast-countdown toast-countdown-${safeType}${isPersistent ? ' toast-countdown-persistent' : ''}"
                 ${isPersistent ? '' : `style="animation-duration: ${duration}ms;"`}></div>
        `;

        item.addEventListener('click', () => this._dismiss(item));
        container.appendChild(item);

        // Screen reader announce
        const sr = document.getElementById('sr-status');
        if (sr) { sr.textContent = message; setTimeout(() => { sr.textContent = ''; }, 1000); }

        const timerId = isPersistent ? null : setTimeout(() => this._dismiss(item), duration);
        return {
            dismiss: () => {
                if (timerId !== null) clearTimeout(timerId);
                this._dismiss(item);
            }
        };
    },

    _dismiss(item) {
        if (!item || item.classList.contains('dismissing')) return;
        item.classList.add('dismissing');
        item.addEventListener('animationend', () => item.remove(), { once: true });
    },
};

const showToast = (msg, type = 'info') => ToastModule.show(msg, type);

// ═══════════════════════════════════════════════════════════════════
// ThemeModule — always light (green theme)
// ═══════════════════════════════════════════════════════════════════
const ThemeModule = {
    init() {
        // Force light theme — no dark mode
        document.documentElement.setAttribute('data-theme', 'light');
        localStorage.setItem('theme', 'light');
    },
    _apply() {},
    _updateButtons() {},
};

// ═══════════════════════════════════════════════════════════════════
// PrinterModule — Load printer list from API
// ═══════════════════════════════════════════════════════════════════
const PrinterModule = {
    _printers: [],   // cached printer list for polling

    async init() {
        try {
            const response = await fetch(`${API_BASE}/printers`);
            const printers = await response.json();
            this._printers = printers;

            this._renderPrinters(printers, true);

            // Change listener for printer select dropdown
            document.getElementById('printer-select')?.addEventListener('change', e => {
                const name = e.target.value;
                if (!name) { AppState.selectedPrinter = null; }
                else {
                    const p = this._printers.find(pr => pr.name === name);
                    AppState.selectedPrinter = p || { name };
                }
                PrintModule.updateButton();
                StepIndicatorModule.update();
                if (AppState.selectedPrinter) {
                    SRModule.announce(I18nModule.t('sr.printerSelected')(AppState.selectedPrinter.name));
                }
            });

            this.startPolling();
        } catch (err) {
            showToast(I18nModule.t('toast.printerLoadError')(err.message), 'error');
        }
    },

    // forceDefault=true only on initial load; polls pass false to preserve user's choice
    _renderPrinters(printers, forceDefault = false) {
        const sel = document.getElementById('printer-select');
        if (!sel) return;
        sel.innerHTML = `<option value="">${I18nModule.t('header.printer.placeholder')}</option>`;
        printers.forEach(p => {
            const opt = document.createElement('option');
            opt.value       = p.name;
            opt.textContent = p.name + (p.isDuplex ? ' ✦' : '');
            sel.appendChild(opt);
        });
        // Auto-select default printer only on initial load
        const def = printers.find(p => p.isDefault) || printers[0];
        if (def && forceDefault) {
            sel.value = def.name;
            AppState.selectedPrinter = def;
            PrintModule.updateButton();
            StepIndicatorModule.update();
            sel.dispatchEvent(new Event('change')); // Fix: notify PrinterSettingsModule of auto-selection
        }
    },

    startPolling() {
        setInterval(async () => {
            try {
                const res = await fetch(`${API_BASE}/printers`);
                if (!res.ok) return;
                this._printers = await res.json();
                // Re-render dropdown options, preserving user's current printer selection
                const prevName = AppState.selectedPrinter?.name || null;
                this._renderPrinters(this._printers, false);
                if (prevName) {
                    const match = this._printers.find(p => p.name === prevName);
                    if (match) {
                        // Printer still exists — restore DOM and state
                        const sel = document.getElementById('printer-select');
                        if (sel) sel.value = match.name;
                        AppState.selectedPrinter = match;
                    } else {
                        // Previously selected printer disappeared from poll response.
                        // B21-FE-5 fix: do NOT clear selectedPrinter while a print job is active —
                        // a transient poll miss during printing would break subsequent iterations.
                        if (AppState.currentJob || AppState.pendingPrintQueue) {
                            console.warn('[PrinterModule] Selected printer missing in poll but print job active — keeping selection.');
                        } else {
                            // No active job — safe to clear
                            AppState.selectedPrinter = null;
                            const sel = document.getElementById('printer-select');
                            if (sel) sel.value = '';
                            PrintModule.updateButton();
                            StepIndicatorModule.update();
                            showToast(I18nModule.t('toast.printerLost'), 'warning');
                        }
                    }
                }
            } catch { /* silently ignore poll failures */ }
        }, 30_000);
    },
};

// ═══════════════════════════════════════════════════════════════════
// UploadModule — Drag-drop / click file upload
// ═══════════════════════════════════════════════════════════════════
const UploadModule = {
    init() {
        const area  = document.getElementById('upload-area');
        const input = document.getElementById('file-input');

        // upload-area may not exist in the new app-shell layout
        if (area) {
            area.addEventListener('click', () => input.click());
            area.addEventListener('dragover', e => { e.preventDefault(); area.classList.add('drag-over'); });
            area.addEventListener('dragleave', () => area.classList.remove('drag-over'));
            area.addEventListener('drop', async e => {
                e.preventDefault();
                area.classList.remove('drag-over');
                const files = Array.from(e.dataTransfer.files).filter(f =>
                    ['.doc', '.docx', '.pdf', '.jpg', '.jpeg', '.png', '.tif', '.tiff', '.bmp', '.webp'].includes('.' + f.name.split('.').pop().toLowerCase())
                );
                for (const f of files) await this._upload(f);
            });
        }
        input?.addEventListener('change', async e => {
            const files = Array.from(e.target.files || []).filter(f =>
                ['.doc', '.docx', '.pdf', '.jpg', '.jpeg', '.png', '.tif', '.tiff', '.bmp', '.webp'].includes('.' + f.name.split('.').pop().toLowerCase())
            );
            for (const f of files) await this._upload(f);
            input.value = '';
        });
    },

    async _upload(file) {
        const ext = '.' + file.name.split('.').pop().toLowerCase();
        if (!['.doc', '.docx', '.pdf', '.jpg', '.jpeg', '.png', '.tif', '.tiff', '.bmp', '.webp'].includes(ext)) {
            showToast(I18nModule.t('toast.fileTypeUnsupported')(file.name), 'error');
            return;
        }

        // BUG-U3 fix: reject oversized files immediately before attempting upload
        if (file.size > 100 * 1024 * 1024) {
            showToast(I18nModule.t('toast.fileTooLarge')(file.name), 'error');
            return;
        }

        const uploadCard = document.getElementById('upload-area')?.closest('.card');
        if (uploadCard) clearCardError(uploadCard);

        let convertingToast = null;
        const wrap = document.getElementById('upload-progress-wrap');
        const bar  = document.getElementById('upload-progress-bar');

        try {
            const formData = new FormData();
            formData.append('file', file);

            // Show progress bar
            if (wrap) wrap.classList.remove('hidden');
            if (bar) { bar.classList.add('uploading'); bar.style.width = '0%'; }

            const result = await new Promise((resolve, reject) => {
                const xhr = new XMLHttpRequest();
                xhr.open('POST', `${API_BASE}/upload`);
                xhr.upload.onprogress = (e) => {
                    if (e.lengthComputable && bar) {
                        bar.style.width = Math.round((e.loaded / e.total) * 100) + '%';
                    }
                };
                xhr.onload = () => {
                    if (xhr.status >= 200 && xhr.status < 300) {
                        resolve(JSON.parse(xhr.responseText));
                    } else {
                        let message = `Upload failed: ${xhr.status}`;
                        try {
                            const parsed = JSON.parse(xhr.responseText);
                            message = parsed.message || parsed.detail || message;
                        } catch (_) {}
                        reject(new Error(message));
                    }
                };
                xhr.onerror = () => reject(new Error('Network error during upload'));
                xhr.send(formData);
            });

            if (!result.success) { showToast(I18nModule.t('toast.uploadError')(result.message), 'error'); return; }

            // Create and register file entry
            const entry = AppState.createFileEntry(result.fileId, result.originalFileName, ext !== '.pdf');
            AppState.addFile(entry);

            // Convert if needed
            if (entry.needsConversion) {
                convertingToast = ToastModule.showPersistent(I18nModule.t('toast.converting')(entry.name));
                // BUG-U1 fix: check convert response — failure must remove the orphaned entry
                const convertRes = await fetch(`${API_BASE}/convert?fileId=${entry.id}`, { method: 'POST' });
                if (!convertRes.ok) {
                    convertingToast?.dismiss();
                    convertingToast = null;
                    const idx = AppState.files.indexOf(entry);
                    if (idx !== -1) AppState.removeFile(idx);
                    TabsModule.render();
                    showToast(I18nModule.t('toast.convertError')(entry.name), 'error');
                    return;
                }
            }

            // Update file info display (legacy UI elements — may not exist in app-shell)
            const fnEl = document.getElementById('file-name');
            const fsEl = document.getElementById('file-status');
            if (fnEl) fnEl.textContent = entry.name;
            if (fsEl) fsEl.textContent = I18nModule.t('file.ready');

            // Load PDF for this file entry
            await PreviewModule.renderEntry(entry);
            if (!entry.pdfDoc) {
                throw new Error(I18nModule.t('toast.previewNotReady'));
            }

            // Render thumb strip + preview panel for new file
            ThumbStripModule.render();
            if (typeof PreviewPanelModule !== 'undefined') {
                PreviewPanelModule.render(AppState.activeFile);
            }
            convertingToast?.dismiss();
            convertingToast = null;

            document.getElementById('page-range-section')?.classList.remove('hidden');
            PrintModule.updateButton();
            TabsModule.render();
            // Sync copies widget + modebar to newly active (uploaded) file
            CopiesModule.sync();
            const modeBarUpload = document.getElementById('sheet-view-modebar');
            if (modeBarUpload) {
                const lsMode = AppState.activeFile?.landscapeMode ?? 'together';
                modeBarUpload.querySelectorAll('.sheet-modebar-btn').forEach(b => {
                    b.classList.toggle('active', b.dataset.lsmode === lsMode);
                });
            }
            showToast(I18nModule.t('toast.uploadSuccess')(entry.name, entry.totalPageCount));
            StepIndicatorModule.update();
            SRModule.announce(I18nModule.t('sr.fileLoaded')(entry.name, entry.totalPageCount));
        } catch (err) {
            convertingToast?.dismiss();
            const card = document.getElementById('upload-area')?.closest('.card');
            showCardError(card, `Lỗi khi tải file: ${err.message}`, () => document.getElementById('file-input')?.click());
            showToast(I18nModule.t('toast.fileLoadError')(err.message), 'error');
        } finally {
            if (wrap) wrap.classList.add('hidden');
            if (bar) { bar.classList.remove('uploading'); bar.style.width = '0%'; }
        }
    },

    removeFile(index) {
        // Capture id BEFORE AppState.removeFile() splices the array
        const removedId = AppState.files[index]?.id;
        if (removedId) {
            PreviewPanelModule.removeFileRoot(removedId);
            ThumbStripModule.removeFileRoot(removedId);
        }

        AppState.removeFile(index);
        if (AppState.files.length === 0) {
            const fi = document.getElementById('file-input');
            if (fi) fi.value = '';
            document.getElementById('upload-area')?.classList.remove('hidden');
            document.getElementById('file-info')?.classList.add('hidden');
            document.getElementById('page-range-section')?.classList.add('hidden');
            const pi = document.getElementById('page-range-input');
            if (pi) pi.value = '';
            // Clear thumb strip and preview panel
            ThumbStripModule.render();
            if (typeof PreviewPanelModule !== 'undefined') {
                PreviewPanelModule.clear();
            }
            CopiesModule.reset();   // reset widget to defaults — no files remain
            TabsModule.render();    // clear stale tab DOM (ghost tab fix)
            TabsModule._dragSourceIdx = null; // clear stale drag state
        } else {
            const active = AppState.activeFile;
            if (active) {
                const fnEl = document.getElementById('file-name');
                const fsEl = document.getElementById('file-status');
                if (fnEl) fnEl.textContent = active.name;
                if (fsEl) fsEl.textContent = I18nModule.t('file.ready');            }
            ThumbStripModule.render();
            if (typeof PreviewPanelModule !== 'undefined') {
                PreviewPanelModule.render(AppState.activeFile);
            }
            // Sync copies widget + modebar + page-range to new active file
            CopiesModule.sync();
            // B14-FE-3: reset typing flag so updateDisplay correctly overwrites the
            // stale range text from the removed file instead of skipping the update.
            AppState.isUserTypingPageRange = false;
            if (typeof PageSelectModule !== 'undefined') PageSelectModule.updateDisplay();
            TabsModule.render(); // rebuild tabs to remove the closed file's tab
            const modeBarRemove = document.getElementById('sheet-view-modebar');
            if (modeBarRemove) {
                const lsMode = AppState.activeFile?.landscapeMode ?? 'together';
                modeBarRemove.querySelectorAll('.sheet-modebar-btn').forEach(b => {
                    b.classList.toggle('active', b.dataset.lsmode === lsMode);
                });
            }
        }
        PrintModule.updateButton();
        StepIndicatorModule.update();
    },

    _remove() {
        // Legacy single-file remove — removes all files (called from remove-file button)
        AppState.reset();
        const fi = document.getElementById('file-input');
        if (fi) fi.value = '';
        document.getElementById('upload-area')?.classList.remove('hidden');
        document.getElementById('file-info')?.classList.add('hidden');
        document.getElementById('page-range-section')?.classList.add('hidden');
        const pi = document.getElementById('page-range-input');
        if (pi) pi.value = '';
        ThumbStripModule.render();
        if (typeof PreviewPanelModule !== 'undefined') {
            PreviewPanelModule.clear();
        }
        CopiesModule.reset();   // clear widget — no files remain
        TabsModule._dragSourceIdx = null; // clear stale drag state
        PrintModule.updateButton();
        StepIndicatorModule.update();
    },
};

// ═══════════════════════════════════════════════════════════════════
// TabsModule — File tab strip for multi-file support
// ═══════════════════════════════════════════════════════════════════
const TabsModule = {
    _dragSourceIdx: null,   // index of tab being dragged (null = no drag)

    init() {
        const addBtn = document.getElementById('file-tab-add-btn');
        if (addBtn) {
            addBtn.addEventListener('click', () => {
                document.getElementById('file-input')?.click();
            });
        }
    },

    render() {
        const tabList = document.getElementById('file-tab-list');
        const tabBar  = document.getElementById('file-tabs');
        if (!tabList || !tabBar) return;

        // Never hide the entire tab bar — the "+ Thêm file" button must always be visible.
        // Just clear the tab list when there are 0 files (no tabs needed yet).
        tabBar.classList.remove('hidden');
        tabList.innerHTML = '';

        if (AppState.files.length === 0) return;
        AppState.files.forEach((file, idx) => {
            const tab = document.createElement('div');
            tab.className = 'file-tab' + (idx === AppState.activeFileIndex ? ' active' : '');
            tab.title = file.name;
            tab.draggable = true;

            // ── Landscape badge ──
            const isAllLandscape = (
                file._originalOrientationMap != null &&
                file.totalPageCount > 0 &&
                (() => {
                    for (let p = 1; p <= file.totalPageCount; p++) {
                        if (file._originalOrientationMap.get(p) !== true) return false;
                    }
                    return true;
                })()
            );

            const name = document.createElement('span');
            name.className   = 'file-tab-name';
            name.textContent = file.name.length > 20 ? file.name.slice(0, 18) + '…' : file.name;

            if (isAllLandscape) {
                const badge = document.createElement('span');
                badge.className = 'file-tab-landscape-badge';
                badge.textContent = '🌄';
                badge.title = I18nModule.t('tab.landscapeBadge');
                tab.appendChild(badge);
            }
            tab.appendChild(name);

            const closeBtn = document.createElement('button');
            closeBtn.className   = 'file-tab-close';
            closeBtn.textContent = '×';
            closeBtn.title       = I18nModule.t('tab.close');
            closeBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                UploadModule.removeFile(idx); // self-contained: renders tabs internally
            });
            tab.appendChild(closeBtn);

            tab.addEventListener('click', () => {
                this.setActive(idx);
            });

            // ── Drag-to-reorder handlers ──
            tab.addEventListener('dragstart', (e) => {
                this._dragSourceIdx = idx;
                e.dataTransfer.effectAllowed = 'move';
            });
            tab.addEventListener('dragover', (e) => {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                tab.classList.add('drag-over');
            });
            tab.addEventListener('dragleave', () => {
                tab.classList.remove('drag-over');
            });
            tab.addEventListener('drop', (e) => {
                e.preventDefault();
                tab.classList.remove('drag-over');
                const from = this._dragSourceIdx;
                this._dragSourceIdx = null;
                if (from !== null && from !== idx) {
                    this._reorderFiles(from, idx);
                }
            });
            tab.addEventListener('dragend', () => {
                this._dragSourceIdx = null;
                document.querySelectorAll('.file-tab').forEach(t => t.classList.remove('drag-over'));
            });

            tabList.appendChild(tab);
        });
    },

    _reorderFiles(fromIdx, toIdx) {
        const files = AppState.files;
        // Bounds guard: indices must be valid after any async/removal that could shift them
        if (fromIdx < 0 || fromIdx >= files.length || toIdx < 0 || toIdx >= files.length) return;
        const activeFile = AppState.activeFile;  // capture BEFORE splice — index still valid
        const [moved] = files.splice(fromIdx, 1);
        files.splice(toIdx, 0, moved);

        // Keep active file pointing to the same file object
        AppState.activeFileIndex = files.indexOf(activeFile);

        // Sync copies widget + modebar to (possibly moved) active file
        CopiesModule.sync();
        const modeBar = document.getElementById('sheet-view-modebar');
        if (modeBar) {
            const lsMode = AppState.activeFile?.landscapeMode ?? 'together';
            modeBar.querySelectorAll('.sheet-modebar-btn').forEach(b => {
                b.classList.toggle('active', b.dataset.lsmode === lsMode);
            });
        }

        this.render();
        ThumbStripModule.render();
        PreviewPanelModule.render(AppState.activeFile);
    },

    // Switch to file at given index — updates tabs, thumbs, and preview panel
    setActive(idx) {
        if (idx < 0 || idx >= AppState.files.length) return;
        const outgoingFile = AppState.activeFile;
        if (outgoingFile?._scrollPos && ThumbStripModule._container) {
            outgoingFile._scrollPos.thumb = ThumbStripModule._container.scrollTop;
        }
        AppState.activeFileIndex = idx;

        // Update file-info display (legacy elements — may not exist)
        const active = AppState.activeFile;
        if (active) {
            const fnEl = document.getElementById('file-name');
            const fsEl = document.getElementById('file-status');
            if (fnEl) fnEl.textContent = active.name;
            if (fsEl) fsEl.textContent = I18nModule.t('file.ready');
        }

        // Sync modebar to new active file's landscapeMode (§9)
        const modeBar = document.getElementById('sheet-view-modebar');
        if (modeBar) {
            const lsMode = AppState.activeFile?.landscapeMode ?? 'together';
            modeBar.querySelectorAll('.sheet-modebar-btn').forEach(b => {
                b.classList.toggle('active', b.dataset.lsmode === lsMode);
            });
        }

        // Sync copies widget (§10)
        CopiesModule.sync();
        // Sync page-range input to new file's selection (prevents stale text causing accidental re-selection)
        if (typeof PageSelectModule !== 'undefined') PageSelectModule.updateDisplay();

        this.render();
        ThumbStripModule.render();
        if (typeof PreviewPanelModule !== 'undefined') {
            PreviewPanelModule.render(AppState.activeFile);
        }
        PrintModule.updateButton();

        // Instant scroll sideview to page 1 of the newly active file
        const _targetFile = AppState.files[idx];
        const _targetFileId = _targetFile?.id;
        requestAnimationFrame(() => {
            if (AppState.activeFile?.id !== _targetFileId) return;
            const _liveIdx = AppState.files.findIndex(f => f.id === _targetFileId);
            if (_liveIdx < 0) return;
            const _activeRoot = ThumbStripModule._fileRoots?.get(_targetFileId) ?? ThumbStripModule._container;
            const savedThumb = _targetFile?._scrollPos?.thumb ?? 0;
            if (savedThumb > 0 && ThumbStripModule._container) {
                ThumbStripModule._setActiveHighlight(_liveIdx, 1, false);
                ThumbStripModule._container.scrollTop = savedThumb;
            } else {
                const firstThumb = _activeRoot?.querySelector(`.thumb-item[data-file-index="${_liveIdx}"][data-page="1"]`);
                if (firstThumb) firstThumb.scrollIntoView({ behavior: 'auto', block: 'start' });
                ThumbStripModule._setActiveHighlight(_liveIdx, 1);
            }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// PreviewModule — PDF thumbnail grid with lazy IntersectionObserver
// ═══════════════════════════════════════════════════════════════════
const PreviewModule = {
    _observer: null,

    async render(fileId) {
        try {
            const url      = `${API_BASE}/file/${fileId}`;
            const loadTask = pdfjsLib.getDocument({
                url,
                rangeChunkSize:           65536,  // 64 KB chunks
                disableStream:            false,  // Enable streaming
                isOffscreenCanvasSupported: true, // Render off main thread
                useWasm:                  true,   // WASM decoders for JBIG2/JPEG2000
            });
            AppState.currentPdfDoc  = await loadTask.promise;
            AppState.totalPageCount = AppState.currentPdfDoc.numPages;
            AppState.pageOrder = Array.from({ length: AppState.totalPageCount }, (_, i) => i + 1);
            AppState.selectAllPages();
            HoverPreviewModule.clearCache();

            PageSelectModule.updateDisplay();

            showToast(I18nModule.t('toast.pdfLoaded')(AppState.totalPageCount));
        } catch (err) {
            console.error('Error loading PDF:', err);
            showToast(I18nModule.t('toast.fileLoadError')(err.message), 'error');
        }
    },

    async renderEntry(entry) {
        try {
            const url      = `${API_BASE}/file/${entry.id}`;
            const loadTask = pdfjsLib.getDocument({
                url,
                rangeChunkSize:           65536,  // 64 KB chunks
                disableStream:            false,  // Enable streaming
                isOffscreenCanvasSupported: true, // Render off main thread
                useWasm:                  true,   // WASM decoders for JBIG2/JPEG2000
            });
            entry.pdfDoc           = await loadTask.promise;
            // Warm page 1 in background — pre-populates PDF.js internal page cache
            // Use setTimeout (not queueMicrotask) to yield to active file's own render first
            setTimeout(async () => {
                try {
                    const page = await entry.pdfDoc?.getPage(1);
                    if (page) page.cleanup();
                } catch (_) {}
            }, 100);
            entry.totalPageCount   = entry.pdfDoc.numPages;
            entry.pageOrder        = Array.from({ length: entry.totalPageCount }, (_, i) => i + 1);
            entry.selectedPages    = new Set(entry.pageOrder);

            // If this is the active file, sync legacy state and update UI
            if (AppState.activeFile === entry) {
                HoverPreviewModule.clearCache();
                PageSelectModule.updateDisplay();
            }
        } catch (err) {
            console.error('Error loading PDF entry:', err);
            showToast(I18nModule.t('toast.fileLoadError')(err.message), 'error');
        }
    },

    _createPlaceholder(pageNum) {
        const div = document.createElement('div');
        div.className = 'page-thumbnail selected';
        div.dataset.pageNumber = pageNum;
        div.style.cssText = 'border-color:#22c55e;';
        div.setAttribute('tabindex', '0');
        div.setAttribute('role', 'option');
        div.setAttribute('aria-label', `Trang ${pageNum}`);
        div.setAttribute('aria-selected', 'true');

        const label = document.createElement('div');
        label.style.cssText = 'position:absolute;bottom:4px;right:4px;background:rgba(0,0,0,0.8);color:white;padding:3px 6px;border-radius:4px;font-size:11px;font-weight:600;z-index:2;';
        label.textContent = pageNum;
        div.appendChild(label);

        let _clickTimer = null;
        div.addEventListener('click', e => {
            if (e.button !== 0) return;
            clearTimeout(_clickTimer);
            _clickTimer = setTimeout(() => PageSelectModule.toggle(pageNum), 220);
        });
        div.addEventListener('dblclick', () => {
            clearTimeout(_clickTimer); // cancel the single-click toggle
            ZoomModal.open(pageNum);
        });
        div.addEventListener('contextmenu', e => { e.preventDefault(); ContextMenu.show(e, pageNum); });
        HoverPreviewModule.attach(div, pageNum);
        return div;
    },

    async _renderCanvas(thumb, pageNum) {
        try {
            const page     = await AppState.currentPdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale: 1.5 });
            const canvas   = document.createElement('canvas');
            const ctx      = canvas.getContext('2d');
            canvas.width  = viewport.width;
            canvas.height = viewport.height;
            canvas.style.cssText = 'width:100%;height:auto;display:block;border-radius:6px;';
            await page.render({ canvasContext: ctx, viewport }).promise;
            thumb.insertBefore(canvas, thumb.firstChild);
            thumb.dataset.rendered = '1';

            // Add orientation badge (1)
            const isLandscape = viewport.width > viewport.height;
            const badge = document.createElement('div');
            badge.className = `orientation-badge${isLandscape ? ' landscape' : ''}`;
            badge.textContent = isLandscape ? I18nModule.t('orientation.landscape') : I18nModule.t('orientation.portrait');
            thumb.appendChild(badge);
        } catch (err) {
            console.error(`Error rendering page ${pageNum}:`, err);
        }
    },

    updateThumbnails() {
        document.querySelectorAll('.page-thumbnail').forEach(thumb => {
            const n      = parseInt(thumb.dataset.pageNumber);
            const sel    = AppState.selectedPages.has(n);
            const single = AppState.singleSidedPages.has(n);
            thumb.classList.toggle('selected', sel);
            thumb.style.borderColor = sel ? (single ? '#3b82f6' : '#22c55e') : 'rgba(148,163,184,0.2)';
            thumb.title = `Trang ${n} - In ${single ? '1' : '2'} mat`;
            thumb.setAttribute('aria-selected', sel ? 'true' : 'false');
            // Restore rotation attribute (U)
            const rot = AppState.pageRotations.get(n);
            if (rot) { thumb.dataset.rotation = rot; } else { delete thumb.dataset.rotation; }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// PageSelectModule — Page range input + selection management
// ═══════════════════════════════════════════════════════════════════
const PageSelectModule = {
    init() {
        const input = document.getElementById('page-range-input');
        if (!input) return;

        let _rangeDebounce = null;

        input.addEventListener('focus', () => { AppState.isUserTypingPageRange = true; });
        input.addEventListener('blur',  () => { AppState.isUserTypingPageRange = false; this.updateDisplay(); });
        input.addEventListener('input', e => {
            AppState.isUserTypingPageRange = true;
            // B13-FE-2/3: capture activeFile at schedule time so a file-switch during
            // the 200ms debounce cannot apply File A's typed range to File B.
            const targetFile = AppState.activeFile;
            clearTimeout(_rangeDebounce);
            _rangeDebounce = setTimeout(() => {
                if (!targetFile) return; // file removed during debounce window — bail
                const text = e.target.value.trim();
                if (!text) {
                    // Re-check activeFile is still the same before clearing
                    if (AppState.activeFile === targetFile) {
                        AppState.selectAllPages();
                    } else {
                        targetFile.selectedPages = new Set();
                        for (let i = 1; i <= targetFile.totalPageCount; i++) targetFile.selectedPages.add(i);
                    }
                    input.style.borderColor = '';
                } else {
                    const parsed = this._parseRange(text, targetFile.totalPageCount);
                    if (parsed.size === 0 && text.length > 0) {
                        // Invalid range — show red border, don't change selection
                        input.style.borderColor = 'rgba(239, 68, 68, 0.6)';
                    } else {
                        // R8 cleanup: remove SS status for pages no longer selected
                        const toUnset = [...targetFile.singleSidedPages].filter(p => !parsed.has(p));
                        if (toUnset.length > 0) unsetSingleSided(targetFile, toUnset);
                        targetFile.selectedPages = parsed;
                        input.style.borderColor = '';
                    }
                }
                PreviewModule.updateThumbnails();
                this._updateTexts();
                PrintModule.updateButton();
                StepIndicatorModule.update();
                // Rebuild SheetView immediately if active (page selection changes sheet grouping)
                if (AppState.viewMode === 'sheet' && AppState.activeFile) {
                    PreviewPanelModule.render(AppState.activeFile);
                } else {
                    PreviewPanelModule.onStateChanged();
                }
            }, 200);
        });
    },

    toggle(pageNum) {
        if (AppState.activeFile) togglePageSelection(AppState.activeFile, pageNum);

        // Pop animation (O)
        const thumb = document.querySelector(`.page-thumbnail[data-page-number="${pageNum}"]`);
        if (thumb) {
            thumb.classList.remove('pop');
            void thumb.offsetWidth; // force reflow to re-trigger animation
            thumb.classList.add('pop');
            thumb.addEventListener('animationend', () => thumb.classList.remove('pop'), { once: true });
        }

        PreviewModule.updateThumbnails();
        this.updateDisplay();
        PrintModule.updateButton();
        // Rebuild SheetView immediately if active (toggle changes which pages appear on sheets)
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            PreviewPanelModule.render(AppState.activeFile);
        } else {
            PreviewPanelModule.onStateChanged();
        }
    },

    updateDisplay() {
        this._updateTexts();
        if (!AppState.isUserTypingPageRange) {
            const input = document.getElementById('page-range-input');
            if (!input) return;
            const all = AppState.selectedPages.size === AppState.totalPageCount;
            input.value = all ? '' : this._formatRange(Array.from(AppState.selectedPages).sort((a,b) => a-b));
        }
    },

    _updateTexts() {
        const all  = AppState.selectedPages.size === AppState.totalPageCount;
        const text = all ? 'Tat ca' : `${AppState.selectedPages.size} trang`;
        const el1  = document.getElementById('sidebar-selected-pages');
        const el2  = document.getElementById('inline-selected');
        if (el1) el1.textContent = text;
        if (el2) el2.textContent = all ? 'Da chon: Tat ca' : `Da chon: ${text}`;
        SRModule.announce(all ? I18nModule.t('sr.allSelected') : I18nModule.t('sr.selected')(AppState.selectedPages.size));
    },

    _parseRange(text, maxPage = AppState.totalPageCount) {
        const pages = new Set();
        text.split(',').forEach(part => {
            part = part.trim();
            if (part.includes('-')) {
                // BUG-3 fix: reject segments with more than one dash (e.g. "1-2-3")
                // to prevent silent data loss from [a,b] destructuring discarding extra elements.
                const segments = part.split('-').map(s => parseInt(s.trim()));
                if (segments.length !== 2) return; // malformed range — skip silently
                const [a, b] = segments;
                if (!isNaN(a) && !isNaN(b))
                    for (let i = Math.min(a,b); i <= Math.max(a,b); i++)
                        if (i >= 1 && i <= maxPage) pages.add(i);
            } else {
                const n = parseInt(part);
                if (!isNaN(n) && n >= 1 && n <= maxPage) pages.add(n);
            }
        });
        return pages;
    },

    _formatRange(pages) {
        if (!pages.length) return '';
        const ranges = [];
        let start = pages[0], end = pages[0];
        for (let i = 1; i <= pages.length; i++) {
            if (i < pages.length && pages[i] === end + 1) { end = pages[i]; }
            else { ranges.push(start === end ? `${start}` : `${start}-${end}`); if (i < pages.length) { start = end = pages[i]; } }
        }
        return ranges.join(',');
    },
};

// ═══════════════════════════════════════════════════════════════════
// ZoomModal — Full-page preview modal, click to toggle selection
// ═══════════════════════════════════════════════════════════════════
const ZoomModal = {
    init() {
        document.getElementById('zoom-modal-close')?.addEventListener('click', () => this.close());
        document.getElementById('zoom-modal-overlay')?.addEventListener('click', () => this.close());

        document.getElementById('all-double-btn')?.addEventListener('click', () => {
            AppState.selectAllPages(); AppState.singleSidedPages.clear();
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay(); this._updateModalStyles();
            ThumbStripModule._syncSelectionHighlights?.();
            if (AppState.viewMode === 'sheet' && AppState.activeFile) PreviewPanelModule.render(AppState.activeFile);
            else PreviewPanelModule.onStateChanged();
            showToast(I18nModule.t('toast.allDouble'));
        });

        document.getElementById('all-single-btn')?.addEventListener('click', () => {
            AppState.selectAllPages(); AppState.singleSidedPages = new Set(AppState.selectedPages);
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay(); this._updateModalStyles();
            ThumbStripModule._syncSelectionHighlights?.();
            if (AppState.viewMode === 'sheet' && AppState.activeFile) PreviewPanelModule.render(AppState.activeFile);
            else PreviewPanelModule.onStateChanged();
            showToast(I18nModule.t('toast.allSingle'));
        });

        // FIX: Deselect All truly empties selection
        document.getElementById('deselect-all-btn')?.addEventListener('click', () => {
            AppState.selectedPages.clear(); AppState.singleSidedPages.clear();
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay(); this._updateModalStyles();
            ThumbStripModule._syncSelectionHighlights?.();
            if (AppState.viewMode === 'sheet' && AppState.activeFile) PreviewPanelModule.render(AppState.activeFile);
            else PreviewPanelModule.onStateChanged();
            PrintModule.updateButton();
            showToast(I18nModule.t('toast.deselectAll'));
        });
    },

    open(pageNum) {
        const modal = document.getElementById('page-zoom-modal');
        if (!modal) return;
        modal.classList.remove('hidden');
        this._renderAllPages(pageNum);
    },

    close() { document.getElementById('page-zoom-modal')?.classList.add('hidden'); },

    async _renderAllPages(scrollToPage) {
        const container = document.querySelector('.zoom-canvas-container');
        if (!container) return;
        container.innerHTML = '<div class="loading">Dang tai...</div>';

        const list = document.createElement('div');
        list.style.cssText = 'width:100%;max-width:800px;margin:0 auto;';

        const results = (await Promise.all(
            Array.from({ length: AppState.totalPageCount }, (_, i) => i + 1).map(async n => {
                try {
                    const page = await AppState.currentPdfDoc.getPage(n);
                    return { index: n, element: this._buildPageContainer(page, n) };
                } catch { return null; }
            })
        )).filter(Boolean).sort((a, b) => a.index - b.index);

        results.forEach(r => list.appendChild(r.element));
        container.innerHTML = '';
        container.appendChild(list);

        document.getElementById('zoom-page-title').textContent = `Tat ca trang (${AppState.totalPageCount})`;
        document.getElementById('zoom-page-info').textContent  = 'Cuon de xem tat ca';
        ['zoom-prev','zoom-next'].forEach(id => { const el = document.getElementById(id); if (el) el.style.display = 'none'; });

        setTimeout(() => {
            const target = list.querySelector(`[data-page="${scrollToPage}"]`);
            if (target) {
                const cRect = container.getBoundingClientRect();
                const tRect = target.getBoundingClientRect();
                container.scrollTo({ top: tRect.top - cRect.top + container.scrollTop - 20, behavior: 'smooth' });
            }
        }, 100);
    },

    _buildPageContainer(page, n) {
        const isSel    = AppState.selectedPages.has(n);
        const isSingle = AppState.singleSidedPages.has(n);
        const border   = isSel ? (isSingle ? '#3b82f6' : '#22c55e') : 'transparent';

        const div = document.createElement('div');
        div.className = 'modal-page-container';
        div.dataset.page = n;
        div.style.cssText = `margin-bottom:1.5rem;position:relative;border:3px solid ${border};border-radius:8px;padding:1rem;background:rgba(30,41,59,0.5);cursor:pointer;`;

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;align-items:center;justify-content:space-between;margin-bottom:.75rem;';
        const title = document.createElement('h4');
        title.textContent = `Trang ${n}`;
        title.style.cssText = 'margin:0;font-size:1rem;';
        header.appendChild(title);

        const badge = document.createElement('div');
        badge.className   = isSingle ? 'single-sided-badge' : 'double-sided-badge';
        badge.textContent = isSingle ? I18nModule.t('zoom.singleSided') : I18nModule.t('zoom.doubleSided');
        if (!isSel) badge.style.opacity = '0.3';

        const canvas = document.createElement('canvas');
        const rotation = AppState.pageRotations.get(n) ?? null;
        const vp       = RotationHelper.viewport(page, 1.2, rotation);
        canvas.width   = vp.width; canvas.height = vp.height;
        canvas.style.cssText = 'width:100%;height:auto;display:block;border-radius:6px;';
        page.render({ canvasContext: canvas.getContext('2d'), viewport: vp });

        // CSS flip only (viewport handles CW/CCW/180)
        canvas.style.transform = RotationHelper.toCSS(rotation);
        const badgeRotation = AppState.activeFile?._togetherRotations?.has(n) ? null : rotation;
        RotationHelper.updateBadge(div, badgeRotation);

        div.appendChild(header); div.appendChild(badge); div.appendChild(canvas);

        div.addEventListener('contextmenu', function(e) { e.preventDefault(); ContextMenu.show(e, n); });
        return div;
    },

    _updateModalStyles() {
        const modal = document.getElementById('page-zoom-modal');
        if (!modal || modal.classList.contains('hidden')) return;
        // Full re-render to apply viewport-based rotation + selection colors
        const container = document.querySelector('.zoom-canvas-container');
        const scrollPos = container?.scrollTop ?? 0;
        this._renderAllPages().then(() => {
            if (container) container.scrollTop = scrollPos;
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// ContextMenu — 2-level right-click menu for page actions
// ═══════════════════════════════════════════════════════════════════
const ContextMenu = {
    _currentPage: null,
    _currentOpts: null,  // { isUserBlank: bool }
    _hideTimer: null,

    init() {
        const menu = document.getElementById('page-context-menu');
        if (!menu) return;

        // Close on outside click
        document.addEventListener('click', e => {
            if (!e.target.closest('#page-context-menu')) this.hide();
        });
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') this.hide();
        });

        // Wire all leaf action items (anywhere inside menu)
        menu.querySelectorAll('.context-menu-item[data-action]').forEach(item => {
            item.addEventListener('click', e => {
                e.stopPropagation();
                this._handleAction(item.dataset.action);
            });
        });

        // Submenu hover logic for parent items
        menu.querySelectorAll('.cm-has-sub').forEach(parent => {
            const subId = parent.dataset.submenu;
            const sub   = document.getElementById(subId);
            if (!sub) return;

            let leaveTimer = null;

            const openSub = () => {
                clearTimeout(leaveTimer);
                // Close all other submenus first
                menu.querySelectorAll('.context-submenu').forEach(s => {
                    if (s !== sub) s.classList.add('hidden');
                });
                sub.classList.remove('hidden');
                // Check overflow after browser has laid out the submenu
                requestAnimationFrame(() => {
                    const rect = sub.getBoundingClientRect();
                    if (rect.right > window.innerWidth - 8) {
                        sub.classList.add('flip-left');
                    } else {
                        sub.classList.remove('flip-left');
                    }
                });
            };
            const closeSub = () => {
                leaveTimer = setTimeout(() => sub.classList.add('hidden'), 200);
            };

            parent.addEventListener('mouseenter', () => { clearTimeout(leaveTimer); openSub(); });
            parent.addEventListener('mouseleave', () => closeSub());
            sub.addEventListener('mouseenter',   () => clearTimeout(leaveTimer));
            sub.addEventListener('mouseleave',   () => closeSub());
        });
    },

    show(event, pageNum, opts = {}) {
        this._currentPage = pageNum;
        this._currentOpts = opts;
        const menu = document.getElementById('page-context-menu');

        // Update header
        const header = document.getElementById('context-menu-header');
        if (header) {
            header.textContent = (pageNum === 0) ? I18nModule.t('ctx.blankPage') : I18nModule.t('ctx.page')(pageNum);
        }

        // Update single/double check marks
        const isSingle = AppState.singleSidedPages.has(pageNum);
        const checkDouble = document.getElementById('check-double');
        const checkSingle = document.getElementById('check-single');
        if (checkDouble) checkDouble.textContent = isSingle ? '' : '✓';
        if (checkSingle) checkSingle.textContent = isSingle ? '✓' : '';

        // Show/hide "Chèn" group — only visible in sheet view
        const insertItem = menu.querySelector('.cm-sheet-only');
        if (insertItem) {
            const inSheetView = AppState.viewMode === 'sheet';
            insertItem.style.display = inSheetView ? '' : 'none';
        }

        // Hide "Mặt in" group when right-clicking a user blank
        const sidesItem = menu.querySelector('[data-submenu="sub-sides"]');
        if (sidesItem) {
            sidesItem.style.display = (opts.isUserBlank) ? 'none' : '';
        }


        // Show/hide + toggle text for deselect-page / reselect-page
        const deselectPageItem = document.getElementById('cm-deselect-page');
        if (deselectPageItem) {
            if (pageNum > 0 && !opts.isUserBlank) {
                const isSelected = AppState.selectedPages.has(pageNum);
                deselectPageItem.dataset.action = isSelected ? 'deselect-page' : 'reselect-page';
                const span = deselectPageItem.querySelector('[data-i18n]');
                if (span) span.textContent = isSelected
                    ? I18nModule.t('ctx.deselectPage')
                    : I18nModule.t('ctx.reselectPage');
                deselectPageItem.style.display = '';
            } else {
                deselectPageItem.style.display = 'none';
            }
        }
        menu.querySelectorAll('.context-submenu').forEach(s => s.classList.add('hidden'));

        // Position
        menu.style.left = `${event.clientX}px`;
        menu.style.top  = `${event.clientY}px`;
        menu.classList.remove('hidden');
        event.preventDefault();

        // Adjust if overflowing viewport
        const rect = menu.getBoundingClientRect();
        if (rect.right  > window.innerWidth)  menu.style.left = `${window.innerWidth  - rect.width  - 8}px`;
        if (rect.bottom > window.innerHeight) menu.style.top  = `${window.innerHeight - rect.height - 8}px`;
    },

    hide() {
        const menu = document.getElementById('page-context-menu');
        if (!menu) return;
        menu.classList.add('hidden');
        menu.querySelectorAll('.context-submenu').forEach(s => s.classList.add('hidden'));
        this._currentPage = null;
        this._currentOpts = null;
    },

    _handleAction(action) {
        const n = this._currentPage;

        // Snapshot rotation BEFORE _applyRotation mutates pageRotations.
        // Used below to detect portrait↔landscape orientation change.
        const prevRotation = AppState.pageRotations.get(n) ?? null;

        switch (action) {
            case 'all-double-sided':
                AppState.selectAllPages(); AppState.singleSidedPages.clear();
                showToast(I18nModule.t('toast.allDouble')); break;
            case 'all-single-sided':
                AppState.selectAllPages(); AppState.singleSidedPages = new Set(AppState.selectedPages);
                showToast(I18nModule.t('toast.allSingle')); break;
            case 'deselect-all':
                AppState.selectedPages.clear(); AppState.singleSidedPages.clear();
                PrintModule.updateButton();
                showToast(I18nModule.t('toast.deselectAll')); break;
            case 'double-sided':
                if (n) { if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n); if (AppState.activeFile) unsetSingleSided(AppState.activeFile, [n]); showToast(I18nModule.t('toast.pageDuplex')(n)); } break;
            case 'single-sided':
                if (n) { if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n); if (AppState.activeFile) setSingleSided(AppState.activeFile, n); showToast(I18nModule.t('toast.pageSimplex')(n)); } break;
            case 'rotate-cw90':     this._applyRotation(n, 'CW90'); break;
            case 'rotate-ccw90':    this._applyRotation(n, 'CCW90'); break;
            case 'rotate-fliph':    this._applyRotation(n, 'FlipHorizontal'); break;
            case 'rotate-flipv':    this._applyRotation(n, 'FlipVertical'); break;
            case 'rotate-180':      this._applyRotation(n, 'Rotate180'); break;
            case 'rotate-reset':    this._applyRotation(n, null); break;
            case 'insert-blank-before': this._insertBlank(n, 'before'); return; // return to skip re-render below
            case 'insert-blank-after':  this._insertBlank(n, 'after');  return;
            case 'insert-image-before': this._pickImage('before'); return;
            case 'insert-image-after':  this._pickImage('after');  return;
            case 'deselect-page':
                if (n && AppState.activeFile) {
                    togglePageSelection(AppState.activeFile, n);
                    PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay();
                    ZoomModal._updateModalStyles(); this.hide();
                    PrintPreviewModule.onStateChanged();
                    ThumbStripModule._syncSelectionHighlights?.();
                    if (AppState.viewMode === 'sheet' && AppState.activeFile) {
                        PreviewPanelModule.render(AppState.activeFile);
                    } else {
                        PreviewPanelModule.onStateChanged();
                    }
                    PrintModule.updateButton();
                    this._scrollAndBlinkEjected(n);
                }
                return;
            case 'reselect-page':
                if (n && AppState.activeFile) togglePageSelection(AppState.activeFile, n);
                break;
        }
        PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay();
        ZoomModal._updateModalStyles(); this.hide();
        PrintPreviewModule.onStateChanged();
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            // Selection/side actions (double-sided, single-sided, deselect-all, etc.) change which
            // pages appear on each sheet → must fully rebuild the sheet layout immediately.
            // Rotation actions use smart branch: only CW90/CCW90 transpose width↔height.
            const isRotationAction = ['rotate-cw90','rotate-ccw90','rotate-fliph','rotate-flipv','rotate-180','rotate-reset'].includes(action);
            if (isRotationAction) {
                // Smart branch: only CW90/CCW90 transpose width↔height (per PDF.js PageViewport).
                // If orientation flips portrait↔landscape, the sheet pairing layout must rebuild.
                // Otherwise (Rotate180, FlipH, FlipV, or same-axis 90° → 90°), patch image only (~15ms).
                const newRotation  = AppState.pageRotations.get(n) ?? null;
                const prevSwaps90  = prevRotation === 'CW90' || prevRotation === 'CCW90';
                const newSwaps90   = newRotation  === 'CW90' || newRotation  === 'CCW90';
                const orientationChanged = prevSwaps90 !== newSwaps90;
                if (orientationChanged) {
                    PreviewPanelModule.render(AppState.activeFile);
                } else {
                    PreviewPanelModule._patchRotatedPage(AppState.activeFile.id, n);
                }
            } else {
                // Selection/side change → sheet grouping may change → full rebuild
                PreviewPanelModule.render(AppState.activeFile);
            }
        } else {
            PreviewPanelModule.onStateChanged();
        }
        ThumbStripModule._syncSelectionHighlights?.();
    },

    // Insert a blank (pageNum=0) before or after the target page in pageOrder
    _insertBlank(targetPage, position) {
        const file = AppState.activeFile;
        if (!file) return;

        // Materialise pageOrder if currently empty (default order)
        if (!file.pageOrder.length) {
            file.pageOrder = Array.from({ length: file.totalPageCount }, (_, i) => i + 1);
        }

        const idx = file.pageOrder.indexOf(targetPage);
        if (idx === -1) {
            // targetPage=0 is itself a blank; append at end
            file.pageOrder.push(0);
        } else {
            const insertAt = position === 'before' ? idx : idx + 1;
            file.pageOrder.splice(insertAt, 0, 0);
        }

        this.hide();
        showToast(I18nModule.t('toast.blankInserted'));
        if (AppState.viewMode === 'sheet' && file) {
            PreviewPanelModule.render(file);
        }
    },

    // Open file picker, upload image as new file
    _pickImage(position) {
        this._pendingImagePosition = position;
        this._pendingImagePage     = this._currentPage;
        this.hide();
        const input = document.getElementById('insert-image-input');
        if (input) { input.value = ''; input.click(); }
    },

    _applyRotation(pageNum, rotation) {
        if (pageNum === null || pageNum === 0) return;
        if (rotation === null) {
            AppState.pageRotations.delete(pageNum);
            showToast(I18nModule.t('toast.rotateReset')(pageNum));
        } else {
            AppState.pageRotations.set(pageNum, rotation);
            const labels = {
                CW90: I18nModule.t('ctx.rotateCW'),
                CCW90: I18nModule.t('ctx.rotateCCW'),
                Rotate180: I18nModule.t('ctx.rotate180'),
                FlipHorizontal: I18nModule.t('ctx.flipH'),
                FlipVertical: I18nModule.t('ctx.flipV'),
            };
            showToast(I18nModule.t('toast.rotated')(pageNum, labels[rotation] || rotation), 'info');
        }

        // Invalidate cached renders for this page
        const fileEntry = AppState.activeFile;
        if (fileEntry) {
            // §5.7 (I3 invariant): If page was auto-rotated by together mode, evict it from
            // _togetherRotations. It is now "owned" by the user — teardown will not touch it.
            // Edge case: user resets rotation to null on a landscape page → p evicted,
            // pageRotations deleted → next _renderSheetView re-injects CCW90 (correct behavior).
            if (fileEntry._togetherRotations?.has(pageNum)) {
                fileEntry._togetherRotations.delete(pageNum);
            }

            const fid = fileEntry.id;
            const prefix = `${fid}-${pageNum}-`;
            PreviewPanelModule._cache.deleteByPrefix(prefix);
            ThumbStripModule._cache.deleteByPrefix(prefix);
            const card = PreviewPanelModule._pageEls.get(`${fid}-${pageNum}`);
            if (card) { card.classList.remove('rendered'); PreviewPanelModule._enqueue(fid, pageNum, card); }
            const thumbEl = ThumbStripModule._container?.querySelector(`.thumb-item[data-file-id="${fid}"][data-page="${pageNum}"]`);
            if (thumbEl) { thumbEl.classList.remove('rendered'); ThumbStripModule._enqueue(fid, pageNum, thumbEl); }

            // Update orientation cache for this page so sheet re-render skips the full getPage() loop.
            // We clear just the one entry; _renderSheetView will re-fetch only missing entries.
            if (fileEntry._orientationMap) {
                fileEntry._orientationMap.delete(pageNum);
            }
        }

        // Update data-rotation on legacy thumbnail
        const thumb = document.querySelector(`.page-thumbnail[data-page-number="${pageNum}"]`);
        if (thumb) {
            if (rotation) thumb.dataset.rotation = rotation;
            else delete thumb.dataset.rotation;
        }
        ZoomModal._updateModalStyles();
    },

    _scrollAndBlinkEjected(pageNum) {
        setTimeout(function() {
            const card = document.querySelector('.ejected-card[data-page="' + pageNum + '"]');
            if (!card) return;
            card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            const allCards = document.querySelectorAll('.ejected-card');
            if (allCards.length <= 1) return;
            let count = 0;
            const iv = setInterval(function() {
                card.style.outline = (count % 2 === 0) ? '2px solid rgba(239,68,68,0.75)' : 'none';
                if (++count >= 12) { clearInterval(iv); card.style.outline = ''; }
            }, 280);
        }, 150);
    },

};

// ===================================================================
// CopiesModule — Copies counter + collate toggle
// ===================================================================
const CopiesModule = {
    get copies()  { return AppState.activeFile?.copies  ?? 1;    },
    get collate() { return AppState.activeFile?.collate ?? true;  },

    init() {
        const dec = document.getElementById('copies-dec');
        const inc = document.getElementById('copies-inc');
        const chk = document.getElementById('collate-check');
        if (!dec || !inc) return;

        dec.addEventListener('click', () => {
            const f = AppState.activeFile;
            if (f && f.copies > 1) { f.copies--; this._update(); }
        });
        inc.addEventListener('click', () => {
            const f = AppState.activeFile;
            if (f && f.copies < 99) { f.copies++; this._update(); }
        });
        chk?.addEventListener('change', (e) => {
            const f = AppState.activeFile;
            if (f) f.collate = e.target.checked;
        });
    },

    _update() {
        const el = document.getElementById('copies-display');
        if (el) el.textContent = this.copies;
        // TEMPORARY: Always hide collate option
        const collateLabel = document.getElementById('collate-label');
        if (collateLabel) collateLabel.style.display = 'none';
    },

    setCopies(n) {
        const f = AppState.activeFile;
        if (f) { f.copies = Math.min(99, Math.max(1, n || 1)); this._update(); }
    },

    sync() {
        const chk = document.getElementById('collate-check');
        if (chk) chk.checked = AppState.activeFile?.collate ?? true;
        this._update();
    },

    reset() {
        const f = AppState.activeFile;
        if (f) { f.copies = 1; f.collate = true; }
        const chk = document.getElementById('collate-check');
        if (chk) chk.checked = true;
        this._update();
    },
};

// ═══════════════════════════════════════════════════════════════════
// HistoryModule — Recent print jobs (localStorage, last 10)
// ═══════════════════════════════════════════════════════════════════
const HistoryModule = {
    _KEY: 'myprinter_history',
    _MAX: 10,

    _load() {
        try { return JSON.parse(localStorage.getItem(this._KEY) || '[]'); }
        catch { return []; }
    },

    _save(items) {
        localStorage.setItem(this._KEY, JSON.stringify(items));
    },

    init() {
        this._render();
        document.getElementById('history-clear-btn')?.addEventListener('click', () => {
            this._save([]);
            this._render();
            showToast(I18nModule.t('toast.historyCleared'), 'info');
        });
        document.getElementById('history-toggle-btn')?.addEventListener('click', () => {
            document.getElementById('history-panel')?.classList.toggle('hidden');
        });
    },

    add(entry) {
        const items = this._load();
        items.unshift({ ...entry, time: new Date().toLocaleString('vi-VN') });
        this._save(items.slice(0, this._MAX));
        this._render();
    },

    removeItem(index) {
        const items = this._load();
        items.splice(index, 1);
        this._save(items);
        this._render();
        showToast(I18nModule.t('toast.historyItemRemoved'), 'info');
    },

    _reprint(index) {
        const items = this._load();
        const item  = items[index];
        if (!item) return;

        // Restore printer selection
        if (item.printerData) {
            AppState.selectedPrinter = item.printerData;
            document.querySelectorAll('.printer-item').forEach(el => {
                const p = JSON.parse(el.dataset.printer || '{}');
                el.classList.toggle('selected', p.name === item.printerData.name);
            });
        }

        // Restore mode
        if (item.mode) {
            const modeSel = document.getElementById('mode-select');
            if (modeSel) { modeSel.value = item.mode; AppState.printMode = item.mode; }
            ViewModeModule.onPrintModeChange(); // B26-FE-3: sync sheet-view layout when mode changes via reprint
        }

        // Restore copies
        if (item.copies) {
            CopiesModule.setCopies(item.copies);
            // _update() is called inside setCopies — no extra call needed
        }

        // Restore collate (per-file, stored in history since 70336ce)
        if (item.collate !== undefined) {
            const f = AppState.activeFile;
            if (f) { f.collate = item.collate; CopiesModule.sync(); }
        }

        // Restore page range — only when active file matches the history entry's file.
        // If a different file is active, the saved range may reference pages that don't
        // exist in the current file, causing silent truncation (e.g. "5-20" → page 5 only).
        const activeFileName = AppState.activeFile?.name ?? '';
        const historyFileName = item.file ?? '';
        const fileMatches = activeFileName === historyFileName;
        if (item.pageRange && AppState.totalPageCount > 0) {
            if (fileMatches) {
                const input = document.getElementById('page-range-input');
                if (input) {
                    input.value = item.pageRange;
                    input.dispatchEvent(new Event('input'));
                }
            }
        }

        const rangeSkipped = item.pageRange && !fileMatches;
        showToast(
            rangeSkipped
                ? I18nModule.t('toast.reprintSuccessMixed')(item.file)
                : I18nModule.t('toast.reprintSuccess')(item.file),
            'info'
        );
        PrintModule.updateButton();
    },

    _render() {
        const container = document.getElementById('history-list');
        if (!container) return;
        const items = this._load();
        if (items.length === 0) {
            container.innerHTML = `<div class="history-empty">${I18nModule.t('history.empty')}</div>`;
            return;
        }
        const modeLabel = {
            normal: I18nModule.t('mode.smart'),
            duplex: I18nModule.t('mode.smart'),
            booklet: I18nModule.t('mode.booklet'),
        };
        container.innerHTML = items.map((item, idx) => {
            const file = escapeHtml(item.file);
            const printer = escapeHtml(item.printer);
            const pages = escapeHtml(item.pages);
            const mode = escapeHtml(modeLabel[item.mode] || item.mode);
            const copies = escapeHtml(item.copies);
            const time = escapeHtml(item.time);
            return `
            <div class="history-item">
                <div class="history-item-actions">
                    <button class="history-action-btn history-reprint-btn" data-idx="${idx}" title="${I18nModule.t('historyItem.reprint')}">🔁</button>
                    <button class="history-action-btn" data-delete="${idx}" title="${I18nModule.t('historyItem.delete')}">✕</button>
                </div>
                <div class="history-file">📄 ${file}</div>
                <div class="history-meta">🖨️ ${printer} · ${escapeHtml(I18nModule.t('historyItem.pages')(pages))} · ${mode} · ${escapeHtml(I18nModule.t('historyItem.copies')(copies))}</div>
                <div class="history-time">${time}</div>
            </div>
        `; }).join('');

        // Attach delete handlers
        container.querySelectorAll('[data-delete]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.removeItem(parseInt(btn.dataset.delete));
            });
        });

        // Attach reprint handlers (Task E)
        container.querySelectorAll('.history-reprint-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this._reprint(parseInt(btn.dataset.idx));
            });
        });
    },
};


// ═══════════════════════════════════════════════════════════════════
// PrintModule — Print command, flip instructions, continue print
// ═══════════════════════════════════════════════════════════════════
const PrintModule = {
    _lastFlipInstruction: null,

    _setPrintButtonIdle(btn = document.getElementById('print-btn')) {
        if (!btn) return;
        btn.dataset.mode = '';
        btn.classList.remove('cancellable');
        btn.textContent = I18nModule.t('btn.print');
        btn.style.background = '';
        btn.style.opacity = '';
    },

    _jobId(job) {
        return job?.jobId || job?.JobId;
    },

    _currentJobId() {
        return this._jobId(AppState.currentJob);
    },

    _recoverableJob() {
        return AppState.currentJob || AppState.recoveryContext;
    },

    _setRecoveryContext(jobState) {
        AppState.recoveryContext = jobState || null;
        this._updateRecoveryButton();
    },

    async refreshRecoveryContext(jobState = null) {
        if (jobState) {
            this._setRecoveryContext(jobState);
            return jobState;
        }

        try {
            const res = await fetch(`${API_BASE}/print/recovery-context`);
            const result = await res.json();
            const recovered = result.jobState || result.JobState || null;
            this._setRecoveryContext(recovered);
            return recovered;
        } catch {
            this._updateRecoveryButton();
            return null;
        }
    },

    async _cancelCurrentPrintJob() {
        const jobIds = new Set();
        const addJobId = (job) => {
            const id = this._jobId(job);
            if (id) jobIds.add(id);
        };
        addJobId(AppState.currentJob);
        addJobId(AppState.recoveryContext);
        for (const job of AppState.pendingManualBatch?.jobs || []) addJobId(job);
        for (const job of AppState.pendingManualReviewQueue || []) addJobId(job);
        try {
            for (const jobId of jobIds) {
                await fetch(`${API_BASE}/print/cancel?jobId=${jobId}`, { method: 'DELETE' });
            }
            showToast(I18nModule.t('toast.printCancelled'), 'info');
            SRModule.announce(I18nModule.t('sr.printCancelled'));
        } catch {
            showToast(I18nModule.t('toast.printCancelFailed'), 'error');
        }

        AppState.currentJob = null;
        AppState.recoveryContext = null;
        AppState.pendingPrintQueue = null;
        AppState.pendingManualBatch = null;
        AppState.pendingManualReviewQueue = null;
        this._setPrintButtonIdle();
        document.getElementById('flip-modal')?.classList.add('hidden');
        document.getElementById('phase1-recovery-modal')?.classList.add('hidden');
        document.getElementById('phase2-recovery-modal')?.classList.add('hidden');
        PrintModule.updateButton();
    },

    init() {
        const btn = document.getElementById('print-btn');
        btn.addEventListener('click', (e) => {
            if (btn.dataset.mode === 'flip-paused' && AppState.currentJob?.jobId) {
                this._showActiveFlipModal();
                return;
            }

            if (btn.dataset.mode === 'phase2-review' && this._currentJobId()) {
                this._cancelCurrentPrintJob();
                return;
            }

            // Cancel mode (A): if job is waiting for flip, cancel it
            if (btn.dataset.mode === 'cancellable' && this._currentJobId()) {
                this._cancelCurrentPrintJob();
                return;
            }

            // Ripple effect (L)
            const ripple = document.createElement('span');
            ripple.className = 'ripple';
            const rect = btn.getBoundingClientRect();
            ripple.style.left = (e.clientX - rect.left) + 'px';
            ripple.style.top  = (e.clientY - rect.top)  + 'px';
            btn.appendChild(ripple);
            ripple.addEventListener('animationend', () => ripple.remove());

            this._startPrint();
        });
        document.getElementById('continue-btn')?.addEventListener('click', async () => {
            await this._continuePrint();
            document.getElementById('flip-modal').classList.add('hidden');
        });
        // Close button on flip modal (user can dismiss and reopen via Cancel Print)
        document.getElementById('flip-modal-close')?.addEventListener('click', () => {
            this._setPrintButtonForFlipPause();
            document.getElementById('flip-modal').classList.add('hidden');
        });
        document.getElementById('phase1-recovery-open-btn')?.addEventListener('click', () => {
            Phase1RecoveryModule.open();
        });
        document.getElementById('flip-cancel-print-btn')?.addEventListener('click', () => this._cancelCurrentPrintJob());
        document.getElementById('recovery-btn')?.addEventListener('click', () => {
            const job = this._recoverableJob();
            if (!job) {
                showToast(I18nModule.t('recovery.noContext'), 'info');
                return;
            }
            AppState.currentJob = job;
            const phase = this._activeRecoveryPhase();
            if (phase === 'phase1') Phase1RecoveryModule.open();
            else if (phase === 'phase2') Phase2RecoveryModule.openRecovery();
        });
        this._updateRecoveryButton();
        this.refreshRecoveryContext();
    },

    _activeRecoveryPhase() {
        const job = this._recoverableJob();
        const jobId = job?.jobId || job?.JobId;
        if (!jobId) return null;

        const waitingForFlip = job.waitingForFlip ?? job.WaitingForFlip;
        const backPassSent = job.backPassSent ?? job.BackPassSent;
        if (waitingForFlip) return 'phase1';
        if (backPassSent) return 'phase2';

        const mode = document.getElementById('print-btn')?.dataset.mode;
        if (mode === 'cancellable' || mode === 'flip-paused') return 'phase1';
        if (mode === 'phase2-review') return 'phase2';
        return null;
    },

    _updateRecoveryButton() {
        const recoveryBtn = document.getElementById('recovery-btn');
        const phase = this._activeRecoveryPhase();
        if (recoveryBtn) {
            recoveryBtn.disabled = !phase;
            if (phase) recoveryBtn.dataset.phase = phase;
            else delete recoveryBtn.dataset.phase;
            const titleKey = phase === 'phase1'
                ? 'recovery.availablePhase1'
                : phase === 'phase2'
                    ? 'recovery.availablePhase2'
                    : 'recovery.unavailable';
            recoveryBtn.title = I18nModule.t(titleKey);
        }
    },

    _setPrintButtonForFlipPause() {
        const btn = document.getElementById('print-btn');
        if (!btn || !this._currentJobId()) return;
        btn.dataset.mode = 'flip-paused';
        btn.classList.remove('cancellable');
        btn.textContent = I18nModule.t('print.reopenFlip');
        btn.disabled = false;
        btn.style.opacity = '1';
        this._updateRecoveryButton();
    },

    _showActiveFlipModal() {
        this._showFlipModal(this._lastFlipInstruction || AppState.currentJob?.instruction || AppState.currentJob?.Instruction);
        const btn = document.getElementById('print-btn');
        if (btn) {
            btn.dataset.mode = 'cancellable';
            btn.classList.add('cancellable');
            btn.textContent = I18nModule.t('print.cancel');
            btn.disabled = false;
            btn.style.opacity = '1';
        }
        this._updateRecoveryButton();
    },

    updateButton() {
        const printBtn   = document.getElementById('print-btn');
        // B32-FE-6 fix: check ANY file has pages selected, not just the active file.
        // _startPrint() prints from AppState.files.filter(f => f.selectedPages.size > 0),
        // so the button must be enabled whenever any file is printable, regardless of
        // which file is currently active in the tab strip.
        const anyFileReady = AppState.files.some(f => f.selectedPages.size > 0);
        const ready = !!(AppState.selectedPrinter && anyFileReady);
        if (printBtn)   printBtn.disabled   = !ready;
        this._updateRecoveryButton();
        SummaryModule.update();
        if (PrintPreviewModule._isOpen) PrintPreviewModule._updateFooterSummary();
    },

    async _startPrint() {
        if (!AppState.selectedPrinter) { showToast(I18nModule.t('toast.selectPrinterFirst'), 'error'); return; }

        // Collect all files with at least 1 selected page
        const filesToPrint = AppState.files.filter(f => f.selectedPages.size > 0);
        if (filesToPrint.length === 0) {
            showToast(I18nModule.t('toast.noPagesSelected'), 'error');
            return;
        }

        // B33-FE-4 fix: disable the Print button BEFORE awaiting the confirmation modal.
        // Previously the button was only disabled after the modal resolved, leaving a window
        // where a rapid double-click would start two concurrent _startPrint() invocations
        // and submit two POST /api/print requests. Disabling here collapses that race window.
        const btn = document.getElementById('print-btn');
        const originalText = btn?.textContent;
        if (btn) { btn.disabled = true; btn.style.opacity = '0.8'; }

        // Show confirmation dialog
        const confirmed = await ConfirmPrintModal.show();
        if (!confirmed) {
            // User cancelled — re-enable button
            if (btn) { btn.disabled = false; btn.style.opacity = ''; btn.textContent = originalText; }
            PrintModule.updateButton();
            return;
        }

        const mode = AppState.printMode || 'duplex'; // B25-FE-1: read from AppState (single source of truth); DOM may be stale after reset()
        const modeCode = (mode === 'duplex' || mode === 'normal') ? 0 : 1;
        const manualJobs = [];

        try {
            for (let i = 0; i < filesToPrint.length; i++) {
                const file = filesToPrint[i];

                btn.textContent = filesToPrint.length > 1
                    ? I18nModule.t('print.printing')(i + 1, filesToPrint.length)
                    : I18nModule.t('print.sendingSingle');

                const sel = file.selectedPages;
                const pageRange = (sel.size > 0 && sel.size < file.totalPageCount)
                    ? Array.from(sel).sort((a,b) => a-b).join(',')
                    : null;

                // Compute duplexSide for together mode (spec §5.8)
                let duplexSide = null;  // default: null = no override → backend uses printer default
                // ('LongEdge' is NEVER sent explicitly — null preserves existing behavior for portrait/mixed cases)
                if (mode !== 'booklet' && file.pdfDoc != null) {
                    let allLandscape = true;
                    // Check only pages actually being printed (Bug B8 fix):
                    // previously looped 1..totalPageCount, so unselected portrait pages in a mixed
                    // doc would keep allLandscape=false even when only landscape pages are selected.
                    const pagesToCheck = (sel.size > 0 && sel.size < file.totalPageCount)
                        ? [...sel]                                                        // partial selection → check only selected
                        : Array.from({ length: file.totalPageCount }, (_, i) => i + 1); // all selected → check all
                    for (const p of pagesToCheck) {
                        if (!await this._isPrintLandscapePage(file, p)) {
                            allLandscape = false;
                            break;
                        }
                    }
                    if (allLandscape) duplexSide = 'ShortEdge';
                }

                // Build request-local pageRotations — fallback for booklet+together when
                // _originalOrientationMap is null (page view, or sheet view before first-render commit).
                // MUST NOT mutate file.pageRotations — request-local only.
                let pageRotationsForPrint = new Map(file.pageRotations);
                if (mode === 'booklet'
                    && file.landscapeMode === 'together'
                    && file._originalOrientationMap == null
                    && file.pdfDoc != null) {
                    // Snapshot pdfDoc reference before first await — rest of UI can mutate live
                    // file.* while the async loop yields to the event loop.
                    const pdfDoc = file.pdfDoc;
                    // Only check pages actually being printed — mirrors duplexSide pagesToCheck logic.
                    const pagesToPrint = (sel.size > 0 && sel.size < file.totalPageCount)
                        ? [...sel]
                        : Array.from({ length: file.totalPageCount }, (_, idx) => idx + 1);
                    // Sequential loop (not Promise.all) — avoids firing all pdfDoc.getPage() at once
                    // on large documents; simpler and safer in a print path.
                    for (const p of pagesToPrint) {
                        let page = null;
                        try {
                            page = await pdfDoc.getPage(p);
                            if (await RotationHelper.detectLandscape(page) && !pageRotationsForPrint.has(p)) {
                                pageRotationsForPrint.set(p, 'CCW90');
                            }
                        } catch (err) {
                            // getPage failed — treat as portrait (no CCW90 injected for this page).
                            // Log so the failure is diagnosable; do not abort the whole print.
                            console.warn(`[booklet-together] getPage(${p}) failed, skipping rotation:`, err);
                        } finally {
                            page?.cleanup();
                        }
                    }
                }

                const body = {
                    fileId:           file.id,
                    printerName:      AppState.selectedPrinter.name,
                    mode:             modeCode,
                    pageRange,
                    singleSidedPages: file.singleSidedPages.size > 0 ? Array.from(file.singleSidedPages) : null,
                    copies:           file.copies,
                    collate:          file.collate,
                    pageOrder:        (() => {
                        const effective = buildEffectivePageOrder(file);
                        return effective.length > 0 ? effective : null;
                    })(),
                    pageRotations:    pageRotationsForPrint.size > 0
                        ? Array.from(pageRotationsForPrint.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
                        : null,
                    duplexSide,           // null | 'ShortEdge'  (null = no override; 'LongEdge' is never sent explicitly)
                    manualFlipDir: duplexSide,  // same value; maps to PrintRequest.ManualFlipDir on backend
                };

                showToast(I18nModule.t('toast.sendingFile')(file.name), 'info');
                SRModule.announce(I18nModule.t('sr.printingFile')(file.name));

                const res    = await fetch(`${API_BASE}/print`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                const result = await res.json();

                if (!result.success) {
                    showToast(I18nModule.t('toast.printFileError')(file.name, result.message), 'error');
                    btn.disabled = false;
                    btn.textContent = originalText;
                    btn.style.opacity = '';
                    return;
                }

                if (result.jobState?.waitingForFlip) {
                    // Manual duplex: collect all front-pass jobs first, then show one flip modal.
                    const manualJob = result.jobState;
                    // B28-FE-2 fix: save history metadata so _continuePrint can record
                    // this job in history after Phase 2 completes.
                    manualJob._historyEntry = {
                        file:        file.name,
                        fileId:      file.id,
                        printer:     AppState.selectedPrinter.name,
                        printerData: AppState.selectedPrinter,
                        pages:       file.selectedPages.size,
                        pageRange,
                        mode,
                        copies:      file.copies,
                        collate:     file.collate,
                    };
                    manualJobs.push(manualJob);
                    continue;
                }

                // Add to history for each file printed
                HistoryModule.add({
                    file:        file.name,
                    fileId:      file.id,
                    printer:     AppState.selectedPrinter.name,
                    printerData: AppState.selectedPrinter,
                    pages:       file.selectedPages.size,
                    pageRange,
                    mode:        mode,
                    copies:      file.copies,
                    collate:     file.collate,
                });

            }

            if (manualJobs.length > 0) {
                AppState.pendingManualBatch = { jobs: manualJobs, originalText };
                AppState.currentJob = manualJobs[0];
                this._setRecoveryContext(manualJobs[0]);
                this._showFlipModal(manualJobs[0].instruction || manualJobs[0].Instruction);
                btn.dataset.mode = 'cancellable';
                btn.classList.add('cancellable');
                btn.textContent = I18nModule.t('print.cancel');
                btn.disabled = false;
                btn.style.opacity = '1';
                this._updateRecoveryButton();
                showToast(I18nModule.t('toast.frontDone'), 'info');
                return;
            }

            // All files printed successfully
            const fileCount = filesToPrint.length;
            btn.textContent = I18nModule.t('print.done')(fileCount);
            btn.style.background = 'linear-gradient(135deg, #10b981, #059669)';
            btn.style.opacity = '1';
            showToast(fileCount > 1 ? I18nModule.t('toast.printSuccess')(fileCount) : I18nModule.t('toast.printSuccess')(1), 'success');
            SRModule.announce(I18nModule.t('sr.printSuccess'));

            setTimeout(() => {
                btn.disabled = false;
                btn.textContent = originalText;
                btn.style.background = '';
                btn.style.opacity = '';
                PrintModule.updateButton();
            }, 2000);

        } catch (err) {
            showToast(I18nModule.t('toast.printError')(err.message), 'error');
            const actionSec = document.querySelector('.action-section') || document.getElementById('print-btn')?.closest('.card');
            showCardError(actionSec, I18nModule.t('error.printFailed')(err.message), () => document.getElementById('print-btn')?.click());
            btn.disabled = false;
            btn.textContent = originalText;
            btn.style.opacity = '';
        }
    },

    async _continuePrint() {
        // BUG-1 fix: guard against null currentJob (timer race after cancel, or ESC edge case)
        if (!AppState.currentJob) return;
        try {
            showToast(I18nModule.t('toast.printingBack'), 'info');
            const manualBatch = AppState.pendingManualBatch;
            if (manualBatch?.jobs?.length) {
                const completedJobs = [];
                const completedJobIds = new Set();
                const jobsForBackPass = manualBatch.jobs.slice().reverse();
                for (const job of jobsForBackPass) {
                    const jobId = this._jobId(job);
                    if (!jobId) continue;

                    let result;
                    try {
                        const res = await fetch(`${API_BASE}/print/continue?jobId=${jobId}`, { method: 'POST' });
                        result = await res.json();
                    } catch (batchErr) {
                        AppState.pendingManualBatch = {
                            ...manualBatch,
                            jobs: manualBatch.jobs.filter(item => !completedJobIds.has(this._jobId(item)))
                        };
                        showToast(I18nModule.t('toast.continueError')(batchErr.message), 'error');
                        AppState.currentJob = job;
                        this._setRecoveryContext(job);
                        await this.refreshRecoveryContext(job);
                        return;
                    }

                    if (!result.success) {
                        AppState.pendingManualBatch = {
                            ...manualBatch,
                            jobs: manualBatch.jobs.filter(item => !completedJobIds.has(this._jobId(item)))
                        };
                        showToast(I18nModule.t('toast.uploadError')(result.message), 'error');
                        AppState.currentJob = job;
                        this._setRecoveryContext(job);
                        await this.refreshRecoveryContext(job);
                        return;
                    }

                    const completedJob = result.jobState || result.JobState || job;
                    completedJob._historyEntry = job._historyEntry;
                    completedJobs.push(completedJob);
                    completedJobIds.add(jobId);
                }

                AppState.pendingManualBatch = null;
                const completedById = new Map(completedJobs.map(job => [this._jobId(job), job]));
                const reviewJobs = manualBatch.jobs
                    .map(job => completedById.get(this._jobId(job)))
                    .filter(Boolean);
                AppState.pendingManualReviewQueue = reviewJobs.slice(1);
                AppState.currentJob = reviewJobs[0] || null;
                if (AppState.currentJob) this._setRecoveryContext(AppState.currentJob);

                const btn = document.getElementById('print-btn');
                if (btn && AppState.currentJob) {
                    btn.dataset.mode = 'phase2-review';
                    btn.classList.add('cancellable');
                    btn.textContent = I18nModule.t('print.cancel');
                    btn.disabled = false;
                    btn.style.opacity = '1';
                }
                this._updateRecoveryButton();
                showToast(I18nModule.t('phase2Recovery.backSent'), 'info');
                return;
            }

            const res    = await fetch(`${API_BASE}/print/continue?jobId=${this._currentJobId()}`, { method: 'POST' });
            const result = await res.json();
            const btn = document.getElementById('print-btn');
            const resetBtn = ({ clearRecoveryContext = true } = {}) => {
                AppState.currentJob = null;
                if (clearRecoveryContext) AppState.recoveryContext = null;
                if (btn) {
                    this._setPrintButtonIdle(btn);
                    PrintModule.updateButton();
                }
            };
            if (result.success) {
                if (result.jobState?.backPassSent || result.jobState?.BackPassSent) {
                    const histEntry = AppState.currentJob?._historyEntry;
                    AppState.currentJob = result.jobState;
                    AppState.currentJob._historyEntry = histEntry;
                    this._setRecoveryContext(result.jobState);
                    if (btn) {
                        btn.dataset.mode = 'phase2-review';
                        btn.classList.add('cancellable');
                        btn.textContent = I18nModule.t('print.cancel');
                        btn.disabled = false;
                        btn.style.opacity = '1';
                    }
                    this._updateRecoveryButton();
                    showToast(I18nModule.t('phase2Recovery.backSent'), 'info');
                    return;
                }

                await this._completeCurrentManualJob({ skipServerComplete: true, resetBtn });
            } else {
                // BUG-2 fix: reset button even on failure so UI doesn't get stuck
                showToast(I18nModule.t('toast.uploadError')(result.message), 'error');
                AppState.pendingPrintQueue = null;
                resetBtn({ clearRecoveryContext: false });
                await this.refreshRecoveryContext();
            }
        } catch (err) {
            // BUG-2 fix: also reset on network error
            showToast(I18nModule.t('toast.continueError')(err.message), 'error');
            AppState.currentJob = null;
            AppState.pendingPrintQueue = null;
            const btn = document.getElementById('print-btn');
            if (btn) {
                this._setPrintButtonIdle(btn);
                PrintModule.updateButton();
            }
            await this.refreshRecoveryContext();
        }
    },

    async _completeCurrentManualJob(options = {}) {
        const btn = document.getElementById('print-btn');
        const resetBtn = options.resetBtn || (() => {
            AppState.currentJob = null;
            AppState.recoveryContext = null;
            if (btn) {
                this._setPrintButtonIdle(btn);
                PrintModule.updateButton();
            }
        });

        const jobId = AppState.currentJob?.jobId || AppState.currentJob?.JobId;
        if (!jobId) return;

        if (!options.skipServerComplete) {
            const res = await fetch(`${API_BASE}/print/complete?jobId=${jobId}`, { method: 'POST' });
            const result = await res.json();
            if (!res.ok || !result.success) throw new Error(result.message || res.statusText);
        }

        showToast(I18nModule.t('toast.printComplete'), 'success');
        const histEntry = AppState.currentJob?._historyEntry;
        resetBtn();
        if (histEntry) HistoryModule.add(histEntry);

        const queue = AppState.pendingPrintQueue;
        if (queue && queue.nextIndex < queue.files.length) {
            await this._resumePrintQueue(queue);
        }
        AppState.pendingPrintQueue = null;
    },

    // B17-FE-2 fix: print remaining files in the queue after a manual-flip pause.
    // Mirrors the inner loop of _startPrint but starts from queue.nextIndex.
    async _resumePrintQueue({ files, nextIndex, modeCode, originalText }) {
        // B29-FE-5 fix: derive string mode from modeCode so HistoryModule.add receives a
        // string key that matches modeLabel (not an integer 0/1 which renders as raw number).
        const mode = modeCode === 1 ? 'booklet' : 'duplex';
        const btn = document.getElementById('print-btn');
        for (let i = nextIndex; i < files.length; i++) {
            const file   = files[i];

            if (btn) {
                btn.textContent = I18nModule.t('print.printing')(i + 1, files.length);
                btn.disabled = true;
                btn.style.opacity = '0.7';
            }

            const sel = file.selectedPages;
            const pageRange = (sel.size > 0 && sel.size < file.totalPageCount)
                ? Array.from(sel).sort((a, b) => a - b).join(',')
                : null;

            let duplexSide = null;
            if (mode !== 'booklet' && file.pdfDoc != null) {
                let allLandscape = true;
                const pagesToCheck = (sel.size > 0 && sel.size < file.totalPageCount)
                    ? [...sel]
                    : Array.from({ length: file.totalPageCount }, (_, k) => k + 1);
                for (const p of pagesToCheck) {
                    if (!await this._isPrintLandscapePage(file, p)) { allLandscape = false; break; }
                }
                if (allLandscape) duplexSide = 'ShortEdge';
            }

            // Request-local pageRotations fallback — same pattern as _startPrint.
            // Uses captured `mode` (from modeCode, line 2755), NOT AppState.printMode.
            let pageRotationsForPrint = new Map(file.pageRotations);
            if (mode === 'booklet'
                && file.landscapeMode === 'together'
                && file._originalOrientationMap == null
                && file.pdfDoc != null) {
                // Snapshot pdfDoc reference before first await.
                const pdfDoc = file.pdfDoc;
                // Only check pages actually being printed — mirrors duplexSide pagesToCheck logic.
                const pagesToPrint = (sel.size > 0 && sel.size < file.totalPageCount)
                    ? [...sel]
                    : Array.from({ length: file.totalPageCount }, (_, idx) => idx + 1);
                // Sequential loop — avoids firing all pdfDoc.getPage() at once on large documents.
                for (const p of pagesToPrint) {
                    let page = null;
                    try {
                        page = await pdfDoc.getPage(p);
                        if (await RotationHelper.detectLandscape(page) && !pageRotationsForPrint.has(p)) {
                            pageRotationsForPrint.set(p, 'CCW90');
                        }
                    } catch (err) {
                        console.warn(`[booklet-together] getPage(${p}) failed, skipping rotation:`, err);
                    } finally {
                        page?.cleanup();
                    }
                }
            }

            const body = {
                fileId:           file.id,
                printerName:      AppState.selectedPrinter.name,
                mode:             modeCode,
                pageRange,
                singleSidedPages: file.singleSidedPages.size > 0 ? Array.from(file.singleSidedPages) : null,
                copies:           file.copies,
                collate:          file.collate,
                pageOrder:        (() => {
                    const effective = buildEffectivePageOrder(file);
                    return effective.length > 0 ? effective : null;
                })(),
                pageRotations:    pageRotationsForPrint.size > 0
                    ? Array.from(pageRotationsForPrint.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
                    : null,
                duplexSide,
                manualFlipDir: duplexSide,  // B20-FE-1 fix: mirror _startPrint which sends both fields
            };

            try {
                const res    = await fetch(`${API_BASE}/print`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                const result = await res.json();

                if (!result.success) {
                    showToast(I18nModule.t('toast.printFileError')(file.name, result.message), 'error');
                    if (btn) { btn.disabled = false; btn.textContent = originalText; btn.style.opacity = ''; }
                    return;
                }

                if (result.jobState?.waitingForFlip) {
                    AppState.currentJob = result.jobState;
                    this._setRecoveryContext(result.jobState);
                    // B28-FE-2 fix: save history metadata so _continuePrint can record
                    // this chained job in history after Phase 2 completes.
                    AppState.currentJob._historyEntry = {
                        file:        file.name,
                        fileId:      file.id,
                        printer:     AppState.selectedPrinter.name,
                        printerData: AppState.selectedPrinter,
                        pages:       file.selectedPages.size,
                        pageRange,
                        mode,         // B29-FE-5 fix: string ('duplex'/'booklet'), not modeCode integer
                        copies:      file.copies,
                        collate:     file.collate,
                    };
                    AppState.pendingPrintQueue = (i + 1 < files.length)
                        ? { files, nextIndex: i + 1, modeCode, originalText }
                        : null;
                    this._showFlipModal(result.jobState.instruction);
                    if (btn) {
                        btn.dataset.mode = 'cancellable';
                        btn.classList.add('cancellable');
                        btn.innerHTML = `<span class="btn-icon">✕</span> ${I18nModule.t('print.cancel')}`;
                        btn.disabled = false;
                        btn.style.opacity = '1';
                    }
                    this._updateRecoveryButton();
                    showToast(I18nModule.t('toast.frontDone'), 'info');
                    return;
                }

                HistoryModule.add({
                    file:        file.name,
                    fileId:      file.id,
                    printer:     AppState.selectedPrinter.name,
                    printerData: AppState.selectedPrinter,
                    pages:       file.selectedPages.size,
                    pageRange,
                    mode,         // B29-FE-5 fix: string ('duplex'/'booklet'), not modeCode integer
                    copies:      file.copies,
                    collate:     file.collate,
                });

            } catch (err) {
                showToast(I18nModule.t('toast.printFileError')(file.name, err.message), 'error');
                if (btn) { btn.disabled = false; btn.textContent = originalText; btn.style.opacity = ''; }
                return;
            }
        }

        // All remaining files printed
        const fileCount = files.length;
        if (btn) {
            btn.textContent = I18nModule.t('print.done')(fileCount);
            btn.style.background = 'linear-gradient(135deg, #10b981, #059669)';
            btn.style.opacity = '1';
        }
        showToast(fileCount > 1 ? I18nModule.t('toast.printSuccess')(fileCount) : I18nModule.t('toast.printSuccess')(1), 'success');
        setTimeout(() => {
            if (btn) {
                btn.disabled = false;
                this._setPrintButtonIdle(btn);
                btn.style.background = '';
                btn.style.opacity = '';
                PrintModule.updateButton();
            }
        }, 2000);
    },

    _showFlipModal(instruction) {
        this._lastFlipInstruction = instruction;
        document.getElementById('instruction-visual').innerHTML = `
        <div class="p3wrap">
         <svg viewBox="0 0 420 260" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:260px">
          <defs>
           <linearGradient id="gTop" x1="0" y1="0" x2="1" y2="1">
             <stop offset="0%"  stop-color="#c2692e"/>
             <stop offset="100%" stop-color="#92400e"/>
           </linearGradient>
           <linearGradient id="gFront" x1="0" y1="0" x2="0" y2="1">
             <stop offset="0%"  stop-color="#7c3910"/>
             <stop offset="100%" stop-color="#5a2a07"/>
           </linearGradient>
           <linearGradient id="gRight" x1="0" y1="0" x2="0" y2="1">
             <stop offset="0%"  stop-color="#5a2a07"/>
             <stop offset="100%" stop-color="#3b1a04"/>
           </linearGradient>
           <linearGradient id="gPaper" x1="0" y1="0" x2="1" y2="1">
             <stop offset="0%"  stop-color="#ffffff"/>
             <stop offset="100%" stop-color="#f1f5f9"/>
           </linearGradient>
           <linearGradient id="gPaperR" x1="0" y1="0" x2="1" y2="0">
             <stop offset="0%"  stop-color="#cbd5e1"/>
             <stop offset="100%" stop-color="#94a3b8"/>
           </linearGradient>
           <linearGradient id="gPaperB" x1="0" y1="0" x2="0" y2="1">
             <stop offset="0%"  stop-color="#cbd5e1"/>
             <stop offset="100%" stop-color="#94a3b8"/>
           </linearGradient>
           <linearGradient id="gTrayOut" x1="0" y1="0" x2="1" y2="1">
             <stop offset="0%"  stop-color="#34d399"/>
             <stop offset="100%" stop-color="#059669"/>
           </linearGradient>
          </defs>

          <!-- ===== PRINTER ISO (top-front-right view) ===== -->
          <!-- Iso math: printer centered ~(210,140)
               Top face    : parallelogram, skewed
               Front face  : rectangle below top-front edge
               Right face  : parallelogram, right of front
          -->

          <!-- RIGHT face (darkest) -->
          <polygon points="270,80  310,100  310,180  270,160" fill="url(#gRight)" stroke="#1a0a00" stroke-width="1"/>

          <!-- FRONT face -->
          <polygon points="110,100  270,100  270,180  110,180" fill="url(#gFront)" stroke="#1a0a00" stroke-width="1"/>

          <!-- TOP face (lightest, isometric top) -->
          <polygon points="110,40   270,40   310,60   150,60" fill="url(#gTop)" stroke="#1a0a00" stroke-width="1"/>

          <!-- Connecting edges top-to-body -->
          <line x1="110" y1="40" x2="110" y2="100" stroke="#1a0a00" stroke-width="1"/>
          <line x1="270" y1="40" x2="270" y2="100" stroke="#1a0a00" stroke-width="1"/>
          <line x1="310" y1="60" x2="310" y2="100" stroke="#1a0a00" stroke-width="1"/>

          <!-- OUTPUT TRAY slot on top face -->
          <polygon points="140,46  230,46  256,57  166,57" fill="#3b1a04" stroke="#1a0a00" stroke-width="0.5"/>
          <!-- Paper in output tray (white sheet, iso) -->
          <polygon points="148,44  228,44  250,54  170,54" fill="white" stroke="#10b981" stroke-width="1.5"/>
          <!-- Printed lines on output paper -->
          <line x1="160" y1="47" x2="220" y2="47" stroke="#94a3b8" stroke-width="1"/>
          <line x1="160" y1="50" x2="210" y2="50" stroke="#94a3b8" stroke-width="1"/>
          <text x="232" y="52" font-size="7" fill="#10b981" font-weight="700">✓</text>

          <!-- LED dot on front face -->
          <circle cx="255" cy="115" r="5" fill="#10b981">
            <animate attributeName="opacity" values="1;0.2;1" dur="1.4s" repeatCount="indefinite"/>
          </circle>

          <!-- INPUT TRAY: protrudes below front face -->
          <!-- Tray top face (iso) -->
          <polygon points="120,180  260,180  295,198  155,198" fill="#3d2c1e" stroke="#1a0a00" stroke-width="1"/>
          <!-- Tray front face -->
          <polygon points="120,180  260,180  260,195  120,195" fill="#4e3728" stroke="#1a0a00" stroke-width="1"/>
          <!-- Tray right face -->
          <polygon points="260,180  295,198  295,213  260,195" fill="#2c1a10" stroke="#1a0a00" stroke-width="1"/>
          <!-- Slot opening on tray top -->
          <polygon points="140,182  245,182  278,196  173,196" fill="#1a0a00" stroke="none"/>

          <!-- Labels -->
          <text x="190" y="25" font-size="10" text-anchor="middle" fill="#64748b">Khay ra (Output)</text>
          <line x1="190" y1="27" x2="190" y2="42" stroke="#64748b" stroke-width="0.8" stroke-dasharray="2,2"/>
          <text x="210" y="222" font-size="10" text-anchor="middle" fill="#64748b">Khay nạp (Input)</text>
          <line x1="210" y1="218" x2="210" y2="200" stroke="#64748b" stroke-width="0.8" stroke-dasharray="2,2"/>

          <!-- ===== ANIMATED PAPER (iso 3D sheet) ===== -->
          <!-- Paper moves: start at output tray (top),
               arc out toward viewer (scale up, move forward),
               then into input tray (scale down, move back) -->
          <g id="movingPaper">
           <!-- Paper top face -->
           <polygon class="mp-top"  points="0,0  60,0  72,8  12,8"  fill="url(#gPaper)" stroke="#94a3b8" stroke-width="1"/>
           <!-- Paper right edge (thickness) -->
           <polygon class="mp-rgt"  points="60,0  72,8  72,22  60,14" fill="url(#gPaperR)" stroke="#94a3b8" stroke-width="0.5"/>
           <!-- Paper front face -->
           <polygon class="mp-frt"  points="0,0  60,0  60,14  0,14"  fill="url(#gPaper)" stroke="#94a3b8" stroke-width="1"/>
           <!-- Printed lines on paper face -->
           <line class="mp-l1" x1="4" y1="4"  x2="54" y2="4"  stroke="#94a3b8" stroke-width="0.8"/>
           <line class="mp-l2" x1="4" y1="7"  x2="44" y2="7"  stroke="#94a3b8" stroke-width="0.8"/>
           <line class="mp-l3" x1="4" y1="10" x2="48" y2="10" stroke="#94a3b8" stroke-width="0.8"/>
           <text class="mp-lbl" x="38" y="13" font-size="5" fill="#10b981" font-weight="700">✓M1</text>
           <!-- Paper bottom face (visible when paper arcs toward viewer) -->
           <polygon class="mp-bot" points="0,14  60,14  72,22  12,22" fill="url(#gPaperB)" stroke="#94a3b8" stroke-width="0.5"/>
          </g>

          <!-- Step labels -->
          <text id="sl1" x="80"  y="248" font-size="10" text-anchor="middle" fill="#94a3b8">① Lấy từ khay ra</text>
          <text id="sl2" x="340" y="248" font-size="10" text-anchor="middle" fill="#94a3b8">② Đưa vào khay nạp</text>

         </svg>
        </div>
        `;
        document.getElementById('instruction-text').textContent =
            (typeof instruction === 'object' ? instruction?.text || instruction?.Text : instruction) || I18nModule.t('flip.defaultInstruction');

        document.getElementById('flip-modal').classList.remove('hidden');
        Phase1RecoveryModule.syncFromJob();
        this._updateRecoveryButton();
    },

    async _isPrintLandscapePage(file, pageNum) {
        if (!file._printOrientationMap) file._printOrientationMap = new Map();
        const rotation = file.pageRotations?.get(pageNum) ?? null;
        const cacheKey = `${pageNum}:${rotation ?? '0'}`;
        if (file._printOrientationMap.has(cacheKey)) {
            return file._printOrientationMap.get(cacheKey);
        }

        let page = null;
        try {
            page = await file.pdfDoc.getPage(pageNum);
            const isLandscape = await RotationHelper.detectLandscape(page, rotation);
            file._printOrientationMap.set(cacheKey, isLandscape);
            return isLandscape;
        } catch (err) {
            console.warn(`[print-orientation] getPage(${pageNum}) failed, treating as portrait:`, err);
            return false;
        } finally {
            page?.cleanup();
        }
    },
};

// ═══════════════════════════════════════════════════════════════════
// Phase1RecoveryModule — Reprint damaged front-pass sheets before flip
// ═══════════════════════════════════════════════════════════════════
const Phase1RecoveryModule = {
    _sheets: [],
    _activeCopy: 1,
    _printAttempts: 0,

    init() {
        document.getElementById('phase1-recovery-close')?.addEventListener('click', () => this.close());
        document.getElementById('phase1-recovery-overlay')?.addEventListener('click', () => this.close());
        document.getElementById('phase1-recovery-cancel')?.addEventListener('click', () => this.close());
        document.getElementById('phase1-recovery-apply-range')?.addEventListener('click', () => this._applyRange());
        document.getElementById('phase1-recovery-clear')?.addEventListener('click', () => this._clearSelection());
        document.getElementById('phase1-recovery-submit')?.addEventListener('click', () => this._submit());
    },

    syncFromJob() {
        const btn = document.getElementById('phase1-recovery-open-btn');
        const sheets = this._getSheets();
        this._sheets = sheets;
        this._activeCopy = Math.min(this._activeCopy, this._jobCopies());
        if (btn) btn.disabled = sheets.length === 0;
    },

    open() {
        this.syncFromJob();
        if (this._sheets.length === 0) {
            showToast(I18nModule.t('recovery.noPlan'), 'error');
            return;
        }
        this._renderList();
        this._setStatus('');
        this._printAttempts = 0;
        this._updateSubmitLabel();
        document.getElementById('phase1-recovery-modal')?.classList.remove('hidden');
        document.getElementById('phase1-recovery-range')?.focus();
    },

    close() {
        document.getElementById('phase1-recovery-modal')?.classList.add('hidden');
    },

    _getSheets() {
        const job = AppState.currentJob;
        const plan = job?.manualPlan || job?.ManualPlan;
        return plan?.sheets || plan?.Sheets || [];
    },

    _jobCopies() {
        const copies = AppState.currentJob?.copies ?? AppState.currentJob?.Copies ?? 1;
        return Math.max(1, parseInt(copies, 10) || 1);
    },

    _pageLabel(page, side) {
        if (!page || page.isBlank || page.IsBlank) return I18nModule.t(`recovery.${side}`)(null);
        return I18nModule.t(`recovery.${side}`)(page.originalPageNumber ?? page.OriginalPageNumber);
    },

    _sheetIndex(sheet) {
        return sheet.sheetIndex ?? sheet.SheetIndex;
    },

    _ensureCopyFilter() {
        const list = document.getElementById('phase1-recovery-list');
        if (!list?.parentNode) return;

        document.getElementById('phase1-recovery-copy-row')?.remove();
        const copies = this._jobCopies();
        if (copies <= 1) return;

        const row = document.createElement('div');
        row.id = 'phase1-recovery-copy-row';
        row.className = 'recovery-copy-row';

        const label = document.createElement('label');
        label.htmlFor = 'phase1-recovery-copy';
        label.textContent = I18nModule.t('recovery.copyLabel');

        const select = document.createElement('select');
        select.id = 'phase1-recovery-copy';
        select.className = 'recovery-copy-select';
        for (let copy = 1; copy <= copies; copy++) {
            const option = document.createElement('option');
            option.value = String(copy);
            option.textContent = I18nModule.t('recovery.copyOption')(copy);
            select.appendChild(option);
        }
        select.value = String(this._activeCopy);
        select.addEventListener('change', () => {
            this._activeCopy = Math.max(1, parseInt(select.value, 10) || 1);
            this._renderList();
        });

        row.append(label, select);
        list.parentNode.insertBefore(row, list);
    },

    _renderList() {
        const list = document.getElementById('phase1-recovery-list');
        if (!list) return;
        list.textContent = '';
        this._ensureCopyFilter();

        const copies = this._jobCopies();
        const copyNumber = Math.min(this._activeCopy, copies);

        for (const sheet of this._sheets) {
            const idx = this._sheetIndex(sheet);
            const front = sheet.front || sheet.Front;
            const back = sheet.back || sheet.Back;

            const label = document.createElement('label');
            label.className = 'recovery-sheet-item';

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.dataset.sheetIndex = String(idx);
            checkbox.dataset.copyNumber = String(copyNumber);
            checkbox.addEventListener('change', () => this._updateSelectedStatus());

            const body = document.createElement('div');
            const title = document.createElement('div');
            title.className = 'recovery-sheet-title';
            title.textContent = copies > 1
                ? I18nModule.t('recovery.copySheet')(copyNumber, idx)
                : I18nModule.t('recovery.sheet')(idx);

            const meta = document.createElement('div');
            meta.className = 'recovery-sheet-meta';

            const frontSpan = document.createElement('span');
            frontSpan.textContent = this._pageLabel(front, 'front');

            const backSpan = document.createElement('span');
            backSpan.textContent = this._pageLabel(back, 'back');

            meta.append(frontSpan, backSpan);
            body.append(title, meta);
            label.append(checkbox, body);
            list.appendChild(label);
        }
    },

    _parseRange(text) {
        const selected = new Set();
        for (const raw of String(text || '').split(',')) {
            const part = raw.trim();
            if (!part) continue;
            const match = part.match(/^(\d+)(?:\s*-\s*(\d+))?$/);
            if (!match) continue;
            const start = parseInt(match[1], 10);
            const end = parseInt(match[2] || match[1], 10);
            const lo = Math.min(start, end);
            const hi = Math.max(start, end);
            for (let i = lo; i <= hi; i++) selected.add(i);
        }
        return selected;
    },

    _applyRange() {
        const selected = this._parseRange(document.getElementById('phase1-recovery-range')?.value);
        document.querySelectorAll('#phase1-recovery-list input[type="checkbox"]').forEach(cb => {
            cb.checked = selected.has(parseInt(cb.dataset.sheetIndex, 10));
        });
        this._updateSelectedStatus();
    },

    _clearSelection() {
        document.querySelectorAll('#phase1-recovery-list input[type="checkbox"]').forEach(cb => { cb.checked = false; });
        this._updateSelectedStatus();
    },

    _selectedSheetIndices() {
        return Array.from(document.querySelectorAll('#phase1-recovery-list input[type="checkbox"]:checked'))
            .map(cb => parseInt(cb.dataset.sheetIndex, 10))
            .filter(Number.isFinite);
    },

    _updateSelectedStatus() {
        const count = this._selectedSheetIndices().length;
        this._setStatus(count > 0 ? I18nModule.t('recovery.selected')(count) : '');
    },

    _setStatus(text) {
        const el = document.getElementById('phase1-recovery-status');
        if (el) el.textContent = text || '';
    },

    _updateSubmitLabel() {
        const submit = document.getElementById('phase1-recovery-submit');
        if (!submit) return;
        submit.textContent = this._printAttempts > 0
            ? I18nModule.t('recovery.retrySubmit')
            : I18nModule.t('recovery.submit');
    },

    async _submit() {
        const sheetIndices = this._selectedSheetIndices();
        if (sheetIndices.length === 0) {
            this._setStatus(I18nModule.t('recovery.noSelection'));
            return;
        }

        const jobId = AppState.currentJob?.jobId || AppState.currentJob?.JobId;
        if (!jobId) {
            showToast(I18nModule.t('recovery.noPlan'), 'error');
            return;
        }

        const submit = document.getElementById('phase1-recovery-submit');
        if (submit) submit.disabled = true;
        try {
            const res = await fetch(`${API_BASE}/print/recover/front`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ jobId, sheetIndices })
            });
            const result = await res.json();
            if (!res.ok || !result.success) throw new Error(result.message || res.statusText);

            const message = I18nModule.t('recovery.printed')(result.printedSheets || sheetIndices.length);
            this._printAttempts++;
            this._updateSubmitLabel();
            this._setStatus(`${message} ${I18nModule.t('recovery.retryHint')}`);
            showToast(message, 'success');
        } catch (err) {
            const message = I18nModule.t('recovery.failed')(err.message);
            this._setStatus(message);
            showToast(message, 'error');
        } finally {
            if (submit) submit.disabled = false;
        }
    },
};

// ═══════════════════════════════════════════════════════════════════
// Phase2RecoveryModule — Replace damaged sheets after the back pass
// ═══════════════════════════════════════════════════════════════════
const Phase2RecoveryModule = {
    _rows: [],
    _baseRows: [],
    _activeCopy: 1,
    _lastSheetIndices: [],
    _frontPrintAttempts: 0,

    init() {
        document.getElementById('phase2-recovery-close')?.addEventListener('click', () => this.close());
        document.getElementById('phase2-recovery-overlay')?.addEventListener('click', () => this.close());
        document.getElementById('phase2-recovery-apply-range')?.addEventListener('click', () => this._applyRange());
        document.getElementById('phase2-recovery-clear')?.addEventListener('click', () => this._clearSelection());
        document.getElementById('phase2-recovery-submit')?.addEventListener('click', () => this._startRecovery());
        document.getElementById('phase2-recovery-retry-fronts')?.addEventListener('click', () => this._retryRecoveryFronts());
        document.getElementById('phase2-recovery-continue')?.addEventListener('click', () => this._continueRecovery());
    },

    openRecovery() {
        this._baseRows = this._getPhase2Rows();
        this._activeCopy = Math.min(this._activeCopy, this._jobCopies());
        this._rows = this._visibleRows();
        this._lastSheetIndices = [];
        this._frontPrintAttempts = 0;
        this._renderList();
        const range = document.getElementById('phase2-recovery-range');
        if (range) range.value = '';
        document.getElementById('phase2-recovery-modal')?.classList.remove('hidden');
        this._setStatus('');
        this._showPanel('select');
        range?.focus();
    },

    close() {
        document.getElementById('phase2-recovery-modal')?.classList.add('hidden');
    },

    _showPanel(name) {
        document.getElementById('phase2-select-panel')?.classList.toggle('hidden', name !== 'select');
        document.getElementById('phase2-flip-panel')?.classList.toggle('hidden', name !== 'flip');
    },

    _jobCopies() {
        const copies = AppState.currentJob?.copies ?? AppState.currentJob?.Copies ?? 1;
        return Math.max(1, parseInt(copies, 10) || 1);
    },

    _getPhase2Rows() {
        const job = AppState.currentJob;
        const plan = job?.manualPlan || job?.ManualPlan;
        const sheets = plan?.sheets || plan?.Sheets || [];
        const phase2Pages = plan?.phase2Pages || plan?.Phase2Pages || [];
        const byBackPage = new Map();

        for (const sheet of sheets) {
            const back = sheet.back || sheet.Back;
            const idx = back?.processedIndex ?? back?.ProcessedIndex;
            if (Number.isFinite(idx)) byBackPage.set(idx, sheet);
        }

        return phase2Pages
            .map((processedPage, i) => {
                const sheet = byBackPage.get(processedPage);
                if (!sheet) return null;
                return { passIndex: i + 1, processedPage, sheet };
            })
            .filter(Boolean)
            .sort((a, b) => this._sheetIndex(a.sheet) - this._sheetIndex(b.sheet));
    },

    _visibleRows() {
        const copies = this._jobCopies();
        const copyNumber = Math.min(this._activeCopy, copies);
        return this._baseRows.map(row => ({ ...row, copyNumber }));
    },

    _sheetIndex(sheet) {
        return sheet.sheetIndex ?? sheet.SheetIndex;
    },

    _pageLabel(page, side) {
        if (!page || page.isBlank || page.IsBlank) return I18nModule.t(`recovery.${side}`)(null);
        return I18nModule.t(`recovery.${side}`)(page.originalPageNumber ?? page.OriginalPageNumber);
    },

    _ensureCopyFilter() {
        const list = document.getElementById('phase2-recovery-list');
        if (!list?.parentNode) return;

        document.getElementById('phase2-recovery-copy-row')?.remove();
        const copies = this._jobCopies();
        if (copies <= 1) return;

        const row = document.createElement('div');
        row.id = 'phase2-recovery-copy-row';
        row.className = 'recovery-copy-row';

        const label = document.createElement('label');
        label.htmlFor = 'phase2-recovery-copy';
        label.textContent = I18nModule.t('recovery.copyLabel');

        const select = document.createElement('select');
        select.id = 'phase2-recovery-copy';
        select.className = 'recovery-copy-select';
        for (let copy = 1; copy <= copies; copy++) {
            const option = document.createElement('option');
            option.value = String(copy);
            option.textContent = I18nModule.t('recovery.copyOption')(copy);
            select.appendChild(option);
        }
        select.value = String(this._activeCopy);
        select.addEventListener('change', () => {
            this._activeCopy = Math.max(1, parseInt(select.value, 10) || 1);
            this._rows = this._visibleRows();
            this._renderList();
        });

        row.append(label, select);
        list.parentNode.insertBefore(row, list);
    },

    _renderList() {
        const list = document.getElementById('phase2-recovery-list');
        if (!list) return;
        list.textContent = '';
        this._ensureCopyFilter();

        for (const row of this._rows) {
            const sheet = row.sheet;
            const idx = this._sheetIndex(sheet);
            const front = sheet.front || sheet.Front;
            const back = sheet.back || sheet.Back;

            const label = document.createElement('label');
            label.className = 'recovery-sheet-item';

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.dataset.sheetIndex = String(idx);
            checkbox.dataset.passIndex = String(row.passIndex);
            checkbox.dataset.copyNumber = String(row.copyNumber || 1);
            checkbox.addEventListener('change', () => this._updateSelectedStatus());

            const body = document.createElement('div');
            const title = document.createElement('div');
            title.className = 'recovery-sheet-title';
            title.textContent = this._jobCopies() > 1
                ? I18nModule.t('phase2Recovery.passCopySheet')(row.copyNumber, idx)
                : I18nModule.t('phase2Recovery.passSheet')(row.passIndex, idx);

            const meta = document.createElement('div');
            meta.className = 'recovery-sheet-meta';

            const frontSpan = document.createElement('span');
            frontSpan.textContent = this._pageLabel(front, 'front');

            const backSpan = document.createElement('span');
            backSpan.textContent = this._pageLabel(back, 'back');

            meta.append(frontSpan, backSpan);
            body.append(title, meta);
            label.append(checkbox, body);
            list.appendChild(label);
        }
    },

    _parseRange(text) {
        const selected = new Set();
        for (const raw of String(text || '').split(',')) {
            const part = raw.trim().replace(/^[^\d]+(?=\d)/u, '');
            if (!part) continue;
            const match = part.match(/^(\d+)(?:\s*-\s*(\d+))?$/);
            if (!match) continue;
            const start = parseInt(match[1], 10);
            const end = parseInt(match[2] || match[1], 10);
            const lo = Math.min(start, end);
            const hi = Math.max(start, end);
            for (let i = lo; i <= hi; i++) selected.add(i);
        }
        return selected;
    },

    _applyRange() {
        const selected = this._parseRange(document.getElementById('phase2-recovery-range')?.value);
        let checkedCount = 0;
        document.querySelectorAll('#phase2-recovery-list input[type="checkbox"]').forEach(cb => {
            cb.checked = selected.has(parseInt(cb.dataset.sheetIndex, 10));
            if (cb.checked) checkedCount++;
        });
        if (selected.size > 0 && checkedCount === 0) this._setStatus(I18nModule.t('phase2Recovery.noMatchingSheets'));
        else this._updateSelectedStatus();
    },

    _clearSelection() {
        document.querySelectorAll('#phase2-recovery-list input[type="checkbox"]').forEach(cb => { cb.checked = false; });
        this._updateSelectedStatus();
    },

    _selectedSheetIndices() {
        return Array.from(document.querySelectorAll('#phase2-recovery-list input[type="checkbox"]:checked'))
            .map(cb => parseInt(cb.dataset.sheetIndex, 10))
            .filter(Number.isFinite);
    },

    _updateSelectedStatus() {
        const count = this._selectedSheetIndices().length;
        this._setStatus(count > 0 ? I18nModule.t('recovery.selected')(count) : '');
    },

    _setStatus(text) {
        const el = document.getElementById('phase2-recovery-status');
        if (el) el.textContent = text || '';
    },

    async _startRecovery(sheetIndicesOverride = null) {
        const sheetIndices = Array.isArray(sheetIndicesOverride) ? sheetIndicesOverride : this._selectedSheetIndices();
        if (sheetIndices.length === 0) {
            this._setStatus(I18nModule.t('recovery.noSelection'));
            return;
        }

        const jobId = AppState.currentJob?.jobId || AppState.currentJob?.JobId;
        if (!jobId) {
            showToast(I18nModule.t('recovery.noPlan'), 'error');
            return;
        }

        const submit = document.getElementById('phase2-recovery-submit');
        if (submit) submit.disabled = true;
        try {
            const res = await fetch(`${API_BASE}/print/recover/back/start`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ jobId, sheetIndices })
            });
            const result = await res.json();
            if (!res.ok || !result.success) throw new Error(result.message || res.statusText);
            if (result.jobState) {
                const histEntry = AppState.currentJob?._historyEntry;
                AppState.currentJob = result.jobState;
                AppState.currentJob._historyEntry = histEntry;
                PrintModule._setRecoveryContext(result.jobState);
            }
            this._lastSheetIndices = sheetIndices.slice();
            this._frontPrintAttempts++;

            const message = this._frontPrintAttempts > 1
                ? I18nModule.t('phase2Recovery.frontRetried')(result.printedSheets || sheetIndices.length)
                : I18nModule.t('phase2Recovery.frontPrinted')(result.printedSheets || sheetIndices.length);
            this._setStatus(message);
            showToast(message, 'success');
            if (result.waitingForRecoveryFlip) this._showPanel('flip');
        } catch (err) {
            const message = I18nModule.t('recovery.failed')(err.message);
            this._setStatus(message);
            showToast(message, 'error');
        } finally {
            if (submit) submit.disabled = false;
        }
    },

    async _retryRecoveryFronts() {
        const sheetIndices = this._lastSheetIndices.length > 0
            ? this._lastSheetIndices
            : this._selectedSheetIndices();
        if (sheetIndices.length === 0) {
            this._setStatus(I18nModule.t('recovery.noSelection'));
            return;
        }

        const btn = document.getElementById('phase2-recovery-retry-fronts');
        if (btn) btn.disabled = true;
        try {
            await this._startRecovery(sheetIndices);
        } finally {
            if (btn) btn.disabled = false;
        }
    },

    async _continueRecovery() {
        const jobId = AppState.currentJob?.jobId || AppState.currentJob?.JobId;
        if (!jobId) return;

        const btn = document.getElementById('phase2-recovery-continue');
        if (btn) btn.disabled = true;
        try {
            const res = await fetch(`${API_BASE}/print/recover/back/continue?jobId=${jobId}`, { method: 'POST' });
            const result = await res.json();
            if (!res.ok || !result.success) throw new Error(result.message || res.statusText);
            if (result.jobState) {
                const histEntry = AppState.currentJob?._historyEntry;
                AppState.currentJob = result.jobState;
                AppState.currentJob._historyEntry = histEntry;
                PrintModule._setRecoveryContext(result.jobState);
            }

            const message = I18nModule.t('phase2Recovery.backPrinted')(result.printedSheets || 0);
            this._setStatus(message);
            showToast(message, 'success');
            this.close();
            PrintModule._updateRecoveryButton();
        } catch (err) {
            const message = I18nModule.t('recovery.failed')(err.message);
            this._setStatus(message);
            showToast(message, 'error');
        } finally {
            if (btn) btn.disabled = false;
        }
    },

};

// ═══════════════════════════════════════════════════════════════════
// KeyboardModule — Global keyboard shortcuts
// ═══════════════════════════════════════════════════════════════════
const KeyboardModule = {
    init() {
        document.addEventListener('keydown', (e) => {
            // Skip if typing in an input
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

            if (e.ctrlKey && e.key === 'p') {
                e.preventDefault();
                // In app-shell layout, Ctrl+P triggers print directly if ready
                const btn = document.getElementById('print-btn');
                if (btn && !btn.disabled) btn.click();
            }

            if (e.ctrlKey && e.key === 'o') {
                e.preventDefault();
                document.getElementById('file-input')?.click();
            }

            if (e.key === 'Escape') {
                document.getElementById('page-zoom-modal')?.classList.add('hidden');
                document.getElementById('flip-modal')?.classList.add('hidden');
                if (PrintPreviewModule._isOpen) { PrintPreviewModule.close(); return; }
                ContextMenu.hide();
            }

            if (e.key === 'a' && !e.ctrlKey) {
                AppState.selectAllPages();
                PreviewModule.updateThumbnails();
                PageSelectModule.updateDisplay();
                PrintModule.updateButton();
            }

            // Arrow key navigation for thumbnail grid (B)
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                const grid = document.getElementById('sidebar-preview-grid');
                if (!grid) return;
                const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail'));
                if (!thumbs.length) return;
                const focused = document.activeElement;
                const idx = thumbs.indexOf(focused);
                if (idx === -1) {
                    thumbs[0].focus();
                    return;
                }
                e.preventDefault();
                const next = e.key === 'ArrowDown'
                    ? thumbs[Math.min(idx + 1, thumbs.length - 1)]
                    : thumbs[Math.max(idx - 1, 0)];
                next.focus();
                next.scrollIntoView({ block: 'nearest' });
            }

            // Space/Enter to toggle selection when thumbnail is focused (B)
            if ((e.key === ' ' || e.key === 'Enter') && document.activeElement?.classList.contains('page-thumbnail')) {
                e.preventDefault();
                const n = parseInt(document.activeElement.dataset.pageNumber);
                if (!isNaN(n)) PageSelectModule.toggle(n);
            }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// SummaryModule — Calculate and display print summary
// ═══════════════════════════════════════════════════════════════════
const SummaryModule = {
    update() {
        const el = document.getElementById('print-summary');
        if (!el) return;

        const mode    = AppState.printMode || 'duplex'; // B26-FE-1: read from AppState (source of truth); DOM may be stale after reset()
        const printer = AppState.selectedPrinter;

        // Collect all files that have pages selected
        const activeFiles = AppState.files.filter(f => f.selectedPages.size > 0);

        const multiFile = activeFiles.length > 1;

        // Aggregate pages and sheets across all active files
        let totalPages = 0;
        let totalSheets = 0;
        for (const f of activeFiles) {
            const fPages = f.selectedPages.size;
            const fCopies = f.copies ?? 1;
            let fSheets;
            if (mode === 'booklet') {
                fSheets = Math.ceil(fPages / 4) * fCopies;
            } else {
                // B13-FE-6: use real layout logic (matches orientation grouping + padding)
                // instead of simplified Math.ceil(double/2)+single formula which undercounts
                // mixed-orientation docs (each orientation group gets padded to even count).
                const layout = buildSheetLayout(f, mode, null, f.landscapeMode ?? 'together');
                fSheets = layout.sheets.length * fCopies;
            }
            totalPages  += fPages;
            totalSheets += fSheets;
        }

        // Copies label: show only if single file (multi-file each has own copies)
        const copies = activeFiles.length === 1 ? (activeFiles[0].copies ?? 1) : null;

        // Estimate time: ~15s per sheet (realistic for manual duplex + processing)
        const totalSec = totalSheets * 15;
        const timeStr = totalSec < 60
            ? I18nModule.t('summary.lessThanMinute')
            : totalSec < 3600
                ? I18nModule.t('summary.minutes')(Math.ceil(totalSec / 60))
                : I18nModule.t('summary.hoursMinutes')(Math.floor(totalSec / 3600), Math.ceil((totalSec % 3600) / 60));

        el.classList.remove('hidden');
        el.innerHTML = `
            <span>${I18nModule.t('summary.pages')(totalPages)}</span>
            <span>·</span>
            <span>${I18nModule.t('summary.sheets')(totalSheets)}</span>
            <span>·</span>
            <span>⏱ ${timeStr}</span>
            ${copies !== null && copies > 1 ? `<span>${I18nModule.t('summary.copies')(copies)}</span>` : ''}
            ${multiFile ? `<span>${I18nModule.t('summary.files')(activeFiles.length)}</span>` : ''}
            ${printer ? `<span>· 🖨️ ${escapeHtml(printer.name)}</span>` : ''}
        `;
    },
};

// ═══════════════════════════════════════════════════════════════════
// SRModule — Screen reader announcements via aria-live (C)
// ═══════════════════════════════════════════════════════════════════
const SRModule = {
    _timer: null,
    announce(msg) {
        const el = document.getElementById('sr-status');
        if (!el) return;
        el.textContent = '';
        clearTimeout(this._timer);
        // Brief delay to ensure screen reader picks up the change
        this._timer = setTimeout(() => {
            el.textContent = msg;
            this._timer = setTimeout(() => { el.textContent = ''; }, 1500);
        }, 50);
    },
};

// ═══════════════════════════════════════════════════════════════════
// StepIndicatorModule — Progress indicator across 5 workflow steps (2)
// ═══════════════════════════════════════════════════════════════════
const StepIndicatorModule = {
    update() {
        const step1done = !!AppState.selectedPrinter;
        const step2done = !!AppState.uploadedFile;
        const step3done = AppState.totalPageCount > 0;
        const step4done = step3done; // always available once file loaded
        const step5done = false; // never "done" — it's the action

        // Determine current active step
        let activeStep;
        if (!step1done) activeStep = 1;
        else if (!step2done) activeStep = 2;
        else if (!step3done) activeStep = 3;
        else activeStep = 5;

        const doneSteps = [
            step1done,
            step2done,
            step3done,
            step4done,
            step5done,
        ];

        for (let i = 1; i <= 5; i++) {
            const item = document.getElementById(`step-ind-${i}`);
            if (!item) continue;
            const isDone   = doneSteps[i - 1] && i < activeStep;
            const isActive = i === activeStep;
            item.classList.toggle('done',   isDone && !isActive);
            item.classList.toggle('active', isActive);
        }

        // Update connecting lines
        for (let i = 1; i <= 4; i++) {
            const line = document.getElementById(`step-line-${i}`);
            if (!line) continue;
            line.classList.toggle('done', doneSteps[i - 1] && activeStep > i);
        }
    },
};

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
        <span class="error-text">${escapeHtml(message)}</span>
        ${retryFn ? `<button class="card-error-retry">${I18nModule.t('error.retry')}</button>` : ''}
    `;
    if (retryFn) {
        errEl.querySelector('.card-error-retry').addEventListener('click', () => {
            clearCardError(cardEl);
            retryFn();
        });
    }
    const body = cardEl.querySelector('.card-body') || cardEl;
    body.appendChild(errEl);
}

function clearCardError(cardEl) {
    if (!cardEl) return;
    cardEl.classList.remove('error');
    cardEl.querySelector('.card-error-message')?.remove();
}

// ═══════════════════════════════════════════════════════════════════
// ConfirmPrintModal — Summary before sending print job (7)
// ═══════════════════════════════════════════════════════════════════
const ConfirmPrintModal = {
    _resolve: null,

    init() {
        document.getElementById('confirm-modal-close')?.addEventListener('click',  () => this._close(false));
        document.getElementById('confirm-modal-overlay')?.addEventListener('click', () => this._close(false));
        document.getElementById('confirm-print-cancel-btn')?.addEventListener('click', () => this._close(false));
        document.getElementById('confirm-print-ok-btn')?.addEventListener('click',    () => this._close(true));

        document.addEventListener('keydown', e => {
            const modal = document.getElementById('confirm-print-modal');
            if (modal?.classList.contains('hidden')) return;
            if (e.key === 'Escape') this._close(false);
            if (e.key === 'Enter')  { e.preventDefault(); this._close(true); }
        });
    },

    // Returns a Promise<boolean>: true if user confirmed, false if cancelled
    show() {
        return new Promise(resolve => {
            this._resolve = resolve;
            this._populate();
            document.getElementById('confirm-print-modal')?.classList.remove('hidden');
            document.getElementById('confirm-print-ok-btn')?.focus();
        });
    },

    _close(confirmed) {
        document.getElementById('confirm-print-modal')?.classList.add('hidden');
        if (this._resolve) { this._resolve(confirmed); this._resolve = null; }
    },

    _populate() {
        const container = document.getElementById('confirm-print-summary');
        if (!container) return;

        const mode    = AppState.printMode || 'duplex'; // B26-FE-2: read from AppState not DOM
        const printer = AppState.selectedPrinter;
        const modeLabel = {
            duplex: I18nModule.t('mode.smart'),
            normal: I18nModule.t('mode.smart'),
            booklet: I18nModule.t('mode.bookletFull'),
        };
        const filesToPrint = AppState.files.filter(f => f.selectedPages.size > 0);
        if (filesToPrint.length === 0) {
            container.innerHTML = `<div class="confirm-row"><span>${I18nModule.t('confirm.noPages')}</span></div>`;
            return;
        }
        const multiFile = filesToPrint.length > 1;

        // Aggregate totals across ALL files to be printed (not just active file)
        let totalSheets = 0;
        let totalPages  = 0;
        for (const f of filesToPrint) {
            const fPages = f.selectedPages.size;
            const fCopies = f.copies ?? 1;
            // Use real layout logic to match what buildSheetLayout actually produces.
            // Orientation grouping + per-group padding in duplex mode can diverge from
            // the simplified formula, so we derive the count from the layout directly.
            const fLayout = buildSheetLayout(f, mode, null, f.landscapeMode ?? 'together');
            const fSheets = fLayout.sheets.length * fCopies;
            totalSheets += fSheets;
            totalPages  += fPages;
        }
        const pages  = totalPages;
        const sheets = totalSheets;
        const copies = filesToPrint.length === 1 ? (filesToPrint[0]?.copies ?? 1) : null; // null = mixed

        const totalSec = sheets * 15;
        const timeStr  = totalSec < 60
            ? I18nModule.t('summary.lessThanMinute')
            : I18nModule.t('summary.minutes')(Math.ceil(totalSec / 60));

        // File label — use filesToPrint[0].name (not AppState.uploadedFile which proxies activeFile)
        const fileLabel = multiFile
            ? `${filesToPrint.length} file`
            : (filesToPrint[0]?.name || '—');

        // Page range label (only meaningful for single-file)
        const sel = multiFile ? null : Array.from(filesToPrint[0]?.selectedPages ?? []).sort((a,b)=>a-b);
        const rangeStr = multiFile ? I18nModule.t('confirmRow.rangeAllFiles')
            : sel?.length === filesToPrint[0]?.totalPageCount ? I18nModule.t('confirmRow.rangeAll')
            // B31-FE-6 fix: use _formatRange to show "1-5, 8, 10-12" instead of a raw
            // comma-separated list of integers which is unreadable for large selections.
            : (PageSelectModule._formatRange(sel) || I18nModule.t('confirmRow.rangeAll'));

        const copiesStr = copies !== null
            ? I18nModule.t('confirmRow.sheets')(sheets, copies)
            : I18nModule.t('confirmRow.sheetsMixed')(sheets);

        const safeFileLabel = escapeHtml(fileLabel);
        const safePrinterName = escapeHtml(printer?.name || '—');
        const safeModeLabel = escapeHtml(modeLabel[mode] || mode);
        const safePagesValue = escapeHtml(I18nModule.t('confirmRow.pages')(pages, rangeStr));
        const safeCopiesStr = escapeHtml(copiesStr);
        const safeTimeStr = escapeHtml(timeStr);

        container.innerHTML = `
            <div class="confirm-row">
                <span class="confirm-row-icon">📄</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.file')}</span>
                <span class="confirm-row-value">${safeFileLabel}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">🖨️</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.printer')}</span>
                <span class="confirm-row-value">${safePrinterName}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📋</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.mode')}</span>
                <span class="confirm-row-value">${safeModeLabel}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📖</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.pagesLabel')}</span>
                <span class="confirm-row-value">${safePagesValue}</span>
            </div>
            <div class="confirm-row highlight">
                <span class="confirm-row-icon">🗒️</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.sheetsLabel')}</span>
                <span class="confirm-row-value">${safeCopiesStr}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">⏱️</span>
                <span class="confirm-row-label">${I18nModule.t('confirmRow.time')}</span>
                <span class="confirm-row-value">${safeTimeStr}</span>
            </div>
        `;
    },
};

// ═══════════════════════════════════════════════════════════════════
// HoverPreviewModule — Hover popup with larger page preview (J)
// ═══════════════════════════════════════════════════════════════════
const HoverPreviewModule = {
    SHOW_DELAY:    150, // ms before showing
    PREVIEW_W:     280,
    PREVIEW_H:     360,
    _timer:        null,
    _activeThumb:  null,
    _cache:        new Map(), // pageNum → offscreen canvas

    _preview()  { return document.getElementById('hover-preview'); },
    _pCanvas()  { return document.getElementById('hover-preview-canvas'); },

    attach(thumb, pageNum) {
        thumb.addEventListener('mouseenter', () => {
            clearTimeout(this._timer);
            this._timer = setTimeout(() => this._show(thumb, pageNum), this.SHOW_DELAY);
        });
        thumb.addEventListener('mouseleave', () => {
            clearTimeout(this._timer);
            this._hide();
        });
        // Keyboard
        thumb.addEventListener('focus', () => this._show(thumb, pageNum));
        thumb.addEventListener('blur',  () => this._hide());
    },

    async _show(thumb, pageNum) {
        if (!AppState.currentPdfDoc) return;
        const preview = this._preview();
        const pCanvas = this._pCanvas();
        if (!preview || !pCanvas) return;

        const rotation = AppState.pageRotations.get(pageNum) ?? null;
        const cacheKey = `${pageNum}-${rotation ?? '0'}`;

        // Render or use cached (cache key includes rotation)
        if (!this._cache.has(cacheKey)) {
            try {
                const page     = await AppState.currentPdfDoc.getPage(pageNum);
                const viewport = RotationHelper.viewport(page, 1.0, rotation);
                const scale    = Math.min(this.PREVIEW_W / viewport.width, this.PREVIEW_H / viewport.height);
                const vp2      = RotationHelper.viewport(page, scale, rotation);
                const off      = document.createElement('canvas');
                off.width      = vp2.width;
                off.height     = vp2.height;
                await page.render({ canvasContext: off.getContext('2d'), viewport: vp2 }).promise;
                this._cache.set(cacheKey, off);
            } catch { return; }
        }

        const cached = this._cache.get(cacheKey);
        pCanvas.width  = cached.width;
        pCanvas.height = cached.height;
        pCanvas.getContext('2d').drawImage(cached, 0, 0);
        // CSS flip only (viewport handles CW/CCW/180)
        pCanvas.style.transform = RotationHelper.toCSS(rotation);

        // Position
        const rect = thumb.getBoundingClientRect();
        this._position(preview, rect);

        preview.hidden = false;
        this._activeThumb = thumb;
        requestAnimationFrame(() => preview.classList.add('visible'));
    },

    _hide() {
        const preview = this._preview();
        if (!preview) return;
        preview.classList.remove('visible');
        this._activeThumb = null;
        // Hide after transition
        setTimeout(() => { if (!preview.classList.contains('visible')) preview.hidden = true; }, 130);
    },

    _position(preview, anchorRect) {
        const W = this.PREVIEW_W + 20; // approx width + gap
        const H = this.PREVIEW_H + 20;
        let left = anchorRect.right + 12;
        let top  = anchorRect.top + (anchorRect.height / 2) - (H / 2);

        // Flip left if would overflow right
        if (left + W > window.innerWidth) left = anchorRect.left - W - 4;
        // Clamp vertically
        top = Math.max(8, Math.min(top, window.innerHeight - H - 8));

        preview.style.left = Math.round(left) + 'px';
        preview.style.top  = Math.round(top)  + 'px';
    },

    clearCache() { this._cache.clear(); },
};

// ═══════════════════════════════════════════════════════════════════
// DragReorderModule — Pointer-event drag to reorder page thumbnails (I)
// ═══════════════════════════════════════════════════════════════════
const DragReorderModule = {
    _ghost:        null,
    _dragging:     null,
    _placeholder:  null,
    _startY:       0,
    _startX:       0,
    _dragPageNum:  null,
    _dragStarted:  false,
    _ghostOffsetX: 0,
    _ghostOffsetY: 0,

    init() {
        // Bind lazily — called from PrintPreviewModule after grid is rendered
    },

    bindGrid(grid) {
        grid.addEventListener('pointerdown', e => this._onDown(e));
    },

    _onDown(e) {
        const thumb = e.target.closest('.preview-thumb-item');
        if (!thumb) return;
        if (e.button !== 0) return;

        this._dragging    = thumb;
        this._dragPageNum = parseInt(thumb.dataset.pageNumber);
        this._startY      = e.clientY;
        this._startX      = e.clientX;
        this._dragStarted = false;

        const rect = thumb.getBoundingClientRect();
        this._ghostOffsetX = e.clientX - rect.left;
        this._ghostOffsetY = e.clientY - rect.top;

        document.addEventListener('pointermove', this._onMove = e => this._move(e));
        document.addEventListener('pointerup',   this._onUp   = e => this._drop(e));
    },

    _createGhost() {
        const rect = this._dragging.getBoundingClientRect();
        this._ghost = this._dragging.cloneNode(true);
        this._ghost.className = 'drag-ghost';
        this._ghost.style.cssText = [
            `width: ${rect.width}px`,
            `height: ${rect.height}px`,
            `left: ${rect.left}px`,
            `top: ${rect.top}px`,
            'position: fixed',
            'z-index: 9999',
            'pointer-events: none',
            'opacity: 0.85',
            'box-shadow: 0 8px 32px rgba(0,0,0,0.4)',
            'border-radius: 8px',
            'transition: none',
        ].join(';');
        document.body.appendChild(this._ghost);
    },

    _move(e) {
        if (!this._dragging) return;

        // Threshold check — only start drag after 6px movement
        if (!this._dragStarted) {
            const dy = Math.abs(e.clientY - this._startY);
            const dx = Math.abs(e.clientX - this._startX);
            if (dy < 6 && dx < 6) return;
            this._dragStarted = true;
            this._createGhost();
            this._dragging.classList.add('dragging');
        }

        if (!this._ghost) return;

        // Move ghost to follow cursor exactly
        this._ghost.style.top  = (e.clientY - this._ghostOffsetY) + 'px';
        this._ghost.style.left = (e.clientX - this._ghostOffsetX) + 'px';

        // Find drop target
        const grid   = document.getElementById('preview-thumb-grid');
        const thumbs = Array.from(grid.querySelectorAll('.preview-thumb-item:not(.dragging)'));
        const y      = e.clientY;

        // Remove old placeholder
        this._placeholder?.remove();
        this._placeholder = null;

        // Find insertion point
        let insertBefore = null;
        for (const t of thumbs) {
            const r = t.getBoundingClientRect();
            if (y < r.top + r.height / 2) { insertBefore = t; break; }
        }

        this._placeholder = document.createElement('div');
        this._placeholder.className = 'drop-placeholder';
        if (insertBefore) {
            grid.insertBefore(this._placeholder, insertBefore);
        } else {
            grid.appendChild(this._placeholder);
        }
    },

    _drop(e) {
        document.removeEventListener('pointermove', this._onMove);
        document.removeEventListener('pointerup',   this._onUp);

        if (this._ghost)    { this._ghost.remove();    this._ghost = null; }
        if (this._dragging) this._dragging.classList.remove('dragging');

        // If user never crossed threshold — it was just a click, do nothing
        if (!this._dragStarted) {
            this._placeholder?.remove();
            this._placeholder = null;
            this._dragging    = null;
            this._dragPageNum = null;
            this._dragStarted = false;
            return;
        }

        if (this._placeholder) {
            // Reorder AppState.pageOrder
            const grid = document.getElementById('preview-thumb-grid');

            // Compute new order from DOM after inserting dragging before placeholder
            const newOrder = [];
            let placed = false;
            for (const t of grid.childNodes) {
                if (t === this._placeholder) {
                    if (!placed) { newOrder.push(this._dragPageNum); placed = true; }
                } else if (t.classList?.contains('preview-thumb-item') && t !== this._dragging) {
                    newOrder.push(parseInt(t.dataset.pageNumber));
                }
            }
            if (!placed) newOrder.push(this._dragPageNum);

            AppState.pageOrder = newOrder;

            // Re-render grid in new order
            this._placeholder.remove();
            this._placeholder = null;
            this._reRenderGrid(newOrder);

            // Also re-order main view pages
            PrintPreviewModule._reorderMainView(newOrder);

            showToast(I18nModule.t('toast.pageReordered'), 'info');
            // Rebuild SheetView if active — pageOrder changed (Invariant 7)
            if (AppState.viewMode === 'sheet' && AppState.activeFile) {
                PreviewPanelModule.render(AppState.activeFile);
            }
        }

        this._dragging    = null;
        this._dragPageNum = null;
        this._placeholder = null;
        this._dragStarted = false;
    },

    _reRenderGrid(order) {
        const grid = document.getElementById('preview-thumb-grid');
        if (!grid) return;
        const thumbs = Array.from(grid.querySelectorAll('.preview-thumb-item'));
        // Use render-index as key — pageNum=0 (blank) may appear multiple times
        // Each thumb consumed only once via _used flag to handle duplicates
        order.forEach(pageNum => {
            const t = thumbs.find(el => parseInt(el.dataset.pageNumber) === pageNum && !el._used);
            if (t) {
                t._used = true;
                grid.appendChild(t);
            }
        });
        thumbs.forEach(t => delete t._used); // cleanup
        PreviewModule.updateThumbnails();
    },
};

// ═══════════════════════════════════════════════════════════════════
// CanvasPool — Reuse offscreen canvases to reduce GC pressure & GPU memory fragmentation
// ═══════════════════════════════════════════════════════════════════
const CanvasPool = {
    _pool: [],
    _MAX_POOL: 10,

    acquire() {
        return this._pool.pop() || document.createElement('canvas');
    },

    release(canvas) {
        if (this._pool.length >= this._MAX_POOL) return; // drop if pool full
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        canvas.width = 0;
        canvas.height = 0;
        this._pool.push(canvas);
    },
};

// LRUBlobCache — Memory-bounded LRU cache for rendered PDF canvases
// Evicts oldest entries when total pixel memory exceeds MAX_BYTES.
// ═══════════════════════════════════════════════════════════════════
class LRUBlobCache {
    #map        = new Map(); // key → { canvas: OffscreenCanvas, url: string }
    #totalBytes = 0;
    #maxBytes;

    constructor(maxMB = 50) {
        this.#maxBytes = maxMB * 1024 * 1024;
    }

    has(key)  { return this.#map.has(key); }
    get size() { return this.#map.size; }

    // Returns { canvas, url } or undefined
    get(key) {
        if (!this.#map.has(key)) return undefined;
        const val = this.#map.get(key);
        this.#map.delete(key);
        this.#map.set(key, val); // move to MRU position
        return val;
    }

    // entry = { canvas: OffscreenCanvas, url: string }
    set(key, entry) {
        const bytes = entry.canvas.width * entry.canvas.height * 4;
        if (this.#map.has(key)) {
            const old = this.#map.get(key);
            this.#totalBytes -= old.canvas.width * old.canvas.height * 4;
            URL.revokeObjectURL(old.url);
            CanvasPool.release(old.canvas);
            this.#map.delete(key);
        }
        while (this.#totalBytes + bytes > this.#maxBytes && this.#map.size > 0) {
            const oldestKey = this.#map.keys().next().value;
            const oldest    = this.#map.get(oldestKey);
            this.#totalBytes -= oldest.canvas.width * oldest.canvas.height * 4;
            URL.revokeObjectURL(oldest.url);
            CanvasPool.release(oldest.canvas);
            this.#map.delete(oldestKey);
        }
        this.#map.set(key, entry);
        this.#totalBytes += bytes;
    }

    delete(key) {
        if (!this.#map.has(key)) return;
        const entry = this.#map.get(key);
        this.#totalBytes -= entry.canvas.width * entry.canvas.height * 4;
        URL.revokeObjectURL(entry.url);
        CanvasPool.release(entry.canvas);
        this.#map.delete(key);
    }

    deleteByPrefix(prefix) {
        for (const k of [...this.#map.keys()]) {
            if (k.startsWith(prefix)) this.delete(k);
        }
    }

    clear() {
        for (const entry of this.#map.values()) {
            URL.revokeObjectURL(entry.url);
            CanvasPool.release(entry.canvas);
        }
        this.#map.clear();
        this.#totalBytes = 0;
    }
}

// ═══════════════════════════════════════════════════════════════════
// Rotation helpers — shared by ThumbStripModule and PreviewPanelModule
// ═══════════════════════════════════════════════════════════════════
const RotationHelper = {
    // Maps rotation key → pdf.js viewport rotation degrees (0/90/180/270)
    // Flips are handled via CSS since pdf.js doesn't support them natively
    toDeg(rotation) {
        return { CW90: 90, CCW90: 270, Rotate180: 180 }[rotation] ?? 0;
    },

    intrinsicDeg(page) {
        const deg = Number(page?.rotate ?? 0);
        return Number.isFinite(deg) ? ((deg % 360) + 360) % 360 : 0;
    },

    effectiveDeg(page, rotation) {
        return (this.intrinsicDeg(page) + this.toDeg(rotation)) % 360;
    },

    viewport(page, scale, rotation = null) {
        return page.getViewport({ scale, rotation: this.effectiveDeg(page, rotation) });
    },

    isLandscape(page, rotation = null) {
        const vp = this.viewport(page, 1, rotation);
        return vp.width > vp.height;
    },

    inkLandscapeScore(imageData, width, height) {
        const rowInk = new Array(height).fill(0);
        const colInk = new Array(width).fill(0);
        const data = imageData.data;

        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                const i = (y * width + x) * 4;
                const alpha = data[i + 3];
                if (alpha === 0) continue;
                const luminance = data[i] * 0.299 + data[i + 1] * 0.587 + data[i + 2] * 0.114;
                if (luminance < 220) {
                    rowInk[y]++;
                    colInk[x]++;
                }
            }
        }

        const rowMin = Math.max(1, width * 0.01);
        const colMin = Math.max(1, height * 0.01);
        const rowRuns = this._inkRuns(rowInk, rowMin);
        const colRuns = this._inkRuns(colInk, colMin);
        const rowEnergy = this._projectionEnergy(rowInk);
        const colEnergy = this._projectionEnergy(colInk);

        if (colRuns >= rowRuns * 1.2 || (colEnergy > 0 && rowEnergy / colEnergy < 0.8)) return 1;
        if (rowRuns >= colRuns * 1.2 || (rowEnergy > 0 && colEnergy / rowEnergy < 0.8)) return -1;
        return 0;
    },

    _inkRuns(values, minInk) {
        let runs = 0;
        let inRun = false;
        for (const value of values) {
            if (value >= minInk) {
                if (!inRun) {
                    runs++;
                    inRun = true;
                }
            } else {
                inRun = false;
            }
        }
        return runs;
    },

    _projectionEnergy(values) {
        let energy = 0;
        for (let i = 1; i < values.length; i++) {
            energy += Math.abs(values[i] - values[i - 1]);
        }
        return energy;
    },

    async detectLandscape(page, rotation = null) {
        if (this.isLandscape(page, rotation)) return true;
        const visual = await this.detectVisualLandscape(page, rotation);
        return visual === true;
    },

    async detectVisualLandscape(page, rotation = null) {
        const base = this.viewport(page, 1, rotation);
        const scale = Math.min(0.25, 180 / Math.max(base.width, base.height));
        if (!Number.isFinite(scale) || scale <= 0) return null;

        const vp = this.viewport(page, scale, rotation);
        const canvas = typeof OffscreenCanvas !== 'undefined'
            ? new OffscreenCanvas(Math.max(1, Math.ceil(vp.width)), Math.max(1, Math.ceil(vp.height)))
            : document.createElement('canvas');
        canvas.width = Math.max(1, Math.ceil(vp.width));
        canvas.height = Math.max(1, Math.ceil(vp.height));

        const ctx = canvas.getContext('2d', { willReadFrequently: true, alpha: false });
        if (!ctx) return null;

        await page.render({ canvasContext: ctx, viewport: vp, intent: 'display' }).promise;
        const score = this.inkLandscapeScore(ctx.getImageData(0, 0, canvas.width, canvas.height), canvas.width, canvas.height);
        return score > 0 ? true : score < 0 ? false : null;
    },

    // CSS transform for flip cases (only applies to canvas element)
    toCSS(rotation) {
        if (rotation === 'FlipHorizontal') return 'scaleX(-1)';
        if (rotation === 'FlipVertical')   return 'scaleY(-1)';
        return '';
    },

    // Human-readable label for badge
    toLabel(rotation) {
        return { CW90: '+90°', CCW90: '-90°', Rotate180: '180°',
                 FlipHorizontal: '↔', FlipVertical: '↕' }[rotation] ?? '';
    },

    // Update or remove the rotation badge on a card/thumb element
    updateBadge(el, rotation) {
        let badge = el.querySelector('.rot-badge');
        if (!rotation) {
            badge?.remove();
            return;
        }
        if (!badge) {
            badge = document.createElement('div');
            badge.className = 'rot-badge';
            el.appendChild(badge);
        }
        badge.textContent = this.toLabel(rotation);
    },
};

// ═══════════════════════════════════════════════════════════════════
// PreviewPanelModule — Persistent right-panel PDF preview
// Replaces the old modal main view.
// ═══════════════════════════════════════════════════════════════════
const PreviewPanelModule = {
    _container:   null,
    _renderTasks: new Map(),           // 'fileId-pageNum-rot-scale' → RenderTask
    _cache:       new LRUBlobCache(50), // 50 MB LRU — evicts oldest pages
    _pageEls:     new Map(),           // 'fileId-pageNum' → .preview-page-card el
    _renderQueue: [],
    _activeRenders: 0,
    _MAX_CONCURRENT: 6,
    _scrollRAF:   false,               // rAF guard for scroll-triggered renders
    _scrollIdleTimer: null,            // fires cleanup after scroll stops
    _scrollVelocity: 0,                // px/sec — for velocity-aware rendering
    _lastScrollY: 0,
    _lastScrollTime: 0,
    _currentFileId: null,
    _viewMode: 'page',
    _sheetEls: new Map(),              // sheetIndex → .sheet-card el
    _isDeleting: false,
    _pageRoots:       new Map(),   // fileId → <div.preview-file-root> for page-view
    _sheetRoots:      new Map(),   // fileId → <div.preview-file-root> for sheet-view
    _sheetFingerprints: new Map(), // fileId → last-rendered sheet layout fingerprint string
    _modeJustToggled: false,       // one-shot flag — set by toggle handler, cleared by every render path
    _sheetRenderGen: 0,            // 0 = no pending async sheet rebuild; non-zero = active rebuild token
    _nextSheetRenderGen: 0,        // monotonically increasing; each full-rebuild gets a unique token

    init() {
        this._container = document.getElementById('preview-panel');
        this._container?.addEventListener('scroll', () => {
            // Track velocity for smart rendering
            const now = performance.now();
            const scrollY = this._container.scrollTop;
            const dt = now - (this._lastScrollTime || now);
            if (dt > 0) this._scrollVelocity = Math.abs(scrollY - this._lastScrollY) / dt * 1000;
            this._lastScrollY = scrollY;
            this._lastScrollTime = now;

            this._onScroll();

            // rAF-based render (replaces setTimeout debounce)
            if (!this._scrollRAF) {
                this._scrollRAF = true;
                requestAnimationFrame(() => {
                    this._scrollRAF = false;
                    // Skip rendering if scrolling too fast (>3000px/s)
                    if (this._scrollVelocity < 3000) {
                        this._renderVisible();
                    }
                });
            }

            // After scroll stops: render anything missed + unmount off-screen
            clearTimeout(this._scrollIdleTimer);
            this._scrollIdleTimer = setTimeout(() => {
                this._scrollVelocity = 0;
                this._renderVisible();
                this._unmountOffScreen();
            }, 200);
        }, { passive: true });
    },

    _getOrCreatePageRoot(fileEntry) {
        let root = this._pageRoots.get(fileEntry.id);
        if (root) {
            // Re-attach if sheet-view's innerHTML='' detached this root from the container
            if (!this._container.contains(root)) {
                this._container.appendChild(root);
            }
            return { root, isNew: false };
        }
        root = document.createElement('div');
        root.className = 'preview-file-root';
        root.dataset.fileId = fileEntry.id;
        this._pageRoots.set(fileEntry.id, root);
        this._container.appendChild(root);
        return { root, isNew: true };
    },

    _getOrCreateSheetRoot(fileEntry) {
        let root = this._sheetRoots.get(fileEntry.id);
        if (!root) {
            root = document.createElement('div');
            root.className = 'preview-file-root preview-file-root--hidden';
            root.dataset.fileId = fileEntry.id;
            root.dataset.viewMode = 'sheet';
            this._sheetRoots.set(fileEntry.id, root);
            this._container.appendChild(root);
        }
        return root;
    },

    // Render all pages of active file
    render(fileEntry) {
        // Invariant 3: reset blankAbsorbedBy before each rebuild
        if (fileEntry) fileEntry.blankAbsorbedBy = new Map();

        // §5.0: Teardown together-mode CCW90 when leaving sheet view.
        // Loop ALL files (not just fileEntry) — non-active files can also hold stale
        // CCW90 from a previous together-mode render.
        if (this._viewMode !== 'sheet') {
            for (const f of AppState.files) {
                if (f._togetherRotations?.size > 0) _teardownTogether(f);
            }
        }

        if (fileEntry && this._currentFileId && this._currentFileId !== fileEntry.id && this._container && !this._modeJustToggled) {
            const outgoing = AppState.files.find(f => f.id === this._currentFileId);
            if (outgoing?._scrollPos) {
                outgoing._scrollPos[this._viewMode] = this._container.scrollTop;
            }
        }

        if (!this._container || !fileEntry?.pdfDoc) {
            this._modeJustToggled = false;
            return;
        }

        if (this._viewMode === 'sheet') {
            return this._renderSheetView(fileEntry); // Invariant 10: return Promise
        }

        // Cancel queue and in-flight tasks (still needed — even on cache-hit, old tasks must stop)
        this._renderQueue = [];
        this._activeRenders = 0;  // ← MUST be reset; stale count blocks _drainQueue()
        for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
        this._renderTasks.clear();

        // Hide all roots; show only target file's page root
        for (const [fid, r] of this._pageRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);
        for (const r of this._sheetRoots.values()) r.classList.add('preview-file-root--hidden');

        const _prevFileId = this._currentFileId;
        this._currentFileId = fileEntry.id;

        const { root, isNew } = this._getOrCreatePageRoot(fileEntry);

        if (!isNew) {
            // Validate that DOM card count matches totalPageCount.
            // Compare against totalPageCount (NOT pageOrder.length):
            //   - page-view cards are created for real pages 1..totalPageCount only
            //   - blank pages (pageOrder entries === 0) have NO page-view card
            //   - totalPageCount is immutable for a loaded PDF
            //   - pageOrder.length > totalPageCount after blank insertion, causing a
            //     false mismatch and unnecessary rebuild if compared against pageOrder.length
            const domCardCount = root.querySelectorAll('.preview-page-card').length;
            if (domCardCount === fileEntry.totalPageCount) {
                // DOM card structure is valid. However, _pageEls may contain stale sheet-view
                // card references if the user previously switched to sheet-view and back
                // (sheet-view D14 fix clears _pageEls for this file; sheet cards overwrite
                // page cards with the same keys). Repopulate _pageEls from the DOM if needed.
                const firstKey = `${fileEntry.id}-1`;
                const firstEl  = this._pageEls.get(firstKey);
                if (!firstEl || firstEl.querySelector('img.sheet-page-img')) {
                    // _pageEls is empty for this file OR contains stale sheet cards —
                    // rebuild the map from the actual page-view DOM cards in pageRoot.
                    for (const k of [...this._pageEls.keys()]) {
                        if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
                    }
                    root.querySelectorAll('.preview-page-card').forEach(card => {
                        const p = parseInt(card.dataset.page);
                        if (p) this._pageEls.set(`${fileEntry.id}-${p}`, card);
                    });
                }
                // DOM is valid and _pageEls is now correct — re-sync state only, no rebuild
                this._syncSelectionUI();
                if (_prevFileId !== fileEntry.id || this._modeJustToggled) {
                    this._container.scrollTop = fileEntry._scrollPos?.page ?? 0;
                    this._modeJustToggled = false;
                }
                requestAnimationFrame(() => this._renderVisible());
                return;
            }
            // totalPageCount changed (shouldn't happen for loaded PDFs, but be safe) —
            // fall through to rebuild this root
            root.innerHTML = '';
            for (const k of [...this._pageEls.keys()]) {
                if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
            }
        }
        // First time or card count mismatch: build/rebuild the page root below

        // Create all placeholder cards immediately
        for (let p = 1; p <= fileEntry.totalPageCount; p++) {
            const card       = document.createElement('div');
            card.className   = 'preview-page-card';
            card.dataset.fileId = fileEntry.id;
            card.dataset.page   = p;

            // Apply initial selection state
            if (fileEntry.selectedPages?.has(p)) card.classList.add('selected-for-print');
            if (fileEntry.singleSidedPages?.has(p) && fileEntry.selectedPages?.has(p))
                card.classList.add('single-sided-print');

            // Right-click = context menu
            card.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                ContextMenu.show(e, parseInt(card.dataset.page));
            });

            const canvas = document.createElement('canvas');
            card.appendChild(canvas);

            const key = `${fileEntry.id}-${p}`;
            this._pageEls.set(key, card);
            root.appendChild(card);
        }

        // Scroll to top, then render visible pages
        if (fileEntry._scrollPos === null) {
            fileEntry._scrollPos = { page: 0, sheet: 0, thumb: 0 };
            this._container.scrollTop = 0;
        } else {
            this._container.scrollTop = fileEntry._scrollPos.page;
        }
        this._modeJustToggled = false;
        // rAF ensures DOM has been laid out before we measure visibility
        requestAnimationFrame(() => this._renderVisible());
    },

    // Sheet view: render pages grouped as physical sheets (front + back)
    async _renderSheetView(fileEntry) {
        if (!this._container || !fileEntry?.pdfDoc) return;

        // [0] DEFENSIVE TEARDOWN: clean up stale together-mode state if mode was switched
        // while a different file was being viewed.
        if (fileEntry.landscapeMode !== 'together' && fileEntry._togetherRotations?.size > 0) {
            _teardownTogether(fileEntry);
        }

        // ── SHEET CACHE-HIT: skip full rebuild on pure tab-switch ────────────────
        // Compute a cheap fingerprint of all state that affects sheet layout.
        // If it matches the last-rendered fingerprint for this file AND the DOM root
        // already exists, just show/hide roots and re-enqueue visible blobs.
        const _fp = [
            fileEntry.pageOrder.join(','),
            [...fileEntry.selectedPages].sort((a,b)=>a-b).join(','),
            [...(fileEntry.singleSidedPages||new Set())].sort((a,b)=>a-b).join(','),
            [...(fileEntry.pageRotations||new Map()).entries()].sort((a,b)=>a[0]-b[0]).map(([k,v])=>`${k}:${v}`).join(','),
            fileEntry.landscapeMode,
            AppState.printMode,
        ].join('|');
        const _existingRoot = this._sheetRoots.get(fileEntry.id);
        if (_existingRoot && this._sheetFingerprints.get(fileEntry.id) === _fp) {
            // Layout is valid — just switch visibility and re-render visible blobs
            this._renderQueue  = [];
            this._activeRenders = 0;
            for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
            this._renderTasks.clear();
            const _prevFileId = this._currentFileId;
            this._currentFileId = fileEntry.id;
            for (const r of this._pageRoots.values())   r.classList.add('preview-file-root--hidden');
            for (const [fid, r] of this._sheetRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);
            _existingRoot.classList.remove('preview-file-root--hidden');
            if (_prevFileId !== fileEntry.id || this._modeJustToggled) {
                this._container.scrollTop = fileEntry._scrollPos?.sheet ?? 0;
                this._modeJustToggled = false;
            }
            this._sheetRenderGen = 0;
            requestAnimationFrame(() => this._renderVisible());
            return;
        }
        // ── END SHEET CACHE-HIT ──────────────────────────────────────────────────

        this._renderQueue = [];
        this._activeRenders = 0;
        for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
        this._renderTasks.clear();
        const _prevFileId = this._currentFileId;
        // Save current sheet scroll before rebuild — same-file state-change only.
        // _existingRoot: skips first-time sheet render (no prior sheet DOM).
        // _prevFileId === fileEntry.id: true only for same-file rebuilds; captured
        //   BEFORE L3975 so it still holds the ID of the file the container was
        //   showing. False for tab-switch (would save outgoing file's scroll into
        //   incoming file's slot).
        // !_modeJustToggled: skips mode-toggle context (Task 2f saved correctly).
        if (fileEntry._scrollPos && _existingRoot && _prevFileId === fileEntry.id && !this._modeJustToggled) {
            fileEntry._scrollPos.sheet = this._container.scrollTop;
        }
        this._currentFileId = fileEntry.id;

        // Hide all roots; show only this file's sheet root
        for (const r of this._pageRoots.values())   r.classList.add('preview-file-root--hidden');
        for (const [fid, r] of this._sheetRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);

        const sheetRoot = this._getOrCreateSheetRoot(fileEntry);
        sheetRoot.classList.remove('preview-file-root--hidden');
        sheetRoot.innerHTML = ''; // layout must rebuild — fingerprint mismatch or first load

        // Clear stale _pageEls entries for this file IMMEDIATELY after innerHTML = ''.
        // _renderSheetView is async — scroll events during the upcoming await may iterate
        // _pageEls and call getBoundingClientRect() on now-detached elements (returns zeros),
        // which would cause phantom _enqueueBlob() calls. (per D14)
        for (const k of [...this._pageEls.keys()]) {
            if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
        }

        this._sheetEls.clear();

        // Invalidate the cached fingerprint — prevents same-file reverted-state render
        // from false-cache-hitting against the already-cleared root DOM.
        // _sheetFingerprints is only re-written on successful Task 3c completion.
        this._sheetFingerprints.delete(fileEntry.id);

        const _myGen = ++this._nextSheetRenderGen;
        this._sheetRenderGen = _myGen;

        // Reset blob pipeline (was previously resetting this._container globally)
        this._blobQueue = [];
        this._activeBlobRenders = 0;

        // Detect page orientations — use per-file cache to avoid O(n) getPage() on every re-render.
        // Cache stored on fileEntry._orientationMap; individual entries deleted on rotation so only
        // the rotated page(s) are re-fetched (O(1) after first load).
        if (!fileEntry._orientationMap) {
            fileEntry._orientationMap = new Map();
        }
        const orientationMap = fileEntry._orientationMap;
        const totalPages = fileEntry.totalPageCount;
        const pdfDoc = fileEntry.pdfDoc;

        // Collect pages that are missing from the cache (first load = all; after rotation = 1)
        const missingPages = [];
        for (let p = 1; p <= totalPages; p++) {
            if (!orientationMap.has(p)) missingPages.push(p);
        }
        if (missingPages.length > 0) {
            const orientationPromises = missingPages.map(p =>
                pdfDoc.getPage(p).then(page => {
                    const rot = fileEntry.pageRotations?.get(p) ?? null;
                    return { p, isLandscape: RotationHelper.isLandscape(page, rot) };
                }).catch(() => ({ p, isLandscape: false }))
            );
            const results = await Promise.all(orientationPromises);
            for (const { p, isLandscape } of results) {
                orientationMap.set(p, isLandscape);
            }
        }
        // Guard: user may have switched file during async detection
        if (this._currentFileId !== fileEntry.id) {
            this._modeJustToggled = false;
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;
            return;
        }
        if (this._viewMode !== 'sheet') {
            this._modeJustToggled = false;
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;
            return;
        }  // view mode changed back to page — abort

        // ── AUTO-DETECT LANDSCAPE MODE ────────────────────────────────────────
        // Detect intrinsic orientation once per file, then auto-set landscapeMode:
        //   - all landscape → separate
        //   - all portrait or mixed → together
        if (fileEntry._originalOrientationMap == null) {
            const intrinsicPromises = [];
            for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                intrinsicPromises.push(
                    pdfDoc.getPage(p).then(page => {
                        return { p, isLandscape: RotationHelper.isLandscape(page) };
                    }).catch(() => ({ p, isLandscape: false }))
                );
            }
            const intrinsicResults = await Promise.all(intrinsicPromises);
            const intrinsicMap = new Map();
            for (const { p, isLandscape } of intrinsicResults) {
                intrinsicMap.set(p, isLandscape);
            }
            fileEntry._pendingOrientationMap = intrinsicMap;
        }

        // Guard after intrinsic detection await
        if (this._currentFileId !== fileEntry.id) {
            this._modeJustToggled = false;
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;
            return;
        }
        if (this._viewMode !== 'sheet') {
            this._modeJustToggled = false;
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;
            return;
        }

        // Commit pending intrinsic map
        if (fileEntry._pendingOrientationMap != null) {
            fileEntry._originalOrientationMap = fileEntry._pendingOrientationMap;
            fileEntry._pendingOrientationMap  = null;
        }

        // Auto-set landscapeMode based on intrinsic orientation
        if (fileEntry._originalOrientationMap != null) {
            let allLandscape = true;
            for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                if (fileEntry._originalOrientationMap.get(p) !== true) {
                    allLandscape = false;
                    break;
                }
            }
            const autoMode = (allLandscape && AppState.printMode !== 'booklet') ? 'separate' : 'together';
            if (fileEntry.landscapeMode !== autoMode) {
                if (fileEntry.landscapeMode === 'together' && autoMode !== 'together') {
                    _teardownTogether(fileEntry);
                }
                fileEntry.landscapeMode = autoMode;
            }
        }

        // Update tab badge after intrinsic detection
        if (fileEntry._originalOrientationMap != null && TabsModule._dragSourceIdx == null) {
            TabsModule.render();
        }

        // ── TOGETHER MODE: inject CCW90 → invalidate cache ──────────────────────
        if (fileEntry.landscapeMode === 'together') {
            for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                if (fileEntry._originalOrientationMap.get(p) === true) {
                    if (!fileEntry.pageRotations.has(p)) {
                        fileEntry.pageRotations.set(p, 'CCW90');
                        fileEntry._togetherRotations.add(p);
                    }
                }
            }

            // Invalidate stale cache
            for (const p of fileEntry._togetherRotations) {
                orientationMap.delete(p);
            }
            for (const p of fileEntry._togetherRotations) {
                orientationMap.set(p, false);
            }
        }
        // ── END TOGETHER MODE ────────────────────────────────────────────────────


        // Shared commit guard: abort before ANY state or DOM commit if either
        // (1) a newer rebuild has taken over, OR
        // (2) this file was deleted while the async rebuild was in flight.
        // Without check (2), last-file deletion can still pass the `_currentFileId`
        // and `_sheetRenderGen` guards if removal/clear did not schedule a newer
        // render. A stale invocation could then overwrite blankAbsorbedBy, rebuild
        // detached DOM, and re-add `_sheetFingerprints` for a file that no longer
        // exists in `AppState.files`. After this point there are no more awaits →
        // JS single-thread guarantees atomicity of all remaining commits.
        const _fileStillExists = AppState.files.some(f => f.id === fileEntry.id);
        if (this._sheetRenderGen !== _myGen || !_fileStillExists) {
            this._modeJustToggled = false;
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;
            return;
        }

        const printMode = AppState.printMode;
        const { sheets, blankAbsorbedBy, deselectedPages } = buildSheetLayout(fileEntry, printMode, orientationMap, fileEntry.landscapeMode);
        fileEntry.blankAbsorbedBy = blankAbsorbedBy; // Invariant 9

        let blankQueuePos = 0;

        // ── Layout wrapper: sheets-column + ejected-column inside sheet-view-inner ──
        const inner = document.createElement('div');
        inner.className = 'sheet-view-inner';

        const sheetsCol = document.createElement('div');
        sheetsCol.className = 'sheets-column';

        sheets.forEach(sheet => {
            const sheetCard = document.createElement('div');
            sheetCard.className = 'sheet-card';
            sheetCard.dataset.sheetIndex = sheet.sheetIndex;

            const label = document.createElement('div');
            label.className = 'sheet-label';
            const totalSheets = sheets.length;

            if (sheet.isBooklet) {
                label.textContent = I18nModule.t('sheet.bookletLabel')(sheet.sheetIndex, totalSheets);
            } else {
                label.textContent = I18nModule.t('sheet.label')(sheet.sheetIndex, totalSheets);
            }
            sheetCard.appendChild(label);

            const facesRow = document.createElement('div');
            facesRow.className = sheet.isLandscape ? 'sheet-faces-col' : 'sheet-faces-row';

            // Helper to create a page face (real page or blank)
            const makeFace = (pageNum, faceLabel, isLandscapeHint = false) => {
                const face = document.createElement('div');
                face.className = 'sheet-face';

                const fl = document.createElement('div');
                fl.className = 'sheet-face-label';
                fl.textContent = faceLabel;
                face.appendChild(fl);

                if (pageNum === null || pageNum === undefined || pageNum === 0) {
                    // Blank page — render as a plain white sheet (no content)
                    // pageNum === 0 means user-inserted blank
                    const blank = document.createElement('div');
                    blank.className = 'blank-page-card';
                    blank.classList.toggle('landscape', isLandscapeHint);
                    // Right-click on user blank → context menu with page=0 for remove action
                    if (pageNum === 0) {
                        blank.style.border = '2px solid #667eea';
                        blank.addEventListener('contextmenu', (e) => {
                            e.preventDefault();
                            ContextMenu.show(e, 0, { isUserBlank: true });
                        });
                        const renderPos = blankQueuePos++;
                        blank.dataset.blankRenderPos = renderPos; // stable position on DOM node
                        const xBtn = document.createElement('button');
                        xBtn.className = 'blank-delete-btn';
                        xBtn.textContent = '×';
                        xBtn.title = I18nModule.t('sheet.deleteBlank');
                        xBtn.addEventListener('click', (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            if (PreviewPanelModule._isDeleting) return; // drop second click
                            PreviewPanelModule._isDeleting = true;

                            const entry = AppState.files.find(f => f.id === fileEntry.id);
                            if (!entry) {
                                PreviewPanelModule._isDeleting = false;
                                return;
                            }

                            // BUG-8-4 fix: re-derive blank's pageOrder index at click time by
                            // re-scanning for the Nth zero (rPos) in pageOrder. This is immune
                            // to stale render-position indices caused by inserting/removing other
                            // blanks between render and click.
                            const rPos = parseInt(blank.dataset.blankRenderPos);
                            let blankCount = -1;
                            let currentIdx = -1;
                            for (let bi = 0; bi < entry.pageOrder.length; bi++) {
                                if (entry.pageOrder[bi] === 0) {
                                    blankCount++;
                                    if (blankCount === rPos) { currentIdx = bi; break; }
                                }
                            }

                            try {
                                if (currentIdx >= 0 && entry.pageOrder[currentIdx] === 0) {
                                    entry.pageOrder.splice(currentIdx, 1);
                                    const result = PreviewPanelModule.render(entry);
                                    if (result && typeof result.finally === 'function') {
                                        result.finally(() => { PreviewPanelModule._isDeleting = false; });
                                    } else {
                                        PreviewPanelModule._isDeleting = false;
                                    }
                                } else {
                                    PreviewPanelModule._isDeleting = false;
                                }
                            } catch (ex) {
                                PreviewPanelModule._isDeleting = false;
                                throw ex;
                            }
                        });
                        blank.appendChild(xBtn);
                    }
                    face.appendChild(blank);
                } else {
                    // Real page card
                    const card = document.createElement('div');
                    card.className = 'preview-page-card';
                    card.dataset.fileId = fileEntry.id;
                    card.dataset.page = pageNum;

                    if (fileEntry.selectedPages?.has(pageNum)) card.classList.add('selected-for-print');
                    if (fileEntry.singleSidedPages?.has(pageNum) && fileEntry.selectedPages?.has(pageNum))
                        card.classList.add('single-sided-print');

                    card.addEventListener('contextmenu', (e) => {
                        e.preventDefault();
                        ContextMenu.show(e, parseInt(card.dataset.page));
                    });

                    const img = document.createElement('img');
                    img.className = 'sheet-page-img';
                    img.alt       = '';
                    img.draggable = false;
                    card.appendChild(img);

                    const key = `${fileEntry.id}-${pageNum}`;
                    this._pageEls.set(key, card);
                    face.appendChild(card);
                }
                return face;
            };

            if (sheet.isBooklet) {
                // Booklet: show all 4 page slots: front-left, front-right | back-left, back-right
                const frontGroup = document.createElement('div');
                frontGroup.className = 'sheet-face-group';
                const frontLabel = document.createElement('div');
                frontLabel.className = 'sheet-face-group-label';
                frontLabel.textContent = I18nModule.t('sheet.front');
                frontGroup.appendChild(frontLabel);
                const frontRow = document.createElement('div');
                frontRow.className = 'sheet-face-group-row';
                frontRow.appendChild(makeFace(sheet.front, I18nModule.t('sheet.bookletFacePage')(sheet.front)));
                frontRow.appendChild(makeFace(sheet.front2, I18nModule.t('sheet.bookletFacePage')(sheet.front2)));
                frontGroup.appendChild(frontRow);

                const backGroup = document.createElement('div');
                backGroup.className = 'sheet-face-group';
                const backLabel2 = document.createElement('div');
                backLabel2.className = 'sheet-face-group-label';
                backLabel2.textContent = I18nModule.t('sheet.back');
                backGroup.appendChild(backLabel2);
                const backRow = document.createElement('div');
                backRow.className = 'sheet-face-group-row';
                backRow.appendChild(makeFace(sheet.back, I18nModule.t('sheet.bookletFacePage')(sheet.back)));
                backRow.appendChild(makeFace(sheet.back2, I18nModule.t('sheet.bookletFacePage')(sheet.back2)));
                backGroup.appendChild(backRow);

                facesRow.appendChild(frontGroup);
                facesRow.appendChild(backGroup);
            } else {
                // Duplex or simplex: front face + optional back face
                // BUG-3 fix: pass isLandscapeHint to front face so user-blank fronts (front===0)
                // get correct landscape aspect-ratio when the sheet is landscape
                const frontIsLandscape = sheet.isLandscape ?? (orientationMap?.get(sheet.front) ?? false);
                const frontFace = makeFace(sheet.front, I18nModule.t('sheet.page')(sheet.front), frontIsLandscape);
                // Add page-curl hint on front card (duplex always has back)
                const frontCard = frontFace.querySelector('.preview-page-card');
                if (frontCard) frontCard.classList.add('with-curl');
                facesRow.appendChild(frontFace);
                {
                    const backPageNum = sheet.back;
                    const backFaceLabel = backPageNum
                        ? I18nModule.t('sheet.backPage')(backPageNum)
                        : sheet.isSingleForced ? I18nModule.t('sheet.backSimplex') : I18nModule.t('sheet.backBlank');
                    const backIsLandscape = sheet.isLandscape ?? (orientationMap?.get(sheet.front) ?? false);
                    facesRow.appendChild(makeFace(backPageNum, backFaceLabel, backIsLandscape));
                }
            }

            sheetCard.appendChild(facesRow);
            this._sheetEls.set(sheet.sheetIndex, sheetCard);
            sheetsCol.appendChild(sheetCard);
        });

        // ── Ejected column: deselected pages (R9–R12) ────────────────
        const ejectedCol = document.createElement('div');
        ejectedCol.className = 'ejected-column';
        ejectedCol.hidden = (deselectedPages.length === 0);

        if (deselectedPages.length > 0) {
            const header = document.createElement('div');
            header.className = 'ejected-column-header';
            header.textContent = I18nModule.t('sheet.noprint');
            ejectedCol.appendChild(header);

            deselectedPages.forEach(pageNum => {
                const card = document.createElement('div');
                card.className = 'ejected-card';
                card.dataset.fileId = fileEntry.id;   // required for _renderSheetPage
                card.dataset.page = pageNum;           // required for _renderSheetPage
                card.title = I18nModule.t('sheet.ejectHint')(pageNum);

                const lbl = document.createElement('div');
                lbl.className = 'ejected-card-label';
                lbl.textContent = I18nModule.t('sheet.ejectLabel')(pageNum);
                card.appendChild(lbl);

                const img = document.createElement('img');
                img.className = 'sheet-page-img';    // same class as sheet cards → _renderVisible picks it up
                img.alt = '';
                img.draggable = false;
                card.appendChild(img);

                card.addEventListener('click', () => {
                    const entry = AppState.files.find(f => f.id === fileEntry.id);
                    if (!entry) return;
                    togglePageSelection(entry, pageNum); // else branch: add to selectedPages
                    PreviewPanelModule.render(entry);
                    PrintModule.updateButton();
                    PageSelectModule.updateDisplay();
                });

                const key = `${fileEntry.id}-${pageNum}`;
                this._pageEls.set(key, card);
                ejectedCol.appendChild(card);
            });
        }

        // Vertical separator between sheets and ejected columns
        const separator = document.createElement('div');
        separator.className = 'ejected-separator';
        separator.hidden = (deselectedPages.length === 0);

        inner.appendChild(sheetsCol);
        inner.appendChild(separator);
        inner.appendChild(ejectedCol);
        sheetRoot.appendChild(inner);

        // Store fingerprint so next render() for this file can skip rebuild on cache-hit
        this._sheetFingerprints.set(fileEntry.id, _fp);

        if (fileEntry._scrollPos === null) {
            fileEntry._scrollPos = { page: 0, sheet: 0, thumb: 0 };
            this._container.scrollTop = 0;
        } else {
            this._container.scrollTop = fileEntry._scrollPos.sheet;
        }
        this._modeJustToggled = false;
        this._sheetRenderGen = 0;
        requestAnimationFrame(() => this._renderVisible());
    },

    // Render pages currently visible (+ 2 screens lookahead)
    _renderVisible() {
        if (!this._container || !this._currentFileId) return;
        const cRect     = this._container.getBoundingClientRect();
        const lookahead = cRect.height * 2; // 2 screens ahead (was 1)

        this._pageEls.forEach((el, key) => {
            if (!key.startsWith(this._currentFileId + '-')) return;
            if (el.classList.contains('rendered')) return;
            const eRect = el.getBoundingClientRect();
            const visible = eRect.bottom >= cRect.top - lookahead && eRect.top <= cRect.bottom + lookahead;
            if (visible) {
                const fileId  = el.dataset.fileId;
                const pageNum = parseInt(el.dataset.page);
                // Sheet-view cards use img blob path; page-view cards use canvas path
                if (el.querySelector('img.sheet-page-img')) {
                    this._enqueueBlob(fileId, pageNum, el);
                } else {
                    this._enqueue(fileId, pageNum, el);
                }
            }
        });
    },

    // ── Blob render queue (for sheet-view <img> cards) ────────────
    _blobQueue: [],
    _activeBlobRenders: 0,
    _MAX_BLOB_CONCURRENT: 6,

    _enqueueBlob(fileId, pageNum, el) {
        if (el.classList.contains('rendered')) return;
        if (this._blobQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;
        this._blobQueue.push({ fileId, pageNum, el });
        this._drainBlobQueue();
    },

    _drainBlobQueue() {
        while (this._activeBlobRenders < this._MAX_BLOB_CONCURRENT && this._blobQueue.length > 0) {
            const job = this._blobQueue.pop(); // LIFO
            this._activeBlobRenders++;
            this._renderSheetPage(job.fileId, job.pageNum, job.el).finally(() => {
                this._activeBlobRenders--;
                this._drainBlobQueue();
            });
        }
    },

    async _renderSheetPage(fileId, pageNum, el) {
        const SHEET_SCALE = 0.8; // reduced scale for sheet view thumbnails
        const img = el.querySelector('img.sheet-page-img');
        if (!img) return;

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const rotation      = fileEntry.pageRotations?.get(pageNum) ?? null;
        const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
        RotationHelper.updateBadge(el, badgeRotation);

        const entry = await this._renderBlobPage(fileId, pageNum, SHEET_SCALE, this._cache);
        if (!entry) return;

        // Check element still in DOM (user may have switched views)
        if (!this._container?.contains(el)) return;

        img.style.transform = ''; // clear any phase-1 CSS hint
        img.src = entry.url;
        el.classList.add('rendered');
    },

    _enqueue(fileId, pageNum, el) {
        const key = `${fileId}-${pageNum}`;
        // Skip if already rendering or rendered
        if (this._renderTasks.has(key)) return;
        if (el.classList.contains('rendered')) return;
        // Skip if already in queue
        if (this._renderQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;
        this._renderQueue.push({ fileId, pageNum, el });
        this._drainQueue();
    },

    // LIFO queue: prioritize most recently enqueued (= pages user just scrolled to)
    _drainQueue() {
        while (this._activeRenders < this._MAX_CONCURRENT && this._renderQueue.length > 0) {
            const job = this._renderQueue.pop(); // LIFO — pop, not shift
            this._activeRenders++;
            this._renderPage(job.fileId, job.pageNum, job.el).finally(() => {
                this._activeRenders--;
                this._drainQueue();
            });
        }
    },

    // Render a page to blob URL + OffscreenCanvas, store in cache.
    // Used by sheet view and thumb strip (NOT page view — that stays canvas-only).
    // Returns { canvas: OffscreenCanvas, url: string } or null on error/cancel.
    async _renderBlobPage(fileId, pageNum, scale, cacheRef) {
        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return null;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const key      = `${fileId}-${pageNum}-${rotation ?? '0'}-${scale}`;

        // Cache hit
        if (cacheRef.has(key)) return cacheRef.get(key);

        // Cancel stale task
        const taskKey  = `${fileId}-${pageNum}-blob`;
        const existing = this._renderTasks.get(taskKey);
        if (existing) { try { existing.cancel(); } catch(_){} }

        const page = await fileEntry.pdfDoc.getPage(pageNum);
        const vp   = RotationHelper.viewport(page, scale, rotation);
        // OffscreenCanvas required for convertToBlob() — HTMLCanvasElement only has toBlob()
        const off  = new OffscreenCanvas(vp.width, vp.height);

        const task = page.render({
            canvasContext: off.getContext('2d', { alpha: false }),
            viewport:      vp,
            intent:        'display',
        });
        this._renderTasks.set(taskKey, task);

        try {
            await task.promise;
            const blob = await off.convertToBlob({ type: 'image/webp', quality: 0.82 });
            const url  = URL.createObjectURL(blob);
            const entry = { canvas: off, url };
            cacheRef.set(key, entry);
            return entry;
        } catch(err) {
            if (err?.name !== 'RenderingCancelledException') console.warn(err);
            return null;
        } finally {
            page.cleanup();
            this._renderTasks.delete(taskKey);
        }
    },

    // Single-pass rendering: always render at full quality (1.5 scale)
    // Removes the old double-render pattern (0.8 while scrolling → 1.5 after scroll stops)
    async _renderPage(fileId, pageNum, el) {
        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const scale    = 1.5; // single pass — always full quality
        const key      = `${fileId}-${pageNum}-${rotation ?? '0'}-${scale}`;
        const canvas   = el.querySelector('canvas');
        if (!canvas) return;

        // Apply CSS transform for flip + rotation badge
        canvas.style.transform = RotationHelper.toCSS(rotation);
        RotationHelper.updateBadge(el, rotation);

        if (this._cache.has(key)) {
            const { canvas: off } = this._cache.get(key);
            canvas.width  = off.width;
            canvas.height = off.height;
            canvas.style.width  = `${off.width}px`;
            canvas.style.height = `${off.height}px`;
            canvas.getContext('2d').drawImage(off, 0, 0);
            el.classList.add('rendered');
            return;
        }

        const taskKey  = `${fileId}-${pageNum}`;
        const existing = this._renderTasks.get(taskKey);
        if (existing) { try { existing.cancel(); } catch(_){} }

        const page = await fileEntry.pdfDoc.getPage(pageNum);
        const vp   = RotationHelper.viewport(page, scale, rotation);
        const off  = CanvasPool.acquire();
        off.width  = vp.width;
        off.height = vp.height;

        const task = page.render({
            canvasContext: off.getContext('2d', { alpha: false }),
            viewport:      vp,
            intent:        'display',
        });
        this._renderTasks.set(taskKey, task);

        try {
            await task.promise;
            this._cache.set(key, { canvas: off, url: '' });
            canvas.width  = vp.width;
            canvas.height = vp.height;
            canvas.style.width  = `${vp.width}px`;
            canvas.style.height = `${vp.height}px`;
            canvas.getContext('2d').drawImage(off, 0, 0);
            el.classList.add('rendered');
        } catch(err) {
            if (err?.name !== 'RenderingCancelledException') console.warn(err);
        } finally {
            page.cleanup();
            this._renderTasks.delete(taskKey);
        }
    },

    // Called after a single page is rotated in sheet view.
    // Phase 1: instant CSS transform on existing img (~0ms).
    // Phase 2: async re-render at new rotation, swap img.src (~3-7ms).
    async _patchRotatedPage(fileId, pageNum) {
        const key = `${fileId}-${pageNum}`;
        const el  = this._pageEls.get(key);
        if (!el) return;

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry) return;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const rotDeg   = RotationHelper.toDeg(rotation);
        const img      = el.querySelector('img.sheet-page-img');

        if (!img) {
            // Page-view canvas card — incremental canvas re-render
            el.classList.remove('rendered');
            this._enqueue(fileId, pageNum, el);
            return;
        }

        // Phase 1: instant CSS hint (compositor, ~0ms)
        if (rotDeg) img.style.transform = `rotate(${rotDeg}deg)`;

        // Phase 2: invalidate old cache entry, re-render at new rotation
        this._cache.deleteByPrefix(`${fileId}-${pageNum}-`);
        el.classList.remove('rendered');
        // _renderSheetPage will clear the CSS transform and set proper img.src
        this._enqueueBlob(fileId, pageNum, el);
    },

    scrollToPage(pageNum) {
        const fileId = AppState.activeFile?.id;
        if (!fileId) return;
        const key = `${fileId}-${pageNum}`;
        const el  = this._pageEls.get(key);
        // Use instant scroll for manual navigation to reduce perceived lag
        el?.scrollIntoView({ behavior: 'auto', block: 'start' });
    },

    // Sync selection highlight on all visible cards (called after state changes)
    _syncSelectionUI() {
        const activeFile = AppState.activeFile;
        if (!activeFile) return;
        this._pageEls.forEach((card, key) => {
            if (!key.startsWith(activeFile.id + '-')) return;
            const pageNum  = parseInt(card.dataset.page);
            const isSel    = activeFile.selectedPages.has(pageNum);
            const isSingle = activeFile.singleSidedPages?.has(pageNum);
            card.classList.toggle('selected-for-print', isSel);
            card.classList.toggle('single-sided-print', !!(isSingle && isSel));
            // Update rotation badge
            const rotation      = activeFile.pageRotations?.get(pageNum) ?? null;
            const badgeRotation = activeFile._togetherRotations?.has(pageNum) ? null : rotation;
            RotationHelper.updateBadge(card, badgeRotation);
        });
    },

    // Called externally when selection state changes (e.g. from ContextMenu, select-all)
    onStateChanged() {
        if (this._viewMode === 'sheet' && AppState.activeFile) {
            // Sheet layout depends on which pages are selected/single-sided → rebuild immediately
            this.render(AppState.activeFile); // routes through blankAbsorbedBy reset + landscapeMode coerce (Invariants 3, 8)
        } else {
            this._syncSelectionUI();
        }
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

    // Virtual scrolling: release canvas memory for pages far off-screen
    // Keeps placeholder div with correct height so scroll position is preserved
    _unmountOffScreen() {
        if (!this._container || !this._currentFileId) return;
        const activeRoot = this._pageRoots.get(this._currentFileId)
                        ?? this._sheetRoots.get(this._currentFileId);
        if (!activeRoot) return;
        const cRect = this._container.getBoundingClientRect();
        const buffer = cRect.height * 3; // keep 3 screens worth of rendered canvases

        this._pageEls.forEach((el, key) => {
            if (!key.startsWith(this._currentFileId + '-')) return;
            if (!activeRoot.contains(el)) return; // never measure hidden roots — offsetHeight returns 0 there
            if (!el.classList.contains('rendered')) return;
            const eRect = el.getBoundingClientRect();
            const isFar = eRect.bottom < cRect.top - buffer || eRect.top > cRect.bottom + buffer;
            if (isFar) {
                // Sheet-view cards use <img>, page-view cards use <canvas>
                const imgEl    = el.querySelector('img.sheet-page-img');
                const canvasEl = el.querySelector('canvas');
                if (imgEl && imgEl.src) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    imgEl.src = '';
                    el.classList.remove('rendered');
                } else if (canvasEl && (canvasEl.width > 0 || canvasEl.height > 0)) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    canvasEl.width = 0;
                    canvasEl.height = 0;
                    el.classList.remove('rendered');
                }
            }
        });
    },

    removeFileRoot(fileId) {
        const pageRoot = this._pageRoots.get(fileId);
        if (pageRoot) { pageRoot.remove(); this._pageRoots.delete(fileId); }

        const sheetRoot = this._sheetRoots.get(fileId);
        if (sheetRoot) { sheetRoot.remove(); this._sheetRoots.delete(fileId); }
        this._sheetFingerprints.delete(fileId);

        // Remove _pageEls entries for this file
        for (const key of [...this._pageEls.keys()]) {
            if (key.startsWith(fileId + '-')) this._pageEls.delete(key);
        }

        // Cancel in-flight render tasks and recalculate active render counter
        for (const [taskKey, task] of [...this._renderTasks.entries()]) {
            if (taskKey.startsWith(fileId + '-')) {  // '+'-' prevents prefix collision (e.g. id='f1' matching 'f10-...')
                try { task.cancel(); } catch(_) {}
                this._renderTasks.delete(taskKey);
            }
        }
        // Recalculate counters — cancelled tasks may not decrement via .finally()
        this._activeRenders     = this._renderTasks.size;
        this._activeBlobRenders = 0; // blob tasks not tracked by key; safe to reset

        // Drain queues
        this._blobQueue   = (this._blobQueue   ?? []).filter(j => j.fileId !== fileId);
        this._renderQueue = this._renderQueue.filter(j => j.fileId !== fileId);

        // Evict pixel cache for this file
        this._cache.deleteByPrefix(fileId + '-');

        // If the removed file currently owns the preview container, invalidate
        // ownership immediately so any resumed async rebuild aborts safely.
        if (this._currentFileId === fileId) {
            this._currentFileId = null;
            this._sheetRenderGen = 0;
            this._modeJustToggled = false;
        }
    },

    clear() {
        if (this._observer) { this._observer.disconnect(); this._observer = null; }
        if (this._container) {
            this._container.innerHTML = `
                <div class="preview-empty">
                    <span class="preview-empty-icon">🖨</span>
                    <span>${I18nModule.t('preview.empty')}</span>
                </div>`;
        }
        // Reset per-file DOM roots (all files removed — maps now stale)
        this._pageRoots.clear();
        this._sheetRoots.clear();
        this._sheetFingerprints.clear();
        this._pageEls.clear();
        this._sheetEls.clear();
        this._currentFileId = null;
        this._sheetRenderGen = 0;
        this._modeJustToggled = false;
    },
};

// ═══════════════════════════════════════════════════════════════════
// ThumbStripModule — Persistent left thumbnail panel
// Renders thumbnails for ALL loaded files with file dividers.
// Click thumbnail → scroll preview panel to that page.
// ═══════════════════════════════════════════════════════════════════
const ThumbStripModule = {
    _container:    null,
    _fileRoots:    new Map(),   // fileId → <div.thumb-file-root>
    _scrollRAF:    false,
    _scrollIdleTimer: null,
    _renderTasks:  new Map(),
    _cache:        new LRUBlobCache(20), // 20 MB LRU for thumbnails
    _renderQueue:  [],
    _activeRenders: 0,
    _MAX_CONCURRENT: 8,

    removeFileRoot(fileId) {
        const root = this._fileRoots?.get(fileId);
        if (root) { root.remove(); this._fileRoots.delete(fileId); }

        for (const [key, task] of [...this._renderTasks.entries()]) {
            if (key.startsWith(fileId + '-')) {  // '+'-' prevents prefix collision (e.g. id='f1' matching 'f10-...')
                try { task.cancel(); } catch(_) {}
                this._renderTasks.delete(key);
            }
        }
        // NOTE: _renderThumb tasks run as bare awaits (not in _renderTasks Map).
        // Setting _activeRenders = 0 is correct — their .finally() decrements are
        // benign on detached nodes and will not over-decrement below 0 because
        // _drainQueue checks _activeRenders < _MAX_CONCURRENT before spawning.
        this._activeRenders = 0;

        this._renderQueue = this._renderQueue.filter(j => j.fileId !== fileId);
        this._cache.deleteByPrefix(fileId + '-');
    },

    _getOrCreateThumbRoot(fileEntry) {
        let root = this._fileRoots.get(fileEntry.id);
        if (root) return { root, isNew: false };
        root = document.createElement('div');
        root.className = 'thumb-file-root';
        root.dataset.fileId = fileEntry.id;
        this._fileRoots.set(fileEntry.id, root);
        this._container.appendChild(root);
        return { root, isNew: true };
    },

    init() {
        this._container = document.getElementById('thumb-strip');
        // rAF-based scroll handler (replaces setTimeout debounce)
        this._container?.addEventListener('scroll', () => {
            if (!this._scrollRAF) {
                this._scrollRAF = true;
                requestAnimationFrame(() => {
                    this._scrollRAF = false;
                    this._renderVisible();
                });
            }
            // After scroll stops: unmount far off-screen thumbs
            clearTimeout(this._scrollIdleTimer);
            this._scrollIdleTimer = setTimeout(() => {
                this._renderVisible();
                this._unmountOffScreen();
            }, 200);
        }, { passive: true });
    },

    // Full re-render: called on file add/remove/switch
    render() {
        if (!this._container) return;

        // Cancel all in-flight renders and queue
        for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
        this._renderTasks.clear();
        this._renderQueue = [];
        this._activeRenders = 0;

        if (AppState.files.length === 0) {
            // All files removed — reset Maps to prevent stale DOM on next upload
            this._fileRoots?.clear();
            this._container.innerHTML = `
                <div class="preview-empty" style="padding:16px;text-align:center">
                    <span class="preview-empty-icon">📄</span>
                    <span style="font-size:12px">${I18nModule.t('preview.thumbEmpty')}</span>
                </div>`;
            return;
        }

        const f = AppState.activeFile;
        if (!f) return;

        // Hide all file roots except the active one
        for (const [fid, r] of this._fileRoots) {
            r.classList.toggle('thumb-file-root--hidden', fid !== f.id);
        }
        const { root, isNew } = this._getOrCreateThumbRoot(f);
        if (!isNew) {
            // Already built — refresh data-file-index (may have changed after drag-to-reorder)
            // then re-sync selection highlights and re-render visible thumbs
            const currentIdx = AppState.activeFileIndex;
            root.querySelectorAll('.thumb-item').forEach(el => {
                el.dataset.fileIndex = currentIdx;
            });
            this._syncSelectionHighlights();
            requestAnimationFrame(() => this._renderVisible());
            return;
        }
        // else: first time for this file — fall through to item creation loop below

        for (let p = 1; p <= f.totalPageCount; p++) {
            const item = document.createElement('div');
            item.className        = 'thumb-item';
            item.dataset.fileId   = f.id;
            item.dataset.page     = p;
            item.dataset.fileIndex = AppState.activeFileIndex;
            item.setAttribute('tabindex', '0');
            item.setAttribute('role', 'button');
            item.setAttribute('aria-label', `Trang ${p}`);

            const img   = document.createElement('img');
            img.className = 'thumb-img';
            img.alt       = '';
            img.draggable = false;
            const label   = document.createElement('div');
            label.className   = 'thumb-item-label';
            label.textContent = p;

            item.appendChild(img);
            item.appendChild(label);

            if (p === 1) item.classList.add('active');

            item.addEventListener('click', () => this._onThumbClick(AppState.activeFileIndex, p));
            item.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                ContextMenu.show(e, p);
            });
            root.appendChild(item);
        }

        // rAF to ensure layout, then render visible thumbs
        requestAnimationFrame(() => this._renderVisible());
    },

    // Render thumbs currently in view of the scroll container
    _renderVisible() {
        if (!this._container) return;
        const cRect     = this._container.getBoundingClientRect();
        const lookahead = cRect.height * 2; // 2 screens ahead (was 1)

        // Scope to visible root only — hidden roots' items return getBoundingClientRect() as zeros
        const activeFileId = AppState.activeFile?.id;
        const activeThumbRoot = activeFileId ? this._fileRoots?.get(activeFileId) : null;
        const searchRoot = activeThumbRoot ?? this._container;
        searchRoot.querySelectorAll('.thumb-item:not(.rendered)').forEach(el => {
            const eRect = el.getBoundingClientRect();
            if (eRect.bottom >= cRect.top - lookahead && eRect.top <= cRect.bottom + lookahead) {
                const fileId  = el.dataset.fileId;
                const pageNum = parseInt(el.dataset.page);
                this._enqueue(fileId, pageNum, el);
            }
        });
    },

    _enqueue(fileId, pageNum, el) {
        const key = `${fileId}-${pageNum}`;
        if (this._renderTasks.has(key)) return;
        if (el.classList.contains('rendered')) return;
        if (this._renderQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;
        this._renderQueue.push({ fileId, pageNum, el });
        this._drainQueue();
    },

    // LIFO queue: prioritize most recently enqueued thumbnails
    _drainQueue() {
        while (this._activeRenders < this._MAX_CONCURRENT && this._renderQueue.length > 0) {
            const job = this._renderQueue.pop(); // LIFO — pop, not shift
            this._activeRenders++;
            this._renderThumb(job.fileId, job.pageNum, job.el).finally(() => {
                this._activeRenders--;
                this._drainQueue();
            });
        }
    },

    async _renderThumb(fileId, pageNum, el) {
        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const rotation   = fileEntry.pageRotations?.get(pageNum) ?? null;
        const thumbScale = 0.26;
        const key        = `${fileId}-${pageNum}-${rotation ?? '0'}-${thumbScale}`;
        const img        = el.querySelector('img.thumb-img');
        if (!img) return;

        const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
        RotationHelper.updateBadge(el, badgeRotation);

        // Cache hit — just set src (browser re-uses decoded bitmap if URL unchanged)
        if (this._cache.has(key)) {
            const { url } = this._cache.get(key);
            if (img.src !== url) img.src = url;
            el.classList.add('rendered');
            return;
        }

        // Fresh render
        const taskKey  = `${fileId}-${pageNum}`;
        if (this._renderTasks.has(taskKey)) return;
        if (this._renderQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;

        const entry = await PreviewPanelModule._renderBlobPage(fileId, pageNum, thumbScale, this._cache);
        if (!entry) return;

        img.src = entry.url;
        el.classList.add('rendered');
    },

    _onThumbClick(fileIndex, pageNum) {
        // Switch active file if needed
        if (fileIndex !== AppState.activeFileIndex) {
            TabsModule.setActive(fileIndex);
        }
        // Scroll preview panel to this page
        if (typeof PreviewPanelModule !== 'undefined') {
            PreviewPanelModule.scrollToPage(pageNum);
        }
        // Update active highlight
        this._setActiveHighlight(fileIndex, pageNum);
    },

    _setActiveHighlight(fileIndex, pageNum, scroll = true) {
        const activeId   = AppState.activeFile?.id;
        const searchRoot = (activeId && this._fileRoots?.get(activeId)) || this._container;
        searchRoot.querySelectorAll('.thumb-item.active')
            .forEach(el => el.classList.remove('active'));
        const target = searchRoot.querySelector(
            `.thumb-item[data-file-index="${fileIndex}"][data-page="${pageNum}"]`
        );
        target?.classList.add('active');
        if (scroll) target?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },

    // Called when preview panel scrolls — update active thumb highlight
    onPreviewScroll(fileIndex, pageNum) {
        this._setActiveHighlight(fileIndex, pageNum);
    },

    // Sync selection CSS + rotation badges on all thumb items
    _syncSelectionHighlights() {
        if (!this._container) return;
        const _activeId   = AppState.activeFile?.id;
        const _activeRoot = _activeId ? (this._fileRoots?.get(_activeId) ?? this._container) : this._container;
        _activeRoot.querySelectorAll('.thumb-item').forEach(el => {
            const fileId  = el.dataset.fileId;
            const pageNum = parseInt(el.dataset.page);
            const entry   = AppState.files.find(f => f.id === fileId);
            if (!entry) return;
            const isSel    = entry.selectedPages.has(pageNum);
            const isSingle = entry.singleSidedPages?.has(pageNum);
            el.classList.toggle('selected-for-print', isSel);
            el.classList.toggle('single-sided-print', !!(isSingle && isSel));
            // Update rotation badge
            const rotation      = entry.pageRotations?.get(pageNum) ?? null;
            const badgeRotation = entry._togetherRotations?.has(pageNum) ? null : rotation;
            RotationHelper.updateBadge(el, badgeRotation);
        });
    },

    // Virtual scrolling: release canvas memory for thumbnails far off-screen
    _unmountOffScreen() {
        if (!this._container) return;
        const f = AppState.activeFile;
        if (!f) return;
        const activeRoot = this._fileRoots?.get(f.id) ?? this._container;

        const cRect = this._container.getBoundingClientRect();
        const buffer = cRect.height * 4; // keep 4 screens of thumbs

        activeRoot.querySelectorAll('.thumb-item.rendered').forEach(el => {
            const eRect = el.getBoundingClientRect();
            const isFar = eRect.bottom < cRect.top - buffer || eRect.top > cRect.bottom + buffer;
            if (isFar) {
                const img = el.querySelector('img.thumb-img');
                if (img && img.src) {
                    el.style.minHeight = `${el.offsetHeight}px`; // measured on visible element — safe
                    img.src = ''; // release decoded bitmap memory
                    el.classList.remove('rendered');
                }
            }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// ViewModeModule — Handles page/sheet view toggle
// ═══════════════════════════════════════════════════════════════════
const ViewModeModule = {
    init() {
        const group = document.getElementById('view-toggle-group');
        if (!group) return;
        group.addEventListener('click', e => {
            const btn = e.target.closest('.view-toggle-btn');
            if (!btn) return;
            const newMode = btn.dataset.view; // 'page' | 'sheet'
            if (newMode === AppState.viewMode) return;
            if (AppState.activeFile && !AppState.activeFile.pdfDoc) return;
            AppState.viewMode = newMode;
            // Update button active state
            group.querySelectorAll('.view-toggle-btn').forEach(b => {
                b.classList.toggle('active', b.dataset.view === newMode);
            });
            // Show/hide landscape mode bar
            ViewModeModule._syncModeBar();
            // Re-render current file
            const file = AppState.activeFile;
            // Sync _viewMode unconditionally (moved outside if(file) to fix no-file desync)
            PreviewPanelModule._viewMode = newMode;
            if (file?.pdfDoc) {
                const _oldMode = newMode === 'sheet' ? 'page' : 'sheet';
                if (!PreviewPanelModule._modeJustToggled && file._scrollPos && PreviewPanelModule._container) {
                    file._scrollPos[_oldMode] = PreviewPanelModule._container.scrollTop;
                }
                PreviewPanelModule._modeJustToggled = true;
                PreviewPanelModule.render(file);
            } else if (file) {
                // unreachable — Part 1 blocks this
                PreviewPanelModule.render(file);
            }
        });

        // Wire landscape-mode toggle bar
        const modeBar = document.getElementById('sheet-view-modebar');
        if (modeBar) {
            modeBar.addEventListener('click', e => {
                const btn = e.target.closest('[data-lsmode]');
                if (!btn) return;
                const newMode = btn.dataset.lsmode;
                // BUG-M1 fix: guard against redundant double-click re-renders
                if (newMode === AppState.landscapeMode) return;

                // §5.4: Teardown together-mode state for the ACTIVE file only before switching.
                // Only this file's landscapeMode is changing — non-active files keep their own
                // state. Their step [0] defensive teardown handles cleanup when they next render.
                if (AppState.landscapeMode === 'together' && newMode !== 'together') {
                    const activeFile = AppState.activeFile;
                    if (activeFile) _teardownTogether(activeFile);
                }

                AppState.landscapeMode = newMode;
                modeBar.querySelectorAll('.sheet-modebar-btn').forEach(b => {
                    b.classList.toggle('active', b.dataset.lsmode === AppState.landscapeMode);
                });
                PreviewPanelModule.render(AppState.activeFile);
                SummaryModule.update(); // B34-FE-2: summary sheet count depends on landscapeMode
            });
        }
    },

    _syncModeBar() {
        const modeBar = document.getElementById('sheet-view-modebar');
        if (!modeBar) return;
        // Landscape mode is now auto-detected; always hide the manual toggle bar
        modeBar.style.display = 'none';
    },

    // Call this when printMode changes while in sheet view
    onPrintModeChange() {
        this._syncModeBar(); // Sync separate-btn visibility for booklet mode
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            // §5.4b: Teardown together-mode state before switching print mode.
            // Only tear down files that are NOT in together-mode (their CCW90 is stale/unintended).
            // Files still in together-mode keep their rotations — they are correct for printing
            // and will be re-rendered correctly by _renderSheetView on next view.
            for (const f of AppState.files) {
                if (f._togetherRotations?.size > 0 && f.landscapeMode !== 'together') {
                    _teardownTogether(f);
                }
            }
            // Also teardown the active file unconditionally (it will be immediately re-rendered)
            if (AppState.activeFile._togetherRotations?.size > 0) {
                _teardownTogether(AppState.activeFile);
            }
            PreviewPanelModule.render(AppState.activeFile);
        }
    },
};

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP — Init all modules on DOMContentLoaded
// ═══════════════════════════════════════════════════════════════════

// ── GuideModule ────────────────────────────────────────────────
const GuideModule = {
    _activeTab: 'start',
    _stableSizeByLang: new Map(),

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
        window.addEventListener('resize', () => {
            this._stableSizeByLang.clear();
            if (!document.getElementById('guide-modal')?.classList.contains('hidden')) {
                this._applyStableSize(true);
            }
        });
    },

    open() {
        document.getElementById('guide-modal')?.classList.remove('hidden');
        this.renderCurrentTab();
        this._applyStableSize();
    },

    close() {
        document.getElementById('guide-modal')?.classList.add('hidden');
    },

    renderCurrentTab() {
        const body = document.getElementById('guide-body');
        if (!body) return;
        const content = I18nModule.t(`guide.${this._activeTab}.content`);
        body.innerHTML = typeof content === 'string' ? content : '';
        this._applyStableSize();
    },

    _applyStableSize(forceRemeasure = false) {
        const modal = document.querySelector('#guide-modal .modal-guide');
        if (!modal) return;
        if (document.getElementById('guide-modal')?.classList.contains('hidden')) return;

        const lang = I18nModule._lang || 'vi';
        if (forceRemeasure) {
            this._stableSizeByLang.delete(lang);
        }

        let size = this._stableSizeByLang.get(lang);
        if (!size) {
            size = this._measureStableModalSize(modal);
            this._stableSizeByLang.set(lang, size);
        }

        const { width, height } = size;
        modal.style.width = `${width}px`;
        modal.style.maxWidth = `${width}px`;
        modal.style.height = `${height}px`;
        modal.style.maxHeight = `${height}px`;
    },

    _measureStableModalSize(modal) {
        const liveWidth = Math.max(
            Math.round(modal.getBoundingClientRect().width),
            Math.min(Math.round(window.innerWidth * 0.94), 1080),
        );

        const measure = modal.cloneNode(true);
        measure.querySelectorAll('[id]').forEach(el => el.removeAttribute('id'));
        measure.style.position = 'fixed';
        measure.style.left = '-20000px';
        measure.style.top = '0';
        measure.style.visibility = 'hidden';
        measure.style.pointerEvents = 'none';
        measure.style.width = `${liveWidth}px`;
        measure.style.maxWidth = `${liveWidth}px`;
        measure.style.height = 'auto';
        measure.style.maxHeight = 'none';

        const measureBody = measure.querySelector('.guide-body');
        if (measureBody) {
            const recoveryContent = I18nModule.t('guide.recovery.content');
            measureBody.innerHTML = typeof recoveryContent === 'string' ? recoveryContent : '';
        }

        measure.querySelectorAll('.guide-tab').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.tab === 'recovery');
        });

        document.body.appendChild(measure);
        const measuredHeight = Math.ceil(measure.scrollHeight);
        document.body.removeChild(measure);

        return {
            width: liveWidth,
            height: Math.min(measuredHeight, Math.floor(window.innerHeight * 0.85)),
        };
    },
};

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
                const res = await fetch(`${API_BASE}/printer/settings`, {
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

// ── LangToggleModule ───────────────────────────────────────────
const LangToggleModule = {
    init() {
        document.getElementById('lang-toggle-btn')?.addEventListener('click', () => {
            const next = I18nModule._lang === 'vi' ? 'en' : 'vi';
            I18nModule.setLang(next);
        });
    },
};

document.addEventListener('DOMContentLoaded', () => {
    I18nModule.init(); // Init first so _strings is ready for all other modules
    ThemeModule.init();
    PrinterModule.init();
    UploadModule.init();
    TabsModule.init();
    PageSelectModule.init();
    ZoomModal.init();
    ContextMenu.init();
    PrintModule.init();
    Phase1RecoveryModule.init();
    Phase2RecoveryModule.init();
    CopiesModule.init();
    HistoryModule.init();
    KeyboardModule.init();
    DragReorderModule.init();
    ConfirmPrintModal.init();
    PrintPreviewModule.init();
    ThumbStripModule.init();
    PreviewPanelModule.init();
    ViewModeModule.init();
    SummaryModule.update();
    StepIndicatorModule.update();
    GuideModule.init();
    PrinterSettingsModule.init();
    LangToggleModule.init();
    // (I18nModule.init moved to top)

    // ── Mode select handler ───────────────────────────────────
    document.getElementById('mode-select')?.addEventListener('change', e => {
        AppState.printMode = e.target.value;
        PrintModule.updateButton();
        ViewModeModule.onPrintModeChange();
    });

    // ── Global drag-drop — anywhere on the window ─────────────
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

    // ── Insert-image file picker (from context menu) ──────────
    document.getElementById('insert-image-input')?.addEventListener('change', async e => {
        const files = Array.from(e.target.files || []);
        for (const file of files) {
            await UploadModule._upload(file);
        }
        e.target.value = '';
    });
});
