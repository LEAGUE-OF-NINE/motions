# Bundle Reference

Motions finds things inside your bundle **by name**. This page is the list of
names it looks for.

## Timelines

A timeline is matched to a motion by its asset name, lowercased:

| Timeline asset | Used for |
|---|---|
| `s1` | `S1`, first coin |
| `s1_1` | `S1`, second coin |
| `s1_2` | `S1`, third coin |
| `idle` | `Idle` |
| `damaged` | `Damaged` |

The coin index is the suffix after the underscore. A motion with two coins needs
two timelines (`s2` and `s2_1`) and two entries in the `coins` array of
`S2.json`. See [Attacks](Attacks.md) for a worked example.

## Audio

Unity's `AudioTrack` and FMOD tracks are stripped out of your timeline — they
don't survive IL2CPP. Motions instead reads each clip's **display name**, looks
for a `TextAsset` in your bundle called `<clipname>.bytes`, and plays those bytes
back through FMOD Core at the clip's start time.

- WAV and OGG are detected from the file header. Anything else won't play.
- The clip's **clip-in** (leading cut) and **duration** are respected, so
  trimming in the Unity timeline works as you'd expect.

The base project's editor script generates the `.bytes` assets for you at build
time — see [SFX](SFX.md).

## VFX

Any `ControlTrack` in your timeline is treated as a VFX track. The track is
removed from the timeline and its clips are replayed by Motions itself.

Each clip's display name is the **name of the prefab** to spawn from your bundle.
An optional `@suffix` controls where it spawns:

| Clip name | Spawns |
|---|---|
| `slash_vfx` | on the character (default) |
| `slash_vfx@enemy` | on the target |
| `slash_vfx@center` | between attacker and target |
| `slash_vfx@offset_1_0.5_0` | on the character, offset by x=1, y=0.5, z=0 |

The prefab is instantiated ahead of time and enabled at the clip's start time,
then cleaned up when the clip ends.

### Reusing the game's VFX

You can also reference the character's **original** VFX instead of shipping your
own, using the `vfx` array in the JSON. It holds 1-based indices into the list of
the appearance's existing VFX tracks, collected across all of its motions. The
plugin logs that list at battle init:

```
[VFX Tracks] 10101_YiSang_BaseAppearance - 3 tracks:
  1: VFX Track [slash@0.10s]
  2: VFX Track [impact@0.35s]
  3: VFX Track [flash@0.00s]
```

Then in a coin:

```json
"vfx": [1, 3]
```
