using PrinterApp.Models;

namespace PrinterApp.Services;

public interface IWordInteropService
{
    PdfInfo GetPdfInfo(string path);
    int GetPageCount(string path);
    void CreatePdfSubset(string sourcePath, string targetPath, int[] pageNumbers);
    string ProcessMixedOrientation(string sourcePath, int[]? singleSidedPages, out List<ManualDuplexPageInfo> pageInfos);
    string ProcessMixedOrientation(string sourcePath, int[] singleSidedPages);
    string CreateSmartDuplexPdf(string sourcePath, int[] pageNumbers);
    void PrintPdf(string pdfPath, string printerName, string? pageRange = null, string? duplexSide = null);
    void ConvertImageToPdf(string imagePath, string outputPdfPath);
    void ConvertToPdf(string inputPath, string outputPath);
    /// <summary>
    /// Apply per-page rotations to a PDF. Keys in rotationMap are 1-based page numbers.
    /// Returns path to a new PDF with rotations applied.
    /// </summary>
    string ApplyPageRotations(string sourcePath, Dictionary<int, RotationDirection> rotationMap);
}
