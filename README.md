# ClipboardWatcher

Windows-Dienst auf .NET, der einen Benutzer-Agenten startet.  
Der Agent ueberwacht die Zwischenablage und zeigt bei Aenderungen ein kurzes Popup unten rechts (Balloon-Notification).
Zusätzlich prüft der Agent regelmäßig, ob es auf GitHub ein neueres Release gibt.

## Projektstruktur

- `ClipboardWatcher.Service`  
  Windows-Dienst (Worker Service), der den Agenten in der aktiven Benutzer-Session startet.
- `ClipboardWatcher.Agent`  
  WinForms-Hintergrundprozess ohne sichtbares Fenster, der Clipboard-Events verarbeitet und Popups anzeigt.
- `ClipboardWatcher.Core`  
  Gemeinsame Hilfslogik.

## Build

```powershell
dotnet build .\ClipboardWatcher.slnx
```

## Veroeffentlichen

```powershell
dotnet publish .\ClipboardWatcher.Service\ClipboardWatcher.Service.csproj -c Release -r win-x64 --self-contained false
```

Die Publish-Pipeline des Diensts veroeffentlicht den Agenten automatisch in denselben Ordner.

## Setup (Installation/Deinstallation)

Die Setup-Skripte liegen unter `.\setup`.

### Installation

```powershell
.\setup\Install-ClipboardWatcher.ps1
```

Optional mit eigenem Installationspfad:

```powershell
.\setup\Install-ClipboardWatcher.ps1 -InstallPath "C:\Services\ClipboardWatcher"
```

### Deinstallation

```powershell
.\setup\Uninstall-ClipboardWatcher.ps1
```

## Automatische Update-Erkennung

- Der Agent prüft beim Start und danach alle 6 Stunden das neueste GitHub-Release.
- Wenn eine neuere Version gefunden wird, erscheint eine Benachrichtigung mit Link zum Release.
- Über das Tray-Icon ist zusätzlich **"Auf Updates prüfen"** verfügbar.

Standard-Repository für Update-Prüfung:

- `paulpf/Clipboardwatcher`

Anpassbar über Umgebungsvariable:

```powershell
setx CLIPBOARDWATCHER_UPDATE_REPOSITORY "owner/repo"
```

## Wichtiger Hinweis

Ein Windows-Dienst selbst kann keine UI im Benutzerdesktop anzeigen.  
Darum startet der Dienst den Agenten in der aktiven Benutzer-Session, damit das Popup sichtbar ist.
