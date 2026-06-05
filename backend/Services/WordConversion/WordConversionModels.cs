namespace PrinterApp.Services.WordConversion;

internal sealed record WordSourcePageSize(
    int PageNumber,
    double WidthPoints,
    double HeightPoints,
    string PaperName);

internal sealed record WordSectionPageSize(
    int SectionNumber,
    int PageCount,
    double WidthPoints,
    double HeightPoints,
    string PaperName);

internal sealed record PdfPageSize(
    int PageNumber,
    double WidthPoints,
    double HeightPoints,
    double EffectiveWidthPoints,
    double EffectiveHeightPoints,
    int Rotate,
    string PaperName);

internal sealed class WordConversionWorkerResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int SourcePageCount { get; set; }
    public bool HasExactPageSizes { get; set; }
    public List<WordSourcePageSize> SourcePageSizes { get; set; } = new();
    public List<WordSectionPageSize> SectionPageSizes { get; set; } = new();
}
