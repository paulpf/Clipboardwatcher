using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipboardWatcher.Core;

namespace ClipboardWatcher.Agent;

internal sealed class ClipboardWatcherContext : ApplicationContext
{
    private const string DefaultRepository = "paulpf/Clipboardwatcher";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private const int PopupDurationMs = 10000;

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;
    private readonly ClipboardMessageWindow _messageWindow;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _pauseModeMenuItem;
    private readonly ToolStripMenuItem _checkUpdatesMenuItem;
    private readonly ToolStripMenuItem _openUpdateMenuItem;
    private readonly HttpClient _httpClient;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly Version _currentVersion;
    private WpfClipboardPopupWindow? _popupWindow;
    private string? _lastChangeKey;
    private string? _lastError;
    private string? _latestReleaseUrl;

    public ClipboardWatcherContext()
    {
        _currentVersion = ResolveCurrentVersion();
        _httpClient = CreateHttpClient();
        _activeIcon = ResolveTrayIcon();
        _pausedIcon = SystemIcons.Warning;
        _notifyIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Text = $"ClipboardWatcher v{_currentVersion}",
            Visible = true
        };

        var menu = BuildContextMenu();
        _statusMenuItem = GetMenuItemByName(menu, "status");
        _pauseModeMenuItem = GetMenuItemByName(menu, "pause-mode");
        _checkUpdatesMenuItem = GetMenuItemByName(menu, "check-updates");
        _openUpdateMenuItem = GetMenuItemByName(menu, "open-update");
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.BalloonTipClicked += (_, _) => OpenLatestReleaseUrl();

        _messageWindow = new ClipboardMessageWindow(OnClipboardUpdated);
        _updateTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)UpdateCheckInterval.TotalMilliseconds
        };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(isManual: false);
        _updateTimer.Start();
        UpdateStatusText();
        RefreshTrayIconState();
        _ = CheckForUpdatesAsync(isManual: false);
    }

    protected override void ExitThreadCore()
    {
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _httpClient.Dispose();
        _popupWindow?.Close();
        _popupWindow = null;
        _messageWindow.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _activeIcon.Dispose();
        base.ExitThreadCore();
    }

    private void OnClipboardUpdated()
    {
        if (AgentControlState.IsPauseModeEnabled())
        {
            return;
        }

        if (!TryReadClipboardPreview(out var popupPayload, out var errorText))
        {
            if (!string.Equals(_lastError, errorText, StringComparison.Ordinal))
            {
                _lastError = errorText;
                _notifyIcon.ShowBalloonTip(2000, "ClipboardWatcher", errorText, ToolTipIcon.Warning);
            }

            return;
        }

        _lastError = null;
        if (string.Equals(_lastChangeKey, popupPayload.ChangeKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastChangeKey = popupPayload.ChangeKey;
        ShowPopup("Zwischenablage geändert", popupPayload.Content, PopupDurationMs, popupPayload.Thumbnail);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        var items = new[]
        {
            new MenuItemDefinition("status", $"Status: Aktiv (v{_currentVersion})", enabled: false),
            new MenuItemDefinition("pause-mode", "Pause-Modus (kein Auto-Neustart)", onClick: (_, _) => TogglePauseMode(), isCheckable: true, isChecked: AgentControlState.IsPauseModeEnabled()),
            new MenuItemDefinition("check-updates", "Auf Updates prüfen", onClick: async (_, _) => await CheckForUpdatesAsync(isManual: true)),
            new MenuItemDefinition("open-update", "Update öffnen", onClick: (_, _) => OpenLatestReleaseUrl(), enabled: false),
            new MenuItemDefinition("open-install-folder", "Installationsordner öffnen", onClick: (_, _) => OpenInstallDirectory()),
            MenuItemDefinition.Separator(),
            new MenuItemDefinition("restart-agent", "Agent neu starten", onClick: (_, _) => RestartAgent()),
            new MenuItemDefinition("exit-no-restart", "Nur Agent beenden (ohne Neustart)", onClick: (_, _) => ExitWithoutRestart()),
            new MenuItemDefinition("exit-agent", "Beenden", onClick: (_, _) => ExitThread())
        };

        foreach (var item in items)
        {
            menu.Items.Add(CreateToolStripItem(item));
        }

        return menu;
    }

    private static ToolStripMenuItem GetMenuItemByName(ContextMenuStrip menu, string name)
    {
        var menuItem = menu.Items.Find(name, false).OfType<ToolStripMenuItem>().FirstOrDefault();
        if (menuItem is null)
        {
            throw new InvalidOperationException($"Menüeintrag '{name}' wurde nicht gefunden.");
        }

        return menuItem;
    }

    private static ToolStripItem CreateToolStripItem(MenuItemDefinition definition)
    {
        if (definition.IsSeparator)
        {
            return new ToolStripSeparator();
        }

        var menuItem = new ToolStripMenuItem(definition.Text)
        {
            Name = definition.Name,
            Enabled = definition.Enabled,
            CheckOnClick = false,
            Checked = definition.IsChecked
        };

        if (definition.OnClick is not null)
        {
            menuItem.Click += definition.OnClick;
        }

        foreach (var child in definition.Children)
        {
            menuItem.DropDownItems.Add(CreateToolStripItem(child));
        }

        return menuItem;
    }

    private static bool TryReadClipboardPreview(out ClipboardPopupPayload payload, out string errorText)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                var preview = string.IsNullOrWhiteSpace(text)
                    ? "Leerer Text"
                    : ClipboardTextFormatter.ToPreview(text);
                payload = new ClipboardPopupPayload(preview, preview, null);
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var firstItem = files.Count > 0 ? Path.GetFileName(files[0]) : "Datei";
                var preview = files.Count switch
                {
                    0 => "Dateiliste",
                    1 => $"Datei: {firstItem}",
                    _ => $"{files.Count} Dateien, erste: {firstItem}"
                };
                payload = new ClipboardPopupPayload(preview, preview, null);
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsImage())
            {
                using var image = Clipboard.GetImage();
                var width = image?.Width ?? 0;
                var height = image?.Height ?? 0;
                var preview = width > 0 && height > 0
                    ? $"Bild in Zwischenablage ({width}x{height})"
                    : "Bild in Zwischenablage";
                var thumbnail = image is null ? null : CreateThumbnail(image, 136, 84);
                payload = new ClipboardPopupPayload(preview, preview, thumbnail);
                errorText = string.Empty;
                return true;
            }

            if (Clipboard.ContainsAudio())
            {
                const string preview = "Audio in Zwischenablage";
                payload = new ClipboardPopupPayload(preview, preview, null);
                errorText = string.Empty;
                return true;
            }

            const string unknownPreview = "Unbekanntes Zwischenablageformat";
            payload = new ClipboardPopupPayload(unknownPreview, unknownPreview, null);
            errorText = string.Empty;
            return true;
        }
        catch (ExternalException)
        {
            payload = new ClipboardPopupPayload(string.Empty, string.Empty, null);
            errorText = "Zwischenablage ist gerade gesperrt. Neuer Versuch beim naechsten Update.";
            return false;
        }
        catch (ThreadStateException)
        {
            payload = new ClipboardPopupPayload(string.Empty, string.Empty, null);
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
            _openUpdateMenuItem.Enabled = true;
            _openUpdateMenuItem.Text = $"Update öffnen ({release.TagName})";
            UpdateStatusText(release.Version > _currentVersion ? release.TagName : null);

            if (release.Version > _currentVersion)
            {
                ShowPopup("Update verfügbar", $"Neue Version {release.TagName} verfügbar (aktuell v{_currentVersion}).", PopupDurationMs);
                _notifyIcon.ShowBalloonTip(5000, "Update verfügbar", $"Neue Version {release.TagName} verfügbar.", ToolTipIcon.Info);
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

    private void ShowPopup(string title, string content, int durationMs, BitmapSource? thumbnail = null)
    {
        _popupWindow?.Close();
        _popupWindow = new WpfClipboardPopupWindow(title, content, durationMs, thumbnail);
        _popupWindow.Show();
    }

    private static BitmapSource? CreateThumbnail(Image image, int maxWidth, int maxHeight)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height);
        if (scale > 1)
        {
            scale = 1;
        }

        var thumbWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var thumbHeight = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var bitmap = new Bitmap(thumbWidth, thumbHeight, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(image, 0, 0, thumbWidth, thumbHeight);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
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

    private static Icon ResolveTrayIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
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

    private void OpenInstallDirectory()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    private void RestartAgent()
    {
        Application.Restart();
        ExitThread();
    }

    private void ExitWithoutRestart()
    {
        try
        {
            AgentControlState.SetPauseMode(true);
            _pauseModeMenuItem.Checked = true;
            UpdateStatusText();
            ExitThread();
        }
        catch (UnauthorizedAccessException)
        {
            _notifyIcon.ShowBalloonTip(2600, "ClipboardWatcher", "Pause-Modus konnte nicht gesetzt werden (keine Berechtigung).", ToolTipIcon.Warning);
        }
        catch (IOException)
        {
            _notifyIcon.ShowBalloonTip(2600, "ClipboardWatcher", "Pause-Modus konnte nicht gespeichert werden.", ToolTipIcon.Warning);
        }
    }

    private void TogglePauseMode()
    {
        var requestedState = !AgentControlState.IsPauseModeEnabled();
        try
        {
            AgentControlState.SetPauseMode(requestedState);
            _pauseModeMenuItem.Checked = requestedState;
            UpdateStatusText();
            var text = requestedState
                ? "Pause-Modus aktiviert. Der Dienst startet den Agenten nicht automatisch."
                : "Pause-Modus deaktiviert.";
            _notifyIcon.ShowBalloonTip(2400, "ClipboardWatcher", text, ToolTipIcon.Info);
        }
        catch (UnauthorizedAccessException)
        {
            _notifyIcon.ShowBalloonTip(2600, "ClipboardWatcher", "Pause-Modus konnte nicht gesetzt werden (keine Berechtigung).", ToolTipIcon.Warning);
        }
        catch (IOException)
        {
            _notifyIcon.ShowBalloonTip(2600, "ClipboardWatcher", "Pause-Modus konnte nicht gespeichert werden.", ToolTipIcon.Warning);
        }
    }

    private void UpdateStatusText(string? updateTag = null)
    {
        var isPaused = AgentControlState.IsPauseModeEnabled();
        if (isPaused)
        {
            _statusMenuItem.Text = $"Status: Pausiert (v{_currentVersion})";
            RefreshTrayIconState();
            return;
        }

        _statusMenuItem.Text = updateTag is null
            ? $"Status: Aktiv (v{_currentVersion})"
            : $"Status: Update verfügbar ({updateTag})";
        RefreshTrayIconState();
    }

    private void RefreshTrayIconState()
    {
        _notifyIcon.Icon = AgentControlState.IsPauseModeEnabled() ? _pausedIcon : _activeIcon;
    }

    private sealed class ClipboardPopupPayload
    {
        public string ChangeKey { get; }
        public string Content { get; }
        public BitmapSource? Thumbnail { get; }

        public ClipboardPopupPayload(string changeKey, string content, BitmapSource? thumbnail)
        {
            ChangeKey = changeKey;
            Content = content;
            Thumbnail = thumbnail;
        }
    }

    private sealed class MenuItemDefinition
    {
        public string Name { get; }
        public string Text { get; }
        public bool Enabled { get; }
        public EventHandler? OnClick { get; }
        public bool IsSeparator { get; }
        public bool IsCheckable { get; }
        public bool IsChecked { get; }
        public IReadOnlyList<MenuItemDefinition> Children { get; }

        public MenuItemDefinition(string name, string text, EventHandler? onClick = null, bool enabled = true, bool isCheckable = false, bool isChecked = false, IReadOnlyList<MenuItemDefinition>? children = null)
        {
            Name = name;
            Text = text;
            Enabled = enabled;
            OnClick = onClick;
            IsSeparator = false;
            IsCheckable = isCheckable;
            IsChecked = isChecked;
            Children = children ?? Array.Empty<MenuItemDefinition>();
        }

        private MenuItemDefinition()
        {
            Name = string.Empty;
            Text = string.Empty;
            IsSeparator = true;
            Enabled = true;
            IsCheckable = false;
            IsChecked = false;
            Children = Array.Empty<MenuItemDefinition>();
        }

        public static MenuItemDefinition Separator()
        {
            return new MenuItemDefinition();
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
