namespace ClipboardWatcher.Core;

public static class ClipboardTextFormatter
{
    private const int MaxPreviewLength = 220;

    public static string Normalize(string text)
    {
        return text.ReplaceLineEndings(" ").Trim();
    }

    public static string ToPreview(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length <= MaxPreviewLength
            ? normalized
            : normalized[..MaxPreviewLength] + "...";
    }
}
