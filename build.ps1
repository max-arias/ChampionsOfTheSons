# Build entry point. Usage:
#   ./build.ps1            build + test the core (no game needed)
#   ./build.ps1 test       same
#   ./build.ps1 mod        build the RedLoader mod and copy it into the game's Mods folder
#   ./build.ps1 all        both
param([ValidateSet('test', 'mod', 'all')][string]$Target = 'test')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Use a dotnet that has an SDK: PATH first, then the local install in D:\dev\deps\dotnet.
function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and (& $cmd.Source --list-sdks 2>$null)) { return $cmd.Source }
    $local = 'D:\dev\deps\dotnet\dotnet.exe'
    if (Test-Path $local) { $env:DOTNET_ROOT = Split-Path $local; return $local }
    throw 'No .NET SDK found. Install the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10).'
}
$dotnet = Find-Dotnet

if ($Target -in 'test', 'all') {
    & $dotnet test ChampionsOfTheSons.slnx -c Debug
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
if ($Target -in 'mod', 'all') {
    & $dotnet build src/ChampionsOfTheSons.Mod/ChampionsOfTheSons.Mod.csproj -c Debug
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
