# Launch Sons of the Forest with the mod's self-test, wait for it to finish, print the results.
#   ./tools/run-selftest.ps1                 # windowed 640x360, test save 1999999999
#   ./tools/run-selftest.ps1 -Headless       # -batchmode (no window; still uses the GPU)
#   ./tools/run-selftest.ps1 -Headless -NoGraphics   # -batchmode -nographics: crashes in UnityPlayer.dll after world activation (2026-11-18)
# The test save is a copy of a real save in its own slot so real saves are never touched.
param(
    [string]$SaveId = '1999999999',
    [switch]$Headless,
    [switch]$NoGraphics,
    [int]$TimeoutSeconds = 600,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest'
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $GameDir '_Redloader\Latest.log'
$unityLog = Join-Path $env:TEMP 'cots-selftest-unity.log'
$steam = 'C:\Program Files (x86)\Steam\steam.exe'

Get-Process SonsOfTheForest -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$started = Get-Date

$gameArgs = @('--savegame', $SaveId, '--cots-selftest', '-logFile', $unityLog)
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
