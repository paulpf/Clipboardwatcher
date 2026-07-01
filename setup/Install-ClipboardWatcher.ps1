param(
    [string]$SourcePath,
    [string]$InstallPath = "C:\Program Files\ClipboardWatcher",
    [string]$ServiceName = "ClipboardWatcherService"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bitte PowerShell als Administrator ausführen."
    }
}

function Get-ProjectRoot {
    $scriptFolder = Split-Path -Path $PSScriptRoot -Parent
    return $scriptFolder
}

function Ensure-SourcePath {
    param([string]$PathFromUser)

    if (-not [string]::IsNullOrWhiteSpace($PathFromUser)) {
        if (-not (Test-Path -Path $PathFromUser)) {
            throw "SourcePath '$PathFromUser' existiert nicht."
        }

        return (Resolve-Path -Path $PathFromUser).Path
    }

    $projectRoot = Get-ProjectRoot
    $publishPath = Join-Path $projectRoot "ClipboardWatcher.Service\bin\Release\net8.0-windows\win-x64\publish"
    if (-not (Test-Path -Path $publishPath)) {
        Write-Host "Kein vorhandener Publish-Ordner gefunden. Erzeuge Publish-Build..."
        Push-Location $projectRoot
        try {
            dotnet publish ".\ClipboardWatcher.Service\ClipboardWatcher.Service.csproj" -c Release -r win-x64 --self-contained false
        }
        finally {
            Pop-Location
        }
    }

    if (-not (Test-Path -Path $publishPath)) {
        throw "Publish-Verzeichnis wurde nicht erzeugt: $publishPath"
    }

    return (Resolve-Path -Path $publishPath).Path
}

function Stop-And-Delete-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stoppe bestehenden Dienst '$Name'..."
        Stop-Service -Name $Name -Force
    }

    Write-Host "Entferne bestehenden Dienst '$Name'..."
    & sc.exe delete $Name | Out-Null
    Start-Sleep -Seconds 1
}

Assert-Administrator

$resolvedSourcePath = Ensure-SourcePath -PathFromUser $SourcePath
$serviceExecutable = Join-Path $resolvedSourcePath "ClipboardWatcher.Service.exe"
if (-not (Test-Path -Path $serviceExecutable)) {
    throw "Dienst-Executable nicht gefunden: $serviceExecutable"
}

Stop-And-Delete-ServiceIfExists -Name $ServiceName

if (-not (Test-Path -Path $InstallPath)) {
    New-Item -Path $InstallPath -ItemType Directory | Out-Null
}

Write-Host "Kopiere Dateien nach '$InstallPath'..."
Copy-Item -Path (Join-Path $resolvedSourcePath "*") -Destination $InstallPath -Recurse -Force

$installedServiceExecutable = Join-Path $InstallPath "ClipboardWatcher.Service.exe"
Write-Host "Erstelle Dienst '$ServiceName'..."
& sc.exe create $ServiceName binPath= "`"$installedServiceExecutable`"" start= auto | Out-Null

Write-Host "Starte Dienst '$ServiceName'..."
Start-Service -Name $ServiceName

Write-Host "Installation abgeschlossen."
Write-Host "Installationspfad: $InstallPath"
Write-Host "Service: $ServiceName"
Write-Host "Hinweis: Update-Check nutzt standardmäßig das GitHub-Repo 'paulpf/Clipboardwatcher'."
Write-Host "Setze optional CLIPBOARDWATCHER_UPDATE_REPOSITORY (z.B. 'owner/repo')."
