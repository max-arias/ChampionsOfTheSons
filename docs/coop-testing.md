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
   `"LanOnly": true`, `"SkipNetworkAccessibilityTest": true`, `"SaveMode": "New"`. (The first start prints "Please restart the server".)
5. The mod manifest must say `"platform": "Universal"`, otherwise RedLoader refuses to load it on a dedicated server ("is a client mod and cannot be loaded on a dedicated server").

## Run

```powershell
./tools/run-coop-test.ps1            # build + deploy to game and server, run both, print results
./tools/run-coop-test.ps1 -NoBuild
```

The client is started through Steam with `--cots-join 127.0.0.1:27016` (the server's query port): on the title screen the mod asks Steam for the
server at that address and calls the game's own `DedicatedServerBrowser.Connect`, so no one has to click through the menu.
Logs: server `D:\dev\sotf-server\_Redloader\Latest.log` (+ `unity.log`), client `_Redloader\Latest.log` in the game folder.
