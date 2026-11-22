# Co-op testing without a second person

`tools/run-coop-test.ps1` starts the free Sons of the Forest Dedicated Server on this PC, then one headless game client that
joins it, both running the mod. Each side logs `[COTS-COOP]` lines; the script prints both and ends with `COOP TEST PASS` / `FAIL`.
It takes the same machine-wide lock as `tools/run-selftest.ps1` (the client uses the shared game folder), so it waits while another run is in progress.

## One-time setup (done once per PC)

1. **steamcmd** (Valve's official tool): download <https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip> and unzip it into `D:\dev\steamcmd`.
2. **Dedicated server** (Steam app id 2465200, "Sons Of The Forest Dedicated Server", Windows, anonymous login works; confirmed with `steamcmd +login anonymous +app_info_print 2465200`):
   ```powershell
   D:\dev\steamcmd\steamcmd.exe +force_install_dir D:\dev\sotf-server +login anonymous +app_update 2465200 validate +quit
   ```
3. **RedLoader on the server**: copy from the game folder into `D:\dev\sotf-server`: `version.dll`, `doorstop_config.ini` and the `_Redloader` folder **without `Game`**
   (the server generates its own interop assemblies on its first start, about 2 minutes). `Mods\` is filled by the test script.
4. **Start the server once** so it writes its config and `steam_appid.txt`, then edit
   `%USERPROFILE%\AppData\LocalLow\Endnight\SonsOfTheForestDS\dedicatedserver.cfg`:
   `"LanOnly": false`, `"SkipNetworkAccessibilityTest": true`, `"SaveMode": "New"`. (The first start prints "Please restart the server".)
5. The mod manifest must say `"platform": "Universal"`, otherwise RedLoader refuses to load it on a dedicated server ("is a client mod and cannot be loaded on a dedicated server").

## Run

```powershell
./tools/run-coop-test.ps1            # build + deploy to game and server, run both, print results
./tools/run-coop-test.ps1 -NoBuild
```

The client is started with `steam.exe -applaunch` (a `steam://run` link makes Steam wait for a "launch with these arguments?" click, and every later launch queues behind it until Steam restarts; `+connect_ds` is not passed because `-applaunch` takes `+words` as Steam console commands) with `--cots-join <LAN IP>:8766`. On the title screen the mod opens the game's own join-by-ip dialog
(`CoopSteamManager.ShowDedicatedServerJoinByIpDialog`) and accepts it, so no one has to click through the menu. The address must be this PC's
LAN IPv4 and the **game** port: the game finds the server through the LAN list and rejects `127.0.0.1` or the query port
("Couldn't find server by ip/port" in the client's `Player.log`). The script detects the LAN address; override with `-JoinAddress`.
With `"LanOnly": true` the lookup also fails. `-Windowed` runs the client in a 640x360 window instead of `-batchmode`.
Logs: server `D:\dev\sotf-server\_Redloader\Latest.log` (+ `unity.log`), client `_Redloader\Latest.log` in the game folder.

## Playing co-op yourself on one PC, one Steam account

```powershell
./tools/start-local-coop.ps1            # Release build, deploy, start the server, start the game; it joins on its own
./tools/start-local-coop.ps1 -Windowed -ExtraEnemyLevels 3
```

You play as a client of the local Dedicated Server: the same host-authoritative paths a friend's game would use (enemies,
drops and difficulty from the server, your hits forwarded to it, sync of drops and enemy state). Stop the server afterwards
with `Stop-Process -Name SonsOfTheForestDS`.

What one account cannot do (tested 2026-11-22): a second game instance joining the same server. A second instance starts
(from its own folder, with `steam_appid.txt`), but the server refuses its Steam session as a duplicate of the first
(`No Steam Auth - Failed Status=k_EBeginAuthSessionResultDuplicateRequest`). A listen host uses the same Steam session check, so expect the same there (not tested). Seeing
another player's body (weapon models, trails, hand effects on a remote player, COS-117/COS-122) needs a second Steam account
that owns the game. The teammate's side of the protocol is covered by `run-coop-test.ps1`, whose server plays a simulated peer.
