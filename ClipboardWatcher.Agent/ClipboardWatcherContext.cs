using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using ClipboardWatcher.Core;

namespace ClipboardWatcher.Agent;

internal sealed class ClipboardWatcherContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ClipboardMessageWindow _messageWindow;
    private string? _lastPreview;
    private string? _lastError;

    public ClipboardWatcherContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "ClipboardWatcher",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        _notifyIcon.ContextMenuStrip = menu;

        _messageWindow = new ClipboardMessageWindow(OnClipboardUpdated);
    }

    protected override void ExitThreadCore()
    {
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
