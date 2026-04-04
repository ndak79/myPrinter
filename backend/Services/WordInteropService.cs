using Word = Microsoft.Office.Interop.Word;
using System.Runtime.InteropServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using XPdfForm = PdfSharp.Drawing.XPdfForm;
using XGraphics = PdfSharp.Drawing.XGraphics;
using PrinterApp.Models;

namespace PrinterApp.Services;

public record PdfInfo(int PageCount, bool IsLandscape);

public class WordInteropService
{
    /// <summary>
    /// Tạo 1 trang "blank" nhưng có content siêu nhỏ để tránh bị viewer/driver skip.
    /// isLandscape = true → trang ngang, false → trang dọc.
    /// </summary>
    private static PdfPage CreateNonSkippableBlankPage(PdfDocument targetDoc,
                                                   PdfPage templatePage,
                                                   bool isLandscape)
    {
        var page = targetDoc.AddPage();

        // Lấy kích thước từ template, rồi chỉnh orientation nếu cần
        double w = templatePage.Width;
        double h = templatePage.Height;

        bool templateIsLandscape = w > h;

        if (isLandscape && !templateIsLandscape)
        {
            // Cần landscape nhưng template đang portrait → đổi chiều
            (w, h) = (h, w);
        }
        else if (!isLandscape && templateIsLandscape)
        {
            // Cần portrait nhưng template đang landscape
            (w, h) = (h, w);
        }

        page.Width  = w;
        page.Height = h;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Vẽ 1 hình chữ nhật nhỏ màu xám ở GẦN GIỮA TRANG
            // → chắc chắn nằm trong vùng in, có pixel khác trắng.
            double rectSize = 3; // kích thước rất nhỏ, khó thấy
            double centerX  = page.Width  / 2.0;
            double centerY  = page.Height / 2.0;

            gfx.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(255, 250, 250, 250)),
                centerX - rectSize / 2.0,
                centerY - rectSize / 2.0,
                rectSize,
                rectSize
            );
        }

        return page;
    }



    // Word COM is ONLY for DOC/DOCX operations
    public void ConvertToPdf(string inputPath, string outputPath)
    {
        Word.Application? wordApp = null;
        Word.Document? doc = null;

        try
        {
            wordApp = new Word.Application
            {
                Visible = false
            };

            object inputFile = inputPath;
            object outputFile = outputPath;
            object missing = Type.Missing;

            doc = wordApp.Documents.Open(ref inputFile, ReadOnly: true);
            doc.ExportAsFixedFormat(
                outputPath,
                Word.WdExportFormat.wdExportFormatPDF,
                OpenAfterExport: false,
                OptimizeFor: Word.WdExportOptimizeFor.wdExportOptimizeForPrint,
                Range: Word.WdExportRange.wdExportAllDocument
            );
        }
        finally
        {
            CleanupWordObjects(doc, wordApp);
        }
    }

    public void PrintDocument(string filePath, string printerName)
    {
        // For PDF, use PrintPdf instead
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".pdf")
        {
            throw new InvalidOperationException("Use PrintPdf method for PDF files, not PrintDocument");
        }

        Word.Application? wordApp = null;
        Word.Document? doc = null;

        try
        {
            wordApp = new Word.Application { Visible = false };

            object file = filePath;
            object missing = Type.Missing;

            doc = wordApp.Documents.Open(ref file, ReadOnly: true);
            doc.Activate();

            wordApp.ActivePrinter = printerName;

            doc.PrintOut(
                Background: false,
                Range: Word.WdPrintOutRange.wdPrintAllDocument
            );
        }
        finally
        {
            CleanupWordObjects(doc, wordApp);
        }
    }

    // PdfSharp for PDF operations - NOT Word COM!

    /// <summary>
    /// Read basic PDF info (page count + orientation) in a single pass.
    /// Prefer this over calling GetPageCount/IsLandscape separately.
    /// </summary>
    public PdfInfo GetPdfInfo(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"[GetPdfInfo ERROR] File does not exist: {pdfPath}");
            throw new FileNotFoundException($"PDF file not found: {pdfPath}");
        }

        var fileInfo = new FileInfo(pdfPath);
        Console.WriteLine($"[GetPdfInfo] Opening PDF with PdfSharp: {pdfPath}");
        Console.WriteLine($"[GetPdfInfo] File size: {fileInfo.Length} bytes");

        if (fileInfo.Length == 0)
        {
            Console.WriteLine("[GetPdfInfo ERROR] PDF file is empty");
            throw new InvalidOperationException("PDF file is empty");
        }

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
            var pageCount = document.PageCount;

            if (pageCount == 0)
            {
                Console.WriteLine("[GetPdfInfo ERROR] PDF has no pages");
                throw new InvalidOperationException("PDF has no pages");
            }

            var firstPage = document.Pages[0];
            var isLandscape = firstPage.Width > firstPage.Height;

            Console.WriteLine($"[GetPdfInfo] PageCount={pageCount}, Orientation={(isLandscape ? "Landscape" : "Portrait")} (W:{firstPage.Width}, H:{firstPage.Height})");

            return new PdfInfo(pageCount, isLandscape);
        }
        catch (PdfReaderException ex)
        {
            Console.WriteLine($"[GetPdfInfo ERROR] PDF corrupted or invalid: {ex.Message}");
            throw new InvalidOperationException($"PDF file is corrupted or not a valid PDF: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetPdfInfo ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[GetPdfInfo ERROR] Stack: {ex.StackTrace}");
            throw new InvalidOperationException($"Failed to read PDF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Backwards-compatible helper when only the page count is required.
    /// Internally uses GetPdfInfo to avoid duplicating PDF parsing logic.
    /// </summary>
    public int GetPageCount(string pdfPath)
    {
        return GetPdfInfo(pdfPath).PageCount;
    }

    /// <summary>
    /// Backwards-compatible helper when only the orientation is required.
    /// Internally uses GetPdfInfo to avoid duplicating PDF parsing logic.
    /// </summary>
    public bool IsLandscape(string pdfPath)
    {
        return GetPdfInfo(pdfPath).IsLandscape;
    }

    /// <summary>
    /// Get orientation of each page in the PDF.
    /// Returns array where true = landscape, false = portrait.
    /// </summary>
    public bool[] GetPageOrientations(string pdfPath)
    {
        Console.WriteLine($"[GetPageOrientations] Analyzing page orientations in: {pdfPath}");
        
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
        var orientations = new bool[document.PageCount];
        
        for (int i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            var rotate = page.Rotate;
            var width = page.Width.Point;
            var height = page.Height.Point;
            
            // Swap dimensions if rotated 90 or 270 degrees
            if (rotate == 90 || rotate == 270)
            {
                (width, height) = (height, width);
            }
            
            bool isLandscape = width > height;
            orientations[i] = isLandscape;
            
            Console.WriteLine($"[GetPageOrientations] Page {i + 1}: Rotate={rotate}, W={page.Width.Point:F1}, H={page.Height.Point:F1} (Eff W={width:F1}, H={height:F1}) -> {(isLandscape ? "Landscape" : "Portrait")}");
        }
        
        Console.WriteLine($"[GetPageOrientations] Found orientations: {string.Join(",", orientations.Select((o, i) => $"{i + 1}:{(o ? "L" : "P")}"))}");
        return orientations;
    }

    /// <summary>
    /// Creates a new PDF with blank pages inserted after specified single-sided pages.
    /// Also pads to even page count if needed.
    /// </summary>
    public string CreatePdfWithBlankPages(string sourcePath, int[] singleSidedPages, bool[] pageOrientations)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"blanks_{Guid.NewGuid()}.pdf");
        
        try
        {
            using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            var targetDoc = new PdfDocument();
            
            // Expand single-sided to include "trapped" pages (pages between two single-sided pages)
            var expandedSingleSided = ExpandSingleSidedPages(singleSidedPages, sourceDoc.PageCount);
            
            Console.WriteLine($"[CreatePdfWithBlankPages] Original single-sided: [{string.Join(",", singleSidedPages)}]");
            Console.WriteLine($"[CreatePdfWithBlankPages] Expanded (with trapped): [{string.Join(",", expandedSingleSided)}]");
            
            for (int i = 0; i < sourceDoc.PageCount; i++)
            {
                int pageNum = i + 1;
                var sourcePage = sourceDoc.Pages[i];

                // Check for orientation change
                bool currentOrientation = pageOrientations[i];
                if (i > 0)
                {
                    bool prevOrientation = pageOrientations[i - 1];
                    if (currentOrientation != prevOrientation)
                    {
                        // Orientation changed. If we are on an even page count (meaning the last page was Front/Odd),
                        // we need to complete the sheet with a blank so the new orientation starts on a fresh Front.
                        if (targetDoc.PageCount % 2 != 0)
                        {
                            var lastPage = targetDoc.Pages[targetDoc.PageCount - 1];
                            var blank = targetDoc.AddPage();
                            blank.Width = lastPage.Width;
                            blank.Height = lastPage.Height;
                            Console.WriteLine($"[CreatePdfWithBlankPages] Orientation change (P{i}->P{i+1}). Inserted blank to start new orientation on fresh sheet.");
                        }
                    }
                }

                // Add the original page
                targetDoc.AddPage(sourcePage);
                
                // If this is a single-sided page, add a blank page after it
                if (expandedSingleSided.Contains(pageNum))
                {
                    var blankPage = targetDoc.AddPage();
                    blankPage.Width = sourcePage.Width;
                    blankPage.Height = sourcePage.Height;
                    Console.WriteLine($"[CreatePdfWithBlankPages] Added blank after page {pageNum}");
                }
            }
            
            // Pad to even page count
            if (targetDoc.PageCount % 2 != 0)
            {
                var lastPage = targetDoc.Pages[targetDoc.PageCount - 1];
                var blankPage = targetDoc.AddPage();
                blankPage.Width = lastPage.Width;
                blankPage.Height = lastPage.Height;
                Console.WriteLine($"[CreatePdfWithBlankPages] Padded to even: added trailing blank");
            }
            
            int finalPageCount = targetDoc.PageCount;
            targetDoc.Save(outputPath);
            Console.WriteLine($"[CreatePdfWithBlankPages] Created PDF with {finalPageCount} pages at {outputPath}");
            
            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreatePdfWithBlankPages ERROR] {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Expands single-sided pages to include "trapped" pages.
    /// A trapped page is any page between two single-sided pages.
    /// </summary>
    private HashSet<int> ExpandSingleSidedPages(int[] singleSidedPages, int totalPages)
    {
        if (singleSidedPages == null || singleSidedPages.Length == 0)
            return new HashSet<int>();
        
        var sorted = singleSidedPages.OrderBy(p => p).ToArray();
        var result = new HashSet<int>(sorted);
        
        // Find and add trapped pages
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            int start = sorted[i];
            int end = sorted[i + 1];
            
            // Add all pages between start and end (exclusive of start, inclusive of end is already in set)
            for (int p = start + 1; p < end; p++)
            {
                result.Add(p);
            }
        }
        
        return result;
    }

    public void PrintPdf(string pdfPath, string printerName, string? pageRange = null)
    {
        Console.WriteLine($"[PrintPdf] Printing PDF: {pdfPath}");
        Console.WriteLine($"[PrintPdf] Printer: {printerName}");
        Console.WriteLine($"[PrintPdf] Page range: {pageRange ?? "all"}");

        string fileToPrint = pdfPath;
        bool isTempFile = false;

        try
        {
            // If pageRange is specified, create a temp PDF with ONLY those pages
            if (!string.IsNullOrEmpty(pageRange))
            {
                Console.WriteLine($"[PrintPdf] Splitting PDF for page range: {pageRange}");
                fileToPrint = CreateTempPdfWithPages(pdfPath, pageRange);
                isTempFile = true;
                Console.WriteLine($"[PrintPdf] Created temp PDF for printing: {fileToPrint}");
            }

            // Try using the "print" shell verb first (opens default PDF viewer's print dialog)
            bool printSuccess = TryShellPrint(fileToPrint, printerName);
            
            if (!printSuccess)
            {
                // Fallback: Try using PowerShell's Out-Printer
                Console.WriteLine($"[PrintPdf] Shell print failed, trying PowerShell fallback...");
                TryPowerShellPrint(fileToPrint, printerName);
            }

            Console.WriteLine($"[PrintPdf] Print job sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PrintPdf ERROR] {ex.Message}");
        }
        finally
        {
            // Wait before deleting temp file to allow print spooler to read it
            if (isTempFile)
            {
                System.Threading.Thread.Sleep(5000);
                try
                {
                    File.Delete(fileToPrint);
                    Console.WriteLine($"[PrintPdf] Cleaned up temp file: {fileToPrint}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PrintPdf WARNING] Failed to delete temp file: {ex.Message}");
                }
            }
        }
    }

    private bool TryShellPrint(string filePath, string printerName)
    {
        try
        {
            // Use "print" verb which shows the default app's print dialog
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Console.WriteLine($"[TryShellPrint] Starting with 'print' verb...");
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine($"[TryShellPrint] Failed to start process");
                return false;
            }

            Console.WriteLine($"[TryShellPrint] Process started. ID: {process.Id}");
            
            // Wait for the print process to complete or timeout
            bool completed = process.WaitForExit(60000);
            if (!completed)
            {
                Console.WriteLine($"[TryShellPrint] Process did not complete within timeout");
                try { process.Kill(); } catch { }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryShellPrint ERROR] {ex.Message}");
            return false;
        }
    }

    private void TryPowerShellPrint(string filePath, string printerName)
    {
        try
        {
            // Use PowerShell to print
            var psCommand = $"Start-Process -FilePath \"{filePath}\" -Verb Print -Wait";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Console.WriteLine($"[TryPowerShellPrint] Executing PowerShell print command...");
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(60000);
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrEmpty(output))
                    Console.WriteLine($"[TryPowerShellPrint] Output: {output}");
                if (!string.IsNullOrEmpty(error))
                    Console.WriteLine($"[TryPowerShellPrint] Error: {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryPowerShellPrint ERROR] {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new PDF containing only the specified pages from the source PDF.
    /// </summary>
    public void CreatePdfSubset(string sourcePath, string targetPath, int[] pageNumbers)
    {
        try
        {
            var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            var targetDoc = new PdfDocument();

            foreach (var pageNum in pageNumbers.OrderBy(p => p))
            {
                if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
                {
                    Console.WriteLine($"[CreatePdfSubset] Adding page {pageNum} (Index {pageNum - 1}) to subset");
                    targetDoc.AddPage(sourceDoc.Pages[pageNum - 1]);
                }
            }

            int pageCount = targetDoc.PageCount;
            targetDoc.Save(targetPath);
            Console.WriteLine($"[CreatePdfSubset] Created subset PDF with {pageCount} pages at {targetPath}");
            
            // Dispose documents after save
            targetDoc.Dispose();
            sourceDoc.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreatePdfSubset ERROR] Failed to create PDF subset: {ex.Message}");
            Console.WriteLine($"[CreatePdfSubset ERROR] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Creates a new PDF with pages in REVERSE order and rotated 180 degrees.
    /// Uses XGraphics with transform for reliable rotation of actual content.
    /// Used for no-rotation manual duplex printing (second phase).
    /// </summary>
    public string CreateRotatedPdfSubset(string sourcePath, int[] pageNumbers)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"rotated_{Guid.NewGuid()}.pdf");
        
        try
        {
            var targetDoc = new PdfDocument();

            // Process pages in REVERSE order
            foreach (var pageNum in pageNumbers.OrderByDescending(p => p))
            {
                // Open source fresh for each page to avoid issues
                using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                
                if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
                {
                    var sourcePage = sourceDoc.Pages[pageNum - 1];
                    
                    // Use XPdfForm to import the page as a drawable form
                    using var form = XPdfForm.FromFile(sourcePath);
                    form.PageNumber = pageNum;
                    
                    // Get the form's actual dimensions (this is the source page size)
                    double formWidth = form.PointWidth;
                    double formHeight = form.PointHeight;
                    
                    // Create new page with EXACT same dimensions as the form
                    var newPage = targetDoc.AddPage();
                    newPage.Width = XUnit.FromPoint(formWidth);
                    newPage.Height = XUnit.FromPoint(formHeight);
                    
                    // Draw the page rotated 180 degrees using XGraphics
                    using var gfx = XGraphics.FromPdfPage(newPage);
                    
                    // Save graphics state
                    gfx.Save();
                    
                    // Translate to bottom-right corner, then rotate 180 degrees
                    // This effectively flips the content upside down
                    gfx.TranslateTransform(formWidth, formHeight);
                    gfx.RotateTransform(180);
                    
                    // Draw the form at origin with its EXACT original size (no scaling)
                    gfx.DrawImage(form, 0, 0);
                    
                    gfx.Restore();
                }
            }

            int pageCount = targetDoc.PageCount;
            targetDoc.Save(outputPath);
            targetDoc.Dispose();
            
            Console.WriteLine($"[CreateRotatedPdfSubset] Created rotated PDF with {pageCount} pages (reverse order, 180° XGraphics rotation, 1:1 scale) at {outputPath}");
            
            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateRotatedPdfSubset ERROR] Failed to create rotated PDF: {ex.Message}");
            Console.WriteLine($"[CreateRotatedPdfSubset ERROR] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private string CreateTempPdfWithPages(string sourcePath, string pageRange)
    {
        try 
        {
            // Parse page range (supports formats like "1,3,5-7")
            var pagesToInclude = new SortedSet<int>();

            foreach (var part in pageRange.Split(','))
            {
                var token = part.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                // Range "a-b"
                var dashIndex = token.IndexOf('-');
                if (dashIndex > 0)
                {
                    var startToken = token[..dashIndex].Trim();
                    var endToken = token[(dashIndex + 1)..].Trim();

                    if (int.TryParse(startToken, out int startPage) &&
                        int.TryParse(endToken, out int endPage))
                    {
                        if (endPage < startPage)
                        {
                            (startPage, endPage) = (endPage, startPage);
                        }

                        for (int p = startPage; p <= endPage; p++)
                        {
                            pagesToInclude.Add(p);
                        }

                        continue;
                    }
                }

                // Single page "n"
                if (int.TryParse(token, out int pageNum))
                {
                    pagesToInclude.Add(pageNum);
                }
            }

            if (pagesToInclude.Count == 0)
            {
                Console.WriteLine("[CreateTempPdfWithPages] No valid pages found in range, printing original.");
                return sourcePath;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"print_job_{Guid.NewGuid()}.pdf");

            using (var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
            using (var targetDoc = new PdfDocument())
            {
                foreach (var pageNum in pagesToInclude)
                {
                    if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
                    {
                        // Pages are 1-indexed in our logic, PdfSharp uses 0-based indexes
                        targetDoc.AddPage(sourceDoc.Pages[pageNum - 1]);
                    }
                    else
                    {
                        Console.WriteLine($"[CreateTempPdfWithPages WARNING] Page {pageNum} out of range (1-{sourceDoc.PageCount})");
                    }
                }

                targetDoc.Save(tempPath);
            }

            return tempPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateTempPdfWithPages ERROR] Failed to build temp PDF: {ex.Message}");
            return sourcePath;
        }
    }

    private void CleanupWordObjects(Word.Document? doc, Word.Application? app)
    {
        try
        {
            if (doc != null)
            {
                doc.Close(SaveChanges: false);
                Marshal.ReleaseComObject(doc);
            }

            if (app != null)
            {
                app.Quit();
                Marshal.ReleaseComObject(app);
            }

            // Let the GC run naturally; forcing full collections on every print
            // can cause noticeable pauses when printing many documents.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error cleaning up Word objects: {ex.Message}");
        }
    }

        /// <summary>
    /// Xử lý mixed-orientation + single-sided:
    /// - Nhóm các trang liên tiếp cùng orientation.
    /// - Với trang single-sided: chèn thêm 1 trang blank cùng orientation ngay sau nó.
    /// - Sau mỗi group: nếu tổng số trang trong group là lẻ → chèn 1 trang blank để thành chẵn.
    /// Kết quả:
    /// - File PDF mới với số trang chẵn, các group orientation không bị "xé tờ".
    /// - Danh sách ManualDuplexPageInfo để build ManualDuplexPlan.
    /// </summary>
    public string ProcessMixedOrientation(string sourcePath,
                                          int[]? singleSidedPages,
                                          out List<ManualDuplexPageInfo> pageInfos)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"processed_mixed_{Guid.NewGuid()}.pdf");
        var singleSidedSet = new HashSet<int>(singleSidedPages ?? Array.Empty<int>());

        pageInfos = new List<ManualDuplexPageInfo>();

        try
        {
            using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            using var targetDoc = new PdfDocument();

            int pageCount = sourceDoc.PageCount;

            // 1) Tính orientation cho từng trang gốc
            //    true = Landscape, false = Portrait
            var isLandscapeByPage = new bool[pageCount + 1]; // 1-based

            for (int i = 0; i < pageCount; i++)
            {
                var p = sourceDoc.Pages[i];
                double w = p.Width;
                double h = p.Height;

                // Nếu Rotate 90/270 thì width/height đổi vai trò
                int rotate = p.Rotate;
                if (rotate == 90 || rotate == 270)
                {
                    (w, h) = (h, w);
                }

                isLandscapeByPage[i + 1] = w > h;
            }

            // 2) Duyệt theo group consecutive cùng orientation
            int current = 1;
            while (current <= pageCount)
            {
                bool groupOrientation = isLandscapeByPage[current];
                int groupStartLogicalIndex = pageInfos.Count; // đánh dấu để tính size group sau

                var groupPageNumbers = new List<int>();
                int j = current;
                while (j <= pageCount && isLandscapeByPage[j] == groupOrientation)
                {
                    groupPageNumbers.Add(j);
                    j++;
                }
                current = j; // nhảy sang group tiếp theo

                Console.WriteLine($"[ProcessMixedOrientation] New group: orient={(groupOrientation ? "L" : "P")}, pages=[{string.Join(",", groupPageNumbers)}]");

                // 2a) Xử lý từng trang trong group
                foreach (var pageNum in groupPageNumbers)
                {
                    bool isSingleSided = singleSidedSet.Contains(pageNum);

                    // Trang gốc
                    pageInfos.Add(new ManualDuplexPageInfo
                    {
                        // ProcessedIndex sẽ set sau, khi đã có full list
                        OriginalPageNumber = pageNum,
                        IsBlank = false,
                        IsLandscape = groupOrientation
                    });

                    Console.WriteLine($"[ProcessMixedOrientation] Added original page {pageNum} (singleSided={isSingleSided})");

                    if (isSingleSided)
                    {
                        // Thêm 1 blank ngay sau để đảm bảo trang này có mặt sau trống
                        pageInfos.Add(new ManualDuplexPageInfo
                        {
                            OriginalPageNumber = -1,
                            IsBlank = true,
                            IsLandscape = groupOrientation
                        });

                        Console.WriteLine($"[ProcessMixedOrientation] Added BLANK for single-sided page {pageNum}");
                    }
                }

                // 2b) Padding group thành chẵn (nếu cần)
                int groupEndLogicalIndex = pageInfos.Count;
                int groupLogicalCount = groupEndLogicalIndex - groupStartLogicalIndex;

                if (groupLogicalCount % 2 != 0)
                {
                    // Thêm 1 blank cùng orientation để group kết thúc đúng cuối tờ
                    pageInfos.Add(new ManualDuplexPageInfo
                    {
                        OriginalPageNumber = -1,
                        IsBlank = true,
                        IsLandscape = groupOrientation
                    });

                    Console.WriteLine($"[ProcessMixedOrientation] Padding group ({(groupOrientation ? "L" : "P")}) to even count. +1 BLANK");
                }
            }

            // 3) Dựa trên pageInfos → tạo file PDF mới
            for (int i = 0; i < pageInfos.Count; i++)
            {
                var info = pageInfos[i];
                info.ProcessedIndex = i + 1;

                if (info.IsBlank)
                {
                    var templatePage = sourceDoc.Pages[0];
                    CreateNonSkippableBlankPage(targetDoc, templatePage, info.IsLandscape);

                    Console.WriteLine($"[ProcessMixedOrientation] Wrote BLANK page at processed index {info.ProcessedIndex} (ori={ (info.IsLandscape ? "L" : "P") })");
                }
                else
                {
                    var sourcePage = sourceDoc.Pages[info.OriginalPageNumber - 1];
                    targetDoc.AddPage(sourcePage);

                    Console.WriteLine($"[ProcessMixedOrientation] Wrote ORIGINAL page {info.OriginalPageNumber} -> processed index {info.ProcessedIndex}");
                }
            }

            int finalPageCount = targetDoc.PageCount;
            targetDoc.Save(outputPath);
            Console.WriteLine($"[ProcessMixedOrientation] Created processed PDF with {finalPageCount} pages at {outputPath}");

            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessMixedOrientation ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    /// <summary>
    /// Overload cũ: giữ cho các chỗ gọi hiện tại không bị vỡ.
    /// Nếu không cần plan chi tiết thì dùng hàm này.
    /// </summary>
    public string ProcessMixedOrientation(string sourcePath, int[] singleSidedPages)
    {
        return ProcessMixedOrientation(sourcePath, singleSidedPages, out _);
    }


    private void AddNonBlankPage(PdfDocument doc, PdfSharp.Drawing.XUnit width, PdfSharp.Drawing.XUnit height)
    {
        var page = doc.AddPage();
        page.Width = width;
        page.Height = height;
        
        // Draw a visible black dot to verify printer is not skipping
        // TODO: Change back to White or transparent after verification
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Use a 5x5 black square to be clearly visible
            gfx.DrawRectangle(PdfSharp.Drawing.XBrushes.Black, 10, 10, 5, 5);
        }
    }

    /// <summary>
    /// Creates a PDF for Phase 2 (Even pages) with smart rotation based on orientation.
    /// Portrait -> Rotate 180 (Long Edge flip compensation)
    /// Landscape -> Rotate 0 (Short Edge flip compensation)
    /// </summary>
    public string CreateSmartDuplexPdf(string sourcePath, int[] pageNumbers)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"smart_duplex_{Guid.NewGuid()}.pdf");

        Console.WriteLine($"[CreateSmartDuplexPdf] Source: {sourcePath}");
        Console.WriteLine($"[CreateSmartDuplexPdf] Source exists: {File.Exists(sourcePath)}");

        try
        {
            // Mở 1 lần
            using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            using var targetDoc = new PdfDocument();
            using var form = XPdfForm.FromFile(sourcePath);

            Console.WriteLine($"[CreateSmartDuplexPdf] Source opened. PageCount={sourceDoc.PageCount}");

            // Phase 2: vẫn cần thứ tự REVERSE cho các trang even
            var orderedPages = pageNumbers
                .OrderByDescending(p => p)
                .Where(p => p >= 1 && p <= sourceDoc.PageCount)
                .ToArray();

            foreach (var pageNum in orderedPages)
            {
                var srcPage = sourceDoc.Pages[pageNum - 1];

                // Tính orientation thực tế (kể cả Rotate 90/270)
                var rotate = srcPage.Rotate;
                double w = srcPage.Width.Point;
                double h = srcPage.Height.Point;
                if (rotate == 90 || rotate == 270)
                    (w, h) = (h, w);

                bool isLandscape = w > h;
                bool needsRotation = !isLandscape; // Portrait -> cần xoay 180°

                Console.WriteLine($"[CreateSmartDuplexPdf] Page {pageNum}: L={isLandscape}, Rotate={needsRotation}");

                if (needsRotation)
                {
                    // Dùng XPdfForm 1 lần, chỉ đổi PageNumber
                    form.PageNumber = pageNum;

                    var newPage = targetDoc.AddPage();
                    newPage.Width = XUnit.FromPoint(form.PointWidth);
                    newPage.Height = XUnit.FromPoint(form.PointHeight);

                    using var gfx = XGraphics.FromPdfPage(newPage);
                    gfx.Save();
                    gfx.TranslateTransform(form.PointWidth, form.PointHeight);
                    gfx.RotateTransform(180);
                    gfx.DrawImage(form, 0, 0);
                    gfx.Restore();

                    Console.WriteLine($"[CreateSmartDuplexPdf] Page {pageNum} rotated successfully");
                }
                else
                {
                    // Không xoay: import trực tiếp từ sourceDoc
                    targetDoc.AddPage(srcPage);
                    Console.WriteLine($"[CreateSmartDuplexPdf] Page {pageNum} imported successfully");
                }
            }

            int finalPageCount = targetDoc.PageCount;   // ✅ Lấy PageCount TRƯỚC khi Save
            targetDoc.Save(outputPath);                 // ✅ Save đúng 1 lần
            Console.WriteLine($"[CreateSmartDuplexPdf] Saved to {outputPath} with {finalPageCount} pages");

            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateSmartDuplexPdf ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[CreateSmartDuplexPdf ERROR] StackTrace: {ex.StackTrace}");
            throw;
        }
    }

}
