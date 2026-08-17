# Props

Motions can also spawn standalone visual objects not tied to a single motion
clip: a knife that rings a character and flies out on a strike, a totem
planted in the scene for a few rounds, a book that orbits forever. These are
**props**, declared in JSON like everything else here. No Unity editor needed
for a PNG-folder prop, same as [Sprite Motions](SpriteMotions.md).

## Declaring props

Props live in the same `CharacterVFX.json` as `allVFX`, under a new `props`
array:

```json
{
  "$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions/refs/heads/main/schema/charactervfx.json",
  "allVFX": [ ... ],
  "props": [
    {
      "folder": "props/book",
      "anchor": "unit",
      "count": 1,
      "radius": 0.8,
      "speed": 60
    }
  ]
}
```

That's a working prop: a book orbiting the character forever at a radius of
0.8 units, turning 60 degrees a second. See
[Character VFX](CharacterVFX.md) for how the file is set up and found.

## The three anchors

Every prop entry picks one anchor:

| `anchor` | Behaviour |
|---|---|
| `"unit"` (default) | Rings the character. Up to `count` instances orbit a centre point, and `actions` can throw them at a target and back. |
| `"world"` | One instance placed directly in the scene, at a fixed point. Cannot `strike`, see [Validation](#when-a-prop-fails-to-load) below. |
| `"target"` | A ring around **every enemy carrying this entry's `keyword`**, sized by that enemy's own stack. Takes no `actions` at all (see [Rings on your enemies](#rings-on-your-enemies)). |

A `world`-anchored entry with no `keyword` places its single persistent
instance as soon as the character's props are set up and leaves it there for
the whole battle (add a `keyword` to gate it on a buff). `plant` actions are
**additive**: they spawn separate instances during a motion, on top of the
persistent one. Set `"count": 0` for planted instances only.

## Art: PNG folder or bundle prefab

```json
{ "folder": "props/knife" }
```

`folder` points at a folder of PNGs, loaded exactly like a
[Sprite Motion](SpriteMotions.md): natural file sort, an optional
`animation.json` next to the frames for custom timing, otherwise 12fps in
file order. **The folder resolves relative to the `CharacterVFX.json` that
declares it**, not to the mod's root and not to the character folder. So
`"folder": "props/knife"` next to a `CharacterVFX.json` at
`custom_motions/10101_YiSang_BaseAppearance/CharacterVFX.json` means
`custom_motions/10101_YiSang_BaseAppearance/props/knife/`.

```json
{ "prefab": "KnifeVFX" }
```

`prefab` names a GameObject inside any `.bundle` under the character's
appearance folder, the same lookup `vfxName` uses in `allVFX`, so it can
live in a bundle you're already shipping.

**If both are set, `folder` wins** and `prefab` is ignored outright.

A folder-based prop's frames advance on their own clock from the instant it
spawns, on the same `animation.json` timing Sprite Motions uses. They never
sync to `start`/`arrive`; only the instance's *position* does.

## `PropEntry` fields

| Field | Type | Default | Meaning |
|---|---|---|---|
| `folder` | string | none | PNG folder, relative to this JSON. Wins over `prefab` when both are set. |
| `prefab` | string | none | GameObject name inside any bundle under this appearance. |
| `anchor` | string | `"unit"` | `"unit"`, `"world"` or `"target"`. Anything else fails to load. |
| `front` | bool | `true` | Unit anchor: parents to the front effect root instead of the back one. Every anchor: picks the default sorting order (see `order`). |
| `pos` | `[x,y,z]` | `[0,0,0]` | Unit: ring centre, local to the effect root. World: placement point, caster-relative unless `world` is `true`. |
| `world` | bool | `false` | World anchor only. Treat `pos` as absolute scene coordinates instead of relative to the caster. |
| `layer` | string | `"Front"` | Sorting layer name for every renderer on the instance. |
| `order` | int | `0` | Sorting order. `0` means "pick the default for this anchor and side": `1000` in front, `998` behind. |
| `scale` | double | `1` | Uniform size multiplier on every instance of this entry. `0` or less reads as `1`: an invisible prop looks exactly like a failed load, so it isn't a size you can ask for by accident. Applies to `prefab` props as well as PNG ones. |
| `count` | int | `1` | Instance count, when there's no `keyword` gate. Clamped to `maxCount` and to 16 regardless. |
| `radius` | double | `0` | Unit anchor: ring radius. |
| `parkRadius` | double | `0` | Ring radius while `park`ed, for a formation that should hang wider around an enemy than it orbits its own caster. `0` means "same as `radius`". |
| `speed` | double | `0` | Unit anchor: degrees per second the ring turns. `0` pins every slot in place. |
| `phase` | double | `0` | Unit anchor: the ring's start angle, in degrees. |
| `bob` | double | `0` | Vertical bob amplitude. Needs `bobPeriod` set too. |
| `bobPeriod` | double | `0` | Bob period, in seconds. `0` disables bobbing even if `bob` is set. |
| `spin` | double | `0` | Self-rotation, degrees per second. |
| `face` | bool | `false` | **`target` anchor only**: point every instance at the unit it orbits. On a strike, use the action-level `face` instead. |
| `keyword` | string | none | Buff keyword gating the prop. Omit for an always-on prop. |
| `stackThres` | int | `0` | Stack the buff needs before the gate passes. |
| `turnThres` | int | `0` | Turn count the buff needs before the gate passes. |
| `maxCount` | int | `16` | Ceiling on instances. Itself clamped to 16 (see below). Setting it to `0` means "no ceiling of my own", i.e. 16, *not* zero instances; use `count: 0` (or drop the entry) if you want none. |
| `actions` | array | none | `strike` and `plant` behaviours. See below. |

A unit-anchored instance is parented to the character's effect root but does
**not** inherit its sorting layer or order: every renderer gets `layer`/`order`
applied directly, same as a world-anchored instance. If your prop draws behind
or in front of the wrong thing, those two fields are what you want, not
`front`.

`keyword` works as it does in `allVFX`: same buff, same `stackThres`/`turnThres`
rule where falling short of *either* fails the gate outright rather than
counting partially (see [Character VFX](CharacterVFX.md)). The difference is
what a passing gate does: **`count` is ignored and the instance count becomes
the buff's current stack** (clamped to `maxCount`/16). Leave `keyword` out and
`count` is a flat number.

For a `world`-anchored entry, `count`/`keyword` only decide whether its
single instance *exists*; a gate passing with a stack of 5 does not spawn 5
totems. For more than one, use `plant` actions; each firing spawns its own,
independent of `count`.

## `PropAction` fields

```json
{
  "do": "strike",
  "motion": "S1",
  "start": 0.1,
  "arrive": 0.3,
  "returnAt": 0.5,
  "ease": "OutQuad"
}
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `do` | string | `"strike"` | `"strike"` or `"plant"`. Any other value is **rejected at load** - see below. |
| `motion` | string | none, required | A `MOTION_DETAIL` name, the same string an `S1.json` file is named after. |
| `coin` | int | `-1` | Which coin of that motion. `-1` matches any coin. |
| `slot` | string | `"next"` | Strike only. `"next"` hands the *i*-th matching action the *i*-th ring slot (deterministic, in JSON order); `"all"` moves every slot; a number is a 0-based slot index. |
| `start` | fraction | `0` | When the strike starts moving out, as a fraction of the motion's duration. |
| `arrive` | fraction | `0` | When it reaches its target. **Must match the coin's `GiveDamage` phase (see below).** |
| `returnAt` | fraction | `-1` | When it's back home. Negative means the instance is consumed on arrival instead of returning (see note below). |
| `at` | fraction | `0` | Plant only: when the instance is placed. |
| `to` | string | `"enemy"` | Strike/plant-anchor target. `"self"` (the caster), `"center"` (midpoint of caster and first target), `"group"` (middle of every target) and `"enemies"` (one target per slot, see below) are special-cased; anything else, including `"enemy"` itself and any typo, resolves to the first target. |
| `offset` | `[x,y,z]` | `[0,0,0]` | World-space offset added to the target point. |
| `pos` | `[x,y,z]` | `[0,0,0]` | Plant only: offset from the `to` anchor, or an absolute position when the entry has `"world": true`. |
| `ease` | string | none (linear) | One of the four ease names below. |
| `spin` | double | `0` | Degrees per second of self-rotation while this action is active, overriding the entry's ambient `spin`. `0` keeps the ambient spin. |
| `rounds` | int | `1` | Plant only: how many rounds the placed instance survives. `0` or less means until the battle ends. |
| `consume` | string | `"motion"` | What a consumed instance does afterwards. `"motion"` hides it until the motion ends; `"gate"` keeps it gone until the gate's count changes. Only applies when `returnAt` is negative. |
| `from` | string | `"slot"` | Where the strike launches from. `"slot"` is its own ring position; `"self"`, `"center"` and `"enemy"` resolve like `to` does. |
| `fromOffset` | `[x,y,z]` | `[0,0,0]` | World-space offset on that origin. `"self"` alone launches from the character's feet. |
| `arc` | double | `0` | World units the flight path bows. Positive arcs over, negative dips under. Zero at both ends, peaks mid-flight. |
| `windUp` | double | `0` | World units drawn back away from the target before the throw. |
| `windUpTime` | fraction | `0.3` | How much of the outbound window the draw-back takes. Ignored when `windUp` is `0`. |
| `face` | bool | `false` | Rotate the instance to lead along its direction of travel. Ignored if `spin` is set. |

`do` is a closed list. A value that isn't `"strike"` or `"plant"` fails the
whole entry at load with a named error, rather than quietly running as a
strike the way a typo used to — `"strke"` would load clean and then throw a
knife nobody had authored. The two kinds read different fields, so the loader
also checks each action against the kind it named: a `park` on a plant, or a
strike on a `"world"` entry, are both refused by name.

Every time field on an action (`start`, `arrive`, `returnAt`, `at`) is a
fraction of the *motion's* duration, exactly like `phases[].start` in a coin
JSON is a fraction of `totalDuration`. Same number, not a coincidence.

A negative `returnAt` doesn't destroy the instance, it switches the whole
object off, so a `prefab` prop with its own renderers hides too. That's
deliberate: a prop can't spend a buff stack the way an attack does, so
destroying it outright would only have the gate spawn a fresh one on the
next poll.

How long it stays gone is `consume`:

- **`"motion"`** (the default): gone until the motion restarts, then back in
  its ring slot. Good for a knife that's "thrown" every cast.
- **`"gate"`**: gone until the gate's count changes. On a `keyword` entry the
  instance returns when the buff's stack next moves, so a strike really does
  spend one. On a fixed `count` entry with no `keyword` the count never
  changes, so it's gone for the rest of the battle. That's the sharp edge;
  it's opt-in for a reason.

A gate-consumed instance stays where it fell out of the ring rather than
returning to its slot invisibly, and **the ring doesn't close up around it**:
three knives minus one leaves a gap, not an evenly spaced pair. If that reads
wrong, use `"motion"` and let it come back.

## Shaping the throw

By default a strike slides in a straight eased line from its ring slot to the
target. Four fields change that, and they compose:

```jsonc
{
  "do": "strike", "motion": "S1", "coin": 0,
  "start": 0.2, "arrive": 0.5, "returnAt": 0.8,

  "from": "self", "fromOffset": [0.3, 1.1, 0],
  "windUp": 0.4, "windUpTime": 0.35,
  "arc": 0.6,
  "face": true
}
```

- **`from`** moves the launch point off the ring. `"self"` plus a
  `fromOffset` at hand height throws from the character rather than from
  wherever the knife was orbiting.
- **`windUp`** draws the instance back *away* from the target first, along the
  same axis it will travel, so the throw reads as one motion reversed rather
  than a detour. The draw-back is not eased: running the throw's curve over it
  makes the wind-up drift instead of snapping taut.
- **`arc`** bows the path vertically without moving where the instance starts
  or lands, so your `arrive` timing is unaffected.
- **`face`** points the instance along its direction of travel, so a blade
  leads instead of arriving sideways. It's measured off the leg's own axis, not
  frame-to-frame movement, so it doesn't flicker at launch. **`spin` wins if
  you set both**: a rotation rate and an orientation rule can't both own the
  same transform. `face` also outlives the flight on a `park`: a `"ring"`-parked
  prop keeps re-aiming at the point it orbits, and `"hold"`/`"stack"` keep the
  angle they landed with.

The return leg ignores `windUp` and `from`: there's nothing to draw back from
on the way home.

## Hitting more than one enemy

`"to": "enemy"` aims at whichever target the game lists first, so on a skill
that hits three, all three knives pile onto one of them. Two `to` values read
the whole target list instead:

| `to` | Where it aims |
|---|---|
| `"enemies"` | **One target per slot**, dealt round-robin: slot 0 takes the first target, slot 1 the second, and it wraps when there are more slots than targets. Three knives on three enemies means one each; three knives on two enemies means the third doubles up on the first. |
| `"group"` | The **average position of every target**: the middle of the pack, wherever they're standing. Aim here when you want one point, not a spread. |

With no targets, both fall back to nothing: the action is skipped for that
frame rather than firing at the world origin.

`"enemies"` is a per-slot answer, so it only means anything on a `strike`. A
`plant` places one instance, not one per slot, so it reads `"enemies"` as
`"group"`: the sane reading of "put it at the enemies" when you only get one
spot.

**A spread survives a `park`.** Each slot remembers *which* target it was dealt,
so a formation parked across three enemies keeps tracking all three as they
move instead of collapsing onto the first. If a target stops existing, its slot
falls back to the last point it resolved, the same rule a single-target park
follows.

## Parking: move now, throw later

Everything above is a pure function of the *current* motion's clock: when the
motion ends, the prop is back in its ring slot. `park` is the exception. A
strike with `park` set ends by **leaving the instance where it landed**, and
that outlives the motion:

```jsonc
// S2: the knives fan out and hang over the enemy.
{ "do": "strike", "motion": "S2", "coin": 0, "slot": "all",
  "start": 0.15, "arrive": 0.45,
  "to": "enemy", "offset": [0, 1.4, 0],
  "arc": 0.8, "face": true,
  "park": "ring" }

// S3: they drop from wherever they're hanging.
{ "do": "strike", "motion": "S3", "coin": 0, "slot": "all",
  "start": 0.1, "arrive": 0.35,
  "to": "enemy", "offset": [0, 0.4, 0],
  "windUp": 0.3, "ease": "InQuad", "face": true }

// Recall them whenever you like.
{ "do": "strike", "motion": "S1", "coin": 0, "slot": "all",
  "start": 0.0, "arrive": 0.4, "to": "slot" }
```

The park point is the strike's own target (`to` plus `offset`), so there's
nothing extra to author, and it **tracks its anchor**: parked on `"enemy"`, the
formation follows that enemy as it moves, falling back to the last known point
if the target dies.

`park` takes the formation to hold there:

| Mode | What the parked slots do |
|---|---|
| `"ring"` | The entry's own orbit, recentred on the park point: speed and bob still apply, around the enemy instead of the caster. Set `parkRadius` on the entry to hang wider there than the caster's own ring; leave it and the radius is unchanged. With `face`, each slot keeps pointing *at* the park point as it circles. |
| `"hold"` | The same formation, frozen at the angle each slot arrived wearing. Still and aimed. |
| `"stack"` | Every parked slot sits on the point exactly. Fine for one prop; three become one. |

**A later strike launches from wherever the prop is standing**, so the S3 action
above needs no special field: `from` still defaults to `"slot"`, and for a
parked prop "its slot" *is* the parked spot. `windUp`, `arc` and `face` work
off that.

**Park is sticky.** An ordinary strike borrows a parked prop and gives it back:
if it has a `returnAt`, it returns to the parked spot, not to the ring. Two
things end a park:

- **`"to": "slot"`**, a strike aimed at the prop's own ring slot. Arriving
  there un-parks it. That's the explicit recall in the example above.
- **`"parkUntil": "round"`** on the *entry*: every parked slot snaps home at
  the next round start, so a park can't leak across turns. The default,
  `"battle"`, keeps them parked until something recalls them.

`park` and `returnAt` on the same strike are **rejected at load** with a named
complaint. They're two different endings, and guessing which one you meant is
the kind of wrong that's invisible in game.

## Rings on your enemies

`park` moves the caster's own instances, and an entry owns a fixed pool of
them, so parking three knives on a second enemy takes them off the first.
There is no `count` you can write that makes "one set per enemy I have
debuffed" work, because the sets aren't the caster's to hand out.

`"anchor": "target"` solves that. It rings **every enemy carrying the
entry's `keyword`**, and the count on each one is *that enemy's* stack:

```json
{
  "folder": "props/knife",
  "anchor": "target",
  "keyword": "Bleeding",
  "stackThres": 1,

  "radius": 0.9,
  "speed": 60,
  "bob": 0.1,
  "bobPeriod": 2.0,
  "face": true
}
```

Bleed three enemies and all three get knives, sized to how much bleed each is
carrying. The props are a pure function of who's standing there and what
they're holding, so **the debuff expiring is what removes them**: nothing to
recall, nothing that leaks across a round.

| Field | Meaning here |
|---|---|
| `keyword` | Read off **the enemy**, not the caster. Omit it and every living enemy gets a ring of `count`. |
| `stackThres` / `turnThres` | Same all-or-nothing rule as everywhere else: below either one, that enemy gets nothing. |
| `pos` | Offset from the enemy's own position, so `[0, 1.4, 0]` rings them at chest height. |
| `radius` / `speed` / `phase` / `bob` / `spin` | The ring's shape, exactly as on a unit anchor. |
| `face` | Entry-level here (not on an action): every instance points **at the enemy it orbits**. |
| `maxCount` | Caps the ring **per enemy**, since the count comes from that enemy's stack. |

A target entry takes **no `actions`** and is rejected at load if you give it
any. Its rings follow the keyword, not the motion clock, so a `strike` or
`plant` on one would be silently ignored, and silence is the worst way to
find that out.

## Strikes must arrive when `GiveDamage` fires

**The schema can't check this one.**

Props are purely visual: they never deal damage, never run a hit check, and
have no idea what a coin's `phases` array contains. A `strike` Lerps an
instance toward a target between `start` and `arrive`, on its own clock. To
make a thrown knife *look like* it causes the hit, line up the two clocks
yourself:

**A `strike`'s `arrive` must equal the `start` of the coin's `GiveDamage`
phase.**

Both are fractions of the same duration, the coin's `totalDuration`, which
is also what `MotionDuration` is built from (see [JSON Reference](JsonReference.md)).
Setting them to the same number puts the knife's flight and the damage
number on the same frame. Get them out of sync and the knife either lands
before the hit connects, or the enemy staggers before it's been touched.

Worked example. `S1.json`:

```json
{
  "coins": [{
    "totalDuration": 1.0,
    "phases": [
      { "type": "GiveDamage", "start": 0.3, "end": 0.3, "steps": 1 }
    ],
    "hitCheckers": [{ "time": 1.0 }]
  }]
}
```

`CharacterVFX.json`, on the same character:

```json
{
  "props": [{
    "folder": "props/knife",
    "anchor": "unit",
    "count": 1,
    "radius": 1.2,
    "actions": [{
      "do": "strike",
      "motion": "S1",
      "start": 0.1,
      "arrive": 0.3,
      "returnAt": 0.5,
      "ease": "OutQuad"
    }]
  }]
}
```

`GiveDamage.start` and the action's `arrive` are both `0.3`. The knife
leaves its ring slot at `0.1`, reaches the target exactly when the hit
lands at `0.3`, and eases back home by `0.5`. Change one `0.3` without the
other and the timing drifts.

## The four eases

`ease` shapes the 0..1 progress between `start`/`arrive` (or `arrive`/`returnAt`
for the trip back). Names are case-insensitive; anything not on this list,
including leaving `ease` out, is linear:

| `ease` | Curve |
|---|---|
| `InQuad` | Starts slow, speeds up. |
| `OutQuad` | Starts fast, slows into the target. |
| `InOutQuad` | Slow, fast, slow. |
| *(none / unrecognized)* | Linear. |

These are hand-written curves, not DOTween ease names: `zooms` and `rotates`
elsewhere in a coin JSON take the full DOTween list, props take only these
four.

## The 16-instance ceiling

Every prop entry draws from two independent pools, each capped at **16**
instances alive at once: its ring/gated-world pool (whatever `count`,
`maxCount`, or a gating buff's actual stack say) and its planted pool
(however many live `plant` instances its motion has spawned). An entry with
both a ring and `plant` actions can therefore have 16 ring instances *and* 16
planted ones standing at the same time: 32 in total, not one shared pool of
16. `maxCount` is clamped to 16 if you set it higher, and a plain `count` of
`500` clamps down the same way, so a typo'd threshold or an unexpectedly high
stack can't spawn hundreds of objects into a battle.

On the planted side the seventeenth is skipped, not swapped in for the oldest
(a totem you placed stays where you put it), and you get one `[Props]` line in
the log the first time an entry hits the ceiling. The count is per appearance
*and* per entry, so one appearance's plants never crowd out another's. Two
characters wearing the *same* custom appearance in one battle do share a
single pool of 16 for that entry.

## Plants

A `plant` action fires once, the instant the motion's fraction crosses
`at`, and spawns a standalone instance in the world, independent of the
entry's ring and of any other plant on the same entry. It fires again the
next time the motion plays from the start.

```json
{
  "props": [{
    "folder": "props/totem",
    "anchor": "world",
    "count": 0,
    "actions": [{
      "do": "plant",
      "motion": "S2",
      "at": 0.4,
      "to": "self",
      "rounds": 3
    }]
  }]
}
```

`count: 0` matters here: `PropEntry.count` defaults to `1`, and a `world`
entry with no `keyword` places that persistent instance immediately,
regardless of `actions`. Leave it out and you get a totem at the caster's
feet from the start of battle *plus* whatever the plant later adds. With
`count: 0`, casting `S2` places a totem at the caster's position 0.4 of the
way through the motion, and it survives 3 rounds before disappearing
(`rounds: 0` or lower would leave it until the battle ends).

A plant's position is either `to` (`"enemy"`/`"self"`/`"center"`) plus
`pos` as a relative offset, or, if the *entry* has `"world": true`, `pos`
read as an absolute scene position.

`rounds` is a `PropAction` field only; there is no entry-level `rounds`. A
persistent (non-planted) world instance lives exactly as long as its
`keyword` gate keeps passing, or the whole battle if the entry has no gate.

## The schema

That `$schema` line on the first field is worth adding: `CharacterVFX.json`
has its own schema, the same way a coin JSON does:

```json
"$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions/refs/heads/main/schema/charactervfx.json"
```

VS Code (and anything else that reads `$schema`) then autocompletes every
field on this page, shows the description as you type, and underlines the
mistakes *before* you launch the game. It catches every rule the loader
rejects an entry for: an entry with neither `folder` nor `prefab`, an
unknown `anchor`, `do` or `park` mode, an action with no `motion`, `park` and
`returnAt` set together, a strike on a `world` entry, actions on a `target`
entry, a `slot` that is not
`all`/`next`/a number. It also catches the ones the loader can only shrug at:
a misspelled field name, and a fraction written as `40` instead of `0.4`.

Comments are fine alongside it: the loader skips `//` lines and tolerates
trailing commas, so a commented `CharacterVFX.json` still validates and loads.

## When a prop fails to load

Everything degrades quietly. Search `BepInEx/LogOutput.log` for `[Props]`.

Two failures happen at **load time**, when the character's props are first read
out of `CharacterVFX.json`, so they show up before the character ever acts:

- **An entry is rejected outright** (and skipped) if it has neither `folder`
  nor `prefab`, an unrecognized `anchor`, a null action, an action with no
  `motion`, or a `strike` action on a `world`-anchored entry. The log line
  names which of these it was.
- **A `folder` that doesn't exist, or produces no usable frames**, rejects
  only that entry, on the same undecodable-PNG rules as Sprite Motions.

One failure happens at **spawn time**: the entry validates and loads fine, and
the error only appears the first time Motions tries to create an instance (a
unit prop's first ring slot, or a world prop's placement):

- **A `prefab` name not found in any bundle** rejects only that instance,
  logged whenever an instance of it is next created.

Three failures are never logged at all, at any time:

- **An unknown `motion` name.** The prop loads fine and the action never
  triggers. If a strike or plant never seems to fire, check `motion` against
  the `MOTION_DETAIL` name your `S1.json` (or similar) is named after. See
  [Mod Layout](ModLayout.md).
- **A character with props but no motions of its own.** Actions run off the
  motion clock, which only exists while a modded motion is playing. A
  character whose folder has a `CharacterVFX.json` with `props` but no
  bundle and no sprite motion folders never starts one, so its props get
  their rings, bobbing and `keyword` gates, but no `strike` or `plant` ever
  fires. Give the character at least the one motion the actions name.
- **A `layer` name that doesn't exist in your project.** Unity swallows it
  with at most an engine-side warning of its own, and the prop draws on the
  default layer instead of the one you asked for. `layer` is free-form, so a
  typo looks exactly like a sorting-order problem. Check the spelling before
  you start moving `order` around.
