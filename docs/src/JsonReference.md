# JSON Reference

One JSON file per motion, named after the motion (`S1.json`), sitting next to the
bundle. [Attacks](Attacks.md) walks through writing one; this page is the full
list of what you can put in it.

Add the schema line for autocompletion in VSCode:

```json
"$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions-schema/refs/heads/main/schema.json"
```

## Time is a fraction, not seconds

Inside a coin, `totalDuration` is the only value in seconds. **Every other time
is a fraction of it**, between `0.0` and `1.0` — `start`, `end`, `hitCheckers.time`,
and the `start` of zooms, rotates and shakes are all multiplied by `totalDuration`.

So in a coin with `"totalDuration": 2.0`, a phase at `"start": 0.5` fires one
second in. (Zoom, rotate and shake **durations** are the exception: those are in
seconds.)

## Top level

```json
{
  "coins": [ { ... }, { ... } ]
}
```

One object per coin, in order. A coin's object is:

| Field | Type | Meaning |
|---|---|---|
| `totalDuration` | number | Length of the coin, in seconds. |
| `phases` | array | Movement and damage — see below. |
| `hitCheckers` | array | When the coin ends. |
| `zooms` | array | Camera zooms. |
| `rotates` | array | Camera rotations. |
| `shakes` | array | Camera shakes. |
| `vfx` | array of int | Reuse the character's original VFX tracks, 1-indexed. See [Bundles](Bundles.md). |

## phases

```json
{
  "type": "GiveDamage",
  "start": 0.3,
  "end": 0.3,
  "steps": 1
}
```

| Field | Type | Meaning |
|---|---|---|
| `type` | string | `Relative`, `ToTargetWide`, `MoveEnemy` or `GiveDamage`. Anything else is ignored. |
| `start` / `end` | fraction | When the phase runs. |
| `steps` | int | How many markers to spread evenly from `start` to `end`. `1` = a single marker at `start`. `0` or less = the phase is skipped. |

The remaining fields depend on `type`.

### Relative

Moves the character by an offset from where it stands.

| Field | Type | Default |
|---|---|---|
| `move` | `{x, y, z}` | `0,0,0` |
| `isRefreshDir` | bool | `false` |

### ToTargetWide

Moves the character to the target, stopping short by `move`.

| Field | Type | Default |
|---|---|---|
| `move` | `{x, y, z}` | `0,0,0` — arrival offset from the target |

### MoveEnemy

Moves the **target** rather than the attacker, by `move`. Same fields as
`Relative`.

### GiveDamage

Applies the visual hit — damage numbers, knockback, hit reaction.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `damageRatio` | float | `1.0` | Fraction of the coin's damage dealt at this marker. Split it across markers for multi-hit skills (e.g. `0.5` and `0.5`). Ignored if `0`. |
| `damage.multiHit` | int | `1` | Number of visual hits. |
| `damage.isUpAttack` | bool | `false` | Launches the target upward. |
| `damage.multiHitDuration` | float | `0` | Spacing between the visual hits. |
| `sturn` | object | see below | Knockback configuration. |

`sturn`:

| Field | Type | Default |
|---|---|---|
| `sturnType` | string | `KNOCKBACK` |
| `sturnDir` | string | `DIR_TOTARGET` |
| `sturnTiming` | string | `ALL` |
| `forcePower` | float | `5.0` |
| `randomPower` | float | `5.0` |
| `airborneAngle` | float | `0.0` |
| `isRotateTarget` | bool | `false` |
| `targetRotateAngle` | float | `0.0` |

## hitCheckers

Marks the point at which the coin can hand off to the next one.

```json
"hitCheckers": [{ "time": 1.0, "isNextMotionCoinDelay": 0.0 }]
```

If you leave `hitCheckers` out, Motions inserts one at `0.15` of the coin.

## zooms

```json
"zooms": [{ "start": 0.2, "duration": 0.5, "size": -2, "easeType": "OutQuad" }]
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `start` | fraction | — | When the zoom begins. |
| `duration` | seconds | — | How long the zoom clip lasts. |
| `attacker` | bool | `true` | Include the attacker in the framing. |
| `targets` | bool | `true` | Include the targets in the framing. |
| `between` | float | `0` | Bias the focus point between attacker and targets. |
| `axisY` | float | `0` | Vertical offset of the focus point. |
| `size` | float | `-2` | Zoom amount. Negative zooms in when `isRelative`. |
| `zoomDuration` | float | `-1` | Zoom travel time. `-1` = use the clip duration. |
| `isRelative` | bool | `true` | Treat `size` as relative to the current zoom. |
| `focusSpeed` | float | `0.2` | How fast the camera chases the focus point. |
| `easeType` | string | `Unset` | Any DOTween ease name, e.g. `OutQuad`, `InOutSine`. |

## rotates

```json
"rotates": [{ "start": 0.0, "duration": 0.4, "targetAngle": { "x": 0, "y": 0, "z": 15 } }]
```

| Field | Type | Meaning |
|---|---|---|
| `start` | fraction | When the rotation begins. |
| `duration` | seconds | How long it lasts. |
| `targetAngle` | `{x, y, z}` | Euler angles to rotate the camera to. |
| `focusRotateSpeed` | float | How fast the camera chases the angle. |
| `easeType` | string | DOTween ease name. |

## shakes

```json
"shakes": [{ "start": 0.3, "duration": 0.2, "strength": 0.25 }]
```

| Field | Type | Default |
|---|---|---|
| `start` | fraction | — |
| `duration` | seconds | — |
| `strength` | float | `0.25` |
| `vibrato` | int | `120` |
| `randomness` | float | `90` |
| `fadeOut` | bool | `true` |
