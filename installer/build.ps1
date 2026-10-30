<#
.SYNOPSIS
    Builds the Hey Ganesh per-user installer package (DEC-055).

.DESCRIPTION
    Publishes the tray (src/Experience/HeyGanesh.Tray) self-contained for win-x64, so the target PC needs no
    .NET runtime, into <Out>\app, and puts install.ps1 and uninstall.ps1 beside it. The package is that folder:
    copy it to the PC and run install.ps1 there. Nothing is signed, and nothing needs admin rights.

    The first run downloads the .NET runtime packs from nuget.org into the NuGet cache; later runs use the cache.

    Exit codes: 0 built, 1 refused or incomplete, otherwise dotnet publish's own code.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1
#>
param(
    # Where the package is written. Replaced on every build, so it must be empty or a previous package.
    [string]$Out = (Join-Path $PSScriptRoot '..\artifacts\installer'),
    [string]$Configuration = 'Release',
    # Also write HeyGanesh-win-x64.zip next to the package folder, for handing to someone.
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'src\Experience\HeyGanesh.Tray\HeyGanesh.Tray.csproj'
$out = [IO.Path]::GetFullPath($Out)
$marker = Join-Path $out 'package.txt'

# Only an empty folder or a previous package is ever deleted: a mistyped -Out must not wipe someone's files.
if (Test-Path $out) {
    $existing = @(Get-ChildItem -LiteralPath $out -Force)
    if ($existing.Count -gt 0 -and -not (Test-Path $marker)) {
        Write-Host "REFUSED: $out is not empty and is not a Hey Ganesh package (no package.txt); nothing was changed."
        exit 1
    }
    Remove-Item -LiteralPath $out -Recurse -Force
}

$app = Join-Path $out 'app'
Write-Host "publishing the tray, self-contained win-x64, $Configuration, into $app"
# DebugType=none: no .pdb files in what people install.
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:DebugType=none -o $app
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet publish failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $out
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $out
# The installed copy of uninstall.ps1 lives in the install folder, so Apps & features can run it from there.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $app

# A package that is missing any of these would install but not run: the runtime (self-contained), WinForms
# for the tray, the lesson cards' page, and the WebView2 loader.
$required = @(
    'HeyGanesh.Tray.exe', 'HeyGanesh.Tray.dll', 'HeyGanesh.App.dll', 'coreclr.dll', 'System.Windows.Forms.dll',
    'Microsoft.Web.WebView2.Core.dll', 'uninstall.ps1'
)
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $app $_)) })
if (-not (Test-Path (Join-Path $app 'LessonPage'))) { $missing += 'LessonPage\' }
if (-not (Get-ChildItem -LiteralPath $app -Recurse -Filter 'WebView2Loader.dll' -ErrorAction SilentlyContinue)) { $missing += 'WebView2Loader.dll' }
if ($missing.Count -gt 0) {
    Write-Host "INCOMPLETE: the package lacks $($missing -join ', ')"
    exit 1
}

$version = (Get-Item (Join-Path $app 'HeyGanesh.Tray.exe')).VersionInfo.FileVersion
$files = @(Get-ChildItem -LiteralPath $app -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Set-Content -LiteralPath $marker -Encoding ASCII -Value @(
    'Hey Ganesh installer package (DEC-055)',
    "version: $version",
    "built: $((Get-Date).ToString('yyyy-MM-dd HH:mm'))",
    "files: $($files.Count), $([math]::Round($bytes / 1MB)) MB",
    'install: powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1'
)

if ($Zip) {
    $zipPath = "$out-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath
    Write-Host "zip: $zipPath"
}

Write-Host "PACKAGE OK: $out (version $version, $($files.Count) files, $([math]::Round($bytes / 1MB)) MB)"
exit 0
