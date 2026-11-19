# Build + deploy the mod from THIS checkout, launch Sons of the Forest with the mod's self-test, wait, print results.
#   ./tools/run-selftest.ps1 -Headless       # -batchmode (no window; still uses the GPU) — the normal way
#   ./tools/run-selftest.ps1                 # windowed 640x360
#   ./tools/run-selftest.ps1 -Headless -NoGraphics   # -nographics crashes in UnityPlayer.dll after world activation (2026-11-18)
#   -NoBuild                                  # run whatever is deployed already
#   -Only GamepadFeature                      # run just that feature's checks (type name of its IModFeature): ~2 min instead of ~9
# One game folder is shared by every checkout/worktree, so build+deploy+run happen under a machine-wide lock:
# never deploy any other way (build with -p:DeployToGame=false otherwise).
# The test save is a copy of a real save in its own slot (1999999999) so real saves are never touched.
param(
    [string]$SaveId = '1999999999',
    [switch]$Headless,
    [switch]$NoGraphics,
    [switch]$NoBuild,
    [string]$Only = '',
    [int]$TimeoutSeconds = 1200,   # the full suite takes ~11 min since Wave 2
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$log = Join-Path $GameDir '_Redloader\Latest.log'
$unityLog = Join-Path $env:TEMP 'cots-selftest-unity.log'
$steam = 'C:\Program Files (x86)\Steam\steam.exe'

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

    Get-Process SonsOfTheForest -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    $started = Get-Date

    $gameArgs = @('--savegame', $SaveId, '--cots-selftest', '-logFile', $unityLog)
    if ($Only) { $gameArgs += "--cots-selftest-only=$Only" }
    if ($Headless) { $gameArgs += @('-batchmode'); if ($NoGraphics) { $gameArgs += @('-nographics') } }
    else { $gameArgs += @('-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360') }

    # Through Steam so the game doesn't restart itself and drop the arguments.
    & $steam -applaunch 1326470 @gameArgs

    $done = $false
    while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 5
        if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $started) {
            if (Select-String -Path $log -Pattern 'SELFTEST DONE' -Quiet) { $done = $true; break }
        }
        $running = Get-Process SonsOfTheForest -ErrorAction SilentlyContinue
        if (-not $running -and ((Get-Date) - $started).TotalSeconds -gt 60) { break }
    }
    Start-Sleep -Seconds 3
    Get-Process SonsOfTheForest -ErrorAction SilentlyContinue | Stop-Process -Force

    # Keep the whole log: the next run (any checkout) overwrites it, and the summary below shows only the last lines.
    if (Test-Path $log) { Copy-Item $log (Join-Path $env:TEMP 'cots-selftest-last.log') -Force }
    "elapsed: {0:N0}s, finished: {1}" -f ((Get-Date) - $started).TotalSeconds, $done
    if (Test-Path $log) {
        Select-String -Path $log -Pattern '\[COTS-SELFTEST\]|Champions of the Sons|ERROR|Exception|plugins to load' |
            ForEach-Object { $_.Line } | Select-Object -Last 80
    }
    if (-not $done) {
        "--- unity log tail ($unityLog):"
        if (Test-Path $unityLog) { Get-Content $unityLog -Tail 40 }
        exit 1
    }
    if (Select-String -Path $log -Pattern 'SELFTEST DONE failures=0' -Quiet) { exit 0 } else { exit 2 }
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
