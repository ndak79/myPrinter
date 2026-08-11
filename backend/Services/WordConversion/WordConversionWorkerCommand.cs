using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace PrinterApp.Services.WordConversion;

public static class WordConversionWorkerCommand
{
    public const string CommandSwitch = "--myprinter-word-convert-worker";

    public static bool IsWorkerCommand(string[] args)
    {
        return args.Any(arg => string.Equals(arg, CommandSwitch, StringComparison.OrdinalIgnoreCase));
    }

    public static int Run(string[] args)
    {
        var resultPath = GetOption(args, "--result");
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            Console.Error.WriteLine("Missing --result argument.");
            return 2;
        }

        try
        {
            var inputPath = RequireOption(args, "--input");
            var outputPath = RequireOption(args, "--output");
            var result = ConvertOnSta(inputPath, outputPath);
            WriteResult(resultPath, result);
            return 0;
        }
        catch (Exception ex)
        {
            WriteResult(resultPath, new WordConversionWorkerResult
            {
                Success = false,
                Error = $"{ex.GetType().Name}: {ex.Message}"
            });
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static WordConversionWorkerResult ConvertOnSta(string inputPath, string outputPath)
    {
        if (!OperatingSystem.IsWindows())
            return WordAutomationConverter.ConvertToPdf(inputPath, outputPath);

        WordConversionWorkerResult? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = WordAutomationConverter.ConvertToPdf(inputPath, outputPath);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result ?? throw new InvalidOperationException("Word conversion worker completed without a result.");
    }

    private static string RequireOption(string[] args, string name)
    {
        var value = GetOption(args, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing {name} argument.");
        return value;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void WriteResult(string resultPath, WordConversionWorkerResult result)
    {
        var resultDirectory = Path.GetDirectoryName(resultPath);
        if (!string.IsNullOrWhiteSpace(resultDirectory))
            Directory.CreateDirectory(resultDirectory);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
    }
}

internal static class WordAutomationConverter
{
    public static WordConversionWorkerResult ConvertToPdf(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Word input file was not found.", inputPath);

        dynamic? word = null;
        dynamic? documents = null;
        dynamic? document = null;

        try
        {
            var wordType = Type.GetTypeFromProgID("Word.Application")
                ?? throw new InvalidOperationException("Microsoft Word is not installed or its COM registration is unavailable.");

            word = Activator.CreateInstance(wordType)
                ?? throw new InvalidOperationException("Could not create Microsoft Word COM application.");

            word.Visible = false;
            word.DisplayAlerts = 0;
            TrySetAutomationSecurity(word);

            documents = word.Documents;
            document = documents.Open(
                FileName: inputPath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Visible: false,
                OpenAndRepair: false);

            var result = new WordConversionWorkerResult();
            try
            {
                document.Repaginate();
            }
            catch (Exception ex)
            {
                AppendSourcePageInspectionWarning(
                    result,
                    $"Word pagination was unavailable: {ex.GetType().Name}: {ex.Message}");
            }

            document.ExportAsFixedFormat(
                OutputFileName: outputPath,
                ExportFormat: 17,
                OpenAfterExport: false,
                OptimizeFor: 0,
                Range: 0);

            result.Success = true;

            try
            {
                var inspection = InspectSourcePageSizes(document);
                result.SourcePageCount = inspection.SourcePageCount;
                result.SectionPageSizes = inspection.SectionPageSizes;
                result.SourcePageSizes = inspection.SourcePageSizes;
                result.HasExactPageSizes = inspection.HasExactPageSizes;
            }
            catch (Exception ex)
            {
                AppendSourcePageInspectionWarning(
                    result,
                    $"Word source inspection failed: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }
        finally
        {
            CloseDocument(document);
            ReleaseComObject(documents);
            QuitWord(word);
        }
    }

    private static void AppendSourcePageInspectionWarning(WordConversionWorkerResult result, string warning)
    {
        result.SourcePageInspectionWarning = string.IsNullOrWhiteSpace(result.SourcePageInspectionWarning)
            ? warning
            : $"{result.SourcePageInspectionWarning} {warning}";
        Console.Error.WriteLine($"[WordConversion] WARNING: {warning}");
    }

    private static WordConversionWorkerResult InspectSourcePageSizes(dynamic document)
    {
        var pageCount = Convert.ToInt32(document.ComputeStatistics(2));
        var sections = ReadSectionPageSizes(document);
        var sourcePages = BuildExactSourcePages(pageCount, sections);

        return new WordConversionWorkerResult
        {
            SourcePageCount = pageCount,
            SectionPageSizes = sections,
            SourcePageSizes = sourcePages,
            HasExactPageSizes = sourcePages.Count == pageCount
        };
    }

    private static List<WordSectionPageSize> ReadSectionPageSizes(dynamic document)
    {
        var sections = new List<WordSectionPageSize>();
        dynamic? sectionCollection = null;

        try
        {
            sectionCollection = document.Sections;
            var sectionCount = Convert.ToInt32(sectionCollection.Count);

            for (var i = 1; i <= sectionCount; i++)
            {
                dynamic? section = null;
                dynamic? pageSetup = null;
                dynamic? range = null;

                try
                {
                    section = sectionCollection[i];
                    pageSetup = section.PageSetup;
                    range = section.Range;

                    var width = Convert.ToDouble(pageSetup.PageWidth);
                    var height = Convert.ToDouble(pageSetup.PageHeight);
                    var pages = Convert.ToInt32(range.ComputeStatistics(2));

                    sections.Add(new WordSectionPageSize(
                        i,
                        pages,
                        width,
                        height,
                        PaperSizeClassifier.Classify(width, height)));
                }
                finally
                {
                    ReleaseComObject(range);
                    ReleaseComObject(pageSetup);
                    ReleaseComObject(section);
                }
            }
        }
        finally
        {
            ReleaseComObject(sectionCollection);
        }

        return sections;
    }

    private static List<WordSourcePageSize> BuildExactSourcePages(int pageCount, List<WordSectionPageSize> sections)
    {
        if (pageCount <= 0 || sections.Count == 0)
            return new List<WordSourcePageSize>();

        if (sections.All(section => PaperSizeClassifier.SameSize(
                sections[0].WidthPoints,
                sections[0].HeightPoints,
                section.WidthPoints,
                section.HeightPoints)))
        {
            return RepeatPages(pageCount, sections[0].WidthPoints, sections[0].HeightPoints, sections[0].PaperName, startPage: 1);
        }

        var sectionPageSum = sections.Sum(section => section.PageCount);
        if (sectionPageSum != pageCount)
            return new List<WordSourcePageSize>();

        var pages = new List<WordSourcePageSize>(pageCount);
        var pageNumber = 1;
        foreach (var section in sections)
        {
            pages.AddRange(RepeatPages(section.PageCount, section.WidthPoints, section.HeightPoints, section.PaperName, pageNumber));
            pageNumber += section.PageCount;
        }

        return pages;
    }

    private static List<WordSourcePageSize> RepeatPages(
        int count,
        double width,
        double height,
        string paperName,
        int startPage)
    {
        var pages = new List<WordSourcePageSize>(count);
        for (var i = 0; i < count; i++)
            pages.Add(new WordSourcePageSize(startPage + i, width, height, paperName));
        return pages;
    }

    private static void TrySetAutomationSecurity(dynamic word)
    {
        try { word.AutomationSecurity = 3; } catch { }
    }

    private static void CloseDocument(dynamic? document)
    {
        if (document == null)
            return;

        try { document.Close(SaveChanges: false); } catch { }
        ReleaseComObject(document);
    }

    private static void QuitWord(dynamic? word)
    {
        if (word == null)
            return;

        try { word.Quit(SaveChanges: false); } catch { }
        ReleaseComObject(word);
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
        }
    }
}
