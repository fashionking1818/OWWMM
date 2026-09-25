[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$project = Join-Path $repoRoot 'src\OWWMM\OWWMM.csproj'
$packageRoot = Join-Path $repoRoot 'artifacts\package'
$stage = Join-Path $packageRoot 'OWWMM'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$archive = Join-Path $releaseRoot "OWWMM-$Version-win-x64.zip"
$sourceArchive = Join-Path $repoRoot 'src\OWWMM\ThirdParty\7zip\7z2603-src.7z'
$sourceCopy = Join-Path $releaseRoot '7z2603-src.7z'
$numericVersion = ($Version -split '[-+]')[0]

$stageFullPath = [IO.Path]::GetFullPath($stage)
$packagePrefix = [IO.Path]::GetFullPath($packageRoot) + [IO.Path]::DirectorySeparatorChar
if (-not $stageFullPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected package staging path: $stageFullPath"
}
if (-not (Test-Path -LiteralPath $sourceArchive -PathType Leaf)) {
    throw "Bundled 7-Zip source is missing: $sourceArchive"
}

Write-Host '[1/4] Building Windows x64 application...'
& dotnet restore $project --runtime win-x64 -p:NuGetAudit=false --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed: $LASTEXITCODE" }
& dotnet clean $project --configuration Release --runtime win-x64 --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed: $LASTEXITCODE" }
if (Test-Path -LiteralPath $stageFullPath) {
    Remove-Item -LiteralPath $stageFullPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stageFullPath -Force | Out-Null
& dotnet publish $project --configuration Release --runtime win-x64 --self-contained false `
    --output $stageFullPath -p:Version=$Version -p:AssemblyVersion=$numericVersion.0 `
    -p:FileVersion=$numericVersion.0 -p:InformationalVersion=$Version -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

Write-Host '[2/4] Checking package contents...'
$bundledSource = Join-Path $stageFullPath 'ThirdParty\7zip\7z2603-src.7z'
if (Test-Path -LiteralPath $bundledSource) {
    Remove-Item -LiteralPath $bundledSource -Force
}
foreach ($relativePath in @(
    'OWWMM.exe', 'OWWMM.dll', 'OWWMM.runtimeconfig.json',
    'ThirdPartyNotices\NOTICE.md',
    'ThirdParty\7zip\win-x64\7z.exe', 'ThirdParty\7zip\win-x64\7z.dll'
)) {
    $path = Join-Path $stageFullPath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package file is missing: $relativePath"
    }
}
foreach ($name in @('config', 'src', 'tests', 'artifacts')) {
    if (Test-Path -LiteralPath (Join-Path $stageFullPath $name)) {
        throw "Unexpected package directory: $name"
    }
}
foreach ($name in @('README.md', 'DistributionNotice.md')) {
    if (Test-Path -LiteralPath (Join-Path $stageFullPath $name)) {
        throw "Repository documentation was included in the application package: $name"
    }
}
$readmes = @(Get-ChildItem -LiteralPath $stageFullPath -File -Recurse | Where-Object {
    $_.Name.Equals('README.md', [StringComparison]::OrdinalIgnoreCase)
})
if ($readmes.Count -gt 0) { throw 'A README was included in the application package.' }
$debugSymbols = @(Get-ChildItem -LiteralPath $stageFullPath -Filter '*.pdb' -File -Recurse)
if ($debugSymbols.Count -gt 0) { throw 'Debug symbols were included in the package.' }
$nestedArchives = @(Get-ChildItem -LiteralPath $stageFullPath -File -Recurse | Where-Object {
    $_.Extension -in @('.zip', '.7z', '.rar', '.tar')
})
if ($nestedArchives.Count -gt 0) { throw 'An archive was included inside the application package.' }

Write-Host '[3/4] Creating release files...'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -LiteralPath $stageFullPath -DestinationPath $archive -CompressionLevel Optimal
Copy-Item -LiteralPath $sourceArchive -Destination $sourceCopy -Force

Write-Host '[4/4] Writing SHA-256 checksums...'
foreach ($file in @($archive, $sourceCopy)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $file)" | Set-Content -LiteralPath "$file.sha256" -Encoding ascii
    Write-Host "$(Split-Path -Leaf $file): $hash"
}
Write-Host "Release files: $releaseRoot"
