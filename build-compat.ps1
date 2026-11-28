param([string]$DotNet = 'dotnet', [string]$NuGetSource = 'https://www.nuget.org/api/v2/', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $DotNet restore src/CadDustLab.csproj --source $NuGetSource --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & $DotNet build src/CadDustLab.csproj -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    if (-not $SkipTests) {
        & $DotNet build tests/preview/Preview.csproj -c Release --source $NuGetSource
        if ($LASTEXITCODE) { throw 'Test build failed' }
        Push-Location tests/preview
        try {
            foreach ($runtime in @('net47','net8.0','net10.0')) {
                foreach ($mode in @('--input','--language','--freeze','--health','--crouch','--gameplay24','--compat','--rounds','--draft','')) {
                    Write-Host "Testing $runtime $mode"
                    if ($runtime -eq 'net47') { & './bin/Release/net47/Preview.exe' $mode }
                    elseif ($runtime -eq 'net8.0') { & dotnet './bin/Release/net8.0/Preview.dll' $mode }
                    else { & $DotNet './bin/Release/net10.0/Preview.dll' $mode }
                    if ($LASTEXITCODE) { throw "Tests failed: $runtime $mode" }
                }
            }
        } finally { Pop-Location }
        & $DotNet build tests/checks/MessagePumpChecks.csproj -c Release --source $NuGetSource
        if ($LASTEXITCODE) { throw 'Message-pump test build failed' }
        & './tests/checks/bin/Release/net47/MessagePumpChecks.exe'
        if ($LASTEXITCODE) { throw 'Framework message-pump checks failed' }
        & dotnet './tests/checks/bin/Release/net8.0-windows/MessagePumpChecks.dll'
        if ($LASTEXITCODE) { throw '.NET 8 message-pump checks failed' }
        & $DotNet './tests/checks/bin/Release/net10.0-windows/MessagePumpChecks.dll'
        if ($LASTEXITCODE) { throw '.NET 10 message-pump checks failed' }
    }
    & (Join-Path $PSScriptRoot 'scripts/package-compat.ps1')
    if (-not $SkipTests) {
        Push-Location tests/preview
        try {
            foreach ($family in @('2020','2021-2024')) {
                & './bin/Release/net47/Preview.exe' --module "../../dist/v1.1.0/CS16.bundle/Contents/$family/AutoCADCS.dll"
                if ($LASTEXITCODE) { throw "Packaged Framework module failed: $family" }
            }
            & dotnet './bin/Release/net8.0/Preview.dll' --module '../../dist/v1.1.0/CS16.bundle/Contents/2025-2026/AutoCADCS.dll'
            if ($LASTEXITCODE) { throw 'Packaged .NET 8 module failed' }
            & $DotNet './bin/Release/net10.0/Preview.dll' --module '../../dist/v1.1.0/CS16.bundle/Contents/2027/AutoCADCS.dll'
            if ($LASTEXITCODE) { throw 'Packaged .NET 10 module failed' }
        } finally { Pop-Location }
    }
} finally { Pop-Location }
