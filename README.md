# Motions

A BepInEx IL2CPP plugin for Limbus Company that lets you inject custom motion
timelines (animation, VFX, and FMOD audio cues) onto character appearances,
loaded from mod folders at runtime.

## How it works

At a high level: Harmony patches hook the game's character/appearance lifecycle,
load Unity Timeline assets from mod folders (`BepInEx/plugins/Lethe/mods`,
shared with Lethe), and attach a
"sidecar" GameObject that plays the custom timeline in sync with the original
character, firing sound and VFX cues along the way.

## Documentation

[league-of-nine.github.io/motions](https://league-of-nine.github.io/motions) - guide and reference for **making motions** (setting up Unity, creating your first motion, mod layout, JSON reference, and more).

Sources live in `docs/src` as an mdBook. To preview locally:

```sh
cargo install mdbook mdbook-template
mdbook serve docs --open
```

## Building the plugin

### Prerequisites

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Lethe](https://lethelc.site) installed

### Steps

1. Install everything above and launch the game once so BepInEx generates its `interop` assemblies
2. Copy `Directory.Build.example.props` to `Directory.Build.props`
3. Edit `Directory.Build.props` to point `LimbusCompanyFolder` at your game install
4. `dotnet build` - this compiles `motions.dll` and copies it straight into your game's `BepInEx/plugins` folder

Then just launch the game to test.

## Code layout

| File | What it does |
| ---- | ------------ |
| `Plugin.cs` | BepInEx entry point; sets up config, logging, and Harmony patches |
| `Motions/Motions.cs` | Harmony patches that orchestrate the motion system |
| `Motions/BuffPatches.cs` | Harmony patches for buff VFX |
| `Motions/MotionData.cs` | Central caches and asset-lookup helpers (no patches, no timeline logic) |
| `Motions/MotionInjector.cs` | Sidecar attachment, motion injection, and custom motion playback |
| `Motions/CueExtractor.cs` | Extracts sound/VFX cues from bundle timelines; timeline caching |
| `Motions/SidecarSyncBehavior.cs` | Runtime behaviour on the sidecar: syncs with the master director, fires cues |
| `Motions/TimelineBuilder.cs` | Builds Unity Timeline assets |
| `Motions/Fmod.cs` | FMOD audio helpers |
| `Motions/Types.cs` | Shared types |

## Contributing

- Want to **make motions** (no coding)? Start with the [docs](https://league-of-nine.github.io/motions) - improvements to the guide are welcome too, sources are in `docs/src`.
- Want to hack on the **plugin**? Build it as above, then look at `Motions/Motions.cs` - every feature starts from a Harmony patch there.
- Open an issue or PR on GitHub; small, focused PRs are easiest to review.
