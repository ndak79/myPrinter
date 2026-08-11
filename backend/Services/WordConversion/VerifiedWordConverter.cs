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
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Word conversion failed.");

            var actualPages = PdfPageSizeVerifier.InspectPdf(outputPath);
            if (actualPages.Count == 0)
                throw new InvalidOperationException("Converted PDF contains no pages.");

            var sourcePageCount = result.SourcePageCount > 0
                ? result.SourcePageCount
                : actualPages.Count;
            var hasRawDocxPages = DocxPageSizeInspector.TryGetUniformSourcePages(
                inputPath,
                sourcePageCount,
                out var rawDocxPages);
            var expectedPages = hasRawDocxPages ? rawDocxPages : result.SourcePageSizes;

            ValidateWorkerResult(result, actualPages.Count, hasIndependentExpectedPages: hasRawDocxPages);
            if (expectedPages.Count > 0)
                PdfPageSizeVerifier.ThrowIfMismatch(expectedPages, actualPages, inputPath, outputPath);

            if (!string.IsNullOrWhiteSpace(result.SourcePageInspectionWarning))
            {
                Console.Error.WriteLine(
                    $"[WordConversion] Source page metadata was unavailable; accepted the validated PDF: " +
                    result.SourcePageInspectionWarning);
            }
        }
        catch
        {
            DeletePartialOutput(outputPath);
            throw;
        }
    }

    private static void ValidateWorkerResult(
        WordConversionWorkerResult result,
        int actualPageCount,
        bool hasIndependentExpectedPages)
    {
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Word conversion failed.");

        if (actualPageCount <= 0)
            throw new InvalidOperationException("Converted PDF contains no pages.");

        if (result.SourcePageCount > 0 && result.SourcePageCount != actualPageCount)
        {
            throw new InvalidOperationException(
                $"Word conversion reported {result.SourcePageCount} source page(s), but the converted PDF contains " +
                $"{actualPageCount} page(s).");
        }

        // Word can export a valid PDF even when COM page metadata inspection fails.
        // In that case the PDF itself is the only reliable page-count evidence.
        if (result.SourcePageCount <= 0)
            return;

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
