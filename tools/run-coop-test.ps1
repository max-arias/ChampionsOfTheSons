# Co-op test loop: local SotF Dedicated Server + one headless game client that joins it, both with the mod.
#   ./tools/run-coop-test.ps1            # build, deploy to game + server, run, print PASS/FAIL from both logs
#   -NoBuild                              # run what is deployed already
# Needs the setup in docs/coop-testing.md (steamcmd + server in D:\dev\sotf-server with RedLoader).
# Takes the same machine-wide lock as run-selftest.ps1, because the client uses the shared game folder.
param(
    [switch]$NoBuild,
    [switch]$Windowed,   # client in a 640x360 window instead of -batchmode (the title-screen join UI may need it)
    [int]$TimeoutSeconds = 420,
    [string]$ServerDir = 'D:\dev\sotf-server',
    # The client must name the server by this PC's LAN address and the GAME port: the game's join-by-ip looks the server up
    # on the LAN and rejects 127.0.0.1 or the query port ("Couldn't find server by ip/port"). Empty = detect the LAN IPv4.
    [string]$JoinAddress = '',
    [string]$GameAddress = '',
    [int]$ServerExtraLevels = 3,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest'
)
$ErrorActionPreference = 'Stop'
if (-not $JoinAddress -or -not $GameAddress) {
    $lan = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -in 'Dhcp', 'Manual' -and $_.IPAddress -notlike '169.*' } | Select-Object -First 1).IPAddress
    if (-not $JoinAddress) { $JoinAddress = "${lan}:8766" }
    if (-not $GameAddress) { $GameAddress = "${lan}:8766" }
}
$repo = Split-Path $PSScriptRoot -Parent
$clientLog = Join-Path $GameDir '_Redloader\Latest.log'
$serverLog = Join-Path $ServerDir '_Redloader\Latest.log'
$steam = 'C:\Program Files (x86)\Steam\steam.exe'
$unityClientLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Endnight\SonsOfTheForest\Player.log'
$unityServerLog = Join-Path $ServerDir 'unity.log'

$mutex = New-Object System.Threading.Mutex($false, 'Global\ChampionsOfTheSonsGameRun')
"waiting for the game lock..."
try { [void]$mutex.WaitOne() } catch [System.Threading.AbandonedMutexException] { }
try {
    if (-not $NoBuild) {
        $dotnet = 'D:\dev\deps\dotnet\dotnet.exe'
        $env:DOTNET_ROOT = Split-Path $dotnet
        & $dotnet build (Join-Path $repo 'src\ChampionsOfTheSons.Mod\ChampionsOfTheSons.Mod.csproj') -c Debug -p:DeployToGame=true -p:GameDir="$GameDir" -v:minimal -nologo
        if ($LASTEXITCODE) { "build failed"; exit 3 }
    }
    # Same build onto the server.
    Copy-Item "$GameDir\Mods\ChampionsOfTheSons.dll", "$GameDir\Mods\ChampionsOfTheSons.pdb" "$ServerDir\Mods" -Force
    New-Item -ItemType Directory -Force "$ServerDir\Mods\ChampionsOfTheSons" | Out-Null
    Copy-Item "$GameDir\Mods\ChampionsOfTheSons\manifest.json" "$ServerDir\Mods\ChampionsOfTheSons" -Force

    Get-Process SonsOfTheForest, SonsOfTheForestDS -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    # The server takes its extra enemy levels from this file (COS-15, COS-130); the client test checks they arrive.
    New-Item -ItemType Directory -Force "$ServerDir\UserData" | Out-Null
    Set-Content "$ServerDir\UserData\ChampionsOfTheSonsServer.cfg" ("[ChampionsOfTheSonsServer]", "extra_enemy_levels = $ServerExtraLevels", "friendly_fire = false")
    Remove-Item $serverLog -ErrorAction SilentlyContinue
    Remove-Item "$ServerDir\unity.log" -ErrorAction SilentlyContinue
    $started = Get-Date

    # Server first; wait until it idles, ready for players.
    $server = Start-Process (Join-Path $ServerDir 'SonsOfTheForestDS.exe') -WorkingDirectory $ServerDir -ArgumentList '-batchmode', '-logFile', $unityServerLog, '--cots-coop-test' -PassThru -WindowStyle Hidden
    $ready = $false
    while (((Get-Date) - $started).TotalSeconds -lt 240) {
        Start-Sleep -Seconds 3
        if ($server.HasExited) { break }
        if ((Test-Path $unityServerLog) -and (Select-String $unityServerLog -Pattern 'Dedicated server loaded' -Quiet)) { $ready = $true; break }
    }
    if (-not $ready) { "server did not come up"; if (Test-Path $unityServerLog) { Get-Content $unityServerLog -Tail 30 }; $server | Stop-Process -Force -ErrorAction SilentlyContinue; exit 1 }
    "server ready after {0:N0}s" -f ((Get-Date) - $started).TotalSeconds

    $clientStart = Get-Date
    # The game joins a dedicated server from "+connect_ds ip:port", but only reads it from Steam's launch command line,
    # which Steam fills for steam://run URLs (not for steam.exe -applaunch).
    $launchArgs = "--cots-join $JoinAddress +connect_ds $GameAddress --cots-expect-extra-levels $ServerExtraLevels"
    $launchArgs += if ($Windowed) { ' -screen-fullscreen 0 -screen-width 640 -screen-height 360' } else { ' -batchmode' }
    Start-Process ("steam://run/1326470//" + [uri]::EscapeDataString($launchArgs) + "/")
    $done = $false
    while (((Get-Date) - $clientStart).TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 5
        if ((Test-Path $clientLog) -and (Get-Item $clientLog).LastWriteTime -gt $clientStart) {
            if (Select-String -Path $clientLog -Pattern 'COOP CLIENT DONE' -Quiet) { $done = $true; break }
        }
        if (-not (Get-Process SonsOfTheForest -ErrorAction SilentlyContinue) -and ((Get-Date) - $clientStart).TotalSeconds -gt 60) { break }
    }
    Start-Sleep -Seconds 3
    Get-Process SonsOfTheForest, SonsOfTheForestDS -ErrorAction SilentlyContinue | Stop-Process -Force

    "elapsed: {0:N0}s, client finished: {1}" -f ((Get-Date) - $started).TotalSeconds, $done
    "--- server ($serverLog)"
    if (Test-Path $serverLog) { Select-String $serverLog -Pattern '\[COTS-COOP\]|Exception|ERROR' | ForEach-Object { $_.Line } | Select-Object -Last 60 }
    "--- client ($clientLog)"
    if (Test-Path $clientLog) { Select-String $clientLog -Pattern '\[COTS-COOP\]|Exception|ERROR' | ForEach-Object { $_.Line } | Select-Object -Last 60 }
    if (-not $done) { "--- client unity log tail:"; if (Test-Path $unityClientLog) { Get-Content $unityClientLog -Tail 30 }; exit 1 }
    if (Select-String -Path $clientLog -Pattern 'COOP CLIENT DONE failures=0' -Quiet) { "COOP TEST PASS"; exit 0 } else { "COOP TEST FAIL"; exit 2 }
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
