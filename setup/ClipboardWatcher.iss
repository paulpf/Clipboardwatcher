; Professional installer package for ClipboardWatcher
; Build with: ISCC.exe /DSourceDir="C:\path\to\publish" /DAppVersion="0.1.2" setup\ClipboardWatcher.iss

#ifndef SourceDir
  #error SourceDir is not defined. Pass /DSourceDir=...
#endif

#ifndef AppVersion
  #define AppVersion "0.1.2"
#endif

[Setup]
AppId={{0E6E8E61-CC70-4A84-9AF7-DB39E9FEEAD7}
AppName=ClipboardWatcher
AppVersion={#AppVersion}
AppVerName=ClipboardWatcher {#AppVersion}
AppPublisher=ClipboardWatcher
AppPublisherURL=https://github.com/paulpf/Clipboardwatcher
AppSupportURL=https://github.com/paulpf/Clipboardwatcher/issues
AppUpdatesURL=https://github.com/paulpf/Clipboardwatcher/releases
DefaultDirName={autopf}\ClipboardWatcher
DefaultGroupName=ClipboardWatcher
OutputDir=..\artifacts\installer
OutputBaseFilename=ClipboardWatcher-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\ClipboardWatcher.Agent.exe
UninstallDisplayName=ClipboardWatcher
WizardStyle=modern
SetupIconFile={#SourceDir}\ClipboardWatcher.Agent.exe
VersionInfoCompany=ClipboardWatcher
VersionInfoDescription=Clipboard monitoring service and tray agent
VersionInfoProductName=ClipboardWatcher
VersionInfoVersion={#AppVersion}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostartservice"; Description: "ClipboardWatcher-Service nach Installation starten"; GroupDescription: "Dienstoptionen"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ClipboardWatcher Agent starten"; Filename: "{app}\ClipboardWatcher.Agent.exe"
Name: "{group}\ClipboardWatcher deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClipboardWatcher Agent"; Filename: "{app}\ClipboardWatcher.Agent.exe"; Tasks: desktopicon

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create ClipboardWatcherService binPath= ""{app}\ClipboardWatcher.Service.exe"" start= auto"; Flags: runhidden waituntilterminated; StatusMsg: "Erstelle Windows-Dienst..."
Filename: "{sys}\sc.exe"; Parameters: "description ClipboardWatcherService ""ClipboardWatcher Service startet den Tray-Agent fuer Clipboard-Benachrichtigungen."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start ClipboardWatcherService"; Flags: runhidden waituntilterminated; Tasks: autostartservice; StatusMsg: "Starte Windows-Dienst..."
Filename: "{app}\ClipboardWatcher.Agent.exe"; Description: "ClipboardWatcher Agent jetzt starten"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ClipboardWatcherService"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete ClipboardWatcherService"; Flags: runhidden waituntilterminated skipifdoesntexist
