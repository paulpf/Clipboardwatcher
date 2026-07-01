param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "0.1.2"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-IsccAvailable {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($null -ne $iscc) {
        return $iscc.Source
    }

    $fallbackPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $fallbackPaths) {
        if (Test-Path -Path $path) {
            return $path
        }
    }

    throw "Inno Setup Compiler (iscc.exe) wurde nicht gefunden. Bitte Inno Setup installieren und iscc.exe in PATH aufnehmen."
}

$projectRoot = Split-Path -Path $PSScriptRoot -Parent
$publishDir = Join-Path $projectRoot "artifacts\publish"
$installerScript = Join-Path $PSScriptRoot "ClipboardWatcher.iss"

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -Path $publishDir -ItemType Directory | Out-Null

Push-Location $projectRoot
try {
    dotnet publish ".\ClipboardWatcher.Service\ClipboardWatcher.Service.csproj" -c $Configuration -r $RuntimeIdentifier --self-contained false -o $publishDir | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish (Service) fehlgeschlagen (ExitCode $LASTEXITCODE)."
    }

    dotnet publish ".\ClipboardWatcher.Agent\ClipboardWatcher.Agent.csproj" -c $Configuration -r $RuntimeIdentifier --self-contained false -o $publishDir | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish (Agent) fehlgeschlagen (ExitCode $LASTEXITCODE)."
    }

    $isccPath = Assert-IsccAvailable
    & $isccPath "/DSourceDir=$publishDir" "/DAppVersion=$Version" $installerScript | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC Build fehlgeschlagen (ExitCode $LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

Write-Host "Installer erfolgreich erstellt unter: $projectRoot\artifacts\installer"
