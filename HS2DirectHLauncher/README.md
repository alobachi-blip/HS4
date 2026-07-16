# HS2 Direct H Launcher

Run `D:\HS4\launch_direct_h.cmd`. It builds/deploys the plugin only when needed, arms one launch, and starts `HoneySelect2.exe` directly.

The plugin requests `HScene` at the first safe frame, skips the normal title/selection/map flow, disables the transition fade/loading image, randomly chooses an H-compatible map, randomizes the opening clothes state, and randomly fills two female plus two male slots from:

- `D:\HS2\UserData\chara\female`
- `D:\HS2\UserData\chara\male`

The two picks per sex are distinct when at least two cards exist. With one card it is used in both slots; with no cards HS2's default/saved characters are used where possible.

The launch flag is one-shot and is removed as soon as the H-scene request succeeds. Normal launches through `HoneySelect2.exe` are unaffected.

Useful commands:

```powershell
# Different game directory
.\launch_direct_h.ps1 -GameRoot "E:\Games\HS2"

# Rebuild/deploy without starting the game
.\launch_direct_h.ps1 -BuildOnly -ForceBuild
```

Persistent mode and card/map settings are available in `BepInEx\config\com.hs2.directhlauncher.cfg` after the plugin has loaded once. `RandomizeMap` and `RandomizeInitialClothes` default to `true`; `MapId` is retained as the fallback. High-numbered custom maps are excluded for startup speed unless `IncludeModMaps` is enabled.
