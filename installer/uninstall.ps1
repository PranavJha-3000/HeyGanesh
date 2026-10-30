<#
.SYNOPSIS
    Removes the per-user Hey Ganesh install (DEC-055).

.DESCRIPTION
    Removes what install.ps1 added: the install folder, the Start-menu shortcut and the Apps & features
    entry, plus the start-at-login value if it starts this install (one that starts another copy is left).
    It lives in the install folder; Apps & features runs it from there.

    Kept on purpose: your keys in Windows Credential Manager (remove them first with the tray's Keys... if you
    want them gone) and the logs in %LOCALAPPDATA%\HeyGanesh.

    Exit codes: 0 removed, 1 refused (nothing changed) or failed.
#>
param(
    [string]$Target = $PSScriptRoot,
    [string]$Shortcuts = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
    [string]$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HeyGanesh',
    [string]$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
)

$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($Target)
$exe = Join-Path $target 'HeyGanesh.Tray.exe'

# Only a folder install.ps1 made is ever deleted.
if (-not (Test-Path (Join-Path $target '.heyganesh-install'))) {
    Write-Host "REFUSED: $target is not a Hey Ganesh install (no .heyganesh-install); nothing was changed."
    exit 1
}

$running = @(Get-Process -Name 'HeyGanesh.Tray' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($running.Count -gt 0) {
    Write-Host "REFUSED: Hey Ganesh is running from $target. Quit it from its tray icon, then uninstall again."
    exit 1
}

# Start at login (DEC-054): removed only when it starts this install.
$run = (Get-ItemProperty -Path $RunKey -Name 'HeyGanesh' -ErrorAction SilentlyContinue).HeyGanesh
if ($run -and [string]::Equals($run, "`"$exe`"", [StringComparison]::OrdinalIgnoreCase)) {
    Remove-ItemProperty -Path $RunKey -Name 'HeyGanesh'
    Write-Host "removed start at login"
}
elseif ($run) {
    Write-Host "left start at login alone: it starts another copy ($run)"
}

$linkPath = Join-Path $Shortcuts 'Hey Ganesh.lnk'
if (Test-Path $linkPath) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($linkPath)
    if ([string]::Equals($link.TargetPath, $exe, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $linkPath -Force
        Write-Host "removed the Start-menu shortcut"
    }
}

# This script may be running from inside the folder it is about to delete. The folder goes before the Apps &
# features entry: if a file is still locked, the entry stays, so the uninstall can be run again from Settings.
Set-Location -LiteralPath $env:TEMP
try {
    Remove-Item -LiteralPath $target -Recurse -Force
}
catch {
    Write-Host "FAILED: $target could not be removed completely ($($_.Exception.Message)); the Apps & features entry was kept, so run the uninstall again."
    exit 1
}

$location = (Get-ItemProperty -Path $UninstallKey -Name 'InstallLocation' -ErrorAction SilentlyContinue).InstallLocation
if ($location -and [string]::Equals($location, $target, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -Path $UninstallKey -Recurse -Force
    Write-Host "removed the Apps & features entry"
}

Write-Host "UNINSTALLED: $target removed. Your keys (Credential Manager) and logs (%LOCALAPPDATA%\HeyGanesh) were kept."
exit 0
