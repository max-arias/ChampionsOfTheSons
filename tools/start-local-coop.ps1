# Plays co-op on this PC with one Steam account (docs/coop-testing.md): starts the local Dedicated Server with the mod and
# then the game through Steam, which joins it on its own (--cots-join). You are a client of a real host-authoritative
# server: enemies, drops and the difficulty come from the server, your hits are forwarded to it.
# A second player on the same account is not possible: the server refuses a second session of the same Steam account
# ("k_EBeginAuthSessionResultDuplicateRequest"). For the teammate's side use tools/run-coop-test.ps1 (simulated peer).
param(
    [switch]$NoBuild,
    [switch]$Windowed,
    [string]$ServerDir = 'D:\dev\sotf-server',
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest',
    [int]$ExtraEnemyLevels = 0
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$steam = 'C:\Program Files (x86)\Steam\steam.exe'
$lan = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -in 'Dhcp', 'Manual' -and $_.IPAddress -notlike '169.*' } | Select-Object -First 1).IPAddress
$join = "${lan}:8766"
$unityServerLog = Join-Path $ServerDir 'unity.log'

if (-not $NoBuild) {
    $dotnet = 'D:\dev\deps\dotnet\dotnet.exe'
    $env:DOTNET_ROOT = Split-Path $dotnet
    & $dotnet build (Join-Path $repo 'src\ChampionsOfTheSons.Mod\ChampionsOfTheSons.Mod.csproj') -c Release -p:DeployToGame=true -p:GameDir="$GameDir" -v:minimal -nologo
    if ($LASTEXITCODE) { "build failed"; exit 3 }
}
Copy-Item "$GameDir\Mods\ChampionsOfTheSons.dll" "$ServerDir\Mods" -Force
New-Item -ItemType Directory -Force "$ServerDir\Mods\ChampionsOfTheSons", "$ServerDir\UserData" | Out-Null
Copy-Item "$GameDir\Mods\ChampionsOfTheSons\manifest.json" "$ServerDir\Mods\ChampionsOfTheSons" -Force
Set-Content "$ServerDir\UserData\ChampionsOfTheSonsServer.cfg" ("[ChampionsOfTheSonsServer]", "extra_enemy_levels = $ExtraEnemyLevels", "friendly_fire = false")

Get-Process SonsOfTheForest, SonsOfTheForestDS -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $unityServerLog -ErrorAction SilentlyContinue
$server = Start-Process (Join-Path $ServerDir 'SonsOfTheForestDS.exe') -WorkingDirectory $ServerDir -ArgumentList '-batchmode', '-logFile', $unityServerLog -PassThru -WindowStyle Hidden
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    Start-Sleep 3
    if ($server.HasExited) { "the server stopped; see $unityServerLog"; exit 1 }
    if ((Test-Path $unityServerLog) -and (Select-String $unityServerLog -Pattern 'Dedicated server loaded' -Quiet)) { break }
}
"server up at $join after {0:N0}s (it keeps running until you stop it: Stop-Process -Name SonsOfTheForestDS)" -f ((Get-Date) - $t0).TotalSeconds
$gameArgs = @('--cots-join', $join)
if ($Windowed) { $gameArgs += @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720') }
# -applaunch, not a steam://run link: the link makes Steam ask before passing arguments.
& $steam -applaunch 1326470 @gameArgs
"the game is starting and will join the server from the title screen"
