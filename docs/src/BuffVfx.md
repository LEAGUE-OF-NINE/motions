# Buff VFX

Motions can also attach a looping VFX prefab to a unit while it has one of
Lethe's custom buffs — the kind of aura that sits behind a character while a
status is active.

## How it works

The mod integrates with the game's native `BattleUnitViewAura` system, which is
the same component the base game uses for buff-triggered visual auras. This means:

- Effects are parented under the game's dedicated `_auraEffectRoot` transform
- Effects are registered in `_auraEffectDict` for proper lifecycle management
- Cleanup on death is handled automatically by `OnDieView()`
- The aura root visibility is controlled by `EnableRoot(bool)`

When a buff is applied or refreshed, the mod:

1. Looks up the buff keyword in its cache of registered aura prefabs
2. Gets the unit's `BattleUnitViewAura` component
3. Creates the aura through the game's system (instantiate → parent under aura root → add to dict → activate)

## Folder structure

Name the folder after the buff, prefixed with `MOTIONBUFF_`:

```
MyMotion/
    custom_motions/
        MOTIONBUFF_MyCustomBuff/
            myeffect.bundle
```

`MyCustomBuff` is the buff ID you gave the buff in Lethe. Motions resolves it to
the runtime keyword the framework assigned it, so the two have to match exactly.

## The prefab

Motions loads the **first prefab** that it finds from the bundle. Therefore,
only use 1 prefab in the bundle that will store your VFX.

The effect spawns behind the character by default. Ending the prefab's name with
`_Front` puts it in front instead:

| Prefab name | Renders |
|---|---|
| `myeffect` | behind the character |
| `myeffect_Front` | in front of the character |

The effect is scaled to the unit's height, and re-triggers (particles cleared and
replayed) each time the buff fires again. If it's already playing, it's left
alone rather than restarted.
