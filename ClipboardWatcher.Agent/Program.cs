namespace ClipboardWatcher.Agent;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ClipboardWatcherContext());
    }    
}