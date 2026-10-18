# Unbound Potential

Buildable Forge of Potential for Valheim 1.0. This is a **source prototype, not a compiled/tested release**.

## Build

Open `UnboundPotential.csproj` in Rider. Set these MSBuild properties to your local paths (for example in an untracked `Directory.Build.props`):

- `BepInExLibDir`: directory containing `BepInEx.dll` and `0Harmony.dll`.
- `PublicizedAssemblies`: directory containing `assembly_valheim_publicized.dll`.
- `ValheimManaged`: Valheim's `valheim_Data/Managed` directory.

Compile against the game assemblies, **never ship them**. Copy only the compiled mod DLL to `BepInEx/plugins`.

## Configuration

The generated BepInEx config has `Forge.Enabled`, `Forge.Requirements` and `Forge.RequiredStation`.

Default requirements: `Bronze:10:True,FineWood:20:True,SurtlingCore:5:True,Stone:20:True`. The format is `PrefabName:Amount:Recover` separated by commas. `RequiredStation=forge` uses the vanilla forge prefab; empty means no nearby station requirement.

Changes require a world reload. This prototype does **not yet synchronize settings**, so do not deploy to multiplayer until ServerSync is integrated and tested.

## Initial test plan

1. Verify the prefab `UpgradeStation` and station prefab `forge` resolve at runtime.
2. Check the hammer Crafting category, icon, recipe and placement preview.
3. Build and interact with the station, then dismantle it and verify resource recovery.
4. Save/reload and test another client, after synchronization support is implemented.
5. Verify naturally generated `AncientUpgradeStation` discovery is unaffected.

The source-derived behavior has been checked against the supplied exported C# files; Unity prefab serialized data and runtime lifecycle still require testing.
