param(
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

Assert-Administrator

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stoppe Dienst '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
    }

    Write-Host "Entferne Dienst '$ServiceName'..."
    & sc.exe delete $ServiceName | Out-Null
}

if (Test-Path -Path $InstallPath) {
    Write-Host "Entferne Installationsverzeichnis '$InstallPath'..."
    Remove-Item -Path $InstallPath -Recurse -Force
}

Write-Host "Deinstallation abgeschlossen."
