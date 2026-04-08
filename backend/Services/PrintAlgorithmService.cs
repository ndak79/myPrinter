using PrinterApp.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using Word = Microsoft.Office.Interop.Word;
using XGraphics = PdfSharp.Drawing.XGraphics;
using XPdfForm = PdfSharp.Drawing.XPdfForm;
using XUnit = PdfSharp.Drawing.XUnit;
using XPen = PdfSharp.Drawing.XPen;
using XColors = PdfSharp.Drawing.XColors;

namespace PrinterApp.Services;

public class PrintAlgorithmService
{
    private readonly IWordInteropService _wordService;

    public PrintAlgorithmService(IWordInteropService wordService)
    {
        _wordService = wordService;
    }

    public PrintJobState CreateNormalDuplexJob(
    string pdfPath,
    string printerName,
    bool isDuplexPrinter,
    string? pageRange = null,
    int[]? singleSidedPages = null,
    int[]? pageOrder = null,
    List<PageRotation>? pageRotations = null,
    string? duplexSide = null,      // "LongEdge" | "ShortEdge" | null
    string? manualFlipDir = null)   // "LongEdge" | "ShortEdge" | null => auto-detect
    {
        // Đọc metadata PDF gốc
        var pdfInfo = _wordService.GetPdfInfo(pdfPath);
        // BUG-7-7 fix: flipDirection must be computed AFTER ApplyPageRotations so that a
        // user-rotated first page doesn't produce the wrong flip heuristic.
        // If the frontend supplies manualFlipDir we use it immediately (it already accounts
        // for Together-mode CCW90 rewriting); otherwise we defer and resolve below, after
        // rotations have been applied to workingPdfPath.
        FlipDirection parsedFlipDir = FlipDirection.LongEdge; // default; may be overwritten
        bool flipDirectionOverridden = !string.IsNullOrEmpty(manualFlipDir) &&
                                       Enum.TryParse<FlipDirection>(manualFlipDir, out parsedFlipDir);
        // Temporary value — heuristic path will overwrite this after ApplyPageRotations.
        FlipDirection flipDirection = flipDirectionOverridden
            ? parsedFlipDir
            : FlipDirection.LongEdge;


        var jobState = new PrintJobState
        {
            TempPdfPath = pdfPath,
            PrinterName = printerName,
            IsManualDuplex = !isDuplexPrinter
        };

        // Store duplexSide (nullable — do NOT coalesce to "LongEdge").
        // null means "no explicit override" => PrintWithSumatra will emit no -print-settings arg.
        jobState.DuplexSide = duplexSide;

        if (isDuplexPrinter)
        {
            // ==========================
            //  AUTO DUPLEX (MÁY IN 2 MẶT)
            // ==========================
            Console.WriteLine("[CreateNormalDuplexJob] Auto duplex printer detected.");

            int[] selectedPages;
            if (!string.IsNullOrWhiteSpace(pageRange))
            {
                selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
                if (selectedPages.Length == 0)
                    throw new InvalidOperationException($"Page range '{pageRange}' khong hop le hoac khong co trang nao.");
            }
            else
            {
                selectedPages = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
            }

            // Apply custom page order if provided (I)
            selectedPages = ApplyPageOrder(selectedPages, pageOrder);

            // INSERT BLANK BACKS for single-sided pages (Bug B9 fix).
            // Auto-duplex printers pair pages sequentially: odd positions → front face,
            // even positions → back face. A page marked single-sided must land on a FRONT
            // face (odd position) and have a blank BACK face.
            //
            // Algorithm (position-aware):
            //   - Walk selectedPages left-to-right, tracking the 1-based print position.
            //   - When an SS page is found at an even (back) position, pad with a blank
            //     first to push it to the next front, then add the SS page + blank back.
            //   - When an SS page is at an odd (front) position, just add blank back.
            //   - If the page is already followed by a user-inserted blank (0), skip the
            //     extra blank to avoid double-blanking on that back face.
            if (singleSidedPages != null && singleSidedPages.Length > 0)
            {
                var singleSidedSet = new HashSet<int>(singleSidedPages);
                var adjusted       = new List<int>(selectedPages.Length + singleSidedPages.Length * 2);
                int position       = 1; // 1-indexed: odd = front face, even = back face

                for (int si = 0; si < selectedPages.Length; si++)
                {
                    int page = selectedPages[si];

                    if (page != 0 && singleSidedSet.Contains(page))
                    {
                        // Ensure SS page lands on a FRONT face (odd position)
                        if (position % 2 == 0)
                        {
                            adjusted.Add(0); // pad current back face → advance to next front
                            position++;
                        }

                        adjusted.Add(page); // SS page on front
                        position++;

                        // Add blank back — unless the next entry is already a blank
                        bool nextIsBlank = (si + 1 < selectedPages.Length && selectedPages[si + 1] == 0);
                        if (!nextIsBlank)
                        {
                            adjusted.Add(0);
                            position++;
                        }
                    }
                    else
                    {
                        adjusted.Add(page);
                        position++;
                    }
                }

                selectedPages = adjusted.ToArray();
                Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex: inserted SS blanks → pages: {string.Join(",", selectedPages)}");
            }

            // Apply per-page rotations before creating subset (U)
            var rotationMap = BuildRotationMap(pageRotations);

            if (!selectedPages.SequenceEqual(Enumerable.Range(1, pdfInfo.PageCount)))
            {
                Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex creating subset for pages: {string.Join(",", selectedPages)}");
                var subsetPath = Path.Combine(Path.GetTempPath(), $"auto_duplex_subset_{Guid.NewGuid()}.pdf");
                _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
                jobState.TempPdfPath = subsetPath;
                jobState.IntermediateFiles.Add(subsetPath); // BUG-8-2: track for cleanup

                // Remap rotation keys from original page numbers to subset indices
                rotationMap = RemapRotations(rotationMap, selectedPages);
            }

            // Apply rotations if any
            if (rotationMap.Count > 0)
            {
                var rotatedPath = _wordService.ApplyPageRotations(jobState.TempPdfPath, rotationMap);
                jobState.IntermediateFiles.Add(rotatedPath); // BUG-8-2: track for cleanup
                jobState.TempPdfPath = rotatedPath;
            }

            jobState.WaitingForFlip = false;
            return jobState;
        }

        // ==========================
        //  MANUAL DUPLEX (MÁY 1 MẶT)
        // ==========================
        Console.WriteLine("[CreateNormalDuplexJob] Manual duplex mode. Building plan...");

        string workingPdfPath = pdfPath;
        int[] effectiveSingleSidedPages = singleSidedPages ?? Array.Empty<int>();
        int[] pagesToPrint;

        // 1) XỬ LÝ PAGE RANGE + PAGE ORDER + TẠO SUBSET NẾU CẦN
        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            // Parse range → danh sách trang gốc (ví dụ 3,4,5)
            pagesToPrint = ParsePageRange(pageRange, pdfInfo.PageCount);

            if (pagesToPrint.Length == 0)
            {
                throw new InvalidOperationException($"[CreateNormalDuplexJob] Page range '{pageRange}' không hợp lệ hoặc không có trang nào.");
            }
        }
        else
        {
            // Không có pageRange → in toàn bộ
            pagesToPrint = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
        }

        // Apply custom page order if provided (I)
        pagesToPrint = ApplyPageOrder(pagesToPrint, pageOrder);

        // Build rotation map early so it is in scope both inside and outside the subset branch.
        // Keys are original page numbers (1-based) at this point.
        var manualRotationMap = BuildRotationMap(pageRotations);

        // Create subset if pages need reordering or filtering
        var naturalOrder = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
        if (!pagesToPrint.SequenceEqual(naturalOrder))
        {
            Console.WriteLine($"[CreateNormalDuplexJob] Creating subset PDF for pages: {string.Join(",", pagesToPrint)}");
            var subsetPath = Path.Combine(Path.GetTempPath(), $"subset_{Guid.NewGuid()}.pdf");

            _wordService.CreatePdfSubset(pdfPath, subsetPath, pagesToPrint);
            workingPdfPath = subsetPath;
            jobState.IntermediateFiles.Add(subsetPath); // BUG-8-2: track for cleanup

            pdfInfo = _wordService.GetPdfInfo(workingPdfPath);

            // REMAP Single-Sided Pages:
            //  - Trang gốc: pagesToPrint[] (ví dụ [3,4,5])
            //  - Subset: 1..N, tương ứng với pagesToPrint[i]
            //  ⇒ SingleSidedPages mới phải là index trong subset.
            if (effectiveSingleSidedPages.Length > 0)
            {
                var originalSingleSidedSet = new HashSet<int>(effectiveSingleSidedPages);
                var newSingleSidedList = new List<int>();

                for (int i = 0; i < pagesToPrint.Length; i++)
                {
                    int originalPageNum = pagesToPrint[i];
                    if (originalSingleSidedSet.Contains(originalPageNum))
                    {
                        newSingleSidedList.Add(i + 1); // map sang index 1-based trong subset
                    }
                }

                effectiveSingleSidedPages = newSingleSidedList.ToArray();
                Console.WriteLine($"[CreateNormalDuplexJob] Remapped single-sided pages (subset): [{string.Join(",", effectiveSingleSidedPages)}]");
            }

            // REMAP Rotations BEFORE resetting pagesToPrint:
            //  pagesToPrint still holds the original page numbers (e.g. [3,1,2,0,4]).
            //  After the reset below it becomes [1..N] and the mapping is lost.
            //  We must remap here — mirroring the auto-duplex path — so that
            //  rotationMap keys are translated from original page numbers to
            //  their 1-based positions in the newly created subset PDF.
            manualRotationMap = RemapRotations(manualRotationMap, pagesToPrint);
            Console.WriteLine($"[CreateNormalDuplexJob] Remapped rotation map inside subset branch (subset size={pdfInfo.PageCount}).");

            pagesToPrint = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
        }

        // Apply per-page rotations before mixed-orientation processing (U)
        if (manualRotationMap.Count > 0)
        {
            var rotatedPath = _wordService.ApplyPageRotations(workingPdfPath, manualRotationMap);
            jobState.IntermediateFiles.Add(rotatedPath); // BUG-8-2: track for cleanup
            workingPdfPath = rotatedPath;
            pdfInfo = _wordService.GetPdfInfo(workingPdfPath);
        }

        // BUG-7-7 fix: resolve deferred flip direction HERE, using the post-rotation pdfInfo.
        // If the user rotated the first page 90°, the heuristic now reads the correct orientation.
        if (!flipDirectionOverridden)
        {
            flipDirection = pdfInfo.IsLandscape ? FlipDirection.ShortEdge : FlipDirection.LongEdge;
            Console.WriteLine($"[CreateNormalDuplexJob] flipDirection resolved after rotations: {flipDirection} (isLandscape={pdfInfo.IsLandscape})");
        }

        // 2) XỬ LÝ MIXED ORIENTATION + SINGLE-SIDED BẰNG ProcessMixedOrientation MỚI
        // Hàm này sẽ:
        //  - nhóm theo orientation,
        //  - chèn blank cho trang single-sided và padding group,
        //  - tạo PDF mới (processed) + trả ra danh sách ManualDuplexPageInfo.
        Console.WriteLine(
            $"[CreateNormalDuplexJob] Processing special requirements: SingleSided=[{string.Join(",", effectiveSingleSidedPages)}] " +
            $"on working PDF: {workingPdfPath}");

        var processedPath = _wordService.ProcessMixedOrientation(
            workingPdfPath,
            effectiveSingleSidedPages,
            out var pageInfos
        );

        // 3) BUILD ManualDuplexPlan TỪ pageInfos
        // Canon LBP2900: face-down stack (trang in sau nằm trên cùng) → faceDownStack = true
        var plan = ManualDuplexPlan.Build(
            processedPath,
            pageInfos,
            faceDownStack: true
        );

        plan.DumpToConsole(); // Debug mapping sheet / phase1 / phase2

        // 4) CẬP NHẬT JOB STATE
        jobState.TempPdfPath    = plan.ProcessedPdfPath;
        jobState.OddPages       = plan.Phase1Pages;   // Phase 1: in mặt lẻ
        jobState.RemainingPages = plan.Phase2Pages;   // Phase 2: in mặt chẵn (đã reorder)
        jobState.ManualPlan     = plan;

        jobState.WaitingForFlip = true;
        jobState.Instruction = new FlipInstruction
        {
            Direction = flipDirection,
            // Dùng tổng số trang processed (đã padding) để thông báo số trang đã in ở phase 1
            Text = GenerateFlipInstructionText(flipDirection, plan.ProcessedPages.Count),
            VisualType = flipDirection == FlipDirection.LongEdge ? "vertical" : "horizontal"
        };

        Console.WriteLine(
            $"[CreateNormalDuplexJob] Final manual duplex plan: " +
            $"Phase1Pages={string.Join(",", jobState.OddPages)}, " +
            $"Phase2Pages={string.Join(",", jobState.RemainingPages)}, " +
            $"TotalProcessedPages={plan.ProcessedPages.Count}"
        );

        return jobState;
    }


    public PrintJobState CreateBookletJob(string pdfPath, string printerName, bool isDuplexPrinter, string? pageRange = null, int[]? singleSidedPages = null, int[]? pageOrder = null, List<PageRotation>? pageRotations = null)
    {
        // If page range is specified, create a temp PDF with only those pages first
        string sourcePdfPath = pdfPath;
        var pdfInfo = _wordService.GetPdfInfo(pdfPath);
        int[] selectedPages;

        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
            if (selectedPages.Length == 0)
                throw new InvalidOperationException("Page range resulted in no valid pages to print.");
        }
        else
        {
            selectedPages = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
        }

        // Apply custom page order if provided (I)
        selectedPages = ApplyPageOrder(selectedPages, pageOrder);

        // B11-BE-1 fix: track all temp files created locally so we can delete them
        // if CreateNormalDuplexJob throws before we can register them on jobState.
        var localTemps = new List<string>();

        if (!selectedPages.SequenceEqual(Enumerable.Range(1, pdfInfo.PageCount)))
        {
            var tempSelectedPdf = Path.Combine(Path.GetTempPath(), $"selected_{Guid.NewGuid()}.pdf");
            _wordService.CreatePdfSubset(pdfPath, tempSelectedPdf, selectedPages);
            sourcePdfPath = tempSelectedPdf;
            localTemps.Add(tempSelectedPdf);
        }

        // Apply per-page rotations before booklet creation (U)
        var bookletRotationMap = BuildRotationMap(pageRotations);
        if (!selectedPages.SequenceEqual(Enumerable.Range(1, pdfInfo.PageCount)))
            bookletRotationMap = RemapRotations(bookletRotationMap, selectedPages);
        if (bookletRotationMap.Count > 0)
        {
            var rotatedSource = _wordService.ApplyPageRotations(sourcePdfPath, bookletRotationMap);
            // rotatedSource replaces sourcePdfPath; if we already have a subset temp, that's
            // still in localTemps; now add the rotated temp too.
            if (rotatedSource != sourcePdfPath) localTemps.Add(rotatedSource);
            sourcePdfPath = rotatedSource;
        }

        var pageCount = _wordService.GetPageCount(sourcePdfPath);
        var paddedCount = RoundUpToMultipleOf4(pageCount);

        // Calculate booklet page order
        var orderedPages = CalculateBookletOrder(paddedCount);

        // Create booklet PDF with 2-up layout
        var bookletPdfPath = Path.Combine(Path.GetTempPath(), $"booklet_{Guid.NewGuid()}.pdf");
        CreateBookletPdf(sourcePdfPath, bookletPdfPath, orderedPages, paddedCount);
        localTemps.Add(bookletPdfPath);

        // Now treat it as normal duplex (no page range needed since booklet PDF is already filtered).
        // If this throws, clean up all locally-owned temp files so nothing is orphaned.
        PrintJobState bookletJobState;
        try
        {
            bookletJobState = CreateNormalDuplexJob(bookletPdfPath, printerName, isDuplexPrinter, pageRange: null, singleSidedPages: null);
        }
        catch
        {
            foreach (var t in localTemps)
                FileSessionService.DeleteFileSafe(t);
            throw;
        }

        // Success — hand ownership of all local temps to jobState for normal cleanup.
        foreach (var t in localTemps)
            bookletJobState.IntermediateFiles.Add(t);

        return bookletJobState;
    }

    public PrintJobState CreateSimplexJob(
        string pdfPath,
        string printerName,
        string? pageRange = null,
        int[]? pageOrder = null,
        List<PageRotation>? pageRotations = null)
    {
        Console.WriteLine("[CreateSimplexJob] Single-sided print.");
        var pdfInfo = _wordService.GetPdfInfo(pdfPath);
        string workingPdfPath = pdfPath;

        int[] selectedPages;
        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
            if (selectedPages.Length == 0)
                throw new InvalidOperationException($"Page range '{pageRange}' is invalid.");
        }
        else
        {
            selectedPages = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
        }

        // Apply custom page order if provided (I)
        selectedPages = ApplyPageOrder(selectedPages, pageOrder);

        if (!selectedPages.SequenceEqual(Enumerable.Range(1, pdfInfo.PageCount)))
        {
            var subsetPath = Path.Combine(Path.GetTempPath(), $"simplex_subset_{Guid.NewGuid()}.pdf");
            _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
            workingPdfPath = subsetPath;
            Console.WriteLine($"[CreateSimplexJob] Subset created: {subsetPath}");
        }

        // Apply per-page rotations (U)
        var simplexRotationMap = BuildRotationMap(pageRotations);
        if (!selectedPages.SequenceEqual(Enumerable.Range(1, pdfInfo.PageCount)))
            simplexRotationMap = RemapRotations(simplexRotationMap, selectedPages);
        if (simplexRotationMap.Count > 0)
        {
            var rotatedPath = _wordService.ApplyPageRotations(workingPdfPath, simplexRotationMap);
            workingPdfPath = rotatedPath;
        }

        var simplexJobState = new PrintJobState
        {
            TempPdfPath    = workingPdfPath,
            PrinterName    = printerName,
            IsManualDuplex = false,
            WaitingForFlip = false,
        };
        // BUG-8-2: register intermediate files for cleanup
        if (workingPdfPath != pdfPath) simplexJobState.IntermediateFiles.Add(workingPdfPath);
        return simplexJobState;
    }

    internal int RoundUpToMultipleOf4(int number)
    {
        return (int)Math.Ceiling(number / 4.0) * 4;
    }

    internal int[] CalculateBookletOrder(int pageCount)
    {
        var sheets = pageCount / 4;
        var order = new List<int>();

        int front1, front2, back1, back2;
        int left = 1;
        int right = pageCount;

        for (int i = 0; i < sheets; i++)
        {
            front1 = right--;
            front2 = left++;
            back1 = left++;
            back2 = right--;

            order.AddRange(new[] { front1, front2, back1, back2 });
        }

        return order.ToArray();
    }

    private void CreateBookletPdf(string sourcePdf, string targetPdf, int[] pageOrder, int totalPages)
    {
        using var sourceDoc = PdfReader.Open(sourcePdf, PdfDocumentOpenMode.Import);
        using var targetDoc = new PdfDocument();

        if (sourceDoc.PageCount == 0)
        {
            throw new InvalidOperationException("Source PDF has no pages.");
        }

        // Use the first page size as the base. For an A4 portrait source this will
        // produce an A4 landscape sheet with two scaled pages side-by-side.
        var firstPage = sourceDoc.Pages[0];

        // BUG-3B fix: account for PDF /Rotate metadata — a page stored as portrait with
        // Rotate=90 is visually landscape; swap w/h before computing booklet sheet dims.
        double sourceWidth = firstPage.Width.Point;
        double sourceHeight = firstPage.Height.Point;
        var firstRotate = firstPage.Rotate;
        if (firstRotate == 90 || firstRotate == 270) (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

        // Booklet sheet: two source pages side-by-side → landscape sheet.
        // Height of source page becomes width of booklet sheet (the folded dimension),
        // width of source page becomes height (the other dimension).
        double bookletPageWidth = sourceHeight;   // e.g. 842 for A4 landscape
        double bookletPageHeight = sourceWidth;   // e.g. 595 for A4 landscape

        using var form = XPdfForm.FromFile(sourcePdf);

        // Process pages in pairs (2 pages side by side on each sheet side)
        for (int i = 0; i < pageOrder.Length; i += 2)
        {
            var newPage = targetDoc.AddPage();
            newPage.Width = XUnit.FromPoint(bookletPageWidth);
            newPage.Height = XUnit.FromPoint(bookletPageHeight);
            newPage.Orientation = PdfSharp.PageOrientation.Landscape;

            using var gfx = XGraphics.FromPdfPage(newPage);

            // Left half (page 1)
            if (pageOrder[i] <= sourceDoc.PageCount)
            {
                DrawPageOnHalf(
                    gfx,
                    form,
                    sourceDoc,
                    pageOrder[i],
                    leftSide: true,
                    pageWidth: bookletPageWidth,
                    pageHeight: bookletPageHeight
                );
            }

            // Right half (page 2)
            if (i + 1 < pageOrder.Length && pageOrder[i + 1] <= sourceDoc.PageCount)
            {
                DrawPageOnHalf(
                    gfx,
                    form,
                    sourceDoc,
                    pageOrder[i + 1],
                    leftSide: false,
                    pageWidth: bookletPageWidth,
                    pageHeight: bookletPageHeight
                );
            }
        }

        targetDoc.Save(targetPdf);
    }

    private void DrawPageOnHalf(
        XGraphics gfx,
        XPdfForm form,
        PdfDocument sourceDoc,
        int pageNumber,
        bool leftSide,
        double pageWidth,
        double pageHeight)
    {
        // Reuse a single XPdfForm instance and just switch PageNumber for each draw
        form.PageNumber = pageNumber;

        // Half page width (e.g. A5 width when printing two A5 pages on one A4 sheet)
        double halfWidth = pageWidth / 2.0;

        // Calculate position
        double x = leftSide ? 0 : halfWidth;
        double y = 0;

        // BUG-3D fix: form.PointWidth/PointHeight reflects raw stored dimensions which
        // ignore PDF /Rotate metadata. Read the actual source page to get Rotate, then
        // swap dimensions before deciding whether the visual page is landscape.
        var srcPage = sourceDoc.Pages[pageNumber - 1];
        double fw = form.PointWidth;
        double fh = form.PointHeight;
        if (srcPage.Rotate == 90 || srcPage.Rotate == 270) (fw, fh) = (fh, fw);
        bool isLandscape = fw > fh;

        if (isLandscape)
        {
            Console.WriteLine($"[DrawPageOnHalf] Rotating landscape page {pageNumber} (-90 degrees) to fit booklet slot.");
            gfx.Save();

            // Translate to the bottom-left corner of the target slot
            // (Because after -90 rotation, X points UP and Y points RIGHT)
            gfx.TranslateTransform(x, y + pageHeight);
            gfx.RotateTransform(-90);

            // Draw the page. 
            // Note: In the rotated coordinate system:
            // - The "Width" argument corresponds to the vertical dimension on the physical page (pageHeight)
            // - The "Height" argument corresponds to the horizontal dimension on the physical page (halfWidth)
            gfx.DrawImage(form, 0, 0, pageHeight, halfWidth);

            gfx.Restore();
        }
        else
        {
            // Normal portrait page - draw as is
            gfx.DrawImage(form, x, y, halfWidth, pageHeight);
        }

        // Draw a light separator line
        var pen = new XPen(XColors.LightGray, 0.5);
        gfx.DrawLine(pen, halfWidth, 0, halfWidth, pageHeight);
    }

    internal int[] ParsePageRange(string pageRange, int totalPages)
    {
        var pages = new HashSet<int>();
        var parts = pageRange.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                // Range like "1-5"
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0].Trim(), out int start) &&
                    int.TryParse(rangeParts[1].Trim(), out int end))
                {
                    if (end < start)
                    {
                        (start, end) = (end, start);
                    }

                    for (int i = start; i <= end && i <= totalPages; i++)
                    {
                        if (i >= 1) pages.Add(i);
                    }
                }
            }
            else if (int.TryParse(trimmed, out int pageNum))
            {
                // Single page like "3"
                if (pageNum >= 1 && pageNum <= totalPages)
                {
                    pages.Add(pageNum);
                }
            }
        }

        return pages.OrderBy(p => p).ToArray();
    }

    /// <summary>
    /// Reorder selected pages according to a custom page order (I — drag-to-reorder).
    /// Pages in pageOrder that exist in selectedPages are placed first (in order),
    /// then any remaining selected pages not in pageOrder are appended at the end.
    /// </summary>
    internal static int[] ApplyPageOrder(int[] selectedPages, int[]? pageOrder)
    {
        if (pageOrder == null || pageOrder.Length == 0)
            return selectedPages;

        var selectedSet = new HashSet<int>(selectedPages);
        // Keep 0 as a blank page marker in addition to selected pages
        var reordered = pageOrder.Where(p => p == 0 || selectedSet.Contains(p)).ToList();
        // Add any selected pages not in pageOrder at the end (safety)
        foreach (var p in selectedPages.Where(p => !reordered.Contains(p)))
            reordered.Add(p);
        return reordered.ToArray();
    }

    /// <summary>
    /// Build a rotation map from the list of PageRotation objects.
    /// </summary>
    internal static Dictionary<int, RotationDirection> BuildRotationMap(List<PageRotation>? pageRotations)
    {
        if (pageRotations == null || pageRotations.Count == 0)
            return new Dictionary<int, RotationDirection>();
        // BUG-8-1 fix: frontend may send duplicate pageNumber entries (e.g. rapid UI clicks).
        // ToDictionary throws ArgumentException on duplicates — use last-wins via GroupBy instead.
        var map = new Dictionary<int, RotationDirection>();
        foreach (var r in pageRotations)
        {
            if (r.Rotation != RotationDirection.None)
                map[r.PageNumber] = r.Rotation; // last entry for a given page wins
        }
        return map;
    }

    /// <summary>
    /// Remap rotation keys from original page numbers to 1-based subset indices.
    /// selectedPages[i] is the original page number at subset index i+1.
    /// </summary>
    internal static Dictionary<int, RotationDirection> RemapRotations(
        Dictionary<int, RotationDirection> rotationMap, int[] selectedPages)
    {
        if (rotationMap.Count == 0) return rotationMap;
        var remapped = new Dictionary<int, RotationDirection>();
        for (int i = 0; i < selectedPages.Length; i++)
        {
            if (rotationMap.TryGetValue(selectedPages[i], out var rot))
                remapped[i + 1] = rot;
        }
        return remapped;
    }

    private string GenerateFlipInstructionText(FlipDirection direction, int pageCount)
    {
        // With the new algorithm, user doesn't need to rotate paper - just put it back straight
        return $"Đã in {(pageCount + 1) / 2} trang mặt lẻ. Vui lòng lấy chồng giấy ra và ĐẶT THẲNG LẠI vào khay giấy (MẶT ĐÃ IN HƯỚNG XUỐNG) để in mặt chẵn. KHÔNG cần xoay giấy.";
    }

    public void ExecutePrintJob(PrintJobState jobState, bool firstPhase = true)
    {
        if (jobState == null) throw new ArgumentNullException(nameof(jobState));

        // PDF files should use PrintPdf, not PrintDocument (Word COM)
        if (!jobState.IsManualDuplex)
        {
            // ==========================
            //  TỰ ĐỘNG 2 MẶT (AUTO DUPLEX)
            // ==========================
            Console.WriteLine("[ExecutePrintJob] Printing with automatic duplex");

            _wordService.PrintPdf(
                jobState.TempPdfPath,
                jobState.PrinterName,
                pageRange: null, // In toàn bộ file (page range – nếu có – đã được xử lý trước đó)
                duplexSide: jobState.DuplexSide   // NEW: null for booklet/default, "ShortEdge" for all-landscape
            );
        }
        else
        {
            // ==========================
            //  MANUAL DUPLEX (IN 2 LẦN)
            // ==========================

            // Dump plan 1 lần ở phase 1 cho dễ debug
            if (firstPhase && jobState.ManualPlan != null)
            {
                jobState.ManualPlan.DumpToConsole();
            }

            if (firstPhase)
            {
                // ----- PHASE 1: IN MẶT LẺ (FRONT) -----
                // jobState.OddPages được gán từ ManualDuplexPlan.Phase1Pages
                if (jobState.OddPages == null || jobState.OddPages.Length == 0)
                {
                    throw new InvalidOperationException("No odd pages (Phase 1) to print in manual duplex job.");
                }

                var oddPagesStr = string.Join(",", jobState.OddPages);
                Console.WriteLine($"[ExecutePrintJob] Manual duplex phase 1: printing odd pages ({oddPagesStr})");

                _wordService.PrintPdf(
                    jobState.TempPdfPath,
                    jobState.PrinterName,
                    pageRange: oddPagesStr
                );
            }
            else
            {
                // ----- PHASE 2: IN MẶT CHẴN (BACK) -----
                // jobState.RemainingPages được gán từ ManualDuplexPlan.Phase2Pages
                if (jobState.RemainingPages == null || jobState.RemainingPages.Length == 0)
                {
                    // This should never happen if ProcessMixedOrientation padded correctly.
                    // Throw instead of silently returning so the caller surfaces a real error
                    // to the user rather than appearing to succeed with nothing printed. (C6 fix)
                    throw new InvalidOperationException(
                        "[ExecutePrintJob] Phase 2 has no pages to print. " +
                        "This indicates ProcessMixedOrientation produced an odd page count — " +
                        "please report this as a bug with the document that triggered it.");
                }

                Console.WriteLine("[ExecutePrintJob] Manual duplex phase 2: creating smart rotated PDF for back pages");
                Console.WriteLine($"[ExecutePrintJob] Second phase pages: {string.Join(",", jobState.RemainingPages)}");

                // CreateSmartDuplexPdf BÂY GIỜ chỉ xoay trang theo orientation,
                // KHÔNG tự OrderByDescending nữa – thứ tự đã được ManualDuplexPlan xử lý đúng.
                var rotatedPdfPath = _wordService.CreateSmartDuplexPdf(
                    jobState.TempPdfPath,
                    jobState.RemainingPages
                );

                Console.WriteLine($"[ExecutePrintJob] Printing rotated back pages from: {rotatedPdfPath}");

                _wordService.PrintPdf(
                    rotatedPdfPath,
                    jobState.PrinterName,
                    pageRange: null // In toàn bộ file rotated theo thứ tự đã build
                );

                // Dọn rác file rotated
                try
                {
                    // Đợi spooler 1 chút cho chắc rồi mới xóa
                    System.Threading.Thread.Sleep(5000);
                    File.Delete(rotatedPdfPath);
                    Console.WriteLine("[ExecutePrintJob] Cleaned up rotated PDF");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExecutePrintJob] WARNING: Failed to delete rotated PDF. {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

}
