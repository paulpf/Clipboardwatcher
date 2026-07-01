param(
    [string]$SourcePath,
    [string]$InstallPath = "C:\Program Files\ClipboardWatcher",
    [string]$ServiceName = "ClipboardWatcherService",
    [switch]$ForcePublish
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
    param(
        [string]$PathFromUser,
        [bool]$PublishAlways
    )

    if (-not [string]::IsNullOrWhiteSpace($PathFromUser)) {
        if (-not (Test-Path -Path $PathFromUser)) {
            throw "SourcePath '$PathFromUser' existiert nicht."
        }

        $resolvedUserPath = (Resolve-Path -Path $PathFromUser).Path
        $userServiceExe = Join-Path $resolvedUserPath "ClipboardWatcher.Service.exe"
        $userAgentExe = Join-Path $resolvedUserPath "ClipboardWatcher.Agent.exe"
        if (-not (Test-Path -Path $userServiceExe)) {
            throw "Service-Executable fehlt im SourcePath: $userServiceExe"
        }
        if (-not (Test-Path -Path $userAgentExe)) {
            throw "Agent-Executable fehlt im SourcePath: $userAgentExe"
        }

        return $resolvedUserPath
    }

    $projectRoot = Get-ProjectRoot
    $publishPath = Join-Path $projectRoot "ClipboardWatcher.Service\bin\Release\net8.0-windows\win-x64\publish"
    $servicePublishExe = Join-Path $publishPath "ClipboardWatcher.Service.exe"
    $agentPublishExe = Join-Path $publishPath "ClipboardWatcher.Agent.exe"
    $publishNeedsRefresh = (-not (Test-Path -Path $publishPath)) -or
        (-not (Test-Path -Path $servicePublishExe)) -or
        (-not (Test-Path -Path $agentPublishExe))
    if ($PublishAlways) {
        if (Test-Path -Path $publishPath) {
            Write-Host "ForcePublish aktiv. Lösche vorhandenen Publish-Ordner '$publishPath'..."
            Remove-Item -Path $publishPath -Recurse -Force
        }
        $publishNeedsRefresh = $true
    }

    if ($publishNeedsRefresh) {
        if ($PublishAlways) {
            Write-Host "ForcePublish aktiv. Erzeuge frischen Publish-Build..."
        }
        else {
            Write-Host "Kein vorhandener Publish-Ordner gefunden. Erzeuge Publish-Build..."
        }

        Push-Location $projectRoot
        try {
            dotnet publish ".\ClipboardWatcher.Service\ClipboardWatcher.Service.csproj" -c Release -r win-x64 --self-contained false | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish ist mit ExitCode $LASTEXITCODE fehlgeschlagen."
            }
        }
        finally {
            Pop-Location
        }
    }

    if (-not (Test-Path -Path $publishPath)) {
        throw "Publish-Verzeichnis wurde nicht erzeugt: $publishPath"
    }
    if (-not (Test-Path -Path $servicePublishExe)) {
        throw "Service-Executable fehlt im Publish-Verzeichnis: $servicePublishExe"
    }
    if (-not (Test-Path -Path $agentPublishExe)) {
        throw "Agent-Executable fehlt im Publish-Verzeichnis: $agentPublishExe"
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
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $stillThere = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $stillThere) {
            return
        }
    }
}

Assert-Administrator

$resolvedSourcePath = Ensure-SourcePath -PathFromUser $SourcePath -PublishAlways $ForcePublish
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

$started = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    $serviceState = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $serviceState -and $serviceState.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
        $started = $true
        break
    }
}

if (-not $started) {
    $recent = Get-WinEvent -LogName Application -MaxEvents 30 |
        Where-Object { $_.ProviderName -in @($ServiceName, "ClipboardWatcher.Service", ".NET Runtime", "Application Error") } |
        Select-Object -First 5 TimeCreated, ProviderName, LevelDisplayName, Message
    $recentText = ($recent | Format-List | Out-String)
    throw "Dienst '$ServiceName' wurde nach Installation nicht gestartet.`n$recentText"
}

Write-Host "Installation abgeschlossen."
Write-Host "Installationspfad: $InstallPath"
Write-Host "Service: $ServiceName"
Write-Host "Hinweis: Update-Check nutzt standardmäßig das GitHub-Repo 'paulpf/Clipboardwatcher'."
Write-Host "Setze optional CLIPBOARDWATCHER_UPDATE_REPOSITORY (z.B. 'owner/repo')."
