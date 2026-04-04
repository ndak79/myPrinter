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
    private readonly WordInteropService _wordService;

    public PrintAlgorithmService(WordInteropService wordService)
    {
        _wordService = wordService;
    }

    public PrintJobState CreateNormalDuplexJob(
    string pdfPath,
    string printerName,
    bool isDuplexPrinter,
    string? pageRange = null,
    int[]? singleSidedPages = null)
    {
        // Đọc metadata PDF gốc
        var pdfInfo = _wordService.GetPdfInfo(pdfPath);
        var flipDirection = pdfInfo.IsLandscape ? FlipDirection.ShortEdge : FlipDirection.LongEdge;

        var jobState = new PrintJobState
        {
            TempPdfPath = pdfPath,
            PrinterName = printerName,
            IsManualDuplex = !isDuplexPrinter
        };

        if (isDuplexPrinter)
        {
            // ==========================
            //  AUTO DUPLEX (MÁY IN 2 MẶT)
            // ==========================
            Console.WriteLine("[CreateNormalDuplexJob] Auto duplex printer detected.");

            if (!string.IsNullOrWhiteSpace(pageRange))
            {
                var selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
                if (selectedPages.Length == 0)
                    throw new InvalidOperationException($"Page range '{pageRange}' khong hop le hoac khong co trang nao.");

                if (selectedPages.Length < pdfInfo.PageCount)
                {
                    Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex with pageRange: {pageRange}. Creating subset PDF.");
                    var subsetPath = Path.Combine(Path.GetTempPath(), $"auto_duplex_subset_{Guid.NewGuid()}.pdf");
                    _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
                    jobState.TempPdfPath = subsetPath;
                    Console.WriteLine($"[CreateNormalDuplexJob] Auto duplex subset created: {subsetPath}");
                }
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

        // 1) XỬ LÝ PAGE RANGE + TẠO SUBSET NẾU CẦN
        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            // Parse range → danh sách trang gốc (ví dụ 3,4,5)
            pagesToPrint = ParsePageRange(pageRange, pdfInfo.PageCount);

            if (pagesToPrint.Length == 0)
            {
                throw new InvalidOperationException($"[CreateNormalDuplexJob] Page range '{pageRange}' không hợp lệ hoặc không có trang nào.");
            }

            // Nếu range không cover toàn bộ tài liệu → tạo subset PDF
            if (pagesToPrint.Length < pdfInfo.PageCount)
            {
                Console.WriteLine($"[CreateNormalDuplexJob] Page range active: {string.Join(",", pagesToPrint)}. Creating subset PDF.");
                var subsetPath = Path.Combine(Path.GetTempPath(), $"subset_{Guid.NewGuid()}.pdf");

                // Tạo subset: chỉ chứa các trang được chọn (theo thứ tự pagesToPrint)
                _wordService.CreatePdfSubset(pdfPath, subsetPath, pagesToPrint);
                workingPdfPath = subsetPath;

                // Cập nhật lại info theo subset
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

                // Sau khi đã tạo subset, pagesToPrint tương ứng 1..N trong subset
                pagesToPrint = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
            }
            else
            {
                // Range cover toàn bộ → cứ coi như 1..N cho workingPdfPath gốc
                pagesToPrint = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
            }
        }
        else
        {
            // Không có pageRange → in toàn bộ
            pagesToPrint = Enumerable.Range(1, pdfInfo.PageCount).ToArray();
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


    public PrintJobState CreateBookletJob(string pdfPath, string printerName, bool isDuplexPrinter, string? pageRange = null, int[]? singleSidedPages = null)
    {
        // If page range is specified, create a temp PDF with only those pages first
        string sourcePdfPath = pdfPath;
        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            var pdfInfo = _wordService.GetPdfInfo(pdfPath);
            var selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
            
            // Create temp PDF with selected pages only
            var tempSelectedPdf = Path.Combine(Path.GetTempPath(), $"selected_{Guid.NewGuid()}.pdf");
            _wordService.CreatePdfSubset(pdfPath, tempSelectedPdf, selectedPages);
            sourcePdfPath = tempSelectedPdf;
        }

        var pageCount = _wordService.GetPageCount(sourcePdfPath);
        var paddedCount = RoundUpToMultipleOf4(pageCount);

        // Calculate booklet page order
        var orderedPages = CalculateBookletOrder(paddedCount);

        // Create booklet PDF with 2-up layout
        var bookletPdfPath = Path.Combine(Path.GetTempPath(), $"booklet_{Guid.NewGuid()}.pdf");
        CreateBookletPdf(sourcePdfPath, bookletPdfPath, orderedPages, paddedCount);

        // Now treat it as normal duplex (no page range needed since booklet PDF is already filtered)
        return CreateNormalDuplexJob(bookletPdfPath, printerName, isDuplexPrinter, pageRange: null, singleSidedPages: singleSidedPages);
    }

    public PrintJobState CreateSimplexJob(
        string pdfPath,
        string printerName,
        string? pageRange = null)
    {
        Console.WriteLine("[CreateSimplexJob] Single-sided print.");

        var pdfInfo = _wordService.GetPdfInfo(pdfPath);
        string workingPdfPath = pdfPath;

        if (!string.IsNullOrWhiteSpace(pageRange))
        {
            var selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
            if (selectedPages.Length == 0)
                throw new InvalidOperationException($"Page range '{pageRange}' is invalid.");

            if (selectedPages.Length < pdfInfo.PageCount)
            {
                var subsetPath = Path.Combine(Path.GetTempPath(), $"simplex_subset_{Guid.NewGuid()}.pdf");
                _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
                workingPdfPath = subsetPath;
                Console.WriteLine($"[CreateSimplexJob] Subset created: {subsetPath}");
            }
        }

        return new PrintJobState
        {
            TempPdfPath    = workingPdfPath,
            PrinterName    = printerName,
            IsManualDuplex = false,
            WaitingForFlip = false,
        };
    }

    private int RoundUpToMultipleOf4(int number)
    {
        return (int)Math.Ceiling(number / 4.0) * 4;
    }

    private int[] CalculateBookletOrder(int pageCount)
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
        double sourceWidth = firstPage.Width;
        double sourceHeight = firstPage.Height;

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

        // Check for landscape orientation (Width > Height)
        // If landscape, we rotate -90 degrees (CCW) so it fits the portrait slot perfectly.
        bool isLandscape = form.PointWidth > form.PointHeight;

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

    private int[] ParsePageRange(string pageRange, int totalPages)
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
                pageRange: null // In toàn bộ file (page range – nếu có – đã được xử lý trước đó)
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
                    Console.WriteLine("[ExecutePrintJob] WARNING: No odd pages to print in first phase.");
                    return;
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
                    Console.WriteLine("[ExecutePrintJob] WARNING: No even pages to print in second phase.");
                    return;
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
