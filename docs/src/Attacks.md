# Animating Attacks

We'll continue by animating **S1**, following the same workflow used for the idle animation (see [Your First Motion](YourFirstMotion.md)).

{{#template templates/video.md id=assets/animate_s1.mp4}}

After exporting, check how it looks in-game:

{{#template templates/video.md id=assets/anim_no_json.mp4}}

The animation plays, but nothing happens: the character doesn’t move, and Sinclair doesn’t get knocked back. To fix this, we need to add the JSON file.

Create a file called `S1.json` in the motions.bundle folder.

---

## Understanding the JSON File

Initially, `S1.json` looks like this:

```json
{
  "$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions-schema/refs/heads/main/schema.json",
  "coins": [
    {
      "totalDuration": 0.0,
      "phases": [],
      "hitCheckers": []
    }
  ]
}
```

**Key points:**

- `$schema`: Helps with autocompletion and validation in VSCode.
- `coins`: List of coins that make up the motion (we only have one).
  - `totalDuration`: Duration of the coin.
  - `phases`: Actions within the coin.
  - `hitCheckers`: Marks when the coin ends. Usually one per coin.

Since our animation timeline is `0.15s`, set `totalDuration` to `0.15`.

Currently, the JSON does nothing without **phases** and a **hitChecker**. We want the animation to:

1. Move the character toward the target.
2. Apply damage and knockback.

---

## Adding Phases

We'll give the coin **two phases**: `ToTargetWide` and `GiveDamage`.

### Phase 1: ToTargetWide

```json
{
  "type": "ToTargetWide",
  "start": 0.0,
  "end": 0.15,
  "steps": 1,
  "move": { "x": 1, "y": 0, "z": 0 },
  "isRefreshDir": false
}
```

**Explanation:**

- `type`: Moves the character toward the target.
- `start` / `end`: Time when the action occurs.
- `steps`: Number of repetitions (1).
- `move`: Offset relative to the target (`x = 1` to avoid overlap).
- `isRefreshDir`: Whether to refresh the character’s facing direction (untested).

> Result: Character moves to 1 unit left of the target over `0.15s`.

---

### Phase 2: GiveDamage

```json
{
  "type": "GiveDamage",
  "start": 0.15,
  "end": 0.15,
  "steps": 1,
  "sturn": {
    "sturnType": "KNOCKBACK",
    "forcePower": 5,
    "randomPower": 0,
    "sturnTiming": "ALL"
  }
}
```

**Explanation:**

- `type`: Applies damage to the target.
- `start` / `end`: Damage occurs at the end of the motion.
- `sturn`: Knockback configuration:
  - `sturnType`: Type of knockback.
  - `forcePower`: Strength of knockback.
  - `randomPower`: Random variation (0).
  - `sturnTiming`: When the knockback applies.

---

## Adding the HitChecker

The coin ends at `0.15s`, so the hitChecker is:

```json
"hitCheckers": [{ "time": 0.15 }]
```

---

## Full `S1.json`

```json
{
  "$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions-schema/refs/heads/main/schema.json",
  "coins": [
    {
      "totalDuration": 0.15,
      "phases": [
        {
          "type": "ToTargetWide",
          "start": 0.0,
          "end": 0.15,
          "steps": 1,
          "move": { "x": 1, "y": 0, "z": 0 },
          "isRefreshDir": false
        },
        {
          "type": "GiveDamage",
          "start": 0.15,
          "end": 0.15,
          "steps": 1,
          "sturn": {
            "sturnType": "KNOCKBACK",
            "forcePower": 5,
            "randomPower": 0,
            "sturnTiming": "ALL"
          }
        }
      ],
      "hitCheckers": [{ "time": 0.15 }]
    }
  ]
}
```

Place `S1.json` in the same folder as `motions.bundle`.

---

## Testing In-Game

Check how it plays now:

{{#template templates/video.md id=assets/s1_finished.mp4}}

The animation works: the character moves, hits, and applies knockback.

Next, let's animate **S2**, which is a multi-attack motion.

---

# Animating Multi-Attack Motions

Multi-attack motions require one timeline per coin in Unity. We'll start with the **first coin**:

1. Drag the `S2_XXX` sprites into the timeline.
   - Do **not** select `S2_a_XXX` sprites; those belong to the second coin.

{{#template templates/video.md id=assets/animate_S2_0.mp4}}

---

### Creating the Second Coin

Each coin must have a separate timeline, prefixed with the skill name and its coin number. For the second coin of **S2**, name the timeline `S2_1`.

Duplicate the first timeline to create it:

{{#template templates/video.md id=assets/create_S2_timeline.mp4}}

Bind the timeline to the scene by dragging it into the hierarchy and connecting the `Appearance` object:

{{#template templates/video.md id=assets/S2_bind_appearance.mp4}}

Replace the copied sprites with the `S2_a` sprites to animate the second coin:

{{#template templates/video.md id=assets/animate_S2_1.mp4}}

Export and move the animation to your mod folder.

---

## Creating the JSON for S2

Since **S2** has two coins, each coin needs its own object.

Make a file called `S2.json` in the mod folder with the json below.

> **Note**
> The .json files should be in the motions.bundle folder

```json
{
  "$schema": "https://raw.githubusercontent.com/LEAGUE-OF-NINE/motions-schema/refs/heads/main/schema.json",
  "coins": [
    {
      "totalDuration": 1.0,
      "phases": [
        {
          "type": "ToTargetWide",
          "start": 0.0,
          "end": 0.15,
          "steps": 1,
          "move": { "x": 1, "y": 0, "z": 0 },
          "isRefreshDir": false
        },
        {
          "type": "GiveDamage",
          "start": 0.3,
          "end": 0.3,
          "steps": 1,
          "sturn": {
            "sturnType": "KNOCKBACK",
            "forcePower": 1,
            "randomPower": 0,
            "sturnTiming": "ALL"
          }
        }
      ],
      "hitCheckers": [{ "time": 1.0 }]
    },
    {
      "totalDuration": 1.0,
      "phases": [
        {
          "type": "ToTargetWide",
          "start": 0.0,
          "end": 0.15,
          "steps": 1,
          "move": { "x": 1, "y": 0, "z": 0 },
          "isRefreshDir": false
        },
        {
          "type": "GiveDamage",
          "start": 0.3,
          "end": 0.3,
          "steps": 1,
          "sturn": {
            "sturnType": "KNOCKBACK",
            "forcePower": 1,
            "randomPower": 0,
            "sturnTiming": "ALL"
          }
        }
      ],
      "hitCheckers": [{ "time": 1.0 }]
    }
  ]
}
```

- `totalDuration`: Duration of the coin.
- `GiveDamage.start`: Syncs damage with the animation timeline.

To give the first coin some impact, let's make it end at 1.0 (`totalDuration` and `hitChecker`), while the hit itself (`GiveDamage`) is at 0.3.

For the second coin, just copy the first one.

> Tip: Check the Unity timeline window to confirm exact timings of your animations.

---

## Testing In-Game

{{#template templates/video.md id=assets/S2_broken_anim.mp4}}

It kinda works!! but our Mei Ling is turning into Yi Sang, this is because the **second timeline wasn't attached** to the motion bundle. Let's fix this:

{{#template templates/video.md id=assets/fix_motion_bundle.mp4}}

Now export the motion bundle and test again:

{{#template templates/video.md id=assets/S2_working.mp4}}

The multi-attack motion works, but it's not perfect you are encouraged to experiment with the values to get the desired result.

You can find more sprites for Mei Ling and other touhou characters here: [https://www.spriters-resource.com/pc_computer/touhou123/](https://www.spriters-resource.com/pc_computer/touhou123/)

You now know how to animate and configure multi-attack skills. Next, move on to [SFX](SFX.md).
