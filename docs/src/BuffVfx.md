# Buff VFX

Motions can also attach a looping VFX prefab to a unit while it has one of
Lethe's custom buffs — the kind of aura that sits behind a character while a
status is active.

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
