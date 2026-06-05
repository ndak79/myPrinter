using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;

namespace PrinterApp.Services.WordConversion;

internal static class DocxPageSizeInspector
{
    private static readonly XNamespace WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool TryGetUniformSourcePages(
        string inputPath,
        int pageCount,
        out List<WordSourcePageSize> sourcePages)
    {
        sourcePages = new List<WordSourcePageSize>();

        if (!Path.GetExtension(inputPath).Equals(".docx", StringComparison.OrdinalIgnoreCase) || pageCount <= 0)
            return false;

        try
        {
            using var archive = ZipFile.OpenRead(inputPath);
            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry == null)
                return false;

            using var stream = documentEntry.Open();
            var document = XDocument.Load(stream);
            var pageSizes = document
                .Descendants(WordNamespace + "pgSz")
                .Select(element => ReadPageSize(element))
                .Where(size => size != null)
                .Select(size => size!.Value)
                .ToList();

            if (pageSizes.Count == 0)
                return false;

            var first = pageSizes[0];
            if (pageSizes.Any(size => !PaperSizeClassifier.SameSize(first.WidthPoints, first.HeightPoints, size.WidthPoints, size.HeightPoints)))
                return false;

            sourcePages = Enumerable.Range(1, pageCount)
                .Select(pageNumber => new WordSourcePageSize(
                    pageNumber,
                    first.WidthPoints,
                    first.HeightPoints,
                    PaperSizeClassifier.Classify(first.WidthPoints, first.HeightPoints)))
                .ToList();
            return true;
        }
        catch
        {
            sourcePages = new List<WordSourcePageSize>();
            return false;
        }
    }

    private static RawPageSize? ReadPageSize(XElement element)
    {
        var widthAttribute = element.Attribute(WordNamespace + "w");
        var heightAttribute = element.Attribute(WordNamespace + "h");

        if (widthAttribute == null || heightAttribute == null)
            return null;

        if (!double.TryParse(widthAttribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var widthTwips) ||
            !double.TryParse(heightAttribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var heightTwips))
        {
            return null;
        }

        return new RawPageSize(widthTwips / 20.0, heightTwips / 20.0);
    }

    private readonly record struct RawPageSize(double WidthPoints, double HeightPoints);
}
