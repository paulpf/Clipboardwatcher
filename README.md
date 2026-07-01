# ClipboardWatcher

Windows-Dienst auf .NET, der einen Benutzer-Agenten startet.  
Der Agent ueberwacht die Zwischenablage und zeigt bei Aenderungen ein kurzes Popup unten rechts (Balloon-Notification).

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

## Installation als Windows-Dienst

1. Publish-Verzeichnis nach z. B. `C:\Services\ClipboardWatcher` kopieren.
2. Dienst als Administrator anlegen:

```powershell
sc.exe create ClipboardWatcherService binPath= "\"C:\Services\ClipboardWatcher\ClipboardWatcher.Service.exe\"" start= auto
```

3. Dienst starten:

```powershell
sc.exe start ClipboardWatcherService
```

## Hinweis

Ein Windows-Dienst selbst kann keine UI im Benutzerdesktop anzeigen.  
Darum startet der Dienst den Agenten in der aktiven Benutzer-Session, damit das Popup sichtbar ist.
