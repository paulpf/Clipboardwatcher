using System.ComponentModel;

namespace ClipboardWatcher.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentLauncher _agentLauncher;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _agentLauncher = new AgentLauncher(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _agentLauncher.EnsureAgentRunningInActiveSession();
                }
                catch (FileNotFoundException ex)
                {
                    _logger.LogError(ex, "Agent nicht gefunden: {Path}", ex.FileName);
                }
                catch (Win32Exception ex)
                {
                    _logger.LogError(ex, "Agent konnte nicht gestartet werden (Win32 Fehlercode {Code}).", ex.NativeErrorCode);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ClipboardWatcher Service wird beendet.");
        }
    }
}
