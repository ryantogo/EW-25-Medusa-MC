# EW-25 Medusa Multi-Crew

A BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) that lets up to three players crew one EW-25 Medusa:

| Seat | Role |
| --- | --- |
| Pilot | Flies the aircraft and handles throttle, gear and countermeasures |
| WSO 1 | Targeting camera, radar/EW, weapon selection, target lists, firing |
| WSO 2 | Same as WSO 1, with its own weapons and targets |

**Status: early development.** The current build only loads and logs the Medusa's seat and weapon layout (recon). Multi-crew is not playable yet.

The host and every crew member need the plugin installed.

## Building

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download) and Nuclear Option with BepInEx 5 installed.

```
dotnet build -c Release
```

The build references the game's DLLs from your install and deploys `MedusaMultiCrew.dll` plus a `meta.json` to `<game>/BepInEx/plugins/com.ryantogo.medusamulticrew/`. That's the layout the NOMM mod manager uses, so the mod shows up in NOMM and can be enabled or disabled there. If NOMM has it disabled, the build updates the copy in `BepInEx/disabledPlugins/` instead of re-enabling it. If the game isn't in the default Steam folder, pass `-p:GameDir="D:\path\to\Nuclear Option"`, or create `GameDir.props.user`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>D:\SteamLibrary\steamapps\common\Nuclear Option</GameDir>
  </PropertyGroup>
</Project>
```

Add `-p:DeployToGame=false` to build without copying.

## Config

`BepInEx/config/com.ryantogo.medusamulticrew.cfg` is created on first launch:

- `General.AircraftFilter`: which aircraft get multi-crew (default `Medusa`).
- `Debug.ReconLogging`: log aircraft layouts when weapons load.
- `Debug.ReconDumpKey`: dump your current aircraft's layout (default F10).

## Docs

- [Game internals](docs/game-internals.md): how the game handles ownership, seats, weapons, targeting and networking, and where the mod hooks in.

## License

GPL-3.0. See [LICENSE](LICENSE).
