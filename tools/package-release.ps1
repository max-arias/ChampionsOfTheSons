# Builds the release zip for sotf-mods.com (COS-29). Does not publish anything.
#   ./tools/package-release.ps1              # checks, Release build, dist/ChampionsOfTheSons-<version>.zip + SHA-256
#   ./tools/package-release.ps1 -SkipChecks  # zip only (no core tests, content check or strict translation check)
# Layout (extract into the game folder, same as the dev deploy and the RedLoader template):
#   Mods/ChampionsOfTheSons.dll
#   Mods/ChampionsOfTheSons/manifest.json, icons/*.png, README.txt, CREDITS.md, CREDITS-icons.md
param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet -or -not (& $dotnet --list-sdks 2>$null)) { $dotnet = 'D:\dev\deps\dotnet\dotnet.exe'; $env:DOTNET_ROOT = Split-Path $dotnet }

$manifest = Get-Content src/ChampionsOfTheSons.Mod/manifest.json -Raw | ConvertFrom-Json
$version = $manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "manifest version '$version' is not semver" }

if (-not $SkipChecks) {
    & $dotnet test ChampionsOfTheSons.slnx -c Release --nologo -v q
    if ($LASTEXITCODE) { throw 'core tests failed' }
    & $dotnet run --project tools/ContentCheck -c Release -- content
    if ($LASTEXITCODE) { throw 'content check failed' }
    python -I tools/check_translations.py
    if ($LASTEXITCODE) { throw 'strict translation check failed (missing or stale translations)' }
}

& $dotnet build src/ChampionsOfTheSons.Mod/ChampionsOfTheSons.Mod.csproj -c Release -p:DeployToGame=false --nologo -v q
if ($LASTEXITCODE) { throw 'mod build failed' }
$out = 'src/ChampionsOfTheSons.Mod/bin/Release/net6.0'

$stage = Join-Path $root "dist/stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$modDir = Join-Path $stage 'Mods/ChampionsOfTheSons'
New-Item -ItemType Directory -Force (Join-Path $modDir 'icons') | Out-Null
Copy-Item "$out/ChampionsOfTheSons.dll" (Join-Path $stage 'Mods')
Copy-Item "$out/manifest.json" $modDir
Copy-Item content/icons/*.png (Join-Path $modDir 'icons')
Copy-Item docs/release/README.txt $modDir
Copy-Item CREDITS.md $modDir
Copy-Item content/icons/CREDITS.md (Join-Path $modDir 'CREDITS-icons.md')

$zip = Join-Path $root "dist/ChampionsOfTheSons-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "release: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "sha256:  $hash"
