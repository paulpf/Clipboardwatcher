using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClipboardWatcher.Core;

namespace ClipboardWatcher.Agent;

internal sealed class ClipboardWatcherContext : ApplicationContext
{
    private const string DefaultRepository = "paulpf/Clipboardwatcher";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private readonly NotifyIcon _notifyIcon;
    private readonly ClipboardMessageWindow _messageWindow;
    private readonly ToolStripMenuItem _checkUpdatesMenuItem;
    private readonly ToolStripMenuItem _openUpdateMenuItem;
    private readonly HttpClient _httpClient;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly Version _currentVersion;
    private string? _lastPreview;
    private string? _lastError;
    private string? _latestReleaseUrl;
    private Version? _latestVersion;

    public ClipboardWatcherContext()
    {
        _currentVersion = ResolveCurrentVersion();
        _httpClient = CreateHttpClient();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = $"ClipboardWatcher v{_currentVersion}",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        _checkUpdatesMenuItem = new ToolStripMenuItem("Auf Updates prüfen", null, async (_, _) => await CheckForUpdatesAsync(isManual: true));
        _openUpdateMenuItem = new ToolStripMenuItem("Update öffnen", null, (_, _) => OpenLatestReleaseUrl())
        {
            Enabled = false
        };
        menu.Items.Add(_checkUpdatesMenuItem);
        menu.Items.Add(_openUpdateMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.BalloonTipClicked += (_, _) => OpenLatestReleaseUrl();

        _messageWindow = new ClipboardMessageWindow(OnClipboardUpdated);
        _updateTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)UpdateCheckInterval.TotalMilliseconds
        };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(isManual: false);
        _updateTimer.Start();
        _ = CheckForUpdatesAsync(isManual: false);
    }

    protected override void ExitThreadCore()
    {
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _httpClient.Dispose();
        _messageWindow.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    private void OnClipboardUpdated()
    {
        if (!TryReadClipboardPreview(out var preview, out var errorText))
        {
            if (!string.Equals(_lastError, errorText, StringComparison.Ordinal))
            {
                _lastError = errorText;
                _notifyIcon.ShowBalloonTip(2000, "ClipboardWatcher", errorText, ToolTipIcon.Warning);
            }

            return;
        }

        _lastError = null;
        if (string.Equals(_lastPreview, preview, StringComparison.Ordinal))
        {
            return;
        }

        _lastPreview = preview;
        _notifyIcon.ShowBalloonTip(2200, "Zwischenablage geändert", preview, ToolTipIcon.Info);
    }

    private static bool TryReadClipboardPreview(out string preview, out string errorText)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                preview = string.IsNullOrWhiteSpace(text)
                    ? "Leerer Text"
                    : ClipboardTextFormatter.ToPreview(text);
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var firstItem = files.Count > 0 ? Path.GetFileName(files[0]) : "Datei";
                preview = files.Count switch
                {
                    0 => "Dateiliste",
                    1 => $"Datei: {firstItem}",
                    _ => $"{files.Count} Dateien, erste: {firstItem}"
                };
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsImage())
            {
                preview = "Bild in Zwischenablage";
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsAudio())
            {
                preview = "Audio in Zwischenablage";
                errorText = string.Empty;
                return true;
            }

            preview = "Unbekanntes Zwischenablageformat";
            errorText = string.Empty;
            return true;
        }
        catch (ExternalException)
        {
            preview = string.Empty;
            errorText = "Zwischenablage ist gerade gesperrt. Neuer Versuch beim naechsten Update.";
            return false;
        }
        catch (ThreadStateException)
        {
            preview = string.Empty;
            errorText = "Clipboard-Zugriff ist nur im STA-Thread moeglich.";
            return false;
        }
    }

    private async Task CheckForUpdatesAsync(bool isManual)
    {
        _checkUpdatesMenuItem.Enabled = false;
        try
        {
            var release = await TryGetLatestReleaseAsync();
            if (release is null)
            {
                if (isManual)
                {
                    _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", "Release-Information konnte nicht gelesen werden.", ToolTipIcon.Warning);
                }

                return;
            }

            _latestReleaseUrl = release.HtmlUrl;
            _latestVersion = release.Version;
            _openUpdateMenuItem.Enabled = true;
            _openUpdateMenuItem.Text = $"Update öffnen ({release.TagName})";

            if (release.Version > _currentVersion)
            {
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "Update verfügbar",
                    $"Neue Version {release.TagName} verfügbar (aktuell v{_currentVersion}). Klicken zum Öffnen.",
                    ToolTipIcon.Info);
                return;
            }

            if (isManual)
            {
                _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", $"Du nutzt bereits die aktuelle Version v{_currentVersion}.", ToolTipIcon.Info);
            }
        }
        catch (HttpRequestException)
        {
            if (isManual)
            {
                _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", "Update-Check fehlgeschlagen (Netzwerk/API).", ToolTipIcon.Warning);
            }
        }
        catch (TaskCanceledException)
        {
            if (isManual)
            {
                _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", "Update-Check wurde wegen Timeout abgebrochen.", ToolTipIcon.Warning);
            }
        }
        catch (JsonException)
        {
            if (isManual)
            {
                _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", "Update-Check konnte Release-Antwort nicht verarbeiten.", ToolTipIcon.Warning);
            }
        }
        finally
        {
            _checkUpdatesMenuItem.Enabled = true;
        }
    }

    private async Task<GitHubReleaseInfo?> TryGetLatestReleaseAsync()
    {
        var repository = ResolveRepository();
        var endpoint = $"https://api.github.com/repos/{repository}/releases/latest";

        using var response = await _httpClient.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync();
        return GitHubReleaseInfo.TryParseLatestReleasePayload(payload, out var releaseInfo)
            ? releaseInfo
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ClipboardWatcher-Agent");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return httpClient;
    }

    private static Version ResolveCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static string ResolveRepository()
    {
        var configured = Environment.GetEnvironmentVariable("CLIPBOARDWATCHER_UPDATE_REPOSITORY");
        return string.IsNullOrWhiteSpace(configured) ? DefaultRepository : configured.Trim();
    }

    private void OpenLatestReleaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_latestReleaseUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _latestReleaseUrl,
                UseShellExecute = true
            });
        }
        catch (Win32Exception)
        {
            _notifyIcon.ShowBalloonTip(2500, "ClipboardWatcher", "Release-Seite konnte nicht geöffnet werden.", ToolTipIcon.Warning);
        }
    }

    private sealed class ClipboardMessageWindow : NativeWindow, IDisposable
    {
        private const int WmClipboardUpdate = 0x031D;
        private readonly Action _onClipboardUpdate;

        public ClipboardMessageWindow(Action onClipboardUpdate)
        {
            _onClipboardUpdate = onClipboardUpdate;
            CreateHandle(new CreateParams());

            if (!AddClipboardFormatListener(Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AddClipboardFormatListener fehlgeschlagen.");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmClipboardUpdate)
            {
                _onClipboardUpdate();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
