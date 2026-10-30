<#
.SYNOPSIS
    Installs Hey Ganesh for the current user (DEC-055).

.DESCRIPTION
    Copies the package's app folder to %LOCALAPPDATA%\Programs\HeyGanesh, adds a "Hey Ganesh" Start-menu
    shortcut, and registers it under Apps & features for this user (HKCU), so it can be removed from Settings.
    No admin rights. It does not start the app and does not turn on start at login: both are your choice,
    from the Start menu and from the tray's menu.

    Running it again over an existing install upgrades it: the old files are replaced. Keys in Windows
    Credential Manager and the logs in %LOCALAPPDATA%\HeyGanesh are not touched.

    Exit codes: 0 installed, 1 refused (nothing changed) or failed.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot 'app'),
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\HeyGanesh'),
    [string]$Shortcuts = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
    [string]$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HeyGanesh'
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($Source)
$target = [IO.Path]::GetFullPath($Target)
$exeName = 'HeyGanesh.Tray.exe'
$installMarker = '.heyganesh-install'

if (-not (Test-Path (Join-Path $source $exeName))) {
    Write-Host "REFUSED: $source has no $exeName; run install.ps1 from a package built by installer\build.ps1."
    exit 1
}

# A running tray holds its files open; replacing them under it would half-upgrade it.
$running = @(Get-Process -Name 'HeyGanesh.Tray' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($running.Count -gt 0) {
    Write-Host "REFUSED: Hey Ganesh is running from $target. Quit it from its tray icon, then run install.ps1 again."
    exit 1
}

# Only an empty folder or an earlier Hey Ganesh install is ever replaced.
if (Test-Path $target) {
    $existing = @(Get-ChildItem -LiteralPath $target -Force)
    if ($existing.Count -gt 0 -and -not (Test-Path (Join-Path $target $installMarker))) {
        Write-Host "REFUSED: $target is not empty and is not a Hey Ganesh install; nothing was changed."
        exit 1
    }
    if ($existing.Count -gt 0) {
        Write-Host "upgrading the install in $target"
        Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force
    }
}
else {
    New-Item -ItemType Directory -Path $target | Out-Null
}

Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
Set-Content -LiteralPath (Join-Path $target $installMarker) -Encoding ASCII -Value 'Hey Ganesh per-user install (DEC-055). uninstall.ps1 removes this folder.'
$exe = Join-Path $target $exeName
$version = (Get-Item $exe).VersionInfo.FileVersion

if (-not (Test-Path $Shortcuts)) { New-Item -ItemType Directory -Path $Shortcuts | Out-Null }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $Shortcuts 'Hey Ganesh.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.IconLocation = "$exe,0"
$link.Description = 'Hey Ganesh desktop tutor: hold Ctrl+Alt to ask'
$link.Save()

$sizeKb = [int](((Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1KB)
New-Item -Path $UninstallKey -Force | Out-Null
$uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $target 'uninstall.ps1')`""
$values = [ordered]@{
    DisplayName     = 'Hey Ganesh'
    DisplayVersion  = $version
    Publisher       = 'Hey Ganesh (local build)'
    InstallLocation = $target
    DisplayIcon     = "$exe,0"
    UninstallString = $uninstall
}
foreach ($name in $values.Keys) { Set-ItemProperty -Path $UninstallKey -Name $name -Value $values[$name] }
Set-ItemProperty -Path $UninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $UninstallKey -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty -Path $UninstallKey -Name EstimatedSize -Value $sizeKb -Type DWord

Write-Host "INSTALLED: Hey Ganesh $version in $target"
Write-Host "  Start it from the Start menu (Hey Ganesh). Its icon appears in the notification area;"
Write-Host "  the key screen opens by itself if a key is missing. Start at login is in the icon's menu."
Write-Host "  Remove it from Settings > Apps > Installed apps, or run $(Join-Path $target 'uninstall.ps1')."
exit 0
