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
    void PrintPdf(string pdfPath, string printerName, string? pageRange = null);
    string AddWatermarkToPdf(string sourcePath, WatermarkOptions opts);
    void ConvertImageToPdf(string imagePath, string outputPdfPath);
    void ConvertToPdf(string inputPath, string outputPath);
}
