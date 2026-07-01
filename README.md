# ClipboardWatcher

[![Build Installer](https://github.com/paulpf/Clipboardwatcher/actions/workflows/build-installer.yml/badge.svg)](https://github.com/paulpf/Clipboardwatcher/actions/workflows/build-installer.yml)

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

### Professioneller Installer (Wizard/MSI-ähnlich)

Für eine klassische Endkunden-Installation (Wizard, Startmenü, Deinstallation) ist ein Inno-Setup-Projekt enthalten:

- `setup\ClipboardWatcher.iss`
- `setup\Build-Installer.ps1`

Voraussetzung: **Inno Setup** (iscc.exe im PATH)

Installer bauen:

```powershell
.\setup\Build-Installer.ps1 -Version "0.1.2"
```

Ergebnis:

- `.\artifacts\installer\ClipboardWatcher-Setup-<Version>.exe`

Der Installer erstellt:

- Startmenü-Einträge unter **Programme > ClipboardWatcher**
- einen vollständigen Eintrag in **Apps/Programme & Features** (Publisher, Support-, Update-Links)

### One-Click Build in GitHub Actions

Es gibt einen Workflow unter:

- `.github/workflows/build-installer.yml`

Der Workflow:

- baut den Installer manuell via **workflow_dispatch**
- baut automatisch bei **Release published**
- lädt die `.exe` als Artifact hoch
- hängt die `.exe` bei Release-Events direkt am Release an

### Installation

```powershell
.\setup\Install-ClipboardWatcher.ps1
```

Optional mit eigenem Installationspfad:

```powershell
.\setup\Install-ClipboardWatcher.ps1 -InstallPath "C:\Services\ClipboardWatcher"
```

Für ein garantiert frisches Deploy (immer neues `dotnet publish`):

```powershell
.\setup\Install-ClipboardWatcher.ps1 -ForcePublish
```

Bei `-ForcePublish` wird der bestehende Publish-Ordner vor dem Build gelöscht.

### Deinstallation

```powershell
.\setup\Uninstall-ClipboardWatcher.ps1
```

## Automatische Update-Erkennung

- Der Agent prüft beim Start und danach alle 6 Stunden das neueste GitHub-Release.
- Wenn eine neuere Version gefunden wird, erscheint eine Benachrichtigung mit Link zum Release.
- Über das Tray-Icon ist zusätzlich **"Auf Updates prüfen"** verfügbar.
- Während der Laufzeit ist dauerhaft ein Tray-/Taskleisten-Icon sichtbar.
- Der Agent nutzt ein eigenes ClipboardWatcher-Icon für Tray/Task-Info.
- Clipboard-Aenderungen werden als eigenes WPF-In-App-Popup unten rechts eingeblendet (unabhängig von Windows-Benachrichtigungseinstellungen).
- Bilder aus der Zwischenablage werden im Popup als Thumbnail angezeigt.
- Das Kontextmenü ist modular aufgebaut und enthält Basisfunktionen:
  - Statusanzeige
  - Pause-Modus (kein Auto-Neustart durch den Dienst)
  - Auf Updates prüfen
  - Update öffnen
  - Installationsordner öffnen
  - Agent neu starten
  - Nur Agent beenden (ohne Neustart)
  - Beenden

Standard-Repository für Update-Prüfung:

- `paulpf/Clipboardwatcher`

Anpassbar über Umgebungsvariable:

```powershell
setx CLIPBOARDWATCHER_UPDATE_REPOSITORY "owner/repo"
```

## Wichtiger Hinweis

Ein Windows-Dienst selbst kann keine UI im Benutzerdesktop anzeigen.  
Darum startet der Dienst den Agenten in der aktiven Benutzer-Session, damit das Popup sichtbar ist.
