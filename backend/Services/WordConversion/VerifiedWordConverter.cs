namespace PrinterApp.Services.WordConversion;

internal sealed class VerifiedWordConverter
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private readonly IWordConversionWorkerClient _workerClient;
    private readonly TimeSpan _timeout;

    public VerifiedWordConverter()
        : this(new WordConversionWorkerClient(), DefaultTimeout)
    {
    }

    public VerifiedWordConverter(IWordConversionWorkerClient workerClient, TimeSpan timeout)
    {
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _timeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public void ConvertToPdf(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Word input file was not found.", inputPath);

        try
        {
            var result = _workerClient.Convert(inputPath, outputPath, _timeout);
            var hasRawDocxPages = DocxPageSizeInspector.TryGetUniformSourcePages(
                inputPath,
                result.SourcePageCount,
                out var rawDocxPages);
            var expectedPages = hasRawDocxPages ? rawDocxPages : result.SourcePageSizes;

            ValidateWorkerResult(result, hasIndependentExpectedPages: hasRawDocxPages);
            var actualPages = PdfPageSizeVerifier.InspectPdf(outputPath);
            PdfPageSizeVerifier.ThrowIfMismatch(expectedPages, actualPages, inputPath, outputPath);
        }
        catch
        {
            DeletePartialOutput(outputPath);
            throw;
        }
    }

    private static void ValidateWorkerResult(WordConversionWorkerResult result, bool hasIndependentExpectedPages)
    {
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Word conversion failed.");

        if (result.SourcePageCount <= 0)
            throw new InvalidOperationException("Word conversion reported a document with no pages.");

        if (hasIndependentExpectedPages)
            return;

        if (!result.HasExactPageSizes)
            throw new InvalidOperationException("Word source has mixed or ambiguous section page sizes; refusing to print an unverified PDF.");

        if (result.SourcePageSizes.Count != result.SourcePageCount)
        {
            throw new InvalidOperationException(
                $"Word conversion reported {result.SourcePageCount} page(s), but supplied {result.SourcePageSizes.Count} source page-size record(s).");
        }
    }

    private static void DeletePartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch
        {
            // Best effort. The caller receives the conversion failure that made the PDF unsafe.
        }
    }
}
