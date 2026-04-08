# Spec: Ejected-Column Deselected Pages Display + togglePageSelection R7 Fix

**Date:** 2026-04-07
**Status:** Final — reviewed by 2 Oracles, 4 BLOCKERs + 3 WARNINGs patched

---

## 1. Problem Statement

Khi user bỏ chọn (deselect) một trang trong sheet view, trang đó biến mất hoàn toàn khỏi giao diện.
Yêu cầu: trang bị bỏ chọn phải **hiển thị trong một cột phụ (ejected-column)** bên phải khu vực sheet-cards, để user biết trang đó vẫn tồn tại và có thể re-select lại.

Đồng thời, Oracle review phát hiện **bug R7**: `togglePageSelection` thực hiện R8 (xóa SS khi deselect) nhưng bỏ qua R7 (xóa absorbed blank khi SS page bị deselect), dẫn đến orphan blank gây layout sai khi re-select.

---

## 2. Scope

### Trong scope:
- **Ejected-column UI**: hiển thị trang bị bỏ chọn bên phải sheet-cards
- **R7 fix trong `togglePageSelection`**: splice absorbed blank khi SS page bị deselect
- **Re-select từ ejected-column**: click mini-card → trang về vị trí cũ
- Ejected-column xuất hiện ở **tất cả sheet-view modes** (duplex, simplex, booklet) — xem E6

### Ngoài scope:
- Backend: không thay đổi gì — Oracle 2 xác nhận không cần
- Page view mode: ejected-column chỉ hiện ở sheet view
- Lasso selection, PrintPreviewModule modal: không thay đổi behavior hiện tại

---

## 3. Quy tắc nghiệp vụ (bổ sung)

| # | Quy tắc |
|---|---------|
| R9 | Trang bị deselect **hiển thị trong ejected-column** theo thứ tự `pageOrder` gốc (không phải thứ tự deselect) |
| R10 | Click trang trong ejected-column → **re-select và trở về vị trí cũ** trong `pageOrder` (không append cuối) |
| R11 | Re-select KHÔNG tự động restore SS status (R8 vẫn áp dụng — user phải set lại thủ công) |
| R12 | Ejected-column **ẩn hoàn toàn** khi không có trang nào bị deselect |
| R13 | Ejected-column **scroll cùng** với sheet-cards (1 scroll bar duy nhất trên `preview-panel`) |
| R14 | Khi deselect trang SS đã absorbed blank (R6): blank đó phải bị **splice khỏi `pageOrder`** (R7), không để mồ côi |
| R15 | `deselectedPages[]` trong output của `buildSheetLayout` chỉ chứa trang thật (≠ 0), theo thứ tự `pageOrder` gốc |

---

## 4. Thuật toán

### 4.1 Fix `togglePageSelection` — thêm R7 cleanup

**Vấn đề hiện tại** (app.js line 126–134):
```javascript
function togglePageSelection(entry, pageNum) {
    if (entry.selectedPages.has(pageNum)) {
        entry.selectedPages.delete(pageNum);
        entry.singleSidedPages.delete(pageNum); // R8 ✓
        // BUG: không làm R7 — absorbed blank bị bỏ mồ côi
    } else {
        entry.selectedPages.add(pageNum);
    }
}
```

**Scenario bug (Oracle 1, Q3–Q4):**
```
pageOrder = [1, 2, 0, 3, 4], SS={2}, selected={1,2,3,4}
→ Sheet1=[1|null], Sheet2=[2|0], Sheet3=[3|4]

Deselect 2 (R8 only):
  pageOrder = [1, 2, 0, 3, 4]  ← blank còn đó
  pages[] = [1, 0, 3, 4]
  → Sheet1=[1|0], Sheet2=[3|4]  ← blank trở thành standalone

Re-select 2 sau đó:
  pages[] = [1, 2, 0, 3, 4]  ← blank vẫn còn
  → Sheet1=[1|2], Sheet2=[0|3], Sheet3=[4|null]  ← BLANK Ở FRONT ← BUG
```

**Fix — thêm R7 vào deselect path:**
```javascript
function togglePageSelection(entry, pageNum) {
    if (entry.selectedPages.has(pageNum)) {
        // R8: deselect clears SS status
        entry.selectedPages.delete(pageNum);
        entry.singleSidedPages.delete(pageNum);

        // R7: nếu trang này đã absorbed một blank (R6), splice blank đó ra
        // Dùng blankAbsorbedBy (đã được populate bởi buildSheetLayout tại lần render gần nhất)
        if (entry.blankAbsorbedBy.has(pageNum)) {
            // Forward scan: tìm blank (0) ngay sau pageNum trong raw pageOrder,
            // skip qua deselected pages (giống logic trong unsetSingleSided §4.3)
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
        // Không auto-add singleSidedPages (R8: re-select bắt đầu như duplex)
    }
    // CONTRACT: Caller PHẢI gọi PreviewPanelModule.render(entry) sau khi trả về
}
```

**Lưu ý quan trọng:** Forward scan chạy SAU `singleSidedPages.delete(pageNum)` — khi check `entry.selectedPages.has(v)` bên trong loop, `pageNum` đã bị xóa khỏi `selectedPages` ở dòng trên → nếu scan gặp lại `pageNum` trong `pageOrder` (không thể — indexOf trả index đầu tiên), check vẫn đúng. An toàn.

---

### 4.2 `buildSheetLayout` — thêm output `deselectedPages[]`

**Thay đổi signature output** (không đổi input):

```
Trước:  return { sheets, blankAbsorbedBy }
Sau:    return { sheets, blankAbsorbedBy, deselectedPages }
```

**Tính `deselectedPages`:**
```javascript
// Thêm vào cuối buildSheetLayout, trước return:

// R15: deselectedPages = trang thật bị bỏ chọn, theo thứ tự pageOrder gốc, không trùng lặp
const seenDeselected = new Set();
const deselectedPages = [];

if (fileEntry.pageOrder.length > 0) {
    for (const p of fileEntry.pageOrder) {
        if (p !== 0 && !fileEntry.selectedPages.has(p) && !seenDeselected.has(p)) {
            seenDeselected.add(p);
            deselectedPages.push(p);
        }
    }
    // W3 fallback: nếu pageOrder chỉ có blanks (p===0), dùng totalPageCount
    if (deselectedPages.length === 0 && fileEntry.selectedPages.size < fileEntry.totalPageCount) {
        for (let p = 1; p <= fileEntry.totalPageCount; p++) {
            if (!fileEntry.selectedPages.has(p)) {
                deselectedPages.push(p);
            }
        }
    }
} else {
    // Fallback: pageOrder rỗng → dùng totalPageCount
    for (let p = 1; p <= fileEntry.totalPageCount; p++) {
        if (!fileEntry.selectedPages.has(p)) {
            deselectedPages.push(p);
        }
    }
}

return { sheets, blankAbsorbedBy, deselectedPages };
```

**Caller update** (app.js `_renderSheetView` line 3264):
```javascript
// Trước:
const { sheets, blankAbsorbedBy } = buildSheetLayout(...);
// Sau:
const { sheets, blankAbsorbedBy, deselectedPages } = buildSheetLayout(...);
```

---

### 4.3 Layout thay đổi — `preview-panel` wrapper

**Hiện tại:** `preview-panel` = flex-column, sheets stack dọc, overflow-y scroll.

**Sau thay đổi:**

```
preview-panel (overflow-y: auto — 1 scroll bar duy nhất)
  └── sheet-view-inner (display: flex, flex-direction: row, align-items: flex-start, gap: 24px)
        ├── sheets-column (flex: 1, min-width: 0)
        │     [sheet-card tờ 1]
        │     [sheet-card tờ 2]
        │     ...
        └── ejected-column (width: 180px, flex-shrink: 0)
              hidden khi deselectedPages.length === 0
              [header "Không in"]
              [mini-card pg N]
              [mini-card pg M]
              ...
```

**HTML structure được tạo dynamic trong `_renderSheetView`** (không thay đổi `index.html`):
```javascript
// _renderSheetView tạo wrapper khi ở sheet mode:
const inner = document.createElement('div');
inner.className = 'sheet-view-inner';

const sheetsCol = document.createElement('div');
sheetsCol.className = 'sheets-column';
// ... append sheet-cards vào sheetsCol ...

const ejectedCol = document.createElement('div');
ejectedCol.className = 'ejected-column';
ejectedCol.hidden = (deselectedPages.length === 0);
// ... append mini-cards vào ejectedCol ...

inner.appendChild(sheetsCol);
inner.appendChild(ejectedCol);
this._container.appendChild(inner);
```

---

### 4.4 Ejected-column mini-card

**Mỗi mini-card:**
- Container: `div.ejected-card`
- Label: `div.ejected-card-label` — "Trang N"
- Thumbnail: `img.sheet-page-img` — **PHẢI dùng class `sheet-page-img`** (không phải `ejected-card-img`) để `_renderVisible` (line ~3484, dùng `getBoundingClientRect`) nhận ra và render blob đúng, và `_unmountOffScreen` (~line 3776) có thể reclaim blob URL
- `dataset.fileId` và `dataset.page` **bắt buộc** để `_renderSheetPage` (~line 3532) đọc đúng file/page
- Click handler: re-select + render

```javascript
const makeEjectedCard = (pageNum) => {
    const card = document.createElement('div');
    card.className = 'ejected-card';
    card.dataset.fileId = fileEntry.id;   // B1: bắt buộc cho _renderSheetPage
    card.dataset.page = pageNum;           // B1: bắt buộc cho _renderSheetPage
    card.title = `Trang ${pageNum} — nhấn để thêm vào bản in`;

    const label = document.createElement('div');
    label.className = 'ejected-card-label';
    label.textContent = `Trang ${pageNum}`;
    card.appendChild(label);

    const img = document.createElement('img');
    img.className = 'sheet-page-img';      // B2: dùng class sheet-page-img (không phải ejected-card-img)
    img.alt = '';
    img.draggable = false;
    card.appendChild(img);

    // Re-select on click
    card.addEventListener('click', () => {
        const entry = AppState.files.find(f => f.id === fileEntry.id);
        if (!entry) return;
        togglePageSelection(entry, pageNum);       // adds to selectedPages (else branch)
        PreviewPanelModule.render(entry);           // CONTRACT line 125
        PrintModule.updateButton();
        PageSelectModule.updateDisplay();
    });

    // Queue thumbnail render (reuse existing blob render pipeline)
    const key = `${fileEntry.id}-${pageNum}`;
    this._pageEls.set(key, card);                  // register for _renderVisible

    return card;
};
```

**Thumbnail rendering:** Reuse `_renderVisible` / `_renderSheetPage` pipeline (app.js ~line 3484 + ~line 3532). `_renderVisible` dùng `getBoundingClientRect` (không phải IntersectionObserver) để detect visible elements, gates trên `img.sheet-page-img`. Mini-card được đăng ký vào `_pageEls` với key `fileId-pageNum` → khi visible → `_renderSheetPage` render blob → set `img.src`. CSS scale down qua `width:100%` trên `.ejected-card img.sheet-page-img`.

**Fallback khi trang đã có trong `_pageEls`** (page cũ và mới cùng pageNum): key giống nhau → Map ghi đè → chỉ 1 entry tồn tại → không conflict.

---

### 4.5 Re-select handler — tại sao dùng `togglePageSelection`

Re-select từ ejected-column là "thêm trang vào selectedPages" → rơi vào `else` branch của `togglePageSelection`:
```javascript
entry.selectedPages.add(pageNum);
// Không auto-add singleSidedPages (R11)
```
Không có side effect, đúng spec. Sau đó `PreviewPanelModule.render(entry)` → `buildSheetLayout` → trang trở về vị trí trong `pageOrder` gốc (R10).

---

### 4.6 Ejected-column không hiện ở page-view mode

`_renderSheetView` chỉ chạy khi `_viewMode === 'sheet'`. Khi switch sang page view, `render()` gọi code path khác → ejected-column không tồn tại trong DOM.

---

### 4.7 Sheet-view page card click handler — PHẢI đổi sang `render()` (B3 fix)

**Vấn đề:** Sheet-view page card click handler hiện tại (app.js ~line 3373) gọi `_syncSelectionUI()` — đây là CSS-only update, không rebuild layout. Sau khi deselect, ejected-column sẽ không xuất hiện và R7 splice không trigger re-render.

**Fix bắt buộc:** Đổi `_syncSelectionUI()` → `PreviewPanelModule.render(entry)` trong handler này.

```javascript
// TRƯỚC (app.js ~line 3373):
pageCard.addEventListener('click', () => {
    togglePageSelection(entry, pageNum);
    _syncSelectionUI();              // ← SAI: không rebuild layout
    PrintModule.updateButton();
    PageSelectModule.updateDisplay();
});

// SAU:
pageCard.addEventListener('click', () => {
    togglePageSelection(entry, pageNum);
    PreviewPanelModule.render(entry); // ← ĐÚNG: rebuild toàn bộ layout + ejected-column
    PrintModule.updateButton();
    PageSelectModule.updateDisplay();
});
```

**Lý do:** `PreviewPanelModule.render(entry)` gọi `_renderSheetView` → `buildSheetLayout` → `deselectedPages` → ejected-column hiện/ẩn đúng.

---

## 5. CSS mới cần thêm (`styles.css`)

```css
/* ── Sheet view 2-column wrapper ────────────────────────────── */
.sheet-view-inner {
  display:          flex;
  flex-direction:   row;
  align-items:      flex-start;
  gap:              24px;
  width:            100%;
}

.sheets-column {
  flex:             1;
  min-width:        0;
  display:          flex;
  flex-direction:   column;
  gap:              24px;          /* replaces gap on preview-panel for sheet mode */
}

/* W1 fix: .sheet-card has margin-bottom:24px + .sheets-column gap:24px = 48px double-spacing */
.sheets-column .sheet-card {
  margin-bottom:    0;
}

/* ── Ejected column ─────────────────────────────────────────── */
.ejected-column {
  width:            180px;
  flex-shrink:      0;
  display:          flex;
  flex-direction:   column;
  gap:              12px;
  padding-top:      4px;           /* align with sheet-card top */
}

.ejected-column-header {
  font-size:        11px;
  font-weight:      600;
  color:            var(--color-text-muted, #9ca3af);
  text-transform:   uppercase;
  letter-spacing:   0.06em;
  padding:          0 2px 4px;
  border-bottom:    1px solid rgba(255,255,255,0.08);
}

.ejected-card {
  display:          flex;
  flex-direction:   column;
  align-items:      center;
  gap:              6px;
  cursor:           pointer;
  border-radius:    8px;
  padding:          8px;
  background:       rgba(255,255,255,0.02);
  border:           1px solid rgba(255,255,255,0.06);
  transition:       background 0.15s, border-color 0.15s;
}

.ejected-card:hover {
  background:       rgba(102,126,234,0.10);
  border-color:     rgba(102,126,234,0.35);
}

/* B2 fix: img inside ejected-card uses class sheet-page-img (not ejected-card-img) */
.ejected-card img.sheet-page-img {
  width:            100%;
  height:           auto;
  border-radius:    4px;
  display:          block;
  opacity:          0.55;          /* visually dimmed = not in print set */
}

.ejected-card-label {
  font-size:        10px;
  color:            var(--color-text-muted, #9ca3af);
  text-align:       center;
  white-space:      nowrap;
}
```

**Điều chỉnh `preview-panel` hiện có:** Khi ở sheet mode, `preview-panel` không cần `gap` riêng (gap được quản lý bởi `.sheets-column`). Tuy nhiên để không break page-view mode, giữ nguyên CSS của `preview-panel` — `.sheets-column` tự quản lý gap bên trong.

---

## 6. Invariants bổ sung

| # | Invariant |
|---|-----------|
| Inv 11 | `deselectedPages[]` trong return của `buildSheetLayout` phải được dùng ngay trong cùng lần `_renderSheetView` — không cache |
| Inv 12 | Ejected mini-card click PHẢI gọi `PreviewPanelModule.render(entry)` sau `togglePageSelection` (CONTRACT line 125) |
| Inv 13 | `togglePageSelection` khi deselect SS page: forward scan cho R7 phải chạy SAU `singleSidedPages.delete()` và SAU `selectedPages.delete()` để check `selectedPages.has(v)` trong scan là chính xác |
| Inv 14 | `ejected-column` được tạo fresh mỗi lần `_renderSheetView` — không patch DOM cũ |

---

## 7. Edge cases

### E1 — Deselect tất cả trang
`deselectedPages = [1,2,...,N]`, ejected-column hiện tất cả. `sheets = []` → sheets-column trống. Không crash.

### E2 — Deselect trang SS có absorbed blank (R14)
```
pageOrder = [1, 2, 0, 3], SS={2}, selected={1,2,3}
Deselect 2:
  R8: singleSidedPages.delete(2)
  R7: blankAbsorbedBy.has(2) → true → splice blank tại rawIdx+1 → pageOrder=[1,2,3]
  selected={1,3}, pageOrder=[1,2,3]

buildSheetLayout: pages[]=[1,3] → Sheet1=[1|3]
ejected-column: [pg2]
```

### E3 — Re-select từ E2
```
Re-select 2: selected={1,2,3}, pageOrder=[1,2,3] (blank đã bị splice)
buildSheetLayout: pages[]=[1,2,3] → Sheet1=[1|2], Sheet2=[3|null]
ejected-column: (rỗng, ẩn)
```
Đúng — blank không còn mồ côi. ✓

### E4 — pageOrder rỗng (fallback)
`deselectedPages` được tính từ `totalPageCount` (§4.2 fallback). Đúng.

### E5 — Duplex với orientation mix
Ejected-column chỉ list page numbers — không cần biết orientation. Render thumbnail đúng vì dùng cùng pipeline `_renderBlobForPage` với rotation từ `fileEntry.pageRotations`.

### E6 — Simplex / booklet mode
`ejected-column` chỉ render trong `_renderSheetView`. Simplex/booklet cũng gọi `_renderSheetView` → ejected-column vẫn hiện. `deselectedPages` tính đúng trong mọi branch vì được tính từ `fileEntry.pageOrder` / `fileEntry.selectedPages` sau khi `sheets` đã xong.

### E7 — Multiple deselect rồi re-select một phần
Mỗi lần render, `deselectedPages` được tính lại fresh từ state hiện tại. Không cần tracking riêng.

---

## 8. Test scenarios

| # | Setup | Expected sheet layout | Expected ejected-column |
|---|-------|-----------------------|------------------------|
| T1 | 4 trang duplex, deselect trang 3 | Sheet1=[1\|2], Sheet2=[4\|null] | [pg3] |
| T2 | 4 trang, trang 2 SS, deselect trang 2 | Sheet1=[1\|3], Sheet2=[4\|null] | [pg2] |
| T3 | 4 trang, trang 2 SS + absorbed blank, deselect trang 2 | Sheet1=[1\|3], Sheet2=[4\|null] *(blank đã splice)* | [pg2] |
| T4 | Re-select trang 2 từ T3 | Sheet1=[1\|2], Sheet2=[3\|4] | (rỗng, ẩn) |
| T5 | Deselect tất cả | sheets trống | [pg1,pg2,pg3,pg4] |
| T6 | Deselect trang 1, re-select → deselect lại | Không crash, state consistent | Hiện/ẩn đúng |
| T7 | pageOrder rỗng, deselect từ range input | Ejected-column dùng totalPageCount fallback | [pgN] |

---

## 9. Không thay đổi

- `buildEffectivePageOrder` — không đổi (Oracle 2 xác nhận đúng)
- Backend C# — không đổi
- `unsetSingleSided` — không đổi (R7 logic của nó đúng; chỉ cần thêm R7 vào `togglePageSelection`)
- `setSingleSided` — không đổi
- Page-view mode rendering — không đổi
- `index.html` — không đổi (DOM tạo dynamic trong `_renderSheetView`)
