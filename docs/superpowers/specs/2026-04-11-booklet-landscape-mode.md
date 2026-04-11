# Spec: Booklet Landscape Mode
**Date:** 2026-04-11
**Status:** Draft (Round 5 reviewed)
**Scope:** `frontend/app.js` only

---

## 1. Problem Statement

Chế độ In Sách (`printMode === 'booklet'`) hiện tại không hỗ trợ landscape mode. Khi tài liệu có trang landscape, `CreateBookletPdf` xử lý từng trang landscape bằng cách xoay -90° inline (`DrawPageOnHalf`), nhưng không có cơ chế cho phép người dùng chọn cách ghép trang landscape.

Kết quả:
- Trang landscape bị xoay -90° và thu nhỏ vừa nửa tờ A4 — đúng về mặt kỹ thuật nhưng không đẹp.
- Với tài liệu thuần landscape (e.g. slide bài giảng), người dùng muốn booklet đẹp hơn.

Spec này thêm `landscapeMode` vào booklet với 2 tùy chọn tương tự In thông minh (duplex):
- **`together`** (default): inject CCW90 vào tất cả trang landscape trước khi tạo booklet → tất cả trang portrait-sized → `DrawPageOnHalf` không cần xoay thêm → booklet đẹp.
- **`separate`**: giữ nguyên hành vi hiện tại — `DrawPageOnHalf` xoay -90° landscape pages inline.

---

## 2. Architecture Discovery

### 2.1 `_renderBookletSheetView` không tồn tại

**Quan trọng:** `_renderBookletSheetView` **không tồn tại** trong codebase. Chỉ có một renderer duy nhất: `_renderSheetView` (app.js:3968).

`_renderSheetView` đã dispatch cả duplex và booklet thông qua `buildSheetLayout`:
```javascript
// app.js:4196-4197
const printMode = AppState.printMode;  // 'duplex' | 'booklet' | ...
const { sheets, blankAbsorbedBy, deselectedPages } = buildSheetLayout(
    fileEntry, printMode, orientationMap, fileEntry.landscapeMode
);
```

`buildSheetLayout` (app.js:312) có branch riêng cho booklet. `fileEntry.landscapeMode` được pass qua — **nhưng** `buildSheetLayout` booklet branch hiện tại không dùng `landscapeMode` param (chỉ có duplex branch dùng).

### 2.2 Together mode logic đã có trong `_renderSheetView`

Lines 4100–4177: together mode injection block đã xử lý snapshot + inject CCW90 + invalidate cache. Logic này **không check printMode** — nên nó đã chạy cho booklet nếu `fileEntry.landscapeMode === 'together'`.

**Kết luận:** Together mode cho booklet preview **đã hoạt động** ngay khi user chọn together — không cần thêm code vào renderer.

**Mechanism:** `buildSheetLayout` booklet branch **không dùng** `orientationMap` hay `landscapeMode` để nhóm trang — booklet layout là orientation-agnostic. Preview đúng vì `_renderSheetView` inject CCW90 vào `fileEntry.pageRotations`, và bitmap renderer dùng `pageRotations` khi rasterize từng trang (`frontend/app.js:4513-4515`, `4558-4572`). Kết quả: mỗi trang booklet preview hiển thị đã xoay đúng.

### 2.3 `duplexSide` — optional cleanup cho booklet

`_startPrint()` (app.js:2564) và `_resumePrintQueue()` (app.js:2773) có thể tính `duplexSide = 'ShortEdge'` cho booklet+together+all-landscape:
```javascript
// Không guard printMode — gửi ShortEdge cho booklet
if (file.landscapeMode === 'together' && file._originalOrientationMap != null) {
    // ...
    if (allLandscape) duplexSide = 'ShortEdge';
}
```

**Backend ignore field này cho booklet:** `CreateBookletJob` không nhận `duplexSide`/`manualFlipDir` — backend route không pass qua (`backend/BackendStartup.cs:236-238`). Gửi `duplexSide: 'ShortEdge'` cho booklet là **harmless request noise**, không phải functional bug.

**Tuy nhiên,** vẫn **bắt buộc** add guard `mode !== 'booklet'` để code rõ ràng, tránh confusion cho future readers. Classify là **code clarity fix**, không phải functional bug fix.

### 2.4 Fallback khi `_originalOrientationMap == null`

Khi `_startPrint` hoặc `_resumePrintQueue` chạy và `_originalOrientationMap === null`, `pageRotations` không có CCW90 → booklet+together print mà không có CCW90 → trang landscape không được xoay trước.

**Trigger:** Fallback dựa trên `_originalOrientationMap == null`, KHÔNG phải viewMode. Xảy ra khi:
- User print từ **page view** (phổ biến nhất) — `_renderSheetView` chưa chạy.
- User print từ **sheet view** trước khi **async first render commit** — `_renderSheetView` đang chờ `Promise.all(intrinsicPromises)`, `_pendingOrientationMap` chưa được commit vào `_originalOrientationMap`.

**Fix:** `_startPrint` detect intrinsic orientations async từ `pdfDoc`, inject vào **request-local** `pageRotationsForPrint` (KHÔNG mutate `file.pageRotations`). Nếu print fail, file state sạch. Overlap với `_pendingOrientationMap` in-flight là benign vì fallback không đụng `_pendingOrientationMap`.

### 2.5 `_syncModeBar` — đã đúng, không cần thay đổi

`_syncModeBar()` (app.js:5204):
```javascript
modeBar.style.display = AppState.viewMode === 'sheet' ? '' : 'none';
```
Không guard printMode → modebar **đã hiện cho booklet** khi ở sheet view. Không cần thay đổi.

---

## 3. Core Principle

### 3.1 Separate mode (hành vi hiện tại — không đổi)

```
Trang landscape → DrawPageOnHalf xoay -90° → đặt vào nửa tờ booklet
Result: trang landscape bị nhỏ lại và xoay, người đọc phải nghiêng đầu
```

Không thay đổi code path này.

### 3.2 Together mode (mới — 2 changes)

```
BOOKLET TOGETHER MODE = "rotate CCW90 mọi trang landscape trước khi tạo booklet"

Landscape (297×210) --[CCW90]--> (210×297) = portrait-sized
Portrait  (210×297) --[no-op]--> (210×297) = unchanged

Sau khi xoay:
  - Tất cả trang đều portrait-sized
  - CreateBookletPdf thấy toàn trang portrait → DrawPageOnHalf không xoay thêm
  - Booklet preview (_renderSheetView booklet branch) hiển thị đúng
```

**Key difference với duplex together mode:** Duplex cần `duplexSide: 'ShortEdge'` khi all-landscape. Booklet **không dùng `duplexSide`** — `CreateBookletJob` quản lý duplex nội bộ.

---

## 4. Data Model — Dùng lại hoàn toàn

Không thêm field mới. Các fields đã có từ spec landscape-together-mode:

```javascript
// FileEntry (createFileEntry)
_togetherRotations:      new Set(),  // Set<pageNum> — pages auto-rotated by together mode
_originalOrientationMap: null,       // Map<pageNum, bool> | null — pre-injection snapshot
_pendingOrientationMap:  null,       // race-condition fix (F1) — committed after guards
landscapeMode:           'together', // 'together' | 'separate' — per-file
```

`_togetherRotations` và `_originalOrientationMap` đã được init trong `createFileEntry()`. Spec này dùng lại chúng cho booklet context mà không thay đổi init code.

---

## 5. Frontend Changes (`app.js`) — 2 changes

### 5.1 Change 1: `_startPrint()` — guard `duplexSide` + request-local fallback injection

**Vấn đề hiện tại (lines 2561–2579):**
```javascript
// BUG: Không guard printMode — gửi ShortEdge sai cho booklet
if (file.landscapeMode === 'together' && file._originalOrientationMap != null) {
    // ...
    if (allLandscape) duplexSide = 'ShortEdge';
}
```

**Hai vấn đề cần xử lý:**

**Cleanup 1 — duplexSide guard (REQUIRED — add for code clarity):**

Backend ignore `duplexSide` cho booklet, nên đây không phải functional bug. Nhưng guard **bắt buộc thêm** để tránh confusing future readers:
```javascript
// NOTE: `mode` và `modeCode` được capture MỘT LẦN trước for-loop trong _startPrint
// (existing code: const mode = AppState.printMode || 'duplex'; const modeCode = ...)
// Chỉ `duplexSide` và `pageRotationsForPrint` được rebuild per-file TRONG loop body.

let duplexSide = null;  // rebuilt inside loop, per file

if (mode !== 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap != null) {
    // ... existing allLandscape logic unchanged ...
    if (allLandscape) duplexSide = 'ShortEdge';
}
```

**Fix 2 — Fallback khi `_originalOrientationMap == null` (REQUIRED):**

Khi `_startPrint` chạy và `_originalOrientationMap === null` (page view hoặc sheet view trước first-render commit), cần inject CCW90 cho request này.

**Quan trọng:** Fallback phải dùng **request-local** data, KHÔNG mutate persistent file state. Lý do: page view design giả định together state là torn down; `PreviewPanelModule.render()` tears down khi rời sheet view; nếu print fail thì không có rollback — mutating file state từ page view để lại hidden rotations.

```javascript
// Build pageRotations for the print request — start from file.pageRotations clone
let pageRotationsForPrint = new Map(file.pageRotations);

if (mode === 'booklet' && file.landscapeMode === 'together'
    && file._originalOrientationMap == null
    && file.pdfDoc != null) {
    // Async-detect intrinsic orientations — request-local only, no file mutation
    const intrinsicPromises = [];
    const intrinsicMap = new Map();
    for (let p = 1; p <= file.totalPageCount; p++) {
        intrinsicPromises.push(
            file.pdfDoc.getPage(p).then(page => {
                const vp = page.getViewport({ scale: 1, rotation: 0 });
                intrinsicMap.set(p, vp.width > vp.height);
            }).catch(() => { intrinsicMap.set(p, false); })
        );
    }
    await Promise.all(intrinsicPromises);
    // Inject CCW90 vào request-local map (không đụng file.pageRotations)
    for (const [p, isLandscape] of intrinsicMap) {
        if (isLandscape && !pageRotationsForPrint.has(p)) {
            pageRotationsForPrint.set(p, 'CCW90');
        }
    }
}

// Serialize pageRotationsForPrint (thay vì file.pageRotations) vào request body:
pageRotations: pageRotationsForPrint.size > 0
    ? Array.from(pageRotationsForPrint.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

**Không thay đổi:** `file.pageRotations`, `file._togetherRotations`, `file._originalOrientationMap`. Chỉ request body thay đổi.

### 5.2 Change 2: `_resumePrintQueue()` — cùng duplexSide fix + request-local fallback

`_resumePrintQueue` (app.js:2752–2801) có cùng `duplexSide` logic. Áp dụng 2 fixes tương tự.

**Quan trọng — mode variable:** `_resumePrintQueue` dùng **captured `modeCode`**, không phải `AppState.printMode`. Đã có `const mode = modeCode === 1 ? 'booklet' : 'duplex'` tại line 2755. Dùng `mode` đó — không đọc `AppState.printMode` (user có thể thay đổi UI mode trong khi paused cho manual flip).

```javascript
// _resumePrintQueue already has:
const mode = modeCode === 1 ? 'booklet' : 'duplex';  // line 2755 — USE THIS

// FIX 1: Guard duplexSide
if (mode !== 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap != null) {
    // ... allLandscape check, unchanged ...
}

// FIX 2: Request-local fallback injection (same pattern as §5.1)
let pageRotationsForPrint = new Map(file.pageRotations);
if (mode === 'booklet' && file.landscapeMode === 'together'
    && file._originalOrientationMap == null
    && file.pdfDoc != null) {
    // ... same async detect + inject into pageRotationsForPrint ...
}
// Use pageRotationsForPrint in request body
```

---

## 6. Backend Changes — Không có

`CreateBookletJob` đã nhận `pageRotations` và apply chúng lên source PDF trước khi tạo booklet. Khi together mode, `pageRotations` chứa CCW90 cho landscape pages → source PDF có trang portrait-sized → `DrawPageOnHalf` nhận portrait → không xoay thêm (vì `isLandscape = fw > fh` → false).

```csharp
// DrawPageOnHalf — behavior với portrait input (fw ≤ fh):
bool isLandscape = fw > fh;  // false → skip rotation branch
gfx.DrawImage(form, x, y, halfWidth, pageHeight);  // Đặt thẳng
```

**Zero backend change.**

---

## 7. ASCII Diagrams

### 7.1 Booklet separate mode (hành vi hiện tại — không đổi)

```
INPUT: [1P  2P  3L  4P]   (3L = page 3 landscape)

Frontend: landscapeMode = 'separate' → không inject CCW90
pageRotations: {} (empty)

Backend CreateBookletJob:
  Apply pageRotations (empty) → source PDF unchanged
  CalculateBookletOrder(4) → [4, 1, 2, 3]   (fold order, NOT document order)

  Physical sheet 1 (1 tờ A4):
    Front: page 4 (left half) + page 1 (right half)
    Back:  page 2 (left half) + page 3 (right half)

  DrawPageOnHalf(page 3, landscape):
    fw > fh → isLandscape = true
    → xoay -90°, thu nhỏ vừa halfWidth×pageHeight
    → trang landscape bị nhỏ lại, người đọc phải nghiêng đầu

Kết quả: booklet PDF (A4 landscape), 1 physical sheet (4 pages = 1 sheet)
```

### 7.2 Booklet together mode — mixed L+P

```
INPUT: [1P  2P  3L  4P]

Frontend _renderSheetView together mode (lines 4100–4177):
  _originalOrientationMap: {1:false, 2:false, 3:true, 4:false}
  Inject CCW90 cho page 3:
    pageRotations:      {3 → 'CCW90'}
    _togetherRotations: {3}
  Invalidate cache: orientationMap.set(3, false)

_startPrint() gửi:
  pageRotations: [{pageNumber: 3, rotation: 'CCW90'}]
  duplexSide: null  (không phải duplex → guard)

Backend CreateBookletJob:
  Apply pageRotations → source PDF: page 3 đã portrait-sized (210×297)
  CalculateBookletOrder(4) → [4, 1, 2, 3]

  Physical sheet 1:
    Front: page 4 (portrait) + page 1 (portrait)
    Back:  page 2 (portrait) + page 3 (portrait, đã CCW90)

  DrawPageOnHalf(page 3):
    fw ≤ fh → isLandscape = false
    → đặt thẳng, không xoay thêm
    → trang 3 đẹp trong booklet

Kết quả: booklet PDF (A4 landscape), 1 physical sheet, trang landscape đẹp
```

### 7.3 Booklet together mode — tài liệu thuần landscape (8 pages)

```
INPUT: [1L  2L  3L  4L  5L  6L  7L  8L]   (slides bài giảng)

Frontend injection: CCW90 cho tất cả 8 trang
pageRotations: {1→CCW90, 2→CCW90, ..., 8→CCW90}

Backend:
  Source PDF: 8 trang portrait-sized (210×297)
  CalculateBookletOrder(8) → [8,1, 2,7, 6,3, 4,5]
  sheetCount = 8 / 4 = 2 physical sheets

  Physical sheet 1 (1 tờ A4, in 2 mặt):
    Front: page 8 (left) + page 1 (right)
    Back:  page 2 (left) + page 7 (right)

  Physical sheet 2 (1 tờ A4, in 2 mặt):
    Front: page 6 (left) + page 3 (right)
    Back:  page 4 (left) + page 5 (right)

  Tất cả DrawPageOnHalf calls: isLandscape = false → đặt thẳng, không xoay

  +─────────────────────+  +─────────────────────+
  │   Physical Sheet 1  │  │   Physical Sheet 2  │
  │─────────────────────│  │─────────────────────│
  │  Front: 8L | 1L     │  │  Front: 6L | 3L     │
  │  Back:  2L | 7L     │  │  Back:  4L | 5L     │
  +─────────────────────+  +─────────────────────+

Sau khi gấp thành sách: trang 1,2,3,4,5,6,7,8 đúng thứ tự
```

### 7.4 Fallback khi `_originalOrientationMap == null`

```
Trigger: _startPrint chạy khi _originalOrientationMap == null.
Xảy ra khi:
  (a) viewMode = 'page' — _renderSheetView chưa chạy bao giờ.
  (b) viewMode = 'sheet' nhưng user click print TRƯỚC KHI async first
      render commit xong (_pendingOrientationMap in-flight).

Cả hai case đều safe vì fallback là request-local:

State (case a — page view):
  _originalOrientationMap = null
  _togetherRotations = {} (empty)
  file.pageRotations = {}  (page view tears down together state)

State (case b — sheet view, in-flight):
  _originalOrientationMap = null  (_pendingOrientationMap not yet committed)
  file.pageRotations may or may not have CCW90 yet

_startPrint() chạy:
  mode = 'booklet', landscapeMode = 'together'
  _originalOrientationMap == null → trigger fallback

  pageRotationsForPrint = new Map(file.pageRotations)  // clone

  Async detect từ pdfDoc.getPage() với rotation=0 (intrinsic):
      intrinsicMap: {1:true, 2:false, 3:true}  (true = intrinsic landscape)

  Inject CCW90 vào pageRotationsForPrint:
      for each p where intrinsicMap[p]=true AND NOT pageRotationsForPrint.has(p):
          pageRotationsForPrint.set(p, 'CCW90')

  file.pageRotations KHÔNG thay đổi
  _pendingOrientationMap KHÔNG bị đụng (benign overlap)

Request body:
  pageRotations: [{pageNumber:1, rotation:'CCW90'}, {pageNumber:3, rotation:'CCW90'}]
  duplexSide: null

Backend nhận portrait-sized pages → booklet đúng
Nếu print FAIL → file state sạch, không có hidden rotations
```

### 7.5 Mode switch sequence — booklet+together → duplex

```
State: booklet + together, file có pages [1L, 2P, 3L]
  pageRotations:      {1→'CCW90', 3→'CCW90'}  (từ sheet view render)
  _togetherRotations: {1, 3}

User switches printMode → duplex:
  1. AppState.printMode = 'duplex'  (landscapeMode KHÔNG thay đổi, vẫn = 'together')

  2. onPrintModeChange() fires (app.js:5211):
     → Always tears down active file nếu có _togetherRotations (app.js:5222-5225):
         _teardownTogether(AppState.activeFile)
         pageRotations:      {}   (cleared)
         _togetherRotations: {}
         _originalOrientationMap: null

  3. PreviewPanelModule.render() chạy → _renderSheetView() (duplex mode)

  4. _renderSheetView step [0]:
     landscapeMode === 'together' AND _togetherRotations.size = 0
     → Condition FALSE (size = 0) → không teardown thêm

  5. Together mode block (lines 4100-4177) chạy:
     _originalOrientationMap = null → snapshot intrinsic orientations
     Inject CCW90 cho pages 1, 3
     pageRotations:      {1→'CCW90', 3→'CCW90'}  (rebuilt)
     _togetherRotations: {1, 3}

  Preview duplex+together: đúng ✓

Mechanism: teardown → re-inject (NOT "giữ nguyên state cũ")
```

---

## 8. Invariants

| # | Condition | Expected behaviour |
|---|-----------|-------------------|
| B1 | `separate` mode booklet | Không đổi. `DrawPageOnHalf` xoay landscape như cũ. `duplexSide: null`. |
| B2 | `together` mode booklet, tất cả portrait | Không inject CCW90. `pageRotations` rỗng → booklet như cũ. |
| B3 | `together` mode booklet, mixed L+P | CCW90 inject cho L pages. Backend nhận portrait-sized pages. DrawPageOnHalf không xoay. |
| B4 | `together` mode booklet, thuần landscape | Tất cả trang CCW90. `duplexSide: null` (không phải duplex). Booklet đẹp. |
| B5 | `_startPrint()` booklet — `duplexSide` | Backend ignore field này cho booklet. Guard `mode !== 'booklet'` REQUIRED — thêm vào để code rõ ràng, tránh confusion. |
| B6 | `_resumePrintQueue()` booklet — `duplexSide` | Cùng guard như B5. REQUIRED. |
| B7 | Fallback khi `_originalOrientationMap == null` | `_startPrint` detect intrinsic orientations async (request-local). `pageRotationsForPrint` có CCW90. `file.pageRotations` KHÔNG mutate. Xảy ra khi page view HOẶC sheet view trước first-render commit. |
| B8 | Fallback + print FAIL | File state sạch — `pageRotationsForPrint` là local var, không persist. `_pendingOrientationMap` không bị đụng. |
| B9 | Switch `booklet+together → duplex` | `onPrintModeChange()` tears down active file. `landscapeMode` không đổi. `_renderSheetView` re-injects vì `landscapeMode === 'together'`. Kết quả cuối: duplex+together hoạt động đúng. |
| B10 | Switch `duplex+together → booklet` | Đã xử lý bởi spec landscape-together-mode §5.4b. |
| B11 | Switch `sheet → page` view khi booklet+together | `PreviewPanelModule.render()` §5.0 teardown chạy nếu mode đổi. `_togetherRotations` không stale. |
| B12 | User manually rotates page khi booklet+together | `ContextMenu._applyRotation` evicts khỏi `_togetherRotations`. Teardown không xóa manual rotation. |
| B13 | File switch khi booklet+together | New file có `_togetherRotations = new Set()`. Injection chạy fresh. |
| B14 | Badge suppression khi booklet+together | Cùng logic §5.5/5.6 spec landscape-together (dùng `_togetherRotations`). Không hiện badge '-90°' cho trang tự động inject. |
| B15 | `pageRotations` string format | ✓ Verified: frontend gửi string `'CCW90'`. Backend dùng `JsonStringEnumConverter` (`BackendStartup.cs:22-26`), enum `RotationDirection` có `CCW90` (`PrintModels.cs:51-65`), tests cover nó. Format đúng. |
| B16 | `sheetCount` cho booklet | `sheetCount = n / 4` (4 pages = 1 physical sheet = 1 tờ A4 in 2 mặt). 8 pages = 2 physical sheets. Diagrams §7 dùng đúng. |
| B17 | Fallback request-local — no file mutation | `pageRotationsForPrint` là local Map clone. Không đụng `file.pageRotations`, `_togetherRotations`, `_originalOrientationMap`. |

---

## 9. Scope of Changes

| File | Change | Location |
|------|--------|----------|
| `frontend/app.js` | `_startPrint()`: guard `duplexSide` + request-local fallback (`pageRotationsForPrint`) §5.1 | ~line 2561 |
| `frontend/app.js` | `_resumePrintQueue()`: same guard + same fallback, dùng captured `mode` từ `modeCode` §5.2 | ~line 2772 |
| `frontend/index.html` | Không thay đổi | — |
| `backend/*` | Không thay đổi | — |

**2 changes, 1 file.** Preview đã hoạt động đúng (§2.2). UI đã hiện modebar cho booklet (§2.5).

---

## 10. Out of Scope

- **Backend booklet algorithm:** `CreateBookletJob` → `CalculateBookletOrder` → `CreateBookletPdf` → `DrawPageOnHalf` không thay đổi.
- **`_renderSheetView` together mode block:** Đã hoạt động đúng cho booklet (§2.2).
- **`_syncModeBar`:** Không cần thay đổi (§2.5).
- **`onPrintModeChange`:** Không cần thay đổi — nó tears down active file + triggers re-render, cơ chế đúng (§7.5).
- **Manual duplex cho booklet:** `CreateBookletJob` đã handle.
- **Watermark:** Apply sau booklet PDF creation — không ảnh hưởng.
- **Multi-file booklet:** Per-file. Không thay đổi.

---

## 11. Verified Assumptions

1. **`pageRotations` string format — CLOSED:** Frontend gửi string `'CCW90'`. Backend đã dùng `JsonStringEnumConverter` (`BackendStartup.cs:22-26`). Enum `RotationDirection` có `CCW90` (`PrintModels.cs:51-65`). Tests đã cover (`ComprehensivePrintLogicTests.cs:436-448`). Không cần thay đổi gì.
