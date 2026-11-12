<#
.SYNOPSIS
    Installs the latest Hey Ganesh release for the current user, in one command.

.DESCRIPTION
    Downloads HeyGanesh-win-x64.zip from this repository's latest GitHub Release into a temporary folder, unpacks
    it, runs the package's own install.ps1 (per user, no admin, see that script for exactly what it does) and
    deletes the temporary folder. Read it before you run it: it is short.

    Anything after -ZipPath is handed to install.ps1 unchanged (for example -Target). -ZipPath installs from a zip
    you already have instead of downloading one; the install checks use it.

.EXAMPLE
    irm https://raw.githubusercontent.com/PranavJha-3000/HeyGanesh/main/installer/install-online.ps1 | iex
#>
param(
    [string]$ZipPath,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$InstallArguments
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Windows PowerShell 5.1 downloads many times slower with its progress bar on
$url = 'https://github.com/PranavJha-3000/HeyGanesh/releases/latest/download/HeyGanesh-win-x64.zip'
$work = Join-Path ([IO.Path]::GetTempPath()) ('heyganesh-install-' + [guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $work | Out-Null
    $zip = Join-Path $work 'HeyGanesh-win-x64.zip'

    if ($ZipPath) {
        Copy-Item -LiteralPath $ZipPath -Destination $zip
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-Host 'Downloading Hey Ganesh (about 75 MB)...'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    }

    $package = Join-Path $work 'package'
    Expand-Archive -LiteralPath $zip -DestinationPath $package
    $install = Join-Path $package 'install.ps1'
    if (-not (Test-Path $install)) {
        throw 'REFUSED: the download has no install.ps1, so nothing was installed.' # not exit: under irm | iex, exit would close the person's terminal
    }

    # A new process, like the README's manual route, so a policy that blocks scripts does not matter here.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $install @InstallArguments
    if ($LASTEXITCODE -ne 0) { throw "install.ps1 stopped with exit code $LASTEXITCODE (its message is above); nothing else was changed." }
    Write-Host 'Open "Hey Ganesh" from the Start menu, then add your keys or sign in to a plan in Settings.'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
