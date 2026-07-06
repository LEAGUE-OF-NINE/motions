# Motions

A BepInEx IL2CPP plugin for Limbus Company that lets you inject custom motion
timelines (animation, VFX, and FMOD audio cues) onto character appearances,
loaded from mod folders at runtime.

## Development

1. Install and open Lethe at least once
2. Copy `Directory.Build.example.props` to `Directory.Build.props`
3. Edit `Directory.Build.props` to point `LimbusCompanyFolder` at your game install
4. Building the project copies `Motions.dll` straight into your game's `BepInEx/plugins` folder
