#--------------------------------------------------------------
# Scan-forbidden.ps1 — HeyGanesh CI
# Detects prohibited actuation APIs in source.
# Failure output contains only file, line, rule — never values.
#--------------------------------------------------------------
param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# Forbidden API patterns (regex)
$forbiddenPatterns = @(
    @{ Rule = 'SENDINPUT'; Pattern = '\bSendInput\b' },
    @{ Rule = 'KEYBD_EVENT'; Pattern = '\bkeybd_event\b' },
    @{ Rule = 'MOUSE_EVENT'; Pattern = '\bmouse_event\b' },
    @{ Rule = 'SETCURSORPOS'; Pattern = '\bSetCursorPos\b' },
    @{ Rule = 'SETFOREGROUNDWINDOW'; Pattern = '\bSetForegroundWindow\b' },
    @{ Rule = 'SETFOCUS'; Pattern = '\bSetFocus\b' },
    @{ Rule = 'SENDKEYS'; Pattern = '\[System\.Windows\.Forms\]::SendKeys' }
)

# Allowed contexts (comments, declarations only)
function Test-IsCommentOrDeclaration {
    param([string]$Line, [string]$FilePath)
    $trimmed = $Line.Trim()
    # Skip pure comments
    if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('#') -or $trimmed.StartsWith('///')) { return $true }
    # Skip P/Invoke declarations (extern, PreserveSig, DllImport)
    if ($Line -match 'extern\b' -or $Line -match 'PreserveSig' -or $Line -match 'DllImport') { return $true }
    # Skip verification host code (tools/) — SetCursorPos/SetForegroundWindow allowed there
    if ($FilePath -match '\\tools\\') { return $true }
    # Skip lines where the API appears only in a comment
    if ($Line -match '//.*\b(SendInput|keybd_event|mouse_event|SetCursorPos|SetForegroundWindow|SetFocus)\b') {
        $codePart = $Line -replace '//.*$', ''
        if ($codePart -notmatch '\b(SendInput|keybd_event|mouse_event|SetCursorPos|SetForegroundWindow|SetFocus)\b') { return $true }
    }
    return $false
}

function Scan-File {
    param([string]$Path)
    $violations = @()
    $lines = Get-Content -Path $Path -ErrorAction SilentlyContinue
    if (-not $lines) { return $violations }
    $lineNum = 0
    foreach ($line in $lines) {
        $lineNum++
        foreach ($fp in $forbiddenPatterns) {
            if ($line -match $fp.Pattern) {
                if (-not (Test-IsCommentOrDeclaration -Line $line -FilePath $Path)) {
                    $violations += @{ File = $Path; Line = $lineNum; Rule = $fp.Rule }
                }
            }
        }
    }
    return $violations
}

# --- Self-test mode ---
if ($SelfTest) {
    $tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "HG_ForbiddenTest_$(Get-Random)"
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    try {
        $posCases = @(
            "class Foo { SendInput(0); }",
            "keybd_event(0,0,0,0)",
            "mouse_event(0,0,0,0,0)",
            "SetCursorPos(0,0)",
            "SetForegroundWindow(hwnd)",
            "SetFocus(hwnd)",
            "[System.Windows.Forms]::SendKeys('test')"
        )
        foreach ($case in $posCases) {
            $path = Join-Path $tmpDir "pos_$([Guid]::NewGuid().ToString('N').Substring(0,8)).cs"
            Set-Content -Path $path -Value $case -Encoding UTF8
        }
        $negCases = @(
            "// TODO: maybe add SendInput later",
            "// SetCursorPos is used only for verification hosts",
            "// setfocus declared to align COM vtable",
            "// reference to NativeMethods.SetCursorPos without invocation"
        )
        foreach ($case in $negCases) {
            $path = Join-Path $tmpDir "neg_$([Guid]::NewGuid().ToString('N').Substring(0,8)).cs"
            Set-Content -Path $path -Value $case -Encoding UTF8
        }
        $posFiles = Get-ChildItem -Path $tmpDir -Filter 'pos_*.cs'
        $negFiles = Get-ChildItem -Path $tmpDir -Filter 'neg_*.cs'
        $posViolations = 0
        foreach ($f in $posFiles) { $posViolations += (Scan-File -Path $f.FullName).Count }
        $negViolations = 0
        foreach ($f in $negFiles) { $negViolations += (Scan-File -Path $f.FullName).Count }
        if ($posViolations -eq 0 -or $negViolations -gt 0) {
            Write-Host "SELFTEST: fail (pos=$posViolations, neg=$negViolations)"
            exit 1
        }
        Write-Host "SELFTEST: pass"
        exit 0
    } finally {
        Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- Normal mode: scan repository source ---
$scope = @(
    'src\Experience\HeyGanesh.App',
    'src\Intelligence\HeyGanesh.ContextOrchestrator',
    'src\Platform\HeyGanesh.GlobalHotkeyMonitor',
    'src\Platform\HeyGanesh.ScreenCapture',
    'src\Perception\HeyGanesh.ElementSelection',
    'src\Perception\HeyGanesh.Vision',
    'src\Intelligence\HeyGanesh.TutorEngine',
    'src\Experience\HeyGanesh.Voice',
    'src\Platform\HeyGanesh.ScreenOverlay',
    'src\Foundation\HeyGanesh.Abstractions',
    'src\Foundation\HeyGanesh.Security',
    'src\Providers\HeyGanesh.Providers.Core',
    'src\Providers\HeyGanesh.Providers.OpenAiCompat',
    'src\Providers\HeyGanesh.Providers.Anthropic',
    'tools\*'
)

$violations = @()
foreach ($d in $scope) {
    $resolved = Resolve-Path $d -ErrorAction SilentlyContinue
    if (-not $resolved) { continue }
    $files = Get-ChildItem -Path $resolved -Include '*.cs' -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $violations += Scan-File -Path $file.FullName
    }
}

if ($violations.Count -gt 0) {
    Write-Host "FORBIDDEN-API: violations found"
    $violations | ForEach-Object { Write-Host "$($_.File):$($_.Line):$($_.Rule)" }
    exit 1
}
Write-Host "FORBIDDEN-API: pass"
exit 0
