# Removes STLTH Recorder without running unins000.exe.
#
# Why this exists: the uninstaller Inno Setup generates is an unsigned executable, and
# on machines with Smart App Control enabled Windows refuses to run it outright — there
# is no "run anyway" for that policy. Installing works, uninstalling does not. Until the
# binaries are signed, this script is the way out: PowerShell scripts are governed by
# the execution policy, not by the binary reputation check that blocks the uninstaller.
#
# By default it removes only what the installer put there. Recordings and the
# transcription models belong to you, not to the installer, so they survive unless you
# ask for them explicitly.
#
#   .\uninstall.ps1                      program files, shortcut, registry entries
#   .\uninstall.ps1 -RemoveModels        also the ~1 GB of recognition models
#   .\uninstall.ps1 -RemoveRecordings    also every recorded session
#   .\uninstall.ps1 -WhatIf              show what would go, delete nothing

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemoveModels,
    [switch]$RemoveRecordings
)

$ErrorActionPreference = 'Stop'

$appName   = 'STLTH Recorder'
$appDir    = Join-Path $env:LOCALAPPDATA $appName
$appId     = '{9C2F5A31-6B4E-4E7B-9C1D-2F8A4E5B7C10}_is1'
$uninstall = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"
$runKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut  = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$appName"
$settings  = Join-Path $appDir 'settings.json'
$models    = Join-Path $appDir 'models'
$sessions  = Join-Path $appDir 'Sessions'

function Show-Size($path) {
    if (-not (Test-Path $path)) { return '' }
    $bytes = (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |
              Measure-Object Length -Sum).Sum
    if (-not $bytes) { return '' }
    return ('  ({0:N0} MB)' -f ($bytes / 1MB))
}

Write-Host "== stopping the app ==" -ForegroundColor Cyan
Get-Process -Name $appName -ErrorAction SilentlyContinue | ForEach-Object {
    if ($PSCmdlet.ShouldProcess("PID $($_.Id)", 'stop')) { $_ | Stop-Process -Force }
}
Start-Sleep -Milliseconds 500

Write-Host "== program files ==" -ForegroundColor Cyan
if (Test-Path $appDir) {
    # Everything except the folders that hold your data.
    Get-ChildItem $appDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $settings } |
        ForEach-Object {
            Write-Host "  $($_.Name)"
            if ($PSCmdlet.ShouldProcess($_.FullName, 'delete')) { Remove-Item $_.FullName -Force }
        }

    $whisper = Join-Path $appDir 'whisper'
    if (Test-Path $whisper) {
        Write-Host "  whisper$(Show-Size $whisper)"
        if ($PSCmdlet.ShouldProcess($whisper, 'delete')) { Remove-Item $whisper -Recurse -Force }
    }
} else {
    Write-Host '  nothing installed'
}

Write-Host "== shortcut and registry ==" -ForegroundColor Cyan
if (Test-Path $shortcut) {
    Write-Host "  $shortcut"
    if ($PSCmdlet.ShouldProcess($shortcut, 'delete')) { Remove-Item $shortcut -Recurse -Force }
}

foreach ($key in @($uninstall, "HKCU:\Software\$appName")) {
    if (Test-Path $key) {
        Write-Host "  $key"
        if ($PSCmdlet.ShouldProcess($key, 'delete')) { Remove-Item $key -Recurse -Force }
    }
}

if ((Get-ItemProperty $runKey -Name $appName -ErrorAction SilentlyContinue)) {
    Write-Host "  autostart entry"
    if ($PSCmdlet.ShouldProcess('autostart', 'delete')) {
        Remove-ItemProperty $runKey -Name $appName -Force
    }
}

Write-Host "== your data ==" -ForegroundColor Cyan

if ($RemoveModels -and (Test-Path $models)) {
    Write-Host "  models$(Show-Size $models)"
    if ($PSCmdlet.ShouldProcess($models, 'delete')) { Remove-Item $models -Recurse -Force }
} elseif (Test-Path $models) {
    Write-Host "  models kept$(Show-Size $models) — pass -RemoveModels to delete"
}

if ($RemoveRecordings -and (Test-Path $sessions)) {
    Write-Host "  recordings$(Show-Size $sessions)"
    if ($PSCmdlet.ShouldProcess($sessions, 'delete')) { Remove-Item $sessions -Recurse -Force }
} elseif (Test-Path $sessions) {
    Write-Host "  recordings kept$(Show-Size $sessions) — pass -RemoveRecordings to delete"
}

if ($RemoveModels -and $RemoveRecordings) {
    if ((Test-Path $settings) -and $PSCmdlet.ShouldProcess($settings, 'delete')) {
        Remove-Item $settings -Force
    }
    if ((Test-Path $appDir) -and -not (Get-ChildItem $appDir -Force -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess($appDir, 'delete')) { Remove-Item $appDir -Force }
    }
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
