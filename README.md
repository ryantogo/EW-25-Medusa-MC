# EW-25 Medusa Multi-Crew

A BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) that lets up to three players crew one EW-25 Medusa:

| Seat | Role |
| --- | --- |
| Pilot | Flies the aircraft and handles throttle, gear and countermeasures |
| WSO 1 | Targeting camera, radar/EW, weapon selection, target lists, firing |
| WSO 2 | Same as WSO 1, with its own weapons and targets |

**Status: early test builds.** Players can take a WSO seat (pilot accept/decline, server-checked roster, weapon split), see the Medusa's cockpit and HUD from that seat, and select targets and fire their own stations with the vanilla controls. See [docs/testing.md](docs/testing.md).

The host and every crew member need the plugin installed.

## Controls

- **Joining:** spectate a friendly Medusa and press **F6** to take a WSO seat. Press F6 again to leave. The pilot gets a **Crew Request** box at the top of the screen and answers with **F7** (accept) or **F8** (decline), or by clicking its buttons. The request expires after 20 s.
- **As WSO:** the normal game controls apply. **Select** targets on the HUD, **Next/Previous Weapon** cycles through your stations, **Fire** fires, **Radar** toggles the radar.
- **Default split:** WSO 1 gets the Laser and left jamming pods. WSO 2 gets the ARAD-116 pylons and right pods. The pilot keeps countermeasures.

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
- `General.AllowAiAircraft`: let players crew AI-flown aircraft (host setting, default on).
- `Keys.JoinLeaveKey` / `AcceptKey` / `DeclineKey`: join or leave a seat while spectating (F6), and the pilot's accept/decline (F7/F8).
- `Seats.Wso1CameraPosition` / `Wso2CameraPosition`: each seat's eye position in aircraft-local metres.
- `Seats.DampRemoteCockpitShake`: stop the network-jitter rattle in crew seats (default on).

## Docs

- [Game internals](docs/game-internals.md): how the game handles ownership, seats, weapons, targeting and networking, and where the mod hooks in.
- [Prior art](docs/prior-art.md): what this mod reuses from SimpleWSO and where it differs.
- [Testing](docs/testing.md): in-game test steps for each phase.

Don't run this mod together with SimpleWSO. Both patch the same game code.

## Credits

Parts of the input, networking, crew view and weapon-firing code are adapted from [SimpleWSO](https://github.com/Motmir/SimpleWSO) by Bongus and Motmir, released under CC0.

## License

GPL-3.0. See [LICENSE](LICENSE).
