// ═══════════════════════════════════════════════════════════════════
// myPrinter — app.js
// Structure: AppState + 6 Module Objects + DOMContentLoaded init
// Compatible with file:// (no ES import/export)
// ═══════════════════════════════════════════════════════════════════

const API_BASE = 'http://localhost:8787/api';

// ─── PDF.js worker ──────────────────────────────────────────────────
pdfjsLib.GlobalWorkerOptions.workerSrc =
    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';

// ═══════════════════════════════════════════════════════════════════
// AppState — All application state centralized here
// ═══════════════════════════════════════════════════════════════════
const AppState = {
    selectedPrinter:      null,
    uploadedFile:         null,
    currentJob:           null,
    currentPdfDoc:        null,
    selectedPages:        new Set(),
    singleSidedPages:     new Set(),
    totalPageCount:       0,
    isUserTypingPageRange: false,

    reset() {
        this.uploadedFile         = null;
        this.currentPdfDoc        = null;
        this.currentJob           = null;
        this.selectedPages        = new Set();
        this.singleSidedPages     = new Set();
        this.totalPageCount       = 0;
        this.isUserTypingPageRange = false;
    },

    selectAllPages() {
        this.selectedPages = new Set();
        for (let i = 1; i <= this.totalPageCount; i++) this.selectedPages.add(i);
    },
};

// ═══════════════════════════════════════════════════════════════════
// ToastModule — Popup notifications with color by type
// ═══════════════════════════════════════════════════════════════════
const ToastModule = {
    _timer: null,

    show(message, type = 'info') {
        const toast    = document.getElementById('toast');
        const toastMsg = document.getElementById('toast-message');
        if (!toast || !toastMsg) return;

        toastMsg.textContent = message;
        toast.style.borderColor = {
            success: 'rgba(16, 185, 129, 0.5)',
            error:   'rgba(239, 68, 68, 0.5)',
            info:    'rgba(148, 163, 184, 0.2)',
        }[type] ?? 'rgba(148, 163, 184, 0.2)';

        toast.classList.remove('hidden');
        clearTimeout(this._timer);
        this._timer = setTimeout(() => toast.classList.add('hidden'), 3000);
    },
};

const showToast = (msg, type = 'info') => ToastModule.show(msg, type);

// ═══════════════════════════════════════════════════════════════════
// ThemeModule — Dark / Light / Auto theme toggle
// ═══════════════════════════════════════════════════════════════════
const ThemeModule = {
    init() {
        const saved = localStorage.getItem('theme') || 'auto';
        this._apply(saved);
        this._updateButtons(saved);

        const toggle = document.getElementById('theme-toggle');
        if (!toggle) return;

        toggle.addEventListener('click', (e) => {
            const btn = e.target.closest('.theme-btn');
            if (!btn) return;
            const theme = btn.dataset.theme;
            localStorage.setItem('theme', theme);
            this._apply(theme);
            this._updateButtons(theme);
        });

        window.matchMedia('(prefers-color-scheme: dark)')
            .addEventListener('change', () => {
                if (localStorage.getItem('theme') === 'auto') this._apply('auto');
            });
    },

    _apply(theme) {
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        document.documentElement.setAttribute(
            'data-theme',
            theme === 'auto' ? (prefersDark ? 'dark' : 'light') : theme
        );
    },

    _updateButtons(active) {
        document.querySelectorAll('.theme-btn').forEach(btn =>
            btn.classList.toggle('active', btn.dataset.theme === active)
        );
    },
};

// ═══════════════════════════════════════════════════════════════════
// PrinterModule — Load printer list from API
// ═══════════════════════════════════════════════════════════════════
const PrinterModule = {
    async init() {
        try {
            const response = await fetch(`${API_BASE}/printers`);
            const printers = await response.json();
            const list     = document.getElementById('printer-list');

            if (!printers.length) {
                list.innerHTML = '<div class="loading">Khong tim thay may in nao</div>';
                return;
            }

            list.innerHTML = printers.map(p => `
                <div class="printer-item" data-printer='${JSON.stringify(p)}'>
                    <div class="printer-info">
                        <span class="printer-icon">🖨️</span>
                        <div class="printer-details">
                            <h3>${p.name}</h3>
                            <div class="printer-status">
                                ${p.isDefault ? '<span class="badge badge-info">Mac dinh</span>' : ''}
                                ${p.isDuplex
                                    ? '<span class="badge badge-success">Ho tro 2 mat</span>'
                                    : '<span class="badge badge-warning">Chi 1 mat</span>'}
                                ${this._statusBadge(p.status)}
                            </div>
                        </div>
                    </div>
                </div>
            `).join('');

            document.querySelectorAll('.printer-item').forEach(item => {
                item.addEventListener('click', () => {
                    document.querySelectorAll('.printer-item').forEach(i => i.classList.remove('selected'));
                    item.classList.add('selected');
                    AppState.selectedPrinter = JSON.parse(item.dataset.printer);
                    PrintModule.updateButton();
                });
            });

            const def = printers.find(p => p.isDefault);
            if (def) {
                const defItem = Array.from(document.querySelectorAll('.printer-item'))
                    .find(el => JSON.parse(el.dataset.printer).name === def.name);
                defItem?.click();
            }
        } catch (err) {
            showToast('Loi khi tai danh sach may in: ' + err.message, 'error');
        }
    },

    _statusBadge(status) {
        return {
            3: '<span class="badge badge-success">San sang</span>',
            4: '<span class="badge badge-warning">Dang in</span>',
            7: '<span class="badge badge-danger">Offline</span>',
        }[status] ?? '';
    },
};

// ═══════════════════════════════════════════════════════════════════
// UploadModule — Drag-drop / click file upload
// ═══════════════════════════════════════════════════════════════════
const UploadModule = {
    init() {
        const area  = document.getElementById('upload-area');
        const input = document.getElementById('file-input');

        area.addEventListener('click', () => input.click());
        area.addEventListener('dragover', e => { e.preventDefault(); area.classList.add('drag-over'); });
        area.addEventListener('dragleave', () => area.classList.remove('drag-over'));
        area.addEventListener('drop', async e => {
            e.preventDefault();
            area.classList.remove('drag-over');
            if (e.dataTransfer.files[0]) await this._upload(e.dataTransfer.files[0]);
        });
        input.addEventListener('change', async e => {
            if (e.target.files[0]) await this._upload(e.target.files[0]);
        });
        document.getElementById('remove-file').addEventListener('click', () => this._remove());
    },

    async _upload(file) {
        const ext = '.' + file.name.split('.').pop().toLowerCase();
        if (!['.doc', '.docx', '.pdf'].includes(ext)) {
            showToast('Loai file khong duoc ho tro', 'error');
            return;
        }
        try {
            const formData = new FormData();
            formData.append('file', file);
            document.getElementById('file-status').textContent = 'Dang tai len...';

            const res    = await fetch(`${API_BASE}/upload`, { method: 'POST', body: formData });
            const result = await res.json();
            if (!result.success) { showToast('Loi: ' + result.message, 'error'); return; }

            AppState.uploadedFile = { id: result.fileId, name: result.originalFileName, needsConversion: ext !== '.pdf' };

            if (AppState.uploadedFile.needsConversion) {
                document.getElementById('file-status').textContent = 'Dang chuyen doi sang PDF...';
                await fetch(`${API_BASE}/convert?fileId=${AppState.uploadedFile.id}`, { method: 'POST' });
            }

            document.getElementById('file-name').textContent   = AppState.uploadedFile.name;
            document.getElementById('file-status').textContent = 'Da san sang';
            document.getElementById('upload-area').classList.add('hidden');
            document.getElementById('file-info').classList.remove('hidden');

            await PreviewModule.render(AppState.uploadedFile.id);
            document.getElementById('page-range-section')?.classList.remove('hidden');
            PrintModule.updateButton();
            showToast('Tai file thanh cong!', 'success');
        } catch (err) {
            showToast('Loi khi tai file: ' + err.message, 'error');
        }
    },

    _remove() {
        AppState.reset();
        document.getElementById('file-input').value = '';
        document.getElementById('upload-area').classList.remove('hidden');
        document.getElementById('file-info').classList.add('hidden');
        const sidebar = document.getElementById('preview-sidebar');
        if (sidebar) sidebar.style.display = 'none';
        document.getElementById('page-range-section')?.classList.add('hidden');
        const pi = document.getElementById('page-range-input');
        if (pi) pi.value = '';
        PrintModule.updateButton();
    },
};

// ═══════════════════════════════════════════════════════════════════
// PreviewModule — PDF thumbnail grid with lazy IntersectionObserver
// ═══════════════════════════════════════════════════════════════════
const PreviewModule = {
    _observer: null,

    async render(fileId) {
        const sidebar = document.getElementById('preview-sidebar');
        const grid    = document.getElementById('sidebar-preview-grid');
        if (!sidebar || !grid) return;

        sidebar.style.display = 'flex';
        grid.innerHTML = '<div class="loading">Dang tai preview...</div>';

        try {
            const blob     = await fetch(`${API_BASE}/file/${fileId}`).then(r => r.blob());
            const url      = URL.createObjectURL(blob);
            const loadTask = pdfjsLib.getDocument(url);
            AppState.currentPdfDoc  = await loadTask.promise;
            AppState.totalPageCount = AppState.currentPdfDoc.numPages;
            AppState.selectAllPages();

            const countEl = document.getElementById('sidebar-page-count');
            if (countEl) countEl.textContent = `${AppState.totalPageCount} trang`;
            PageSelectModule.updateDisplay();

            grid.innerHTML = '';
            this._observer?.disconnect();
            this._observer = new IntersectionObserver(entries => {
                entries.forEach(entry => {
                    if (entry.isIntersecting && !entry.target.dataset.rendered) {
                        const n = parseInt(entry.target.dataset.pageNumber);
                        this._renderCanvas(entry.target, n);
                        this._observer.unobserve(entry.target);
                    }
                });
            }, { rootMargin: '100px' });

            for (let i = 1; i <= AppState.totalPageCount; i++) {
                const thumb = this._createPlaceholder(i);
                grid.appendChild(thumb);
                this._observer.observe(thumb);
            }

            showToast(`Da tai ${AppState.totalPageCount} trang`, 'success');
        } catch (err) {
            console.error('Error rendering PDF:', err);
            showToast('Loi khi tai preview PDF: ' + err.message, 'error');
        }
    },

    _createPlaceholder(pageNum) {
        const div = document.createElement('div');
        div.className = 'page-thumbnail selected';
        div.dataset.pageNumber = pageNum;
        div.style.cssText = 'position:relative;cursor:pointer;border:2px solid #22c55e;border-radius:8px;background:rgba(100,116,139,0.1);transition:all 0.2s;min-height:80px;';

        const label = document.createElement('div');
        label.style.cssText = 'position:absolute;bottom:4px;right:4px;background:rgba(0,0,0,0.8);color:white;padding:3px 6px;border-radius:4px;font-size:11px;font-weight:600;';
        label.textContent = pageNum;
        div.appendChild(label);

        div.addEventListener('click', e => {
            if (e.detail === 1) setTimeout(() => { if (e.detail === 1) PageSelectModule.toggle(pageNum); }, 200);
        });
        div.addEventListener('dblclick', () => ZoomModal.open(pageNum));
        div.addEventListener('contextmenu', e => { e.preventDefault(); ContextMenu.show(e, pageNum); });
        return div;
    },

    async _renderCanvas(thumb, pageNum) {
        try {
            const page     = await AppState.currentPdfDoc.getPage(pageNum);
            const viewport = page.getViewport({ scale: 0.6 });
            const canvas   = document.createElement('canvas');
            const ctx      = canvas.getContext('2d');
            canvas.width  = viewport.width;
            canvas.height = viewport.height;
            canvas.style.cssText = 'width:100%!important;height:auto!important;display:block;border-radius:6px;';
            await page.render({ canvasContext: ctx, viewport }).promise;
            thumb.insertBefore(canvas, thumb.firstChild);
            thumb.dataset.rendered = '1';
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

        input.addEventListener('focus', () => { AppState.isUserTypingPageRange = true; });
        input.addEventListener('blur',  () => { AppState.isUserTypingPageRange = false; this.updateDisplay(); });
        input.addEventListener('input', e => {
            AppState.isUserTypingPageRange = true;
            const text = e.target.value.trim();
            AppState.selectedPages = text ? this._parseRange(text) : (() => { AppState.selectAllPages(); return AppState.selectedPages; })();
            if (!text) AppState.selectAllPages();
            else AppState.selectedPages = this._parseRange(text);
            PreviewModule.updateThumbnails();
            this._updateTexts();
            PrintModule.updateButton();
        });
    },

    toggle(pageNum) {
        if (AppState.selectedPages.has(pageNum)) AppState.selectedPages.delete(pageNum);
        else AppState.selectedPages.add(pageNum);
        PreviewModule.updateThumbnails();
        this.updateDisplay();
        PrintModule.updateButton();
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
    },

    _parseRange(text) {
        const pages = new Set();
        text.split(',').forEach(part => {
            part = part.trim();
            if (part.includes('-')) {
                const [a, b] = part.split('-').map(s => parseInt(s.trim()));
                if (!isNaN(a) && !isNaN(b))
                    for (let i = Math.min(a,b); i <= Math.max(a,b); i++)
                        if (i >= 1 && i <= AppState.totalPageCount) pages.add(i);
            } else {
                const n = parseInt(part);
                if (!isNaN(n) && n >= 1 && n <= AppState.totalPageCount) pages.add(n);
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
            showToast('Da chon tat ca in 2 mat', 'success');
        });

        document.getElementById('all-single-btn')?.addEventListener('click', () => {
            AppState.selectAllPages(); AppState.singleSidedPages = new Set(AppState.selectedPages);
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay(); this._updateModalStyles();
            showToast('Da chon tat ca in 1 mat', 'success');
        });

        // FIX: Deselect All truly empties selection
        document.getElementById('deselect-all-btn')?.addEventListener('click', () => {
            AppState.selectedPages.clear(); AppState.singleSidedPages.clear();
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay(); this._updateModalStyles();
            PrintModule.updateButton();
            showToast('Da bo chon tat ca. Chon trang de in.', 'info');
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
        badge.textContent = isSingle ? '1 MAT' : '2 MAT';
        if (!isSel) badge.style.opacity = '0.3';

        const canvas = document.createElement('canvas');
        const vp     = page.getViewport({ scale: 1.2 });
        canvas.width  = vp.width; canvas.height = vp.height;
        canvas.style.cssText = 'width:100%;height:auto;display:block;border-radius:6px;';
        page.render({ canvasContext: canvas.getContext('2d'), viewport: vp });

        div.appendChild(header); div.appendChild(badge); div.appendChild(canvas);

        div.addEventListener('click', e => {
            if (e.button !== 0) return;
            if (AppState.selectedPages.has(n)) AppState.selectedPages.delete(n);
            else AppState.selectedPages.add(n);
            PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay();
            PrintModule.updateButton(); this._updateModalStyles();
        });
        div.addEventListener('contextmenu', e => { e.preventDefault(); ContextMenu.show(e, n); });
        return div;
    },

    _updateModalStyles() {
        document.querySelectorAll('.modal-page-container').forEach(el => {
            const n = parseInt(el.dataset.page);
            const isSel    = AppState.selectedPages.has(n);
            const isSingle = AppState.singleSidedPages.has(n);
            el.style.borderColor = isSel ? (isSingle ? '#3b82f6' : '#22c55e') : 'transparent';
            const badge = el.querySelector('.single-sided-badge, .double-sided-badge');
            if (badge) {
                badge.className   = isSingle ? 'single-sided-badge' : 'double-sided-badge';
                badge.textContent = isSingle ? '1 MAT' : '2 MAT';
                badge.style.opacity = isSel ? '1' : '0.3';
            }
        });
    },
};

// ═══════════════════════════════════════════════════════════════════
// ContextMenu — Right-click per-page single/double-sided selection
// ═══════════════════════════════════════════════════════════════════
const ContextMenu = {
    _currentPage: null,

    init() {
        document.addEventListener('click', e => { if (!e.target.closest('.context-menu')) this.hide(); });
        document.querySelectorAll('.context-menu-item').forEach(item => {
            item.addEventListener('click', e => { e.stopPropagation(); this._handleAction(item.dataset.action); });
        });
    },

    show(event, pageNum) {
        this._currentPage = pageNum;
        const menu = document.getElementById('page-context-menu');
        document.getElementById('context-menu-header').textContent = `Trang ${pageNum}`;
        const isSingle = AppState.singleSidedPages.has(pageNum);
        document.getElementById('check-double').textContent = isSingle ? '' : '✓';
        document.getElementById('check-single').textContent = isSingle ? '✓' : '';
        menu.style.left = `${event.clientX}px`;
        menu.style.top  = `${event.clientY}px`;
        menu.classList.remove('hidden');
        const rect = menu.getBoundingClientRect();
        if (rect.right  > window.innerWidth)  menu.style.left = `${window.innerWidth  - rect.width  - 10}px`;
        if (rect.bottom > window.innerHeight) menu.style.top  = `${window.innerHeight - rect.height - 10}px`;
    },

    hide() { document.getElementById('page-context-menu').classList.add('hidden'); this._currentPage = null; },

    _handleAction(action) {
        const n = this._currentPage;
        switch (action) {
            case 'all-double-sided':
                AppState.selectAllPages(); AppState.singleSidedPages.clear();
                showToast('Da chon tat ca in 2 mat', 'success'); break;
            case 'all-single-sided':
                AppState.selectAllPages(); AppState.singleSidedPages = new Set(AppState.selectedPages);
                showToast('Da chon tat ca in 1 mat', 'success'); break;
            case 'deselect-all':
                // FIX: truly empties selection
                AppState.selectedPages.clear(); AppState.singleSidedPages.clear();
                PrintModule.updateButton();
                showToast('Da bo chon tat ca', 'info'); break;
            case 'double-sided':
                if (n !== null) { if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n); AppState.singleSidedPages.delete(n); showToast(`Trang ${n} se in 2 mat`, 'info'); } break;
            case 'single-sided':
                if (n !== null) { if (!AppState.selectedPages.has(n)) AppState.selectedPages.add(n); AppState.singleSidedPages.add(n); showToast(`Trang ${n} se in 1 mat`, 'info'); } break;
        }
        PreviewModule.updateThumbnails(); PageSelectModule.updateDisplay();
        ZoomModal._updateModalStyles(); this.hide();
    },
};

// ═══════════════════════════════════════════════════════════════════
// CopiesModule — Copies counter + collate toggle
// ═══════════════════════════════════════════════════════════════════
const CopiesModule = {
    _copies: 1,
    _collate: true,

    get copies() { return this._copies; },
    get collate() { return this._collate; },

    init() {
        const dec = document.getElementById('copies-dec');
        const inc = document.getElementById('copies-inc');
        const chk = document.getElementById('collate-check');
        if (!dec || !inc) return;

        dec.addEventListener('click', () => {
            if (this._copies > 1) { this._copies--; this._update(); }
        });
        inc.addEventListener('click', () => {
            if (this._copies < 99) { this._copies++; this._update(); }
        });
        chk?.addEventListener('change', (e) => {
            this._collate = e.target.checked;
        });
    },

    _update() {
        const el = document.getElementById('copies-display');
        if (el) el.textContent = this._copies;
        // Show collate option only when copies > 1
        const collateLabel = document.getElementById('collate-label');
        if (collateLabel) collateLabel.style.display = this._copies > 1 ? 'flex' : 'none';
    },

    reset() { this._copies = 1; this._collate = true; this._update(); },
};

// ═══════════════════════════════════════════════════════════════════
// PrintModule — Print command, flip instructions, continue print
// ═══════════════════════════════════════════════════════════════════
const PrintModule = {
    init() {
        document.getElementById('print-btn').addEventListener('click', () => this._startPrint());
        document.getElementById('continue-btn')?.addEventListener('click', async () => {
            await this._continuePrint();
            document.getElementById('flip-modal').classList.add('hidden');
        });
    },

    updateButton() {
        const btn = document.getElementById('print-btn');
        btn.disabled = !AppState.selectedPrinter || !AppState.uploadedFile || AppState.selectedPages.size === 0;
    },

    async _startPrint() {
        if (!AppState.selectedPrinter) { showToast('Vui long chon may in', 'error'); return; }
        if (!AppState.uploadedFile)    { showToast('Vui long tai len file can in', 'error'); return; }
        if (AppState.selectedPages.size === 0) { showToast('Vui long chon it nhat 1 trang de in', 'error'); return; }

        const mode  = document.querySelector('input[name="print-mode"]:checked').value;
        const total = AppState.totalPageCount;
        const sel   = AppState.selectedPages;
        const pageRange = (sel.size > 0 && sel.size < total)
            ? Array.from(sel).sort((a,b) => a-b).join(',')
            : null;

        const body = {
            fileId:           AppState.uploadedFile.id,
            printerName:      AppState.selectedPrinter.name,
            mode:             mode === 'normal' ? 0 : 1,
            pageRange,
            singleSidedPages: AppState.singleSidedPages.size > 0 ? Array.from(AppState.singleSidedPages) : null,
            copies:           CopiesModule.copies,
            collate:          CopiesModule.collate,
        };

        try {
            showToast('Dang gui lenh in...', 'info');
            const res    = await fetch(`${API_BASE}/print`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
            const result = await res.json();
            if (!result.success) { showToast('Loi: ' + result.message, 'error'); return; }
            if (result.jobState?.waitingForFlip) {
                AppState.currentJob = result.jobState;
                this._showFlipModal(result.jobState.instruction);
                showToast('Da in mat le! Vui long lam theo huong dan.', 'info');
            } else {
                showToast('In thanh cong!', 'success');
            }
        } catch (err) {
            showToast('Loi khi in: ' + err.message, 'error');
        }
    },

    async _continuePrint() {
        try {
            showToast('Dang in mat chan...', 'info');
            const res    = await fetch(`${API_BASE}/print/continue?jobId=${AppState.currentJob.jobId}`, { method: 'POST' });
            const result = await res.json();
            if (result.success) { showToast('In hoan tat!', 'success'); AppState.currentJob = null; }
            else showToast('Loi: ' + result.message, 'error');
        } catch (err) {
            showToast('Loi khi tiep tuc in: ' + err.message, 'error');
        }
    },

    _showFlipModal(instruction) {
        document.getElementById('instruction-text').textContent =
            'Lay giay ra va dat thang lai vao khay (mat da in huong xuong). KHONG can xoay giay.';
        document.getElementById('instruction-visual').innerHTML = `
            <svg width="300" height="200" viewBox="0 0 300 200" style="margin:0 auto;">
                <defs><marker id="ah2" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                    <polygon points="0 0,10 3.5,0 7" fill="#10b981"/></marker></defs>
                <rect x="100" y="60" width="100" height="80" fill="#f8fafc" stroke="#64748b" stroke-width="2" rx="2"/>
                <rect x="103" y="63" width="94" height="74" fill="white" stroke="#94a3b8" stroke-width="1"/>
                <text x="150" y="100" font-size="16" text-anchor="middle" fill="#94a3b8">Giay da in</text>
                <path d="M 150 145 L 150 175" stroke="#10b981" stroke-width="4" fill="none" marker-end="url(#ah2)"/>
                <rect x="80" y="180" width="140" height="15" fill="#e2e8f0" stroke="#667eea" stroke-width="2" rx="3"/>
                <text x="150" y="192" font-size="10" text-anchor="middle" fill="#667eea">Khay giay</text>
                <text x="150" y="35" font-size="14" text-anchor="middle" fill="#10b981" font-weight="bold">↓ Dat thang lai (khong xoay)</text>
            </svg>`;
        document.getElementById('flip-modal').classList.remove('hidden');
    },
};

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP — Init all modules on DOMContentLoaded
// ═══════════════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    ThemeModule.init();
    PrinterModule.init();
    UploadModule.init();
    PageSelectModule.init();
    ZoomModal.init();
    ContextMenu.init();
    PrintModule.init();
    CopiesModule.init();
});
