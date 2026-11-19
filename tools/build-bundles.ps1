# Builds the mod's HDRP asset bundle (COS-5) into content/bundles/cots with the game's exact Unity version.
# The mod build embeds it when present. See docs/asset-bundles.md.
#   ./tools/build-bundles.ps1
#   ./tools/build-bundles.ps1 -Unity 'C:\Program Files\Unity\Hub\Editor\2022.2.16f1\Editor\Unity.exe'
param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2022.2.16f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (-not (Test-Path $Unity)) { throw "Unity 2022.2.16f1 not found at $Unity (the game's version; bundles from other versions do not load)" }
$project = Join-Path $root 'unity\CotsBundles'
$log = Join-Path $env:TEMP 'cots-bundle-build.log'
$p = Start-Process $Unity -ArgumentList '-batchmode', '-quit', '-nographics', '-projectPath', "`"$project`"", '-executeMethod', 'CotsBundleBuilder.BuildFromCommandLine', '-logFile', "`"$log`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content $log -Tail 40; throw "bundle build failed (exit $($p.ExitCode)); log: $log" }
$out = Join-Path $root 'content\bundles\cots'
Write-Host "bundle: $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
