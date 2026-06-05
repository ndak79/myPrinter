namespace PrinterApp.Services.WordConversion;

internal static class PaperSizeClassifier
{
    internal const double TolerancePoints = 1.5;

    public static string Classify(double widthPoints, double heightPoints)
    {
        var portraitWidth = Math.Min(widthPoints, heightPoints);
        var portraitHeight = Math.Max(widthPoints, heightPoints);

        if (Near(portraitWidth, 595.28) && Near(portraitHeight, 841.89))
            return widthPoints > heightPoints ? "A4 landscape" : "A4 portrait";

        if (Near(portraitWidth, 612.0) && Near(portraitHeight, 792.0))
            return widthPoints > heightPoints ? "Letter landscape" : "Letter portrait";

        return "Custom/unknown";
    }

    public static bool SameSize(double expectedWidth, double expectedHeight, double actualWidth, double actualHeight)
    {
        return Math.Abs(expectedWidth - actualWidth) <= TolerancePoints
            && Math.Abs(expectedHeight - actualHeight) <= TolerancePoints;
    }

    private static bool Near(double actual, double expected)
    {
        return Math.Abs(actual - expected) <= TolerancePoints;
    }
}
