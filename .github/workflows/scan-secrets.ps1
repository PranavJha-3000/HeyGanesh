#--------------------------------------------------------------
# Scan-secrets.ps1 — HeyGanesh CI
# Detects exposed API keys and secrets in source.
# Three modes: default, -SelfTest, -ZeroNuGet.
# Failure output contains only file, line, rule — never values.
#--------------------------------------------------------------
param(
    [switch]$SelfTest,
    [switch]$ZeroNuGet
)

$ErrorActionPreference = 'Stop'

if ($ZeroNuGet) {
    # --- Zero-NuGet guard: parse core .csproj files ---
    $coreProjects = @(
        "src\Experience\HeyGanesh.App\HeyGanesh.App.csproj",
        "src\Intelligence\HeyGanesh.ContextOrchestrator\HeyGanesh.ContextOrchestrator.csproj",
        "src\Platform\HeyGanesh.GlobalHotkeyMonitor\HeyGanesh.GlobalHotkeyMonitor.csproj",
        "src\Platform\HeyGanesh.ScreenCapture\HeyGanesh.ScreenCapture.csproj",
        "src\Perception\HeyGanesh.ElementSelection\HeyGanesh.ElementSelection.csproj",
        "src\Perception\HeyGanesh.Vision\HeyGanesh.Vision.csproj",
        "src\Intelligence\HeyGanesh.TutorEngine\HeyGanesh.TutorEngine.csproj",
        "src\Experience\HeyGanesh.Voice\HeyGanesh.Voice.csproj",
        "src\Platform\HeyGanesh.ScreenOverlay\HeyGanesh.ScreenOverlay.csproj",
        "src\Foundation\HeyGanesh.Abstractions\HeyGanesh.Abstractions.csproj",
        "src\Foundation\HeyGanesh.Security\HeyGanesh.Security.csproj",
        "src\Providers\HeyGanesh.Providers.Core\HeyGanesh.Providers.Core.csproj",
        "src\Providers\HeyGanesh.Providers.OpenAiCompat\HeyGanesh.Providers.OpenAiCompat.csproj",
        "src\Providers\HeyGanesh.Providers.Anthropic\HeyGanesh.Providers.Anthropic.csproj"
    )

    $unauthorized = @()
    foreach ($proj in $coreProjects) {
        if (-not (Test-Path $proj)) { continue }
        [xml]$xml = Get-Content -Path $proj
        $pkgRefs = $xml.SelectNodes('//*[local-name()="PackageReference"]')
        foreach ($pr in $pkgRefs) {
            $lineNum = 0
            $lines = Get-Content -Path $proj
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match 'PackageReference') { $lineNum = $i + 1; break }
            }
            $unauthorized += @{ File = $proj; Line = $lineNum; Rule = 'UNAUTHORIZED_PACKAGE' }
        }
    }

    if ($unauthorized.Count -gt 0) {
        $unauthorized | ForEach-Object { Write-Host "$($_.File):$($_.Line):$($_.Rule)" }
        exit 1
    }
    Write-Host "ZERO-NUGET: pass"
    exit 0
}

# --- Self-test mode ---
if ($SelfTest) {
    $tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "HG_SecretTest_$(Get-Random)"
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    try {
        $fakeKey = "sk-" + (-join ((65..90) + (97..122) | Get-Random -Count 28 | ForEach-Object { [char]$_ }))
        $fixturePath = Join-Path $tmpDir "HeyGanesh-test-key.txt"
        Set-Content -Path $fixturePath -Value "DEEPSEEK_API_KEY=$fakeKey" -Encoding UTF8

        $barePath = Join-Path $tmpDir "bare-key.txt"
        Set-Content -Path $barePath -Value "DEEPSEEK_API_KEY" -Encoding UTF8

        # Scan temp files
        $scannerResult = @()
        $tmpFiles = Get-ChildItem -Path $tmpDir -Filter '*.txt' -Recurse
        foreach ($f in $tmpFiles) {
            $content = Get-Content -Path $f.FullName -ErrorAction SilentlyContinue
            $lineNum = 0
            foreach ($line in $content) {
                $lineNum++
                if ($line -match 'DEEPSEEK_API_KEY\s*[=:]\s*[a-zA-Z0-9]{16,}') {
                    $scannerResult += @{ File = $f.FullName; Line = $lineNum; Rule = 'EXPOSED_SECRET' }
                }
            }
        }

        $hasFiKey = $scannerResult | Where-Object { $_.File -eq $fixturePath -and $_.Rule -eq 'EXPOSED_SECRET' }
        $hasBare = $scannerResult | Where-Object { $_.File -eq $barePath }
        if ($hasFiKey.Count -gt 0 -and $hasBare.Count -eq 0) {
            exit 0
        }
        exit 1
    } finally {
        Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- Normal mode: scan eligible repository files for exposed secrets ---
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

$secrets = @()
foreach ($d in $scope) {
    $resolved = Resolve-Path $d -ErrorAction SilentlyContinue
    if (-not $resolved) { continue }
    $files = Get-ChildItem -Path $resolved -Include '*.cs' -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $content = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
        if (-not $content) { continue }
        $lineNum = 0
        foreach ($line in $content) {
            $lineNum++
            if ($line -match 'DEEPSEEK_API_KEY\s*[=:]\s*[a-zA-Z0-9]{16,}') {
                $secrets += @{ File = $file.FullName; Line = $lineNum; Rule = 'EXPOSED_SECRET' }
            }
        }
    }
}

if ($secrets.Count -gt 0) {
    Write-Host "SECRET: violations found"
    $secrets | ForEach-Object { Write-Host "$($_.File):$($_.Line):$($_.Rule)" }
    exit 1
}
Write-Host "SECRET: pass"
exit 0
