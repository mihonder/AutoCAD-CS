$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundle = Join-Path $project 'dist\v1.1.0\CS16.bundle'
$null = New-Item -ItemType Directory -Path $bundle -Force
$expected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
function Copy-Artifact([string]$Source, [string]$Relative) {
    $dest = Join-Path $bundle $Relative
    $null = $expected.Add([IO.Path]::GetFullPath($dest))
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force
    if ((Test-Path -LiteralPath $dest) -and ((Get-FileHash -LiteralPath $Source).Hash -eq (Get-FileHash -LiteralPath $dest).Hash)) { return }
    Copy-Item -LiteralPath $Source -Destination $dest -Force
}
$families = [ordered]@{'2020'='net47';'2021-2024'='net48';'2025-2026'='net8.0-windows';'2027'='net10.0-windows'}
foreach ($family in $families.Keys) {
    $output = Join-Path $project ('src\bin\v1.1.0\'+$families[$family])
    if (-not (Test-Path -LiteralPath (Join-Path $output 'AutoCADCS.dll'))) { throw "Missing build: $family" }
    foreach ($file in Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'AutoCADCS.deps.json' }) {
        if ($file.Name -match '^(AcMgd|AcDbMgd|AcCoreMgd|AdWindows|accore|acad)\.dll$') { throw "Host SDK DLL in output: $($file.Name)" }
        Copy-Artifact $file.FullName ('Contents\'+$family+'\'+$file.Name)
    }
}
Copy-Artifact (Join-Path $project 'distribution\PackageContents.xml') 'PackageContents.xml'
Copy-Artifact (Join-Path $project 'distribution\cs16-unsupported.lsp') 'Contents\cs16-unsupported.lsp'
Copy-Artifact (Join-Path $project 'distribution\cs16-loader.lsp') 'Contents\cs16-loader.lsp'
Copy-Artifact (Join-Path $project 'distribution\README.txt') 'README.txt'
Copy-Artifact (Join-Path $project 'distribution\THIRD-PARTY.txt') 'THIRD-PARTY.txt'
Copy-Artifact (Join-Path $project 'src\packages.lock.json') 'BUILD-DEPENDENCIES.json'
foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File) {
    $resolved = [IO.Path]::GetFullPath($file.FullName)
    if (-not $resolved.StartsWith($bundle+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output escaped bundle' }
    if (-not $expected.Contains($resolved) -and $file.Name -ne 'SHA256SUMS.txt') { Remove-Item -LiteralPath $resolved }
}
# Validate every series maps to exactly one entry, including unsupported hosts.
[xml]$xml = Get-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Encoding UTF8
foreach ($series in @(19.0,20.1,23.0,23.1,24.0,24.1,24.2,24.3,25.0,25.1,26.0,26.1,27.0)) {
    $matches = @($xml.ApplicationPackage.Components | Where-Object {
        $requirements=$_.RuntimeRequirements
        $minimum=[double]::Parse($requirements.SeriesMin.Substring(1),[Globalization.CultureInfo]::InvariantCulture)
        $maximum=if($requirements.SeriesMax){[double]::Parse($requirements.SeriesMax.Substring(1),[Globalization.CultureInfo]::InvariantCulture)}else{999}
        $series -ge $minimum -and $series -le $maximum
    })
    if ($matches.Count -ne 1) { throw "Ambiguous or missing loader mapping: $series" }
    $module = Join-Path $bundle $matches[0].ComponentEntry.ModuleName
    if (-not (Test-Path -LiteralPath $module)) { throw "Missing loader module: $module" }
}
$hashes = foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash+'  '+$file.FullName.Substring($bundle.Length+1).Replace('\','/')
}
[IO.File]::WriteAllLines((Join-Path $bundle 'SHA256SUMS.txt'),$hashes,(New-Object Text.UTF8Encoding($false)))
$zip = Join-Path $project 'dist\CS16-v1.1.0-AutoCAD2020-2027.zip'
& python (Join-Path $project 'scripts\make_portable.py')
if ($LASTEXITCODE) { throw 'Portable launcher generation failed' }
$portable = Join-Path (Split-Path -Parent $bundle) 'CS16.lsp'
$readme = Join-Path (Split-Path -Parent $bundle) 'README.txt'
Copy-Item -LiteralPath (Join-Path $project 'distribution\README.txt') -Destination $readme -Force
Compress-Archive -LiteralPath @($bundle,$portable,$readme) -DestinationPath $zip -Force
Write-Host "Package ready: $zip"
Get-Item -LiteralPath $zip | Select-Object FullName,Length
