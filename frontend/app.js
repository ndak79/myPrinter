const API_BASE = 'http://localhost:8787/api';

// State
let selectedPrinter = null;
let uploadedFile = null;
let currentJob = null;
let currentPdfDoc = null;
let selectedPages = new Set();
let singleSidedPages = new Set(); // Pages marked for single-sided printing
let totalPageCount = 0;
let currentZoomPage = 1;
let isUserTypingPageRange = false; // Flag to prevent input overwrite during typing

// Configure PDF.js worker
pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupThemeToggle();
    loadPrinters();
    setupFileUpload();
    setupPrintButton();
    setupPageSelection();
    setupZoomModal();
});

// Theme Toggle
function setupThemeToggle() {
    const savedTheme = localStorage.getItem('theme') || 'auto';
    applyTheme(savedTheme);

    const themeToggle = document.getElementById('theme-toggle');
    if (!themeToggle) return;

    // Update active button
    updateThemeButtons(savedTheme);

    themeToggle.addEventListener('click', (e) => {
        const btn = e.target.closest('.theme-btn');
        if (!btn) return;

        const theme = btn.dataset.theme;
        localStorage.setItem('theme', theme);
        applyTheme(theme);
        updateThemeButtons(theme);
    });

    // Listen for system preference changes
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
        if (localStorage.getItem('theme') === 'auto') {
            applyTheme('auto');
        }
    });
}

function applyTheme(theme) {
    const html = document.documentElement;

    if (theme === 'auto') {
        // Follow system preference
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        html.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
    } else {
        html.setAttribute('data-theme', theme);
    }
}

function updateThemeButtons(activeTheme) {
    document.querySelectorAll('.theme-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.theme === activeTheme);
    });
}

// Load printers
async function loadPrinters() {
    try {
        const response = await fetch(`${API_BASE}/printers`);
        const printers = await response.json();

        const printerList = document.getElementById('printer-list');

        if (printers.length === 0) {
            printerList.innerHTML = '<div class="loading">Không tìm thấy máy in nào</div>';
            return;
        }

        printerList.innerHTML = printers.map(printer => `
            <div class="printer-item" data-printer='${JSON.stringify(printer)}'>
                <div class="printer-info">
                    <span class="printer-icon">🖨️</span>
                    <div class="printer-details">
                        <h3>${printer.name}</h3>
                        <div class="printer-status">
                            ${printer.isDefault ? '<span class="badge badge-info">Mặc định</span>' : ''}
                            ${printer.isDuplex ? '<span class="badge badge-success">Hỗ trợ 2 mặt</span>' : '<span class="badge badge-warning">Chỉ 1 mặt</span>'}
                            ${getStatusBadge(printer.status)}
                        </div>
                    </div>
                </div>
            </div>
        `).join('');

        // Add click handlers
        document.querySelectorAll('.printer-item').forEach(item => {
            item.addEventListener('click', () => {
                document.querySelectorAll('.printer-item').forEach(i => i.classList.remove('selected'));
                item.classList.add('selected');
                selectedPrinter = JSON.parse(item.dataset.printer);
                updatePrintButton();
            });
        });

        // Auto-select default printer
        const defaultPrinter = printers.find(p => p.isDefault);
        if (defaultPrinter) {
            const defaultItem = Array.from(document.querySelectorAll('.printer-item'))
                .find(item => JSON.parse(item.dataset.printer).name === defaultPrinter.name);
            defaultItem?.click();
        }
    } catch (error) {
        showToast('Lỗi khi tải danh sách máy in: ' + error.message);
    }
}

function getStatusBadge(status) {
    const statusMap = {
        3: '<span class="badge badge-success">Sẵn sàng</span>',
        4: '<span class="badge badge-warning">Đang in</span>',
        7: '<span class="badge badge-danger">Offline</span>'
    };
    return statusMap[status] || '';
}

// File Upload
function setupFileUpload() {
    const uploadArea = document.getElementById('upload-area');
    const fileInput = document.getElementById('file-input');
    const fileInfo = document.getElementById('file-info');
    const removeBtn = document.getElementById('remove-file');

    uploadArea.addEventListener('click', () => fileInput.click());

    uploadArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadArea.classList.add('drag-over');
    });

    uploadArea.addEventListener('dragleave', () => {
        uploadArea.classList.remove('drag-over');
    });

    uploadArea.addEventListener('drop', async (e) => {
        e.preventDefault();
        uploadArea.classList.remove('drag-over');
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            await uploadFile(files[0]);
        }
    });

    fileInput.addEventListener('change', async (e) => {
        if (e.target.files.length > 0) {
            await uploadFile(e.target.files[0]);
        }
    });

    removeBtn.addEventListener('click', () => {
        uploadedFile = null;
        currentPdfDoc = null;
        selectedPages.clear();
        totalPageCount = 0;
        fileInput.value = '';
        uploadArea.classList.remove('hidden');
        fileInfo.classList.add('hidden');

        // Hide sidebar if exists
        const sidebar = document.getElementById('preview-sidebar');
        if (sidebar) sidebar.style.display = 'none';

        // Hide page range section
        const pageRangeSection = document.getElementById('page-range-section');
        if (pageRangeSection) pageRangeSection.classList.add('hidden');

        // Reset page range input
        const pageRangeInput = document.getElementById('page-range-input');
        if (pageRangeInput) pageRangeInput.value = '';

        // Remove inline preview if exists
        const inlinePreview = document.getElementById('inline-preview-section');
        if (inlinePreview) inlinePreview.remove();

        updatePrintButton();
    });
}

async function uploadFile(file) {
    const allowedTypes = ['.doc', '.docx', '.pdf'];
    const extension = '.' + file.name.split('.').pop().toLowerCase();

    if (!allowedTypes.includes(extension)) {
        showToast('Loại file không được hỗ trợ');
        return;
    }

    try {
        const formData = new FormData();
        formData.append('file', file);

        document.getElementById('file-status').textContent = 'Đang tải lên...';

        const response = await fetch(`${API_BASE}/upload`, {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (!result.success) {
            showToast('Lỗi: ' + result.message);
            return;
        }

        uploadedFile = {
            id: result.fileId,
            name: result.originalFileName,
            needsConversion: !['.pdf'].includes(extension)
        };

        // Convert to PDF if needed
        if (uploadedFile.needsConversion) {
            document.getElementById('file-status').textContent = 'Đang chuyển đổi sang PDF...';

            await fetch(`${API_BASE}/convert?fileId=${uploadedFile.id}`, {
                method: 'POST'
            });
        }

        // Show file info
        document.getElementById('file-name').textContent = uploadedFile.name;
        document.getElementById('file-status').textContent = 'Đã sẵn sàng';
        document.getElementById('upload-area').classList.add('hidden');
        document.getElementById('file-info').classList.remove('hidden');

        // Render PDF previews
        if (extension === '.pdf' || uploadedFile.needsConversion) {
            await renderPdfPreviews(uploadedFile.id);
        }

        // Show page range section
        const pageRangeSection = document.getElementById('page-range-section');
        if (pageRangeSection) {
            pageRangeSection.classList.remove('hidden');
        }

        updatePrintButton();
        showToast('Tải file thành công!');
    } catch (error) {
        showToast('Lỗi khi tải file: ' + error.message);
    }
}

// PDF Preview - Works with both old and new HTML
async function renderPdfPreviews(fileId) {
    try {
        // Check if sidebar exists (new HTML)
        const sidebar = document.getElementById('preview-sidebar');
        let previewGrid;

        if (sidebar) {
            // NEW HTML: Use sidebar
            sidebar.style.display = 'flex';
            previewGrid = document.getElementById('sidebar-preview-grid');
        } else {
            // OLD HTML: Create inline preview section
            console.warn('Sidebar not found - creating inline preview');

            const existingPreview = document.getElementById('inline-preview-section');
            if (existingPreview) {
                previewGrid = document.getElementById('inline-preview-grid');
            } else {
                // Create new preview section
                const fileCard = document.querySelector('#file-info').closest('.card');
                const previewSection = document.createElement('div');
                previewSection.id = 'inline-preview-section';
                previewSection.style.cssText = 'margin-top: 1.5rem; padding: 1.5rem; background: rgba(30,41,59,0.6); border: 1px solid rgba(148,163,184,0.2); border-radius: 12px;';

                const previewTitle = document.createElement('h3');
                previewTitle.textContent = '📄 Page Preview & Selection';
                previewTitle.style.cssText = 'margin-bottom: 1rem; font-size: 1.125rem;';

                const previewInfo = document.createElement('div');
                previewInfo.style.cssText = 'margin-bottom: 1rem; font-size: 0.875rem; color: #94a3b8;';
                previewInfo.innerHTML = '<span id="inline-page-count">0 trang</span> • <span id="inline-selected">Đã chọn: Tất cả</span>';

                previewGrid = document.createElement('div');
                previewGrid.id = 'inline-preview-grid';
                previewGrid.style.cssText = 'display: grid; grid-template-columns: repeat(auto-fill, minmax(100px, 1fr)); gap: 0.75rem; max-height: 400px; overflow-y: auto; padding: 0.5rem;';

                previewSection.appendChild(previewTitle);
                previewSection.appendChild(previewInfo);
                previewSection.appendChild(previewGrid);
                fileCard.appendChild(previewSection);
            }
        }

        previewGrid.innerHTML = '<div class="loading">Đang tải preview...</div>';

        // Load PDF
        const pdfBlob = await fetch(`${API_BASE}/file/${fileId}`).then(r => r.blob());
        const pdfDataUrl = URL.createObjectURL(pdfBlob);

        const loadingTask = pdfjsLib.getDocument(pdfDataUrl);
        currentPdfDoc = await loadingTask.promise;

        totalPageCount = currentPdfDoc.numPages;
        selectedPages.clear();
        // Explicitly select all pages by default
        for (let i = 1; i <= totalPageCount; i++) {
            selectedPages.add(i);
        }

        // Update displays
        const sidebarPageCount = document.getElementById('sidebar-page-count');
        const inlinePageCount = document.getElementById('inline-page-count');
        if (sidebarPageCount) sidebarPageCount.textContent = `${totalPageCount} trang`;
        if (inlinePageCount) inlinePageCount.textContent = `${totalPageCount} trang`;

        updateSelectedPagesDisplay();

        previewGrid.innerHTML = '';

        // Render thumbnails
        for (let pageNum = 1; pageNum <= totalPageCount; pageNum++) {
            const page = await currentPdfDoc.getPage(pageNum);

            const thumbnailDiv = document.createElement('div');
            thumbnailDiv.className = 'page-thumbnail selected';
            thumbnailDiv.dataset.pageNumber = pageNum;
            // Green border for double-sided (default)
            thumbnailDiv.style.cssText = 'position: relative; cursor: pointer; border: 2px solid #22c55e; border-radius: 8px; background: rgba(100,116,139,0.1); transition: all 0.2s;';

            const canvas = document.createElement('canvas');
            const context = canvas.getContext('2d');

            const viewport = page.getViewport({ scale: sidebar ? 0.6 : 0.5 });
            canvas.width = viewport.width;
            canvas.height = viewport.height;
            canvas.style.cssText = 'width: 100% !important; height: auto !important; display: block; border-radius: 6px;';

            await page.render({
                canvasContext: context,
                viewport: viewport
            }).promise;

            const pageLabel = document.createElement('div');
            pageLabel.style.cssText = 'position: absolute; bottom: 4px; right: 4px; background: rgba(0,0,0,0.8); color: white; padding: 3px 6px; border-radius: 4px; font-size: 11px; font-weight: 600;';
            pageLabel.textContent = pageNum;

            thumbnailDiv.appendChild(canvas);
            thumbnailDiv.appendChild(pageLabel);

            // Set tooltip showing side mode
            const sideMode = singleSidedPages.has(pageNum) ? '1 mặt' : '2 mặt';
            thumbnailDiv.title = `Trang ${pageNum} - In ${sideMode}`;

            // Click handlers
            thumbnailDiv.addEventListener('dblclick', () => {
                if (document.getElementById('page-zoom-modal')) {
                    openZoomModal(pageNum);
                }
            });

            thumbnailDiv.addEventListener('click', (e) => {
                if (e.detail === 1) {
                    setTimeout(() => {
                        if (e.detail === 1) togglePageSelection(pageNum);
                    }, 200);
                }
            });

            // Right-click context menu for side selection
            thumbnailDiv.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                showPageContextMenu(e, pageNum);
            });

            previewGrid.appendChild(thumbnailDiv);
        }

        showToast(`Đã tải ${totalPageCount} trang`);
    } catch (error) {
        console.error('Error rendering PDF previews:', error);
        showToast('Lỗi khi tải preview PDF: ' + error.message);
    }
}

// Page Selection
function setupPageSelection() {
    const pageRangeInput = document.getElementById('sidebar-page-range') || document.getElementById('page-range-input');

    if (!pageRangeInput) return;

    // Set flag when user focuses on input to prevent overwrite
    pageRangeInput.addEventListener('focus', () => {
        isUserTypingPageRange = true;
    });

    // Clear flag when user leaves input, and finalize selection
    pageRangeInput.addEventListener('blur', () => {
        isUserTypingPageRange = false;
        // Update display after user finishes typing
        updateSelectedPagesDisplay();
    });

    pageRangeInput.addEventListener('input', (e) => {
        isUserTypingPageRange = true; // Ensure flag is set during input
        const rangeText = e.target.value.trim();

        if (!rangeText) {
            // If empty input, select ALL pages
            selectedPages.clear();
            for (let i = 1; i <= totalPageCount; i++) {
                selectedPages.add(i);
            }
            updateThumbnailsSelection();
            updateSelectedPagesDisplay();
            updatePrintButton();
            return;
        }

        selectedPages.clear();
        const parts = rangeText.split(',');

        for (const part of parts) {
            const trimmed = part.trim();
            if (trimmed.includes('-')) {
                const [start, end] = trimmed.split('-').map(s => parseInt(s.trim()));
                if (!isNaN(start) && !isNaN(end)) {
                    for (let i = Math.min(start, end); i <= Math.max(start, end); i++) {
                        if (i >= 1 && i <= totalPageCount) {
                            selectedPages.add(i);
                        }
                    }
                }
            } else {
                const pageNum = parseInt(trimmed);
                if (!isNaN(pageNum) && pageNum >= 1 && pageNum <= totalPageCount) {
                    selectedPages.add(pageNum);
                }
            }
        }

        updateThumbnailsSelection();
        // Don't call updateSelectedPagesDisplay here - it will overwrite input!
        // Only update the sidebar display text
        const sidebarDisplay = document.getElementById('sidebar-selected-pages');
        const inlineDisplay = document.getElementById('inline-selected');
        const allSelected = selectedPages.size === totalPageCount;
        const text = allSelected ? 'Tất cả' : `${selectedPages.size} trang`;
        if (sidebarDisplay) sidebarDisplay.textContent = text;
        if (inlineDisplay) inlineDisplay.textContent = allSelected ? 'Đã chọn: Tất cả' : `Đã chọn: ${text}`;
        updatePrintButton();
    });
}

function togglePageSelection(pageNum) {
    if (selectedPages.has(pageNum)) {
        selectedPages.delete(pageNum);
    } else {
        selectedPages.add(pageNum);
    }

    updateThumbnailsSelection();
    updateSelectedPagesDisplay();
    updatePrintButton();
}

function updateThumbnailsSelection() {
    const thumbnails = document.querySelectorAll('.page-thumbnail');
    thumbnails.forEach(thumb => {
        const pageNum = parseInt(thumb.dataset.pageNumber);
        const isSelected = selectedPages.has(pageNum);
        const isSingle = singleSidedPages.has(pageNum);

        if (isSelected) {
            thumb.classList.add('selected');
            // Green for double-sided, blue for single-sided
            thumb.style.borderColor = isSingle ? '#3b82f6' : '#22c55e';
        } else {
            thumb.classList.remove('selected');
            thumb.style.borderColor = 'rgba(148,163,184,0.2)';
        }

        // Update tooltip
        const sideMode = isSingle ? '1 mặt' : '2 mặt';
        thumb.title = `Trang ${pageNum} - In ${sideMode}`;
    });
}

function updateSelectedPagesDisplay() {
    const sidebarDisplay = document.getElementById('sidebar-selected-pages');
    const inlineDisplay = document.getElementById('inline-selected');
    const pageRangeInput = document.getElementById('page-range-input');

    // Check if all pages are selected
    const allSelected = selectedPages.size === totalPageCount;
    const text = allSelected ? 'Tất cả' : `${selectedPages.size} trang`;

    if (sidebarDisplay) sidebarDisplay.textContent = text;
    if (inlineDisplay) inlineDisplay.textContent = allSelected ? 'Đã chọn: Tất cả' : `Đã chọn: ${text}`;

    // Update page range input to reflect current selection (only if user is not typing)
    if (pageRangeInput && !isUserTypingPageRange) {
        if (allSelected) {
            pageRangeInput.value = '';
        } else {
            // Convert selected pages to range string
            pageRangeInput.value = formatPageRangeString(Array.from(selectedPages).sort((a, b) => a - b));
        }
    }
}

// Helper function to format page numbers into range string
function formatPageRangeString(pages) {
    if (pages.length === 0) return '';

    const ranges = [];
    let start = pages[0];
    let end = pages[0];

    for (let i = 1; i <= pages.length; i++) {
        if (i < pages.length && pages[i] === end + 1) {
            end = pages[i];
        } else {
            if (start === end) {
                ranges.push(String(start));
            } else {
                ranges.push(`${start}-${end}`);
            }
            if (i < pages.length) {
                start = pages[i];
                end = pages[i];
            }
        }
    }

    return ranges.join(',');
}

// Zoom Modal
function setupZoomModal() {
    const zoomModal = document.getElementById('page-zoom-modal');
    if (!zoomModal) return;

    document.getElementById('zoom-modal-close')?.addEventListener('click', closeZoomModal);
    document.getElementById('zoom-modal-overlay')?.addEventListener('click', closeZoomModal);

    // All Double-sided button
    document.getElementById('all-double-btn')?.addEventListener('click', () => {
        selectedPages.clear();
        singleSidedPages.clear();
        for (let i = 1; i <= totalPageCount; i++) {
            selectedPages.add(i);
        }
        updateThumbnailsSelection();
        updateSelectedPagesDisplay();
        updateModalPageStyles();
        showToast('Đã chọn tất cả in 2 mặt');
    });

    // All Single-sided button
    document.getElementById('all-single-btn')?.addEventListener('click', () => {
        selectedPages.clear();
        singleSidedPages.clear();
        for (let i = 1; i <= totalPageCount; i++) {
            selectedPages.add(i);
            singleSidedPages.add(i);
        }
        updateThumbnailsSelection();
        updateSelectedPagesDisplay();
        updateModalPageStyles();
        showToast('Đã chọn tất cả in 1 mặt');
    });

    // Deselect All button
    document.getElementById('deselect-all-btn')?.addEventListener('click', () => {
        // Deselect all pages
        for (let i = 1; i <= totalPageCount; i++) {
            selectedPages.delete(i);
        }
        // Add page 1 to force partial selection UI if needed, or just clear all
        // Here we just clear all, but since empty set means "All" in our logic, 
        // we might need to handle "None" differently or just assume user wants to start fresh.
        // However, our logic says "selectedPages.size === 0" means "All".
        // So to represent "None", we might need to change logic or just select nothing but handle it.
        // Wait, if selectedPages is empty, it prints all. 
        // If user wants to select specific pages, they start by clicking one.
        // If they click "Deselect All", they probably want to clear their SELECTION, 
        // which effectively means "All" again in the current logic?
        // OR they want to select nothing (which is invalid for printing).
        // Let's assume "Deselect All" clears the set, which defaults to "All".
        // BUT, if the user wants to select just page 5, they might want to clear first.
        // If "Empty" = "All", then "Deselect All" -> "All". This is confusing.
        // Let's change logic: If user clicks "Deselect All", we should probably not allow printing?
        // Or maybe we should just clear the set.

        // Actually, if I want to select just page 5, and currently 1,2,3 are selected.
        // I uncheck 1,2,3. Set is empty. -> All.
        // This logic "Empty = All" is convenient for initial state but tricky for "Deselect All".
        // Let's keep it simple: Clear set.
        selectedPages.clear();
        updateThumbnailsSelection();
        updateSelectedPagesDisplay();
        updateModalPageStyles();
    });

    document.getElementById('zoom-prev')?.addEventListener('click', () => {
        if (currentZoomPage > 1) renderZoomedPage(currentZoomPage - 1);
    });
    document.getElementById('zoom-next')?.addEventListener('click', () => {
        if (currentZoomPage < totalPageCount) renderZoomedPage(currentZoomPage + 1);
    });

    document.getElementById('zoom-canvas')?.addEventListener('click', () => {
        togglePageSelection(currentZoomPage);
        updateThumbnailsSelection();
    });
}

function updateModalCheckboxes() {
    const containers = document.querySelectorAll('.zoom-canvas-container [data-page-number]');
    containers.forEach(container => {
        const pageNum = parseInt(container.dataset.pageNumber);
        const checkbox = container.querySelector('input[type="checkbox"]');
        if (checkbox) {
            checkbox.checked = selectedPages.has(pageNum);
        }
        container.style.borderColor = (selectedPages.has(pageNum)) ? '#667eea' : 'rgba(148,163,184,0.2)';
    });
}

function openZoomModal(pageNum) {
    const modal = document.getElementById('page-zoom-modal');
    if (!modal) return;

    modal.classList.remove('hidden');
    renderZoomedPage(pageNum);
}

function closeZoomModal() {
    const modal = document.getElementById('page-zoom-modal');
    if (modal) modal.classList.add('hidden');
}

async function renderZoomedPage(pageNum) {
    // Render ALL pages in modal for scrolling
    try {
        const container = document.querySelector('.zoom-canvas-container');
        if (!container) return;

        container.innerHTML = '<div class="loading">Đang tải...</div>';

        // Create scrollable page list
        const pagesList = document.createElement('div');
        pagesList.style.cssText = 'width: 100%; max-width: 800px; margin: 0 auto;';

        // Create array of promises for parallel rendering
        const renderPromises = [];

        for (let i = 1; i <= totalPageCount; i++) {
            renderPromises.push((async () => {
                try {
                    const page = await currentPdfDoc.getPage(i);

                    const pageContainer = document.createElement('div');
                    pageContainer.className = 'modal-page-container';
                    pageContainer.dataset.page = i;

                    // Border color: green if selected (and double-sided), blue if single-sided, none if not selected
                    const isSelected = selectedPages.has(i);
                    const isSingle = singleSidedPages.has(i);
                    let borderColor = 'transparent';
                    if (isSelected) {
                        borderColor = isSingle ? '#3b82f6' : '#22c55e'; // blue for single, green for double
                    }
                    pageContainer.style.cssText = `margin-bottom: 1.5rem; position: relative; border: 3px solid ${borderColor}; border-radius: 8px; padding: 1rem; background: rgba(30,41,59,0.5); cursor: pointer;`;

                    // Page header with title only (no checkbox)
                    const pageHeader = document.createElement('div');
                    pageHeader.style.cssText = 'display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem;';

                    const pageTitle = document.createElement('h4');
                    pageTitle.textContent = `Trang ${i}`;
                    pageTitle.style.cssText = 'margin: 0; font-size: 1rem;';

                    pageHeader.appendChild(pageTitle);

                    // Side indicator badge (centered)
                    const sideBadge = document.createElement('div');
                    sideBadge.className = isSingle ? 'single-sided-badge' : 'double-sided-badge';
                    sideBadge.textContent = isSingle ? '1 MẶT' : '2 MẶT';
                    if (!isSelected) {
                        sideBadge.style.opacity = '0.3';
                    }

                    // Canvas for page
                    const canvas = document.createElement('canvas');
                    const context = canvas.getContext('2d');

                    const viewport = page.getViewport({ scale: 1.2 });
                    canvas.width = viewport.width;
                    canvas.height = viewport.height;
                    canvas.style.cssText = 'width: 100%; height: auto; display: block; border-radius: 6px;';

                    await page.render({
                        canvasContext: context,
                        viewport: viewport
                    }).promise;

                    pageContainer.appendChild(pageHeader);
                    pageContainer.appendChild(sideBadge);
                    pageContainer.appendChild(canvas);

                    // Left-click to toggle selection (selected = in print, unselected = not in print)
                    const pageNum = i;
                    pageContainer.addEventListener('click', (e) => {
                        // Ignore if context menu is open
                        if (e.button !== 0) return;
                        togglePageFromModal(pageNum, !selectedPages.has(pageNum));
                        updateModalPageStyles();
                    });

                    // Right-click for context menu (single/double sided)
                    pageContainer.addEventListener('contextmenu', (e) => {
                        e.preventDefault();
                        showPageContextMenu(e, pageNum);
                    });

                    return { index: i, element: pageContainer };
                } catch (err) {
                    console.error(`Error rendering page ${i}:`, err);
                    return null;
                }
            })());
        }

        // Wait for all pages to render
        const results = await Promise.all(renderPromises);

        // Append in correct order
        results.sort((a, b) => (a?.index || 0) - (b?.index || 0));

        results.forEach(result => {
            if (result && result.element) {
                pagesList.appendChild(result.element);
            }
        });

        container.innerHTML = '';
        container.appendChild(pagesList);

        // Scroll to requested page
        // Scroll to requested page using scrollTop for reliability
        const targetPage = pagesList.querySelector(`[data-page-number="${pageNum}"]`);
        if (targetPage) {
            setTimeout(() => {
                // Calculate position relative to the container
                const containerRect = container.getBoundingClientRect();
                const targetRect = targetPage.getBoundingClientRect();
                const relativeTop = targetRect.top - containerRect.top + container.scrollTop;

                container.scrollTo({
                    top: relativeTop - 20, // 20px padding
                    behavior: 'smooth'
                });
            }, 100);
        }

        document.getElementById('zoom-page-title').textContent = `Tất cả trang (${totalPageCount})`;
        document.getElementById('zoom-page-info').textContent = `Cuộn để xem tất cả`;

        // Hide prev/next buttons since we show all pages
        const prevBtn = document.getElementById('zoom-prev');
        const nextBtn = document.getElementById('zoom-next');
        if (prevBtn) prevBtn.style.display = 'none';
        if (nextBtn) nextBtn.style.display = 'none';

    } catch (error) {
        console.error('Error rendering zoomed pages:', error);
    }
}

// Toggle page selection from modal
function togglePageFromModal(pageNum, checked) {
    if (checked) {
        selectedPages.add(pageNum);
    } else {
        selectedPages.delete(pageNum);
    }

    updateThumbnailsSelection();
    updateSelectedPagesDisplay();

    // Update border of page in modal
    const pageContainer = document.querySelector(`.zoom-canvas-container [data-page-number="${pageNum}"]`);
    if (pageContainer) {
        pageContainer.style.borderColor = (selectedPages.has(pageNum)) ? '#667eea' : 'rgba(148,163,184,0.2)';
    }
}

// Print
function setupPrintButton() {
    const printBtn = document.getElementById('print-btn');
    printBtn.addEventListener('click', startPrint);
}

function updatePrintButton() {
    const printBtn = document.getElementById('print-btn');
    // Disable if no printer, no file, OR NO PAGES SELECTED
    printBtn.disabled = !selectedPrinter || !uploadedFile || selectedPages.size === 0;
}

async function startPrint() {
    if (!selectedPrinter) {
        showToast('Vui lòng chọn máy in');
        return;
    }

    if (!uploadedFile) {
        showToast('Vui lòng tải lên file cần in');
        return;
    }

    const mode = document.querySelector('input[name="print-mode"]:checked').value;

    // Get selected page range
    let pageRange = null;
    // If selected pages < total, send range. If equal, send null (all)
    if (selectedPages.size > 0 && selectedPages.size < totalPageCount) {
        const pages = Array.from(selectedPages).sort((a, b) => a - b);
        pageRange = pages.join(',');
    } else if (selectedPages.size === 0) {
        showToast('Vui lòng chọn ít nhất 1 trang để in');
        return;
    }

    const printRequest = {
        fileId: uploadedFile.id,
        printerName: selectedPrinter.name,
        mode: mode === 'normal' ? 0 : 1,
        pageRange: pageRange,
        singleSidedPages: singleSidedPages.size > 0 ? Array.from(singleSidedPages) : null
    };

    try {
        showToast('Đang gửi lệnh in...');

        const response = await fetch(`${API_BASE}/print`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(printRequest)
        });

        const result = await response.json();

        if (!result.success) {
            showToast('Lỗi: ' + result.message);
            return;
        }

        if (result.jobState && result.jobState.waitingForFlip) {
            currentJob = result.jobState;
            showFlipInstructions(result.jobState.instruction);
            showToast('Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.');
        } else {
            showToast('In thành công!');
        }
    } catch (error) {
        showToast('Lỗi khi in: ' + error.message);
    }
}

// Manual Duplex Instructions
function showFlipInstructions(instruction) {
    const modal = document.getElementById('flip-modal');
    const instructionText = document.getElementById('instruction-text');
    const instructionVisual = document.getElementById('instruction-visual');

    // New algorithm: no rotation needed, just put paper back straight
    instructionText.textContent = "Lấy giấy ra và đặt thẳng lại vào khay (mặt đã in hướng xuống). KHÔNG cần xoay giấy.";

    const svg = generateSimpleArrowAnimation();
    instructionVisual.innerHTML = svg;

    modal.classList.remove('hidden');

    document.getElementById('continue-btn').onclick = async () => {
        await continuePrint();
        modal.classList.add('hidden');
    };
}

function generateFlipAnimation() {
    return `
        <svg width="300" height="200" viewBox="0 0 300 200" style="margin: 0 auto;">
            <defs>
                <marker id="arrowhead" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                    <polygon points="0 0, 10 3.5, 0 7" fill="#667eea" />
                </marker>
            </defs>
            
            <rect x="100" y="50" width="100" height="140" 
                  fill="white" 
                  stroke="#667eea" 
                  stroke-width="2" 
                  rx="2" />
            
            <text x="150" y="80" font-size="20" text-anchor="middle" fill="#cbd5e1">ABC</text>
            
            <path d="M 230 100 A 50 50 0 1 1 230 140" 
                  stroke="#667eea" 
                  stroke-width="4" 
                  fill="none" 
                  marker-end="url(#arrowhead)"
                  transform="translate(-80, -20)"/>
                  
            <text x="150" y="180" font-size="16" text-anchor="middle" fill="#667eea" font-weight="bold">
                ↻ Xoay 180°
            </text>
        </svg>
    `;
}

// Simple animation for no-rotation duplex
function generateSimpleArrowAnimation() {
    return `
        <svg width="300" height="200" viewBox="0 0 300 200" style="margin: 0 auto;">
            <defs>
                <marker id="arrowhead2" markerWidth="10" markerHeight="7" refX="0" refY="3.5" orient="auto">
                    <polygon points="0 0, 10 3.5, 0 7" fill="#10b981" />
                </marker>
            </defs>
            
            <!-- Paper stack -->
            <rect x="100" y="60" width="100" height="80" 
                  fill="#f8fafc" 
                  stroke="#64748b" 
                  stroke-width="2" 
                  rx="2" />
            <rect x="103" y="63" width="94" height="74" 
                  fill="white" 
                  stroke="#94a3b8" 
                  stroke-width="1" />
            <text x="150" y="100" font-size="16" text-anchor="middle" fill="#94a3b8">Giấy đã in</text>
            
            <!-- Arrow going down straight -->
            <path d="M 150 145 L 150 175" 
                  stroke="#10b981" 
                  stroke-width="4" 
                  fill="none" 
                  marker-end="url(#arrowhead2)"/>
            
            <!-- Tray -->
            <rect x="80" y="180" width="140" height="15" 
                  fill="#e2e8f0" 
                  stroke="#667eea" 
                  stroke-width="2" 
                  rx="3" />
            <text x="150" y="192" font-size="10" text-anchor="middle" fill="#667eea">Khay giấy</text>
                  
            <text x="150" y="35" font-size="14" text-anchor="middle" fill="#10b981" font-weight="bold">
                ↓ Đặt thẳng lại (không xoay)
            </text>
        </svg>
    `;
}

async function continuePrint() {
    try {
        showToast('Đang in mặt chẵn...');

        const response = await fetch(`${API_BASE}/print/continue?jobId=${currentJob.jobId}`, {
            method: 'POST'
        });

        const result = await response.json();

        if (result.success) {
            showToast('In hoàn tất!');
            currentJob = null;
        } else {
            showToast('Lỗi: ' + result.message);
        }
    } catch (error) {
        showToast('Lỗi khi tiếp tục in: ' + error.message);
    }
}

// Toast
function showToast(message) {
    const toast = document.getElementById('toast');
    const toastMessage = document.getElementById('toast-message');

    toastMessage.textContent = message;
    toast.classList.remove('hidden');

    setTimeout(() => {
        toast.classList.add('hidden');
    }, 3000);
}

// Context Menu for Page Single-Sided Selection
let currentContextPage = null;

function showPageContextMenu(event, pageNum) {
    const menu = document.getElementById('page-context-menu');
    currentContextPage = pageNum;

    // Update header
    document.getElementById('context-menu-header').textContent = `Trang ${pageNum}`;

    // Update checkmarks
    const isSingleSided = singleSidedPages.has(pageNum);
    document.getElementById('check-double').textContent = isSingleSided ? '' : '✓';
    document.getElementById('check-single').textContent = isSingleSided ? '✓' : '';

    // Position menu
    menu.style.left = `${event.clientX}px`;
    menu.style.top = `${event.clientY}px`;
    menu.classList.remove('hidden');

    // Prevent menu from going off screen
    const rect = menu.getBoundingClientRect();
    if (rect.right > window.innerWidth) {
        menu.style.left = `${window.innerWidth - rect.width - 10}px`;
    }
    if (rect.bottom > window.innerHeight) {
        menu.style.top = `${window.innerHeight - rect.height - 10}px`;
    }
}

function hideContextMenu() {
    document.getElementById('page-context-menu').classList.add('hidden');
    currentContextPage = null;
}

// Setup context menu event listeners
document.addEventListener('DOMContentLoaded', () => {
    // Hide context menu on click outside
    document.addEventListener('click', (e) => {
        if (!e.target.closest('.context-menu')) {
            hideContextMenu();
        }
    });

    // Handle context menu item clicks
    document.querySelectorAll('.context-menu-item').forEach(item => {
        item.addEventListener('click', (e) => {
            e.stopPropagation(); // Prevent click from bubbling to document click handler
            const action = item.dataset.action;
            console.log('[ContextMenu] Action clicked:', action, 'totalPageCount:', totalPageCount);

            // Handle BULK actions (no currentContextPage needed)
            if (action === 'all-double-sided') {
                // Select ALL pages and set them all to double-sided
                selectedPages.clear();
                singleSidedPages.clear();
                for (let i = 1; i <= totalPageCount; i++) {
                    selectedPages.add(i);
                }
                updateThumbnailsSelection();
                updateSelectedPagesDisplay();
                updateModalPageStyles();
                updatePrintButton();
                showToast('Đã chọn tất cả in 2 mặt');
                hideContextMenu();
                return;
            }

            if (action === 'all-single-sided') {
                // Select ALL pages and set them all to single-sided
                selectedPages.clear();
                singleSidedPages.clear();
                for (let i = 1; i <= totalPageCount; i++) {
                    selectedPages.add(i);
                    singleSidedPages.add(i);
                }
                updateThumbnailsSelection();
                updateSelectedPagesDisplay();
                updateModalPageStyles();
                updatePrintButton();
                showToast('Đã chọn tất cả in 1 mặt');
                hideContextMenu();
                return;
            }

            if (action === 'deselect-all') {
                // Clear all page selections
                selectedPages.clear();
                updateThumbnailsSelection();
                updateSelectedPagesDisplay();
                updateModalPageStyles();
                updatePrintButton();
                showToast('Đã bỏ chọn tất cả');
                hideContextMenu();
                return;
            }

            // Handle per-page actions (need currentContextPage)
            if (currentContextPage === null) return;

            // Auto-select page if not already selected
            if (!selectedPages.has(currentContextPage)) {
                selectedPages.add(currentContextPage);
            }

            if (action === 'single-sided') {
                singleSidedPages.add(currentContextPage);
                showToast(`Trang ${currentContextPage} sẽ in 1 mặt`);
            } else if (action === 'double-sided') {
                singleSidedPages.delete(currentContextPage);
                showToast(`Trang ${currentContextPage} sẽ in 2 mặt`);
            }

            updateModalPageStyles();
            updateThumbnailsSelection();
            updateSelectedPagesDisplay();
            hideContextMenu();
        });
    });
});

function updateModalPageStyles() {
    // Update borders and badges in modal
    document.querySelectorAll('.modal-page-container').forEach(container => {
        const pageNum = parseInt(container.dataset.page);
        const isSelected = selectedPages.has(pageNum);
        const isSingle = singleSidedPages.has(pageNum);

        // Update border color
        let borderColor = 'transparent';
        if (isSelected) {
            borderColor = isSingle ? '#3b82f6' : '#22c55e'; // blue for single, green for double
        }
        container.style.borderColor = borderColor;

        // Update badge
        let badge = container.querySelector('.single-sided-badge, .double-sided-badge');
        if (badge) {
            badge.className = isSingle ? 'single-sided-badge' : 'double-sided-badge';
            badge.textContent = isSingle ? '1 MẶT' : '2 MẶT';
            badge.style.opacity = isSelected ? '1' : '0.3';
        }
    });
}

// Keep old function name for compatibility
function updatePageBadges() {
    updateModalPageStyles();
}
