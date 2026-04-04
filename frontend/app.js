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
    pageOrder:            [], // 1-based page numbers in display/print order; empty = natural order

    reset() {
        this.uploadedFile         = null;
        this.currentPdfDoc        = null;
        this.currentJob           = null;
        this.selectedPages        = new Set();
        this.singleSidedPages     = new Set();
        this.totalPageCount       = 0;
        this.isUserTypingPageRange = false;
        this.pageOrder            = [];
    },

    selectAllPages() {
        this.selectedPages = new Set();
        for (let i = 1; i <= this.totalPageCount; i++) this.selectedPages.add(i);
    },
};

// ═══════════════════════════════════════════════════════════════════
// ═══════════════════════════════════════════════════════════════════
// ToastModule — Upgraded sliding toast notifications (S)
// ═══════════════════════════════════════════════════════════════════
const ToastModule = {
    _MAX: 3,

    show(message, type = 'info', duration = 3000) {
        const container = document.getElementById('toast-container');
        if (!container) return;

        // Enforce max stack
        const existing = container.querySelectorAll('.toast-item:not(.dismissing)');
        if (existing.length >= this._MAX) {
            this._dismiss(existing[0]);
        }

        const icons = { success: '✅', error: '❌', info: 'ℹ️' };
        const icon  = icons[type] || 'ℹ️';

        const item = document.createElement('div');
        item.className = `toast-item toast-item-border-${type}`;
        item.innerHTML = `
            <div class="toast-item-body">
                <span class="toast-item-icon">${icon}</span>
                <span class="toast-item-msg">${message}</span>
            </div>
            <div class="toast-countdown toast-countdown-${type}"
                 style="animation-duration: ${duration}ms;"></div>
        `;

        item.addEventListener('click', () => this._dismiss(item));
        container.appendChild(item);

        // Screen reader announce
        const sr = document.getElementById('sr-status');
        if (sr) { sr.textContent = message; setTimeout(() => { sr.textContent = ''; }, 1000); }

        setTimeout(() => this._dismiss(item), duration);
    },

    _dismiss(item) {
        if (!item || item.classList.contains('dismissing')) return;
        item.classList.add('dismissing');
        item.addEventListener('animationend', () => item.remove(), { once: true });
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
                <div class="printer-item" data-printer='${JSON.stringify(p)}' data-name="${p.name.replace(/"/g, '&quot;')}">
                    <div class="printer-info">
                        <span class="printer-icon">🖨️</span>
                        <div class="printer-details">
                            <h3>${p.name}</h3>
                            <div class="printer-status">
                                ${this._statusBadge(p.status)}
                                ${p.isDefault
                                    ? '<span class="badge badge-info" data-tooltip="Máy in mặc định của Windows">⭐ Mặc định</span>'
                                    : ''}
                                ${p.isDuplex
                                    ? '<span class="badge badge-success" data-tooltip="Máy in này có thể in 2 mặt tự động">Hỗ trợ 2 mặt</span>'
                                    : '<span class="badge badge-warning" data-tooltip="Máy in này chỉ in 1 mặt — dùng chế độ thủ công">Chỉ 1 mặt</span>'}
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
                    StepIndicatorModule.update();
                    SRModule.announce(`Đã chọn máy in: ${AppState.selectedPrinter.name}`);
                });
            });

            const def = printers.find(p => p.isDefault);
            if (def) {
                const defItem = Array.from(document.querySelectorAll('.printer-item'))
                    .find(el => JSON.parse(el.dataset.printer).name === def.name);
                defItem?.click();
            }
            
            this.startPolling();
        } catch (err) {
            const card = document.getElementById('printer-list')?.closest('.card');
            showCardError(card, `Lỗi tải danh sách máy in: ${err.message}`, () => PrinterModule.init());
        }
    },

    startPolling() {
        setInterval(async () => {
            try {
                const res = await fetch(`${API_BASE}/printers`);
                if (!res.ok) return;
                const printers = await res.json();
                printers.forEach(p => {
                    const safeName = p.name.replace(/"/g, '&quot;');
                    const card = document.querySelector(`.printer-item[data-name="${safeName}"]`);
                    if (!card) return;
                    const badge = card.querySelector('.printer-status');
                    if (badge) {
                        badge.innerHTML = `
                            ${this._statusBadge(p.status)}
                            ${p.isDefault ? '<span class="badge badge-info" data-tooltip="Máy in mặc định của Windows">⭐ Mặc định</span>' : ''}
                            ${p.isDuplex
                                ? '<span class="badge badge-success" data-tooltip="Máy in này có thể in 2 mặt tự động">Hỗ trợ 2 mặt</span>'
                                : '<span class="badge badge-warning" data-tooltip="Máy in này chỉ in 1 mặt — dùng chế độ thủ công">Chỉ 1 mặt</span>'}
                        `;
                    }
                });
            } catch { /* silently ignore poll failures */ }
        }, 30_000);
    },

    _statusBadge(status) {
        const map = {
            3: { cls: 'online',  label: 'Sẵn sàng', tip: 'Máy in đang hoạt động bình thường' },
            4: { cls: 'busy',    label: 'Đang in',   tip: 'Máy in đang xử lý lệnh in khác' },
            7: { cls: 'offline', label: 'Offline',   tip: 'Máy in không kết nối. Kiểm tra dây cáp và bật máy.' },
        };
        const s = map[status];
        if (!s) return '<span class="printer-status-dot unknown" data-tooltip="Trạng thái không xác định"></span>';
        return `<span class="printer-status-dot ${s.cls}" data-tooltip="${s.tip}"></span><span class="badge badge-${s.cls === 'online' ? 'success' : s.cls === 'busy' ? 'warning' : 'danger'}" data-tooltip="${s.tip}">${s.label}</span>`;
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
        const uploadCard = document.getElementById('upload-area')?.closest('.card');
        if (uploadCard) clearCardError(uploadCard);
        try {
            const formData = new FormData();
            formData.append('file', file);
            document.getElementById('file-status').textContent = 'Dang tai len...';

            // Show progress bar
            const wrap = document.getElementById('upload-progress-wrap');
            const bar  = document.getElementById('upload-progress-bar');
            if (wrap) wrap.classList.remove('hidden');
            if (bar) bar.classList.add('uploading');

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
                        reject(new Error(`Upload failed: ${xhr.status}`));
                    }
                };
                xhr.onerror = () => reject(new Error('Network error during upload'));
                xhr.send(formData);
            });

            if (wrap) wrap.classList.add('hidden');
            if (bar) bar.classList.remove('uploading');
            if (bar) bar.style.width = '0%';

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
            StepIndicatorModule.update();
            SRModule.announce(`Đã tải file ${AppState.uploadedFile.name}, ${AppState.totalPageCount} trang`);
        } catch (err) {
            const card = document.getElementById('upload-area')?.closest('.card');
            showCardError(card, `Lỗi khi tải file: ${err.message}`, () => document.getElementById('file-input')?.click());
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
        StepIndicatorModule.update();
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
        grid.innerHTML = `
            <div class="skeleton skeleton-thumb"></div>
            <div class="skeleton skeleton-thumb"></div>
            <div class="skeleton skeleton-thumb"></div>
            <div class="skeleton skeleton-thumb"></div>
        `;

        try {
            const blob     = await fetch(`${API_BASE}/file/${fileId}`).then(r => r.blob());
            const url      = URL.createObjectURL(blob);
            const loadTask = pdfjsLib.getDocument(url);
            AppState.currentPdfDoc  = await loadTask.promise;
            AppState.totalPageCount = AppState.currentPdfDoc.numPages;
            AppState.pageOrder = Array.from({ length: AppState.totalPageCount }, (_, i) => i + 1);
            AppState.selectAllPages();

            const countEl = document.getElementById('sidebar-page-count');
            if (countEl) countEl.textContent = `${AppState.totalPageCount} trang`;
            PageSelectModule.updateDisplay();

            grid.innerHTML = '';
            grid.setAttribute('role', 'listbox');
            grid.setAttribute('aria-label', 'Danh sách trang');
            grid.setAttribute('aria-multiselectable', 'true');
            this._observer?.disconnect();
            HoverPreviewModule.clearCache();
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
        div.setAttribute('tabindex', '0');
        div.setAttribute('role', 'option');
        div.setAttribute('aria-label', `Trang ${pageNum}`);
        div.setAttribute('aria-selected', 'true');

        const label = document.createElement('div');
        label.style.cssText = 'position:absolute;bottom:4px;right:4px;background:rgba(0,0,0,0.8);color:white;padding:3px 6px;border-radius:4px;font-size:11px;font-weight:600;';
        label.textContent = pageNum;
        div.appendChild(label);

        div.addEventListener('click', e => {
            if (e.detail === 1) setTimeout(() => { if (e.detail === 1) PageSelectModule.toggle(pageNum); }, 200);
        });
        div.addEventListener('dblclick', () => ZoomModal.open(pageNum));
        div.addEventListener('contextmenu', e => { e.preventDefault(); ContextMenu.show(e, pageNum); });
        HoverPreviewModule.attach(div, pageNum);
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

            // Add orientation badge (1)
            const isLandscape = viewport.width > viewport.height;
            const badge = document.createElement('div');
            badge.className = `orientation-badge${isLandscape ? ' landscape' : ''}`;
            badge.textContent = isLandscape ? '▭ Ngang' : '▯ Dọc';
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
            clearTimeout(_rangeDebounce);
            _rangeDebounce = setTimeout(() => {
                const text = e.target.value.trim();
                if (!text) {
                    AppState.selectAllPages();
                    input.style.borderColor = '';
                } else {
                    const parsed = this._parseRange(text);
                    if (parsed.size === 0 && text.length > 0) {
                        // Invalid range — show red border, don't change selection
                        input.style.borderColor = 'rgba(239, 68, 68, 0.6)';
                    } else {
                        AppState.selectedPages = parsed;
                        input.style.borderColor = '';
                    }
                }
                PreviewModule.updateThumbnails();
                this._updateTexts();
                PrintModule.updateButton();
                StepIndicatorModule.update();
            }, 200);
        });
    },

    toggle(pageNum) {
        if (AppState.selectedPages.has(pageNum)) AppState.selectedPages.delete(pageNum);
        else AppState.selectedPages.add(pageNum);

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
        SRModule.announce(all ? 'Đã chọn tất cả trang' : `Đã chọn ${AppState.selectedPages.size} trang`);
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
            showToast('Đã xóa lịch sử', 'info');
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
        showToast('Đã xóa mục lịch sử', 'info');
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
            const modeInput = document.querySelector(`input[name="print-mode"][value="${item.mode}"]`);
            if (modeInput) modeInput.click();
        }

        // Restore copies
        if (item.copies) {
            CopiesModule._copies = item.copies;
            CopiesModule._update();
        }

        // Restore page range
        if (item.pageRange && AppState.totalPageCount > 0) {
            const input = document.getElementById('page-range-input');
            if (input) {
                input.value = item.pageRange;
                input.dispatchEvent(new Event('input'));
            }
        }

        showToast(`Đã khôi phục cài đặt in "${item.file}"`, 'info');
        PrintModule.updateButton();
    },

    _render() {
        const container = document.getElementById('history-list');
        if (!container) return;
        const items = this._load();
        if (items.length === 0) {
            container.innerHTML = '<div class="history-empty">Chưa có lịch sử in</div>';
            return;
        }
        const modeLabel = { normal: '2 mặt', booklet: 'Sách A5', simplex: '1 mặt' };
        container.innerHTML = items.map((item, idx) => `
            <div class="history-item">
                <div class="history-item-actions">
                    <button class="history-action-btn history-reprint-btn" data-idx="${idx}" title="In lại">🔁</button>
                    <button class="history-action-btn" data-delete="${idx}" title="Xóa">✕</button>
                </div>
                <div class="history-file">📄 ${item.file}</div>
                <div class="history-meta">🖨️ ${item.printer} · ${item.pages} trang · ${modeLabel[item.mode] || item.mode} · ${item.copies} bản</div>
                <div class="history-time">${item.time}</div>
            </div>
        `).join('');

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
    init() {
        const btn = document.getElementById('print-btn');
        btn.addEventListener('click', (e) => {
            // Cancel mode (A): if job is waiting for flip, cancel it
            if (btn.dataset.mode === 'cancellable' && AppState.currentJob?.jobId) {
                (async () => {
                    try {
                        await fetch(`${API_BASE}/print/cancel?jobId=${AppState.currentJob.jobId}`, { method: 'DELETE' });
                        showToast('Đã hủy lệnh in', 'info');
                        SRModule.announce('Đã hủy lệnh in');
                    } catch {
                        showToast('Không thể hủy lệnh in', 'error');
                    }
                    AppState.currentJob = null;
                    btn.dataset.mode = '';
                    btn.classList.remove('cancellable');
                    btn.innerHTML = '<span class="btn-icon">🖨️</span> Bắt Đầu In';
                    document.getElementById('flip-modal')?.classList.add('hidden');
                    PrintModule.updateButton();
                })();
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
    },

    updateButton() {
        const btn = document.getElementById('print-btn');
        btn.disabled = !AppState.selectedPrinter || !AppState.uploadedFile || AppState.selectedPages.size === 0;
        SummaryModule.update();
    },

    async _startPrint() {
        if (!AppState.selectedPrinter) { showToast('Vui long chon may in', 'error'); return; }
        if (!AppState.uploadedFile)    { showToast('Vui long tai len file can in', 'error'); return; }
        if (AppState.selectedPages.size === 0) { showToast('Vui long chon it nhat 1 trang de in', 'error'); return; }

        // Show confirmation dialog (7)
        const confirmed = await ConfirmPrintModal.show();
        if (!confirmed) return;

        const btn = document.getElementById('print-btn');
        const originalText = btn.textContent;
        btn.disabled = true;
        btn.textContent = '⏳ Đang gửi lệnh in...';
        btn.style.opacity = '0.8';

        const mode  = document.querySelector('input[name="print-mode"]:checked').value;
        const total = AppState.totalPageCount;
        const sel   = AppState.selectedPages;
        const pageRange = (sel.size > 0 && sel.size < total)
            ? Array.from(sel).sort((a,b) => a-b).join(',')
            : null;

        const body = {
            fileId:           AppState.uploadedFile.id,
            printerName:      AppState.selectedPrinter.name,
            mode:             mode === 'normal' ? 0 : (mode === 'booklet' ? 1 : 2),
            pageRange,
            singleSidedPages: AppState.singleSidedPages.size > 0 ? Array.from(AppState.singleSidedPages) : null,
            copies:           CopiesModule.copies,
            collate:          CopiesModule.collate,
            // Drag-reorder (I)
            pageOrder:        AppState.pageOrder.length > 0 ? AppState.pageOrder : null,
        };

        try {
            showToast('Dang gui lenh in...', 'info');
            SRModule.announce('Đang gửi lệnh in...');
            const res    = await fetch(`${API_BASE}/print`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
            const result = await res.json();
            if (!result.success) { 
                showToast('Loi: ' + result.message, 'error'); 
                btn.disabled = false;
                btn.textContent = originalText;
                btn.style.opacity = '';
                return; 
            }
            if (result.jobState?.waitingForFlip) {
                AppState.currentJob = result.jobState;
                this._showFlipModal(result.jobState.instruction);
                // Show cancel button (A)
                btn.dataset.mode = 'cancellable';
                btn.classList.add('cancellable');
                btn.innerHTML = '<span class="btn-icon">✕</span> Huỷ In';
                btn.disabled = false;
                btn.style.opacity = '1';
                showToast('Da in mat le! Vui long lam theo huong dan.', 'info');
            } else {
                // SUCCESS: flash button green
                btn.textContent = '✓ Đã gửi lệnh in!';
                btn.style.background = 'linear-gradient(135deg, #10b981, #059669)';
                btn.style.opacity = '1';
                showToast('In thành công!', 'success');
                SRModule.announce('In thành công!');
                
                HistoryModule.add({
                    file:        AppState.uploadedFile.name,
                    fileId:      AppState.uploadedFile.id,
                    printer:     AppState.selectedPrinter.name,
                    printerData: AppState.selectedPrinter,
                    pages:       AppState.selectedPages.size,
                    pageRange,
                    mode:        document.querySelector('input[name="print-mode"]:checked')?.value || 'normal',
                    copies:      CopiesModule.copies,
                    collate:     CopiesModule.collate,
                });

                setTimeout(() => {
                    btn.disabled = false;
                    btn.textContent = originalText;
                    btn.style.background = '';
                    btn.style.opacity = '';
                    PrintModule.updateButton();
                }, 2000);
            }
        } catch (err) {
            showToast('Loi khi in: ' + err.message, 'error');
            const actionSec = document.querySelector('.action-section') || document.getElementById('print-btn')?.closest('.card');
            showCardError(actionSec, `Lệnh in thất bại: ${err.message}`, () => document.getElementById('print-btn')?.click());
            btn.disabled = false;
            btn.textContent = originalText;
            btn.style.opacity = '';
        }
    },

    async _continuePrint() {
        try {
            showToast('Dang in mat chan...', 'info');
            const res    = await fetch(`${API_BASE}/print/continue?jobId=${AppState.currentJob.jobId}`, { method: 'POST' });
            const result = await res.json();
            if (result.success) {
                showToast('In hoan tat!', 'success');
                AppState.currentJob = null;
                // Reset cancel button (A)
                const btn = document.getElementById('print-btn');
                btn.dataset.mode = '';
                btn.classList.remove('cancellable');
                btn.innerHTML = '<span class="btn-icon">🖨️</span> Bắt Đầu In';
                PrintModule.updateButton();
            }
            else showToast('Loi: ' + result.message, 'error');
        } catch (err) {
            showToast('Loi khi tiep tuc in: ' + err.message, 'error');
        }
    },

    _showFlipModal(instruction) {
        // Animated SVG (3)
        document.getElementById('instruction-visual').innerHTML = `
            <svg width="260" height="180" viewBox="0 0 260 180">
                <defs>
                    <marker id="arrow-flip" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                        <polygon points="0 0,10 3.5,0 7" fill="#10b981"/>
                    </marker>
                </defs>
                <!-- Paper group with animation -->
                <g class="flip-paper-anim">
                    <rect x="80" y="40" width="100" height="80" fill="#f8fafc" stroke="#64748b" stroke-width="2" rx="2"/>
                    <rect x="83" y="43" width="94" height="74" fill="white" stroke="#94a3b8" stroke-width="1"/>
                    <text x="130" y="82" font-size="13" text-anchor="middle" fill="#94a3b8">Giấy đã in</text>
                    <text x="130" y="98" font-size="11" text-anchor="middle" fill="#cbd5e1">mặt 1 ✓</text>
                </g>
                <!-- Arrow down -->
                <path d="M 130 125 L 130 155" stroke="#10b981" stroke-width="3" fill="none" marker-end="url(#arrow-flip)"/>
                <!-- Tray -->
                <rect x="60" y="158" width="140" height="14" fill="#e2e8f0" stroke="#667eea" stroke-width="2" rx="3"/>
                <text x="130" y="169" font-size="10" text-anchor="middle" fill="#667eea">Khay giấy</text>
                <!-- Label top -->
                <text x="130" y="25" font-size="12" text-anchor="middle" fill="#10b981" font-weight="bold">Lấy ra → Lật → Đặt lại</text>
            </svg>
        `;

        document.getElementById('instruction-text').textContent =
            instruction || 'Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.';

        // Reset checklist
        ['flip-check-1', 'flip-check-2', 'flip-check-3'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.checked = false;
        });
        const continueBtn = document.getElementById('continue-btn');
        if (continueBtn) continueBtn.classList.remove('all-checked');

        // Checklist → enable button when all checked
        const checkboxes = document.querySelectorAll('.flip-checkbox');
        const updateContinueBtn = () => {
            const allChecked = Array.from(checkboxes).every(cb => cb.checked);
            continueBtn?.classList.toggle('all-checked', allChecked);
        };
        checkboxes.forEach(cb => {
            cb.removeEventListener('change', updateContinueBtn);
            cb.addEventListener('change', updateContinueBtn);
        });

        // Optional timer
        let _timerInterval = null;
        const timerEnable = document.getElementById('flip-timer-enable');
        const timerBar    = document.getElementById('flip-timer-bar');
        const timerFill   = document.getElementById('flip-timer-fill');

        if (timerEnable) {
            timerEnable.checked = false;
            timerEnable.onchange = () => {
                if (timerEnable.checked) {
                    timerBar?.classList.remove('hidden');
                    let remaining = 30;
                    if (timerFill) {
                        timerFill.style.transition = 'none';
                        timerFill.style.width = '100%';
                        setTimeout(() => {
                            timerFill.style.transition = 'width 30s linear';
                            timerFill.style.width = '0%';
                        }, 50);
                    }
                    _timerInterval = setInterval(() => {
                        remaining--;
                        if (remaining <= 0) {
                            clearInterval(_timerInterval);
                            document.getElementById('continue-btn')?.click();
                        }
                    }, 1000);
                } else {
                    clearInterval(_timerInterval);
                    timerBar?.classList.add('hidden');
                }
            };
        }

        document.getElementById('flip-modal').classList.remove('hidden');
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

        const pages   = AppState.selectedPages.size;
        const copies  = CopiesModule?.copies || 1;
        const mode    = document.querySelector('input[name="print-mode"]:checked')?.value || 'normal';
        const printer = AppState.selectedPrinter;

        if (!AppState.uploadedFile || pages === 0) { el.classList.add('hidden'); return; }

        // Estimate sheets
        let sheets;
        if (mode === 'simplex') {
            sheets = pages * copies;
        } else if (mode === 'booklet') {
            sheets = Math.ceil(pages / 4) * copies;
        } else {
            // normal duplex
            const singleSided = AppState.singleSidedPages.size;
            const doubleSided = pages - singleSided;
            sheets = Math.ceil(doubleSided / 2) + singleSided;
            sheets *= copies;
        }

        // Estimate time: ~15s per sheet (realistic for manual duplex + processing)
        const totalSec = sheets * 15;
        const timeStr = totalSec < 60
            ? `< 1 phút`
            : totalSec < 3600
                ? `~${Math.ceil(totalSec / 60)} phút`
                : `~${Math.floor(totalSec / 3600)}h ${Math.ceil((totalSec % 3600) / 60)}m`;

        el.classList.remove('hidden');
        el.innerHTML = `
            <span>📄 ${pages} trang</span>
            <span>·</span>
            <span>🗒️ ${sheets} tờ</span>
            <span>·</span>
            <span>⏱ ${timeStr}</span>
            ${copies > 1 ? `<span>· ${copies} bản</span>` : ''}
            ${printer ? `<span>· 🖨️ ${printer.name}</span>` : ''}
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
        <span class="error-text">${message}</span>
        ${retryFn ? '<button class="card-error-retry">Thử lại</button>' : ''}
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

        const mode    = document.querySelector('input[name="print-mode"]:checked')?.value || 'normal';
        const pages   = AppState.selectedPages.size;
        const copies  = CopiesModule?.copies || 1;
        const printer = AppState.selectedPrinter;
        const modeLabel = { normal: 'In 2 Mặt Thường', booklet: 'Sách A5 (Booklet)', simplex: 'In 1 Mặt' };

        let sheets;
        if (mode === 'simplex') {
            sheets = pages * copies;
        } else if (mode === 'booklet') {
            sheets = Math.ceil(pages / 4) * copies;
        } else {
            const singleSided = AppState.singleSidedPages.size;
            sheets = (Math.ceil((pages - singleSided) / 2) + singleSided) * copies;
        }

        const totalSec = sheets * 15;
        const timeStr  = totalSec < 60 ? '< 1 phút'
            : `~${Math.ceil(totalSec / 60)} phút`;

        const sel = Array.from(AppState.selectedPages).sort((a,b)=>a-b);
        const rangeStr = sel.length === AppState.totalPageCount
            ? 'Tất cả'
            : sel.join(', ').replace(/,\s/g, ', ');

        container.innerHTML = `
            <div class="confirm-row">
                <span class="confirm-row-icon">📄</span>
                <span class="confirm-row-label">File:</span>
                <span class="confirm-row-value">${AppState.uploadedFile?.name || '—'}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">🖨️</span>
                <span class="confirm-row-label">Máy in:</span>
                <span class="confirm-row-value">${printer?.name || '—'}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📋</span>
                <span class="confirm-row-label">Chế độ:</span>
                <span class="confirm-row-value">${modeLabel[mode] || mode}</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">📖</span>
                <span class="confirm-row-label">Trang:</span>
                <span class="confirm-row-value">${pages} trang (${rangeStr})</span>
            </div>
            <div class="confirm-row highlight">
                <span class="confirm-row-icon">🗒️</span>
                <span class="confirm-row-label">Số tờ:</span>
                <span class="confirm-row-value">${sheets} tờ × ${copies} bản</span>
            </div>
            <div class="confirm-row">
                <span class="confirm-row-icon">⏱️</span>
                <span class="confirm-row-label">Thời gian:</span>
                <span class="confirm-row-value">${timeStr}</span>
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

        // Render or use cached
        if (!this._cache.has(pageNum)) {
            try {
                const page     = await AppState.currentPdfDoc.getPage(pageNum);
                const viewport = page.getViewport({ scale: 1.0 });
                const scale    = Math.min(this.PREVIEW_W / viewport.width, this.PREVIEW_H / viewport.height);
                const vp2      = page.getViewport({ scale });
                const off      = document.createElement('canvas');
                off.width      = vp2.width;
                off.height     = vp2.height;
                await page.render({ canvasContext: off.getContext('2d'), viewport: vp2 }).promise;
                this._cache.set(pageNum, off);
            } catch { return; }
        }

        const cached = this._cache.get(pageNum);
        pCanvas.width  = cached.width;
        pCanvas.height = cached.height;
        pCanvas.getContext('2d').drawImage(cached, 0, 0);

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
    _ghost:       null,
    _dragging:    null,
    _placeholder: null,
    _startY:      0,
    _dragPageNum: null,

    init() {
        const grid = document.getElementById('sidebar-preview-grid');
        if (!grid) return;
        grid.addEventListener('pointerdown', e => this._onDown(e));
    },

    _onDown(e) {
        const thumb = e.target.closest('.page-thumbnail');
        if (!thumb) return;
        // Only left button
        if (e.button !== 0) return;

        this._dragging    = thumb;
        this._dragPageNum = parseInt(thumb.dataset.pageNumber);
        this._startY      = e.clientY;

        // Create ghost
        const rect  = thumb.getBoundingClientRect();
        this._ghost = thumb.cloneNode(true);
        this._ghost.className = 'drag-ghost';
        this._ghost.style.cssText = `
            width: ${rect.width}px;
            height: ${rect.height}px;
            left: ${rect.left}px;
            top:  ${rect.top}px;
        `;
        document.body.appendChild(this._ghost);

        thumb.classList.add('dragging');

        document.addEventListener('pointermove', this._onMove = e => this._move(e));
        document.addEventListener('pointerup',   this._onUp   = e => this._drop(e));
        e.preventDefault();
    },

    _move(e) {
        if (!this._ghost) return;
        const dy = e.clientY - this._startY;
        const rect = this._dragging.getBoundingClientRect();
        this._ghost.style.top = (rect.top + dy) + 'px';

        // Find drop target
        const grid   = document.getElementById('sidebar-preview-grid');
        const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail:not(.dragging)'));
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

        if (this._ghost)       { this._ghost.remove();       this._ghost = null; }
        if (this._dragging)    this._dragging.classList.remove('dragging');

        if (this._placeholder) {
            // Reorder AppState.pageOrder
            const grid    = document.getElementById('sidebar-preview-grid');

            // Compute new order from DOM after inserting dragging before placeholder
            const newOrder = [];
            let placed = false;
            for (const t of grid.childNodes) {
                if (t === this._placeholder) {
                    if (!placed) { newOrder.push(this._dragPageNum); placed = true; }
                } else if (t.classList?.contains('page-thumbnail') && t !== this._dragging) {
                    newOrder.push(parseInt(t.dataset.pageNumber));
                }
            }
            if (!placed) newOrder.push(this._dragPageNum);

            AppState.pageOrder = newOrder;

            // Re-render grid in new order
            this._placeholder.remove();
            this._placeholder = null;
            this._reRenderGrid(newOrder);

            showToast('Đã đổi thứ tự trang', 'info');
        }

        this._dragging    = null;
        this._dragPageNum = null;
        this._placeholder = null;
    },

    _reRenderGrid(order) {
        const grid   = document.getElementById('sidebar-preview-grid');
        const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail'));
        const byPage = new Map(thumbs.map(t => [parseInt(t.dataset.pageNumber), t]));
        // Reorder DOM
        order.forEach(pageNum => {
            const t = byPage.get(pageNum);
            if (t) grid.appendChild(t);
        });
        PreviewModule.updateThumbnails();
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
    HistoryModule.init();
    KeyboardModule.init();
    DragReorderModule.init();
    ConfirmPrintModal.init();
    SummaryModule.update();
    StepIndicatorModule.update();
});
