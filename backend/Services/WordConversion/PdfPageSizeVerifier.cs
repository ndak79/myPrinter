using PdfSharp.Pdf.IO;

namespace PrinterApp.Services.WordConversion;

internal static class PdfPageSizeVerifier
{
    public static List<PdfPageSize> InspectPdf(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("Converted PDF was not created.", pdfPath);

        var fileInfo = new FileInfo(pdfPath);
        if (fileInfo.Length == 0)
            throw new InvalidOperationException("Converted PDF is empty.");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var pages = new List<PdfPageSize>(document.PageCount);

        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            var width = page.Width.Point;
            var height = page.Height.Point;
            var effectiveWidth = width;
            var effectiveHeight = height;

            if (page.Rotate is 90 or 270)
                (effectiveWidth, effectiveHeight) = (effectiveHeight, effectiveWidth);

            pages.Add(new PdfPageSize(
                i + 1,
                width,
                height,
                effectiveWidth,
                effectiveHeight,
                page.Rotate,
                PaperSizeClassifier.Classify(effectiveWidth, effectiveHeight)));
        }

        return pages;
    }

    public static void ThrowIfMismatch(
        IReadOnlyList<WordSourcePageSize> expectedPages,
        IReadOnlyList<PdfPageSize> actualPages,
        string sourcePath,
        string pdfPath)
    {
        if (expectedPages.Count == 0)
            throw new InvalidOperationException("Word conversion did not report source page sizes; refusing to print an unverified PDF.");

        if (expectedPages.Count != actualPages.Count)
        {
            throw new InvalidOperationException(
                $"Word conversion page-count mismatch for '{Path.GetFileName(sourcePath)}': " +
                $"source has {expectedPages.Count} page(s), converted PDF '{Path.GetFileName(pdfPath)}' has {actualPages.Count} page(s).");
        }

        for (var i = 0; i < expectedPages.Count; i++)
        {
            var expected = expectedPages[i];
            var actual = actualPages[i];
            if (PaperSizeClassifier.SameSize(
                    expected.WidthPoints,
                    expected.HeightPoints,
                    actual.EffectiveWidthPoints,
                    actual.EffectiveHeightPoints))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Word conversion page size mismatch on page {actual.PageNumber}: " +
                $"source '{Path.GetFileName(sourcePath)}' is {expected.PaperName} " +
                $"({expected.WidthPoints:F2} x {expected.HeightPoints:F2} pt), but converted PDF " +
                $"'{Path.GetFileName(pdfPath)}' is {actual.PaperName} " +
                $"({actual.EffectiveWidthPoints:F2} x {actual.EffectiveHeightPoints:F2} pt). " +
                "The PDF was rejected to avoid printing with the wrong paper size.");
        }
    }
}
