namespace ClipboardWatcher.Core;

public static class AgentControlState
{
    private const string AppFolder = "ClipboardWatcher";
    private const string PauseFlagFile = "pause-mode.flag";

    public static bool IsPauseModeEnabled()
    {
        return File.Exists(GetPauseFlagPath());
    }

    public static void SetPauseMode(bool enabled)
    {
        var path = GetPauseFlagPath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        if (enabled)
        {
            File.WriteAllText(path, "pause");
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string GetPauseFlagPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        return Path.Combine(root, AppFolder, PauseFlagFile);
    }
}
