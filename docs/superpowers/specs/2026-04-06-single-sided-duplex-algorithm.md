# Spec: Single-Sided / Duplex Mixed Printing Algorithm

**Date:** 2026-04-06  
**Status:** Final — Round 6 fixes applied, ready for implementation

---

## 1. Problem Statement

Khi user đánh dấu một số trang là "in 1 mặt" (single-sided) trong luồng mặc định in 2 mặt (duplex), cần một thuật toán thống nhất để:

1. Tính sheet layout đúng (frontend SheetView preview)
2. Gửi đủ data xuống backend để in ra đúng với preview
3. Xử lý tất cả edge cases: orientation mix, user blanks, drag reorder, batch toggle

---

## 2. Quy tắc nghiệp vụ

| # | Quy tắc |
|---|---------|
| R1 | Trang single-sided **chiếm trọn 1 tờ riêng**: front = trang đó, back = blank |
| R2 | Trang single-sided **luôn ngắt luồng**: trang trước nó bị đóng sớm (blank back) nếu đang lẻ tờ |
| R3 | Hai trang single-sided liền nhau = 2 tờ riêng biệt, không gộp |
| R4 | Portrait và landscape **không share tờ** (mode `separate` — default và backend) |
| R5 | User blank (pageNum=0) trước trang single-sided → chiếm back của tờ trước, không ảnh hưởng |
| R6 | User blank (pageNum=0) ngay **sau** trang single-sided → được **absorbed** làm back của tờ single-sided đó, thay vì sinh tờ trắng mới |
| R7 | Khi bỏ single-sided trang X: nếu blank (0) đã bị absorbed bởi X → xóa blank đó khỏi pageOrder |
| R8 | Deselect trang X xóa X khỏi `singleSidedPages`. Re-select X bắt đầu như trang duplex bình thường (không tự động single-sided lại) |

---

## 3. Bugs cần fix

### 3.1 Critical

**C1 — landscapeMode default mismatch**  
`AppState.landscapeMode` phải là `'separate'` — backend `ProcessMixedOrientation` chỉ hỗ trợ `'separate'`.  
**Fix:** `landscapeMode: 'separate'` trong AppState (app.js line ~34). Xem §4.8.

**C2 — User blank bị backend drop**  
Backend `ApplyPageOrder` phải giữ `0` trong order list và `CreatePdfSubset` phải xử lý `0` như blank marker.  
**Fix:** Xem §4.7.

**C3 — `orientationMap.get(0) = undefined` phá vỡ group detection**  
Blank page (0) không có entry trong orientationMap → bị treat sai orientation → tờ thừa.  
**Fix:** Blank (0) kế thừa orientation của group hiện tại trong Bước 1 (xem §4.1).

**C4 — Drag reorder không rebuild SheetView**  
`DragReorderModule._drop()` phải gọi `PreviewPanelModule.render()` sau khi cập nhật `pageOrder`.  
**Fix:** Xem §4.6.

### 3.2 High

**H1 — Race condition X-button double-click**  
`PreviewPanelModule.render()` là async. Double-click → hai handler chạy với stale closure.  
**Fix:** Lock flag `_isDeleting`. Xem §4.5.

**H2 — `[null|0]` sheets: front = null**  
Trang single-sided ở position lẻ → algo push `null` (auto-blank) → user blank (0) kế tiếp cặp thành `[null|0]`.  
**Fix:** Look-ahead absorption trong Bước 2 (xem §4.1).

**H3 — `isSingleForced` = false khi back = absorbed user blank**  
Condition phải chấp nhận cả `back === null` lẫn `back === 0`.  
**Fix:** Xem §4.9.

### 3.3 Medium

**M1 — Drag reorder với ≥2 blanks: Map key collision**  
`_reRenderGrid` dùng `Map<pageNum, DOMElement>` → key `0` bị overwrite bởi blank thứ 2.  
**Fix:** Dùng render index làm key. Xem §4.6.

**M2 — Stale `singleSidedPages` khi deselect**  
Deselect trang X phải cleanup `singleSidedPages` — dùng centralized helper.  
**Fix:** Xem §4.11.

---

## 4. Thuật toán

> **Notation:** Pseudocode dùng `∈` cho `Set.has()`, `enumerate(pages)` cho indexed iteration `(i, p)`, syntax JS-like. Mixed notation là chủ đích — ưu tiên đọc hiểu hơn parse-ability.

### 4.1 `buildSheetLayout` — Duplex path

**Phạm vi:** Hàm hiện tại có signature `buildSheetLayout(fileEntry, printMode, orientationMap, landscapeMode)` và xử lý simplex/booklet/duplex. Spec này **chỉ định nghĩa nhánh duplex bên trong** — không đổi signature, không xóa simplex/booklet branches. Khi `printMode === 'duplex'`, thay toàn bộ logic hiện tại bằng algorithm dưới đây.

```
Input (truy cập từ fileEntry bên trong duplex branch):
  pages[]          — derived inline trước Bước 1:
                     const pages = fileEntry.pageOrder.filter(p => p === 0 || fileEntry.selectedPages.has(p))
                     // fallback khi pageOrder rỗng:
                     if (!pages.length) pages = [...fileEntry.selectedPages].sort((a,b) => a-b)
  singleSidedPages — fileEntry.singleSidedPages  (Set<pageNum>)
  orientationMap   — tham số truyền vào (Map<pageNum, isLandscape>, built fresh by caller each render cycle)
  landscapeMode    — phải là 'separate' (Invariant 8); 'together' bị reject tại entry

Output (return value):
  { sheets[], blankAbsorbedBy }
  sheets[]         — [{ sheetIndex, front, back, isLandscape, isSingleForced, backIsUserBlank }]
  blankAbsorbedBy  — Map<absorbingPageNum, 0>
                     key = pageNum của trang single-sided đã absorb blank
                     value = 0 (blank pageNum, luôn là 0)
                     Dùng để CHECK "trang X có absorbed blank không?" — KHÔNG dùng như splice index

Caller gán ngay sau khi nhận return:
  const { sheets, blankAbsorbedBy } = buildSheetLayout(...)
  fileEntry.blankAbsorbedBy = blankAbsorbedBy
```

#### Bước 1: Group theo orientation

```
groups = []
currentGroup = { isLandscape: null, pages: [] }

for (i, p) in enumerate(pages):
    if p === 0:
        # Blank kế thừa orientation của group hiện tại.
        # Nếu chưa có group (blank đứng đầu danh sách), dùng lookAheadOrientation().
        effectiveOrientation = currentGroup.isLandscape ?? lookAheadOrientation(pages, i, orientationMap)
    else:
        effectiveOrientation = orientationMap.get(p) ?? false  # false = portrait

    if currentGroup.isLandscape === null:
        currentGroup.isLandscape = effectiveOrientation

    if effectiveOrientation !== currentGroup.isLandscape:
        # Orientation thay đổi → đóng group hiện tại, mở group mới
        groups.push(currentGroup)
        currentGroup = { isLandscape: effectiveOrientation, pages: [] }

    currentGroup.pages.push({ pageNum: p, originalIndex: i })
    # originalIndex = index trong pages[] — chỉ dùng cho debug/trace, KHÔNG dùng làm splice index

groups.push(currentGroup)  # đóng group cuối
```

#### Bước 2: Xử lý single-sided trong từng group

```
logicalPages = []  # global, gộp tất cả groups
blankAbsorbedBy = new Map()  # Map<absorbingPageNum, 0>
                              # Rebuilt từ đầu mỗi lần hàm này chạy

for group in groups:
    groupLogical = []
    i = 0

    while i < group.pages.length:
        entry    = group.pages[i]           # { pageNum, originalIndex }
        p        = entry.pageNum
        nextEntry = group.pages[i + 1]      # có thể undefined
        next     = nextEntry?.pageNum       # safe access

        if p ∈ singleSidedPages:
            # R2: đóng tờ hiện tại nếu đang lẻ (trang trước bị ngắt sớm)
            if groupLogical.length % 2 === 1:
                groupLogical.push({ pageNum: null, isLandscape: group.isLandscape })

            groupLogical.push({ pageNum: p, isLandscape: group.isLandscape })

            # R6: look-ahead — nếu blank (0) ngay sau → absorb làm back của tờ single-sided
            if next === 0:
                groupLogical.push({ pageNum: 0, isLandscape: group.isLandscape })
                blankAbsorbedBy.set(p, 0)  # key = pageNum của SS page, value = 0
                i += 2  # skip blank trong vòng lặp
            else:
                # Không có blank → auto-blank làm back
                groupLogical.push({ pageNum: null, isLandscape: group.isLandscape })
                i += 1

        else:
            groupLogical.push({ pageNum: p, isLandscape: group.isLandscape })
            i += 1

    # Pad group về even (R4: portrait/landscape không share tờ với nhau)
    if groupLogical.length % 2 === 1:
        groupLogical.push({ pageNum: null, isLandscape: group.isLandscape })

    logicalPages.push(...groupLogical)

# Mỗi group đã được pad riêng → không cần pad cuối
```

#### Bước 3: Pair thành sheets

```
sheets = []
sheetIdx = 1

for j = 0 to logicalPages.length step 2:
    f = logicalPages[j]
    b = logicalPages[j + 1]

    # b phải luôn tồn tại vì Bước 2 đã pad mỗi group về even
    if b === undefined:
        throw new Error(`[buildSheetLayout] BUG: odd logicalPages at j=${j}. Bước 2 padding failed.`)

    isSingleForced = f.pageNum !== null        # front phải là trang thật
                  && f.pageNum !== 0           # không phải user blank
                  && (b.pageNum === null        # back là auto-blank
                      || b.pageNum === 0)       # hoặc absorbed user blank
                  && f.pageNum ∈ singleSidedPages

    sheets.push({
        sheetIndex:     sheetIdx++,
        front:          f.pageNum,
        back:           b.pageNum,
        isLandscape:    f.isLandscape,
        isSingleForced: isSingleForced,
        backIsUserBlank: b.pageNum === 0   # phân biệt absorbed user blank vs auto-null
    })

return { sheets, blankAbsorbedBy }
```

---

### 4.2 Toggle single-sided — Bật (→ single-sided)

```
function setSingleSided(fileEntry, pageNum):
    if pageNum === 0: return           # Invariant 5: blank không được là single-sided
    fileEntry.singleSidedPages.add(pageNum)
    rebuildSheetView(fileEntry)        # Invariant 7
```

---

### 4.3 Toggle single-sided — Tắt (→ về duplex)

```
function unsetSingleSided(fileEntry, pageNums[]):
    # 1. Thu thập indices của blanks cần xóa TRƯỚC KHI xóa bất kỳ cái nào
    #    (tránh index shift trong loop)
    blankIndicesToRemove = []
    for pageNum in pageNums:
        if fileEntry.blankAbsorbedBy.has(pageNum):
            # Runtime scan: tìm blank (0) sau pageNum trong raw pageOrder,
            # skip qua deselected pages (có thể nằm giữa SS page và blank).
            # Lý do: pages[] là filtered view, blank có thể adjacent với pageNum trong
            # pages[] nhưng trong raw pageOrder lại bị ngăn bởi deselected pages.
            # Ví dụ: pageOrder=[1,2,5,0,3], selectedPages={1,2,3} (5 deselected)
            #   → pages[]=[1,2,0,3] → 2 absorb 0 ✓
            #   → rawIdx=1, pageOrder[2]=5 ≠ 0 → check phải tiếp tục skip 5
            #   → pageOrder[3]=0 → push(3) ✓
            const rawIdx = fileEntry.pageOrder.indexOf(pageNum)
            if rawIdx >= 0:
                for k = rawIdx + 1 to fileEntry.pageOrder.length:
                    v = fileEntry.pageOrder[k]
                    if v === 0:
                        blankIndicesToRemove.push(k)
                        break
                    if fileEntry.selectedPages.has(v):
                        break  # hit selected page → blank cannot be absorbed beyond this point
                    # else: v is deselected page → skip and continue forward
        fileEntry.singleSidedPages.delete(pageNum)

    # 2. Xóa theo DESCENDING để tránh index shift (Invariant 4)
    blankIndicesToRemove.sort((a, b) => b - a)
    for idx in blankIndicesToRemove:
        fileEntry.pageOrder.splice(idx, 1)

    # 3. Rebuild
    rebuildSheetView(fileEntry)        # Invariant 7
```

**Lý do forward scan:** `pageOrder` có thể chứa deselected pages không có trong `pages[]`. Ví dụ: `pageOrder = [1, 2, 5, 0, 3]`, `selectedPages = {1,2,3}` → `pages[] = [1,2,0,3]`. Blank tại `pages[2]` adjacent với SS page 2 trong filtered view → absorbed. Nhưng raw pageOrder có page 5 (deselected) nằm giữa → `rawIdx+1` check đơn giản trả về `pageOrder[2]=5 ≠ 0` → miss. Forward scan skip qua deselected pages và dừng khi gặp selected page hoặc blank.

---

### 4.4 `blankAbsorbedBy` + `rebuildSheetView` contract

#### `rebuildSheetView(fileEntry)` — định nghĩa

`rebuildSheetView` không phải hàm riêng mới — đây là alias cho chuỗi:
1. `fileEntry.blankAbsorbedBy = new Map()` — clear trước (Invariant 3)
2. `PreviewPanelModule.render(fileEntry)` — render gọi `buildSheetLayout`, nhận `{ sheets, blankAbsorbedBy }`, gán `fileEntry.blankAbsorbedBy = blankAbsorbedBy`

```javascript
// PreviewPanelModule.render():
render(fileEntry) {
    fileEntry.blankAbsorbedBy = new Map()  // Invariant 3: reset trước mỗi rebuild

    // Invariant 8: 'together' không có backend support — coerce tại entry
    if (fileEntry.landscapeMode === 'together') {
        console.warn('[render] landscapeMode="together" unsupported — forcing "separate"')
        fileEntry.landscapeMode = 'separate'
    }

    if (this._viewMode === 'sheet') {
        return this._renderSheetView(fileEntry)  // PHẢI return Promise (Invariant 10)
    }
    // ... existing page view rendering ...
}

// _renderSheetView (async):
async _renderSheetView(fileEntry) {
    // ... build orientationMap (existing) ...

    const { sheets, blankAbsorbedBy } = buildSheetLayout(fileEntry, 'duplex', orientationMap, 'separate')
    fileEntry.blankAbsorbedBy = blankAbsorbedBy  // Invariant 9

    // ... existing DOM render using sheets ...
}
```

#### `blankAbsorbedBy` — semantics

```
Map<absorbingPageNum, 0>
  key   = pageNum của trang single-sided đã absorb blank
  value = 0 (blank pageNum, luôn là 0)

Mục đích: chỉ để CHECK "trang X có absorbed blank không?"
  → blankAbsorbedBy.has(X) === true nếu X đã absorb blank

KHÔNG dùng key hay value như splice index.

Ví dụ: pageOrder = [1, 3, 0, 4], trang 3 là single-sided, absorb blank tại pageOrder[2]:
  blankAbsorbedBy = Map { 3 => 0 }
  unsetSingleSided([3]):
    blankAbsorbedBy.has(3) → true
    pageOrder.indexOf(3) = 1 → pageOrder[2] === 0 → splice(2) ✓
```

#### Contract: entry points bắt buộc gọi `rebuildSheetView`

| Entry point | Khi nào |
|-------------|---------|
| `setSingleSided(fileEntry, pageNum)` | Sau `.add(pageNum)` |
| `unsetSingleSided(fileEntry, pageNums[])` | Sau `.splice()` |
| `DragReorderModule._drop()` | Sau cập nhật `pageOrder` |
| X-button delete blank | Sau `.splice()` |
| `togglePageSelection(entry, pageNum)` | Caller phải gọi sau khi trả về |

> **`buildEffectivePageOrder` (§4.7)** không phải mutation trigger, nhưng **phụ thuộc vào `blankAbsorbedBy` đã được populate**. Điều này được đảm bảo bởi Invariant 7: mọi mutation đều trigger `rebuildSheetView` → `blankAbsorbedBy` luôn fresh tại thời điểm print. Nếu user chưa render SheetView lần nào, `blankAbsorbedBy = new Map()` (initialized tại `createFileEntry`) → `buildEffectivePageOrder` sẽ giữ tất cả blanks → safe (không strip nhầm), chỉ miss absorbed-blank stripping khi user in ngay không qua SheetView — edge case chấp nhận được.

**`onStateChanged()` — PHẢI route qua `render()`:**  
`app.js:3596` hiện gọi `this._renderSheetView(fileEntry)` trực tiếp — bypass `render()` → không reset `blankAbsorbedBy`, không coerce `landscapeMode`. Phải đổi thành `this.render(fileEntry)` để đảm bảo Invariant 3 và 8 được áp dụng.

```javascript
// app.js onStateChanged() — TRƯỚC (sai):
if (this._viewMode === 'sheet' && AppState.activeFile) {
    this._renderSheetView(AppState.activeFile);  // bypass render() → stale blankAbsorbedBy
}

// SAU (đúng):
if (this._viewMode === 'sheet' && AppState.activeFile) {
    this.render(AppState.activeFile);  // goes through reset + coerce
}
```

**Deselect ≠ R7:** Deselect trang X chỉ cleanup Sets (`selectedPages`, `singleSidedPages`). KHÔNG gọi `unsetSingleSided` — không splice blank. Blank (0) ngay sau X trong `pageOrder` vẫn còn đó và sẽ hiển thị đúng khi rebuildSheetView chạy lại (X không còn là SS page → blank không bị absorbed → trở thành standalone user blank).

---

### 4.5 X-button deletion — Race condition fix

```javascript
// Gắn stable identifier vào mỗi blank DOM node khi render:
blankCard.dataset.blankRenderPos = blankQueuePos  // thứ tự blank trong render

xBtn.onclick = () => {
    if (PreviewPanelModule._isDeleting) return  // drop click thứ hai (có chủ đích, không queue)
    PreviewPanelModule._isDeleting = true

    // Tìm lại index tại thời điểm click — không dùng closure stale
    const renderPos = parseInt(blankCard.dataset.blankRenderPos)
    const allBlanks = entry.pageOrder
        .map((p, i) => p === 0 ? i : -1)
        .filter(i => i >= 0)
        const currentIdx = allBlanks[renderPos]  // vị trí của blank thứ N trong pageOrder
                                                  // undefined nếu DOM stale → condition bên dưới false → unlock

    try {
        if (currentIdx >= 0 && entry.pageOrder[currentIdx] === 0) {
            entry.pageOrder.splice(currentIdx, 1)
            const result = PreviewPanelModule.render(entry)
            if (result && typeof result.finally === 'function') {
                result.finally(() => { PreviewPanelModule._isDeleting = false })
            } else {
                PreviewPanelModule._isDeleting = false
            }
        } else {
            PreviewPanelModule._isDeleting = false
        }
    } catch (e) {
        PreviewPanelModule._isDeleting = false  // unlock kể cả khi render() throw sync
        throw e
    }
}
```

Click thứ hai trong khi `_isDeleting=true` bị drop có chủ đích (không queue) — queued clicks sẽ operate trên DOM đã rebuilt với shifted indices, có thể xóa nhầm.

---

### 4.6 Drag reorder — Map key collision fix + SheetView rebuild

```javascript
// Trong _reRenderGrid: dùng render index làm key, không dùng pageNum (key '0' bị overwrite)
const byRenderIdx = new Map()
for (let i = 0; i < thumbEls.length; i++) {
    byRenderIdx.set(i, thumbEls[i])
}

// Trong DragReorderModule._drop(): rebuild SheetView sau khi cập nhật pageOrder
if (AppState.viewMode === 'sheet' && AppState.activeFile) {
    PreviewPanelModule.render(AppState.activeFile)
}
```

---

### 4.7 Backend — Giữ blank (0) trong pipeline

#### C2a — `ApplyPageOrder` (PrintAlgorithmService.cs ~line 522)

```csharp
// Giữ 0 như blank marker — không filter bỏ
var reordered = pageOrder.Where(p => p == 0 || selectedSet.Contains(p)).ToList();
```

#### C2b — `CreatePdfSubset` (WordInteropService.cs ~line 679)

```csharp
if (pageNum == 0)
{
    // Kế thừa orientation từ trang trước trong targetDoc; fallback A4 portrait.
    PdfPage? template = targetDoc.PageCount > 0
        ? targetDoc.Pages[targetDoc.PageCount - 1]
        : null;

    if (template != null)
    {
        bool isLandscape = template.Width.Point > template.Height.Point;
        CreateNonSkippableBlankPage(targetDoc, template, isLandscape);
    }
    else
    {
        // Blank là trang đầu tiên — không có template, tạo A4 portrait thủ công
        var blankPage = targetDoc.AddPage();
        blankPage.Width  = XUnit.FromPoint(595.28);
        blankPage.Height = XUnit.FromPoint(841.89);
        using var gfx = XGraphics.FromPdfPage(blankPage);
        gfx.DrawRectangle(XBrushes.White, 0, 0, 0.01, 0.01);  // non-skippable
    }
}
else if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
{
    targetDoc.AddPage(sourceDoc.Pages[pageNum - 1]);
}
// else: pageNum out of range — skip (existing behavior)
```

> `CreateNonSkippableBlankPage` đã có sẵn tại WordInteropService.cs ~line 24 — thêm tiny invisible element để printer driver không skip blank. Dùng nó khi có template; inline equivalent khi không có template (null-template fallback).

#### Backend `singleSidedPages` data flow

`ProcessMixedOrientation` cần biết trang nào là single-sided để insert system blank sau chúng. Data flow hiện tại (existing code, không thay đổi trong spec này):
- Frontend gửi `PrintRequest` với field `singleSidedPages` (array of original page numbers)
- Backend `ApplyPageOrder` → `CreatePdfSubset` → remaps `singleSidedPages` sang subset indices (1-based position trong subset PDF)
- `ProcessMixedOrientation` nhận remapped SS set và xử lý

> **Lưu ý cho implementer:** `buildEffectivePageOrder` strip absorbed blanks khỏi `pageOrder` TRƯỚC khi gửi. Kết quả là `singleSidedPages` vẫn giữ nguyên (không cần strip) vì backend cần biết trang nào là SS để tạo system blank. Chỉ user blanks bị absorbed mới không gửi — backend sẽ tạo system blank thay thế.

#### R6 absorbed blank — Frontend strips before sending to backend

**Problem:** Khi trang X là single-sided và đã absorb user blank (0) ngay sau nó, frontend preview hiển thị `[X|0]` trên một tờ. Nhưng nếu `pageOrder=[X, 0, Y]` được gửi nguyên vẹn xuống backend:
- `CreatePdfSubset` tạo PDF 3 trang: `[pageX, blank, pageY]`
- `ProcessMixedOrientation` thấy pageX là SS → insert system blank sau pageX
- Kết quả: `[pageX, sysBlank, userBlank, pageY]` — 4 trang, 2 sheets: `[pageX|sysBlank], [userBlank|pageY]`
- **Mismatch với frontend:** Frontend cho `[X|0], [Y|null]`

**Fix — Frontend strip absorbed blanks trước khi build PrintRequest:**  
Trong hàm build print request (app.js ~line 2086), trước khi gửi `pageOrder`, strip tất cả absorbed blanks:

```javascript
// Khi build PrintRequest, derive effective pageOrder cho backend:
function buildEffectivePageOrder(fileEntry) {
    // Derive pages[] — same filtered view buildSheetLayout uses.
    // PHẢI dùng pages[] thay vì raw pageOrder để tránh false-negative khi
    // có deselected page nằm giữa SS page và absorbed blank trong raw pageOrder.
    // Ví dụ: pageOrder=[1,2,5,0,3], selectedPages={1,2,3} → pages[]=[1,2,0,3]
    //   → blank absorbed by page 2. Nhưng raw[i-1]=5 → has(5)=false → blank sẽ KHÔNG bị strip nếu check raw.
    let pages = fileEntry.pageOrder.filter(
        p => p === 0 || fileEntry.selectedPages.has(p)
    )

    // Fallback: nếu pageOrder rỗng nhưng selectedPages không rỗng (edge case hiếm),
    // derive pages từ selectedPages (giống buildSheetLayout fallback) để tránh
    // mismatch: preview có nội dung nhưng backend nhận mảng rỗng.
    if (!pages.length && fileEntry.selectedPages.size > 0) {
        pages = [...fileEntry.selectedPages].sort((a, b) => a - b)
    }

    // Filter: strip blanks absorbed by SS pages, keep standalone blanks.
    return pages.filter((p, i) => {
        if (p !== 0) return true                      // non-blank: always keep
        const prevPage = pages[i - 1]                 // predecessor in filtered view
        return !fileEntry.blankAbsorbedBy.has(prevPage)  // keep standalone blanks only
    })
}
// Dùng buildEffectivePageOrder(fileEntry) thay vì fileEntry.pageOrder trực tiếp khi gửi backend.
// REQUIREMENT: fileEntry.blankAbsorbedBy phải được populate trước khi gọi hàm này
// (được đảm bảo bởi rebuildSheetView đã chạy sau mọi mutation — Invariant 7).
```

> **Lý do:** Backend không cần biết về absorbed blanks — SS page đã có system blank từ `ProcessMixedOrientation`. Chỉ standalone blanks (người dùng thêm vào giữa duplex pages) mới cần gửi xuống để backend tạo blank page tại đúng vị trí.
>
> **Tại sao dùng `pages[]` thay vì `pageOrder`:** Absorption được tính từ filtered view (deselected pages bị bỏ). Nếu dùng raw `pageOrder`, deselected page có thể nằm giữa SS page và blank → `prevPage` trỏ sai → blank không bị strip → double-blank trên backend.

---

### 4.8 `landscapeMode` default

```javascript
// app.js AppState (line ~34)
landscapeMode: 'separate',
// 'together' không có backend support — coerce tại entry (Invariant 8)
```

---

### 4.9 `isSingleForced` condition

```javascript
// Bên trong Bước 3 của buildSheetLayout:
isSingleForced = f.pageNum !== null        // front là trang thật
              && f.pageNum !== 0           // không phải user blank
              && (b.pageNum === null        // back là auto-blank
                  || b.pageNum === 0)       // hoặc absorbed user blank
              && f.pageNum ∈ singleSidedPages
```

`backIsUserBlank` (`b.pageNum === 0`) được expose trên sheet object cho future use. Frontend rendering hiện tại không cần dùng trực tiếp — `back=0` đã rơi vào nhánh `isSingleForced` đúng trong label logic.

---

### 4.10 `lookAheadOrientation` helper

```javascript
// Dùng trong Bước 1 khi blank đứng đầu danh sách, chưa có group
function lookAheadOrientation(pages, blankIdx, orientationMap) {
    for (let i = blankIdx + 1; i < pages.length; i++) {
        if (pages[i] !== 0) return orientationMap.get(pages[i]) ?? false
    }
    return false  // fallback: portrait
}
```

---

### 4.11 Deselect cleanup — centralized helper

**6 deselect paths** trong codebase (lines 500, 1362, 1551, 3063, 3220, 1343) đều phải cleanup `singleSidedPages`. Thay tất cả inline toggle blocks bằng helper sau:

```javascript
function togglePageSelection(entry, pageNum) {
    if (entry.selectedPages.has(pageNum)) {
        entry.selectedPages.delete(pageNum)
        entry.singleSidedPages.delete(pageNum)  // R8: cleanup — re-select bắt đầu như duplex
    } else {
        entry.selectedPages.add(pageNum)
        // Không tự động thêm singleSidedPages — user phải chủ động toggle
    }
    // CONTRACT: Caller PHẢI gọi rebuildSheetView(entry) sau khi hàm này trả về
}

// Thay thế pattern cũ tại lines 500, 1362, 1551, 3063, 3220:
// Cũ: if (entry.selectedPages.has(pageNum)) entry.selectedPages.delete(pageNum)
//     else entry.selectedPages.add(pageNum)
// Mới: togglePageSelection(entry, pageNum)
//      rebuildSheetView(entry)
```

**Range input (line ~1343):** Khi user nhập range text, `selectedPages` được replace bằng Set mới — cleanup `singleSidedPages` trước khi gán:

```javascript
const newSelected = parsePageRange(rangeText)
for (const p of entry.singleSidedPages) {
    if (!newSelected.has(p)) entry.singleSidedPages.delete(p)
}
entry.selectedPages = newSelected
rebuildSheetView(entry)
```

**`blankAbsorbedBy` trong `createFileEntry`:**

```javascript
createFileEntry(id, name, needsConversion) {
    return {
        id, name, needsConversion,
        pdfDoc:           null,
        totalPageCount:   0,
        selectedPages:    new Set(),
        singleSidedPages: new Set(),
        pageOrder:        [],
        pageRotations:    new Map(),
        blankAbsorbedBy:  new Map(),  // safety default — render() reset về new Map() mỗi lần
    }
}
```

---

## 5. Frontend ↔ Backend alignment

| Scenario | Frontend | Backend | Status |
|----------|----------|---------|--------|
| All portrait, single={2}, pages=[1,2,3] | `[1\|null],[2\|null],[3\|null]` | `[1\|sysBlank],[2\|sysBlank],[3\|sysBlank]` | ✅ Aligned |
| Mixed orient, `'separate'`, pages=[1P,2L,3L] | `[1\|null],[2\|3]` | `ProcessMixedOrientation` groups tương tự | ✅ Aligned |
| User blank standalone, pages=[1,0,2] | `[1\|0],[2\|null]` | `pageOrder=[1,0,2]` → blank page tại vị trí 2 | ✅ Aligned |
| User blank absorbed, pages=[1,2,0,3], single={2} | `[1\|null],[2\|0],[3\|null]` | Frontend strip → gửi `[1,2,3]`; backend tạo sysBlank cho 2 | ✅ Aligned (§4.7 strip) |
| `landscapeMode='together'` | Coerced → `'separate'` at entry | N/A — không support | ✅ Coerce + warn |
| SS tại orientation boundary, single={3}, pages=[1P,2P,3P,4L] | `[1\|2],[3\|null],[4\|null]` | 2 orientation groups, SS break đúng | ✅ Aligned |
| Consecutive SS, single={2,3}, pages=[1,2,3,4] | `[1\|null],[2\|null],[3\|null],[4\|null]` | Mỗi SS → 1 tờ riêng | ✅ Aligned |

---

## 6. Invariants

1. `logicalPages.length % 2 === 0` trước khi pair (Bước 3)
2. `logicalPages` trong 1 orientation group phải toàn cùng `isLandscape`
3. `blankAbsorbedBy` luôn được khởi tạo mới (`= new Map()`) ở đầu `rebuildSheetView` — không reuse giữa các lần rebuild
4. Xóa blank từ `pageOrder` theo DESCENDING numeric sort khi batch: `.sort((a, b) => b - a)` — KHÔNG dùng `.sort()` mặc định (lexicographic sai khi index ≥ 10)
5. `0` không bao giờ nằm trong `singleSidedPages`
6. `isSingleForced = true` → back phải là `null` hoặc `0` (absorbed)
7. Mọi mutation vào `pageOrder`, `singleSidedPages`, hoặc `selectedPages` PHẢI gọi `rebuildSheetView()` — không có bypass path nào
8. `landscapeMode` PHẢI là `'separate'`. `'together'` không có backend support — reject/coerce tại entry point của `render()`
9. `buildSheetLayout` duplex branch PHẢI return `{ sheets, blankAbsorbedBy }` — caller gán `fileEntry.blankAbsorbedBy = blankAbsorbedBy` ngay sau đó
10. `render()` PHẢI return Promise của `_renderSheetView()` trong sheet mode — không drop return value
11. `pageOrder` KHÔNG được chứa duplicate non-zero pageNums. Blank (0) có thể xuất hiện nhiều lần. Vi phạm Invariant 11 làm `indexOf` trong §4.3 scan tìm sai instance.

---

## 7. Out of scope

- Booklet mode (không có single-sided concept)
- Simplex mode (dead code, để cleanup riêng)
- `landscapeMode='together'` backend support (cần thêm field vào PrintRequest — future work)
