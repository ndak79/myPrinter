using Word = Microsoft.Office.Interop.Word;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using XPdfForm = PdfSharp.Drawing.XPdfForm;
using XGraphics = PdfSharp.Drawing.XGraphics;
using XUnit = PdfSharp.Drawing.XUnit;
using XImage = PdfSharp.Drawing.XImage;
using PrinterApp.Models;

namespace PrinterApp.Services;

public record PdfInfo(int PageCount, bool IsLandscape);

[SupportedOSPlatform("windows")]
public class WordInteropService : IWordInteropService
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
        double w = templatePage.Width.Point;
        double h = templatePage.Height.Point;

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

        page.Width  = XUnit.FromPoint(w);
        page.Height = XUnit.FromPoint(h);

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Vẽ 1 hình chữ nhật nhỏ màu xám ở GẦN GIỮA TRANG
            // → chắc chắn nằm trong vùng in, có pixel khác trắng.
            double rectSize = 3; // kích thước rất nhỏ, khó thấy
            double centerX  = page.Width.Point  / 2.0;
            double centerY  = page.Height.Point / 2.0;

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

    /// <summary>
    /// Convert a JPG/PNG/TIFF/BMP/WebP image to a single-page A4 PDF using PdfSharp.
    /// Image is centered and scaled to fit within 20mm margins.
    /// WebP and multi-frame TIFF are pre-converted to PNG in memory via System.Drawing.
    /// </summary>
    public void ConvertImageToPdf(string imagePath, string outputPdfPath)
    {
        Console.WriteLine($"[ConvertImageToPdf] Converting: {imagePath} -> {outputPdfPath}");

        var ext = Path.GetExtension(imagePath).ToLower();

        // WebP and TIFF may not load directly in PdfSharp — normalise to PNG first
        string? tempPng = null;
        string effectivePath = imagePath;

        if (ext is ".webp" or ".tif" or ".tiff" or ".bmp")
        {
            tempPng = Path.Combine(Path.GetTempPath(), $"img_convert_{Guid.NewGuid()}.png");
            using var sysBmp = new System.Drawing.Bitmap(imagePath);
            sysBmp.Save(tempPng, System.Drawing.Imaging.ImageFormat.Png);
            effectivePath = tempPng;
            Console.WriteLine($"[ConvertImageToPdf] Pre-converted {ext} → PNG: {tempPng}");
        }

        try
        {
            using var document = new PdfDocument();
            var page = document.AddPage();

            // A4 size
            page.Width  = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);

            using var gfx = XGraphics.FromPdfPage(page);
            using var image = XImage.FromFile(effectivePath);

            double imgW  = image.PointWidth;
            double imgH  = image.PointHeight;
            double pageW = page.Width.Point;
            double pageH = page.Height.Point;

            // 20mm margin on each side
            double margin = XUnit.FromMillimeter(20).Point;
            double maxW   = pageW - 2 * margin;
            double maxH   = pageH - 2 * margin;

            // Scale to fit while preserving aspect ratio
            double scale  = Math.Min(maxW / imgW, maxH / imgH);
            double drawW  = imgW * scale;
            double drawH  = imgH * scale;
            double x      = margin + (maxW - drawW) / 2.0;
            double y      = margin + (maxH - drawH) / 2.0;

            gfx.DrawImage(image, x, y, drawW, drawH);
            document.Save(outputPdfPath);
            Console.WriteLine($"[ConvertImageToPdf] Done. Output: {outputPdfPath}");
        }
        finally
        {
            if (tempPng != null)
            {
                try { File.Delete(tempPng); } catch { /* best-effort cleanup */ }
            }
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
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            var pageCount = document.PageCount;

            if (pageCount == 0)
            {
                Console.WriteLine("[GetPdfInfo ERROR] PDF has no pages");
                throw new InvalidOperationException("PDF has no pages");
            }

            var firstPage = document.Pages[0];
            var isLandscape = firstPage.Width.Point > firstPage.Height.Point;

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
        
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
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
            // Strategy 1: Use SumatraPDF if available — it supports -print-to <printerName>
            var sumatraPath = FindSumatraPdf();
            if (sumatraPath != null)
            {
                Console.WriteLine($"[TryShellPrint] Found SumatraPDF at: {sumatraPath}");
                return PrintWithSumatra(sumatraPath, filePath, printerName);
            }

            // Strategy 2: Temporarily set default printer, shell print, then restore
            Console.WriteLine($"[TryShellPrint] SumatraPDF not found. Using default-printer swap strategy.");
            return PrintBySwappingDefaultPrinter(filePath, printerName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryShellPrint ERROR] {ex.Message}");
            return false;
        }
    }

    private string? FindSumatraPdf()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "SumatraPDF", "SumatraPDF.exe"),
            @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
            @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private bool PrintWithSumatra(string sumatraPath, string pdfPath, string printerName)
    {
        try
        {
            var args = $"-print-to \"{printerName}\" \"{pdfPath}\"";
            Console.WriteLine($"[PrintWithSumatra] Args: {args}");

            var psi = new System.Diagnostics.ProcessStartInfo(sumatraPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                Console.WriteLine("[PrintWithSumatra] Failed to start SumatraPDF process.");
                return false;
            }
            bool completed = proc.WaitForExit(60_000);
            Console.WriteLine($"[PrintWithSumatra] Exit code: {proc.ExitCode}, completed in time: {completed}");
            return completed && proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PrintWithSumatra ERROR] {ex.Message}");
            return false;
        }
    }

    private bool PrintBySwappingDefaultPrinter(string filePath, string printerName)
    {
        var originalDefault = GetDefaultPrinterName();
        Console.WriteLine($"[PrintBySwappingDefaultPrinter] Original default printer: {originalDefault ?? "(none)"}");

        try
        {
            if (!SetDefaultPrinter(printerName))
            {
                Console.WriteLine($"[PrintBySwappingDefaultPrinter] WARNING: SetDefaultPrinter failed for: {printerName}. Printing anyway (may go to wrong printer).");
            }
            else
            {
                Console.WriteLine($"[PrintBySwappingDefaultPrinter] Set default printer to: {printerName}");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName       = filePath,
                Verb           = "print",
                UseShellExecute = true,
                CreateNoWindow  = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("[PrintBySwappingDefaultPrinter] Failed to start print process.");
                return false;
            }

            Console.WriteLine($"[PrintBySwappingDefaultPrinter] Print process started. ID: {process.Id}");
            bool completed = process.WaitForExit(60_000);
            if (!completed)
            {
                Console.WriteLine("[PrintBySwappingDefaultPrinter] Process timed out.");
                try { process.Kill(); } catch { }
            }
            return true;
        }
        finally
        {
            // Always restore original default printer
            if (originalDefault != null)
            {
                SetDefaultPrinter(originalDefault);
                Console.WriteLine($"[PrintBySwappingDefaultPrinter] Restored default printer to: {originalDefault}");
            }
        }
    }

    private string? GetDefaultPrinterName()
    {
        try
        {
            return new System.Drawing.Printing.PrinterSettings().PrinterName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetDefaultPrinterName] Error: {ex.Message}");
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("winspool.drv",
        CharSet = System.Runtime.InteropServices.CharSet.Auto,
        SetLastError = true)]
    private static extern bool SetDefaultPrinter(string Name);

    private void TryPowerShellPrint(string filePath, string printerName)
    {
        // Use C# P/Invoke to set default printer (reliable, avoids PowerShell quoting issues)
        var originalDefault = GetDefaultPrinterName();
        Console.WriteLine($"[TryPowerShellPrint] Original default: {originalDefault ?? "(none)"}");

        try
        {
            if (!SetDefaultPrinter(printerName))
            {
                Console.WriteLine($"[TryPowerShellPrint] WARNING: SetDefaultPrinter failed for: {printerName}");
            }
            else
            {
                Console.WriteLine($"[TryPowerShellPrint] Set default printer to: {printerName}");
            }

            var escapedPath = filePath.Replace("'", "''");
            var psCommand = $"Start-Process -FilePath '{escapedPath}' -Verb Print -Wait";

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };

            Console.WriteLine($"[TryPowerShellPrint] Printing via PowerShell on: {printerName}");
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(60_000);
                var output = process.StandardOutput.ReadToEnd();
                var error  = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(output)) Console.WriteLine($"[TryPowerShellPrint] Output: {output}");
                if (!string.IsNullOrEmpty(error))  Console.WriteLine($"[TryPowerShellPrint] Error: {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryPowerShellPrint ERROR] {ex.Message}");
        }
        finally
        {
            if (originalDefault != null)
            {
                SetDefaultPrinter(originalDefault);
                Console.WriteLine($"[TryPowerShellPrint] Restored default printer to: {originalDefault}");
            }
            else
            {
                Console.WriteLine("[TryPowerShellPrint] WARNING: Could not restore default printer (original was unknown).");
            }
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

            foreach (var pageNum in pageNumbers)
            {
                if (pageNum == 0)
                {
                    PdfPage? template = targetDoc.PageCount > 0
                        ? targetDoc.Pages[targetDoc.PageCount - 1]
                        : null;

                    if (template != null)
                    {
                        bool isLandscape = template.Width.Point > template.Height.Point;
                        CreateNonSkippableBlankPage(targetDoc, template, isLandscape);
                        Console.WriteLine($"[CreatePdfSubset] Added blank page (isLandscape={isLandscape})");
                    }
                    else
                    {
                        var blankPage = targetDoc.AddPage();
                        blankPage.Width  = XUnit.FromPoint(595.28);
                        blankPage.Height = XUnit.FromPoint(841.89);
                        using var gfx = XGraphics.FromPdfPage(blankPage);
                        gfx.DrawRectangle(XBrushes.White, 0, 0, 0.01, 0.01);
                        Console.WriteLine($"[CreatePdfSubset] Added blank page (first page fallback, A4 portrait)");
                    }
                }
                else if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
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
                double w = p.Width.Point;
                double h = p.Height.Point;

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

    /// <summary>
    /// Stamp a diagonal text watermark on every page of the PDF.
    /// Returns path to the new watermarked PDF.
    /// </summary>
    public string AddWatermarkToPdf(string sourcePath, WatermarkOptions opts)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"watermark_{Guid.NewGuid()}.pdf");
        Console.WriteLine($"[AddWatermarkToPdf] Stamping '{opts.Text}' on: {sourcePath}");

        using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        using var targetDoc = new PdfDocument();

        // Parse color from hex "#RRGGBB"
        var hex = opts.Color.TrimStart('#');
        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);
        var alpha = (int)(opts.Opacity / 100.0 * 255);
        var color = XColor.FromArgb(alpha, r, g, b);

        var font = new XFont("Arial", opts.FontSize, XFontStyleEx.Bold);

        for (int i = 0; i < sourceDoc.PageCount; i++)
        {
            var page = targetDoc.AddPage(sourceDoc.Pages[i]);
            using var gfx = XGraphics.FromPdfPage(page);

            gfx.Save();
            double cx = page.Width.Point / 2;
            double cy = page.Height.Point / 2;
            gfx.TranslateTransform(cx, cy);
            gfx.RotateTransform(-45);

            var size = gfx.MeasureString(opts.Text, font);
            gfx.DrawString(
                opts.Text,
                font,
                new XSolidBrush(color),
                new XPoint(-size.Width / 2, size.Height / 4)
            );
            gfx.Restore();
        }

        targetDoc.Save(outputPath);
        Console.WriteLine($"[AddWatermarkToPdf] Done: {outputPath}");
        return outputPath;
    }

    /// <summary>
    /// Apply per-page rotations to a PDF (U — per-page rotation).
    /// Creates a new PDF where each page listed in rotationMap is rotated accordingly.
    /// Pages not in rotationMap are copied as-is.
    /// NOTE: FlipHorizontal and FlipVertical map to 180° as MVP fallback —
    /// true flip requires content stream manipulation not supported by PdfSharp.
    /// </summary>
    public string ApplyPageRotations(string sourcePath, Dictionary<int, RotationDirection> rotationMap)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"rotated_pages_{Guid.NewGuid()}.pdf");
        Console.WriteLine($"[ApplyPageRotations] Applying rotations to {rotationMap.Count} pages in: {sourcePath}");

        try
        {
            using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            using var targetDoc = new PdfDocument();
            using var form = XPdfForm.FromFile(sourcePath);

            for (int i = 0; i < sourceDoc.PageCount; i++)
            {
                int pageNum = i + 1;
                var srcPage = sourceDoc.Pages[i];

                if (rotationMap.TryGetValue(pageNum, out var rotation) && rotation != RotationDirection.None)
                {
                    // Map rotation to degrees
                    int degrees = rotation switch
                    {
                        RotationDirection.CW90 => 90,
                        RotationDirection.CCW90 => 270,
                        RotationDirection.Rotate180 => 180,
                        RotationDirection.FlipHorizontal => 180, // MVP fallback
                        RotationDirection.FlipVertical => 180,   // MVP fallback
                        _ => 0,
                    };

                    if (degrees == 0)
                    {
                        targetDoc.AddPage(srcPage);
                        continue;
                    }

                    form.PageNumber = pageNum;
                    double formW = form.PointWidth;
                    double formH = form.PointHeight;

                    var newPage = targetDoc.AddPage();
                    if (degrees == 90 || degrees == 270)
                    {
                        // Swap dimensions for 90/270
                        newPage.Width = XUnit.FromPoint(formH);
                        newPage.Height = XUnit.FromPoint(formW);
                    }
                    else
                    {
                        newPage.Width = XUnit.FromPoint(formW);
                        newPage.Height = XUnit.FromPoint(formH);
                    }

                    using var gfx = XGraphics.FromPdfPage(newPage);
                    gfx.Save();

                    switch (degrees)
                    {
                        case 90:
                            gfx.TranslateTransform(formH, 0);
                            gfx.RotateTransform(90);
                            break;
                        case 180:
                            gfx.TranslateTransform(formW, formH);
                            gfx.RotateTransform(180);
                            break;
                        case 270:
                            gfx.TranslateTransform(0, formW);
                            gfx.RotateTransform(270);
                            break;
                    }

                    gfx.DrawImage(form, 0, 0, formW, formH);
                    gfx.Restore();

                    Console.WriteLine($"[ApplyPageRotations] Page {pageNum}: rotated {degrees}°");
                }
                else
                {
                    targetDoc.AddPage(srcPage);
                }
            }

            targetDoc.Save(outputPath);
            Console.WriteLine($"[ApplyPageRotations] Created rotated PDF at: {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApplyPageRotations ERROR] {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

}
