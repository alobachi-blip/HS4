# HS2 Direct H Launcher

Run `D:\HS4\launch_direct_h.cmd`. It builds/deploys the plugin only when needed, prepares a dependency-oriented lean BepInEx plugin profile, arms one launch, and starts `HoneySelect2.exe` directly. The original `BepInEx\plugins` directory is never moved or modified; normal game launches keep the full plugin set.

The launcher explicitly disables the optional BepInEx splash. The plugin then suppresses the game Logo scene before its first rendered frame, bypasses its preset/brand/audio coroutine and fixed wait, and requests `HScene` as soon as the required managers exist. It also skips the normal title/selection/map flow, finishes the startup fade immediately, disables the H-scene transition/loading image, randomly chooses an H-compatible map, randomizes the opening clothes state, and randomly fills two female plus two male slots from:

- `D:\HS2\UserData\chara\female`
- `D:\HS2\UserData\chara\male`

The two picks per sex are distinct when at least two cards exist. With one card it is used in both slots; with no cards HS2's default/saved characters are used where possible.

The launch flag is one-shot and is removed as soon as the H-scene request succeeds. Normal launches through `HoneySelect2.exe` are unaffected.

During a direct launch, pacing-only empty frames in the synchronous H resource-table scans are coalesced within an 8 ms per-frame budget. Asset loads and real asynchronous waits are preserved.

The log reports elapsed time at launcher arm, Logo takeover, H-scene request, and first ready animation/characters so remaining startup bottlenecks can be measured directly.

Useful commands:

```powershell
# Different game directory
.\launch_direct_h.ps1 -GameRoot "E:\Games\HS2"

# Rebuild/deploy without starting the game
.\launch_direct_h.ps1 -BuildOnly -ForceBuild

# Compatibility fallback: launch once with the full plugin directory
.\launch_direct_h.ps1 -FullPlugins
```

Persistent mode and card/map settings are available in `BepInEx\config\com.hs2.directhlauncher.cfg` after the plugin has loaded once. `RandomizeMap` and `RandomizeInitialClothes` default to `true`; `MapId` is retained as the fallback.
