# Mod Layout

Motions shares Lethe's mods folder, so a motion pack is just a normal mod folder:

```
BepInEx/plugins/Lethe/mods/
    MyMotion/
        custom_motions/
            10101_YiSang_BaseAppearance/     <- appearance ID
                motions.bundle
                S1.json
                S2.json
            MOTIONBUFF_MyCustomBuff/         <- buff VFX (see Buff VFX)
                buffvfx.bundle
```

- The folder under `custom_motions/` is named after the **appearance ID** of the
  character you're overriding.
- Every `*.bundle` inside that folder is loaded, including in subfolders.
- JSON files are named after the motion they configure: `S1.json`, `S2.json`,
  `Idle.json`, and so on — the name must match a `MOTION_DETAIL` value.
- Prefixing a **mod** folder with `DISABLED_` or `FULLDISABLED_` makes Motions
  skip it entirely.

## Loading and unloading

Bundles are loaded when the battle scene loads, and unloaded when the stage
ends or the scene changes. You don't need to restart the game to test a change —
leaving the battle and re-entering it is enough to pick up a rebuilt bundle.
When adding new BuffVFX however, the game must be fully reloaded for caching
to work.
