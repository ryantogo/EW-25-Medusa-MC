# Prior art: SimpleWSO and NOMulticrew

## SimpleWSO
[Motmir/SimpleWSO](https://github.com/Motmir/SimpleWSO) v1.1.2 (game 0.34.2), by Bongus and Motmir. License: **CC0**, so we can copy and adapt freely. Credit is still given in the README and in file comments.

Usage: spectate a friendly aircraft and press H to become its gunner. Inside, a gunner gets independent station selection, their own target list, target sharing with the pilot (U), and camera offsets (K). It works on any aircraft. The pilot needs the mod too.

### What we reuse (adapted)
| Area | SimpleWSO file | What it solves |
| --- | --- | --- |
| Input | `Core/RewiredInput.cs` | `UnityEngine.Input` doesn't work in NO. Keys and vanilla actions ("Fire", "Next Weapon") have to be polled through Rewired. **Already adopted** in `src/Core/RewiredInput.cs`. |
| Serializers | `Net/SimpleWsoNet.cs` `RegisterSerializers` | Hand-written `Writer<T>.Write` / `Reader<T>.Read` for flat message structs, so no Mirage Weaver is needed. |
| Net init | `SimpleWsoNet.TryInit`, `Plugin.Update` | Lazily find the `NetworkClient` / `NetworkServer` (from any spawned `NetworkIdentity`), retry every 2 s, and reset on scene change. |
| Owner tick | `SimpleWsoNet.OwnerTick` | Desired-state model. A crew client streams `{aim, firing, targets}` frames (≤30 Hz, reliable), and the owner applies them every `FixedUpdate`. |
| Firing | `Gunner/GunnerWeaponFire.cs`, `Patches/GunnerWeaponFirePatch.cs` | Fire, launch and salvo a station without touching `currentWeaponStation`. An "authorized" depth counter plus a `WeaponStation.Fire` prefix stops vanilla from also firing a crew-owned station. |
| Own target list | `Patches/GunnerTargetListPatch.cs`, `VanillaGunnerView.BindGunnerTargetList` | Swap `CombatHUD.targetList` and `LaserDesignator.targetList` for the crew list. Patch `WeaponManager.Add/Remove/Clear/Get/SetTargetList` and `TargetListChanged` so HUD selection never writes the pilot's list. Block `SetStationTargets` for crew stations. |
| Crew view | `Gunner/VanillaGunnerView.cs` | Uses the game's own cockpit camera on someone else's aircraft: `CombatHUD.SetAircraft`, `FlightHud.SetAircraft`, a UI-only `StatusDisplay`, the minimap, cursor flags. Exit restores the previous follow and camera state. |
| Camera | `Patches/CameraFollowCheckPatch.cs`, `Patches/GunnerCameraOffsetPatch.cs` | Stop the spectator `FollowCheck` from dropping the followed aircraft. Offset `cameraPivot` in `CameraCockpitState.UpdateState`. That gives us per-seat camera positions. |
| Turrets | `Gunner/TurretController.cs`, `Patches/TurretRemoteGunnerAimPatch.cs` | Manual turret aim from streamed vectors. Target locks are replicated through the game's own rate-limited `SetStationTurretTarget`, sent only when the target changes. (The Medusa may have no turrets, but this stays useful.) |

### Traps it found (all would bite us)
- **Never throw from `onDisableUnit` handlers.** Vanilla raises them inside `Unit.UnitDisabled`, and an exception unwinds the teardown before `WaitRemoveAircraft`, so the airframe never despawns. Record the intent and leave on the next `Update`.
- **`StatusDisplay.Initialize` subscribes to `aircraft.onDisableUnit` and never unsubscribes.** If we destroy a crew-spawned status display, we have to remove that handler ourselves (`DetachStatusDisplayFromDisable`), or the same despawn bug happens.
- **The `ThreatList` missile alarms loop forever** unless they're cleaned up when the HUD rebinds (`CleanupHudAircraft`, `ThreatListOffMissileWarningPatch`).
- **The spectator `UnitDebug` overlay** has to be hidden when following a non-local aircraft. Leftover G-LOC and blackout effects have to be reset.
- **The cockpit camera bails** when `pilots[0].dead` or the aircraft has ejected, so leave the seat then instead of fighting it.
- **Its README says "The Medusa rattles … in the gunner seat."** That's probably camera shake taken from the remote-sim cockpit rigidbody. Expect it and plan to damp it.

### What we do differently
| SimpleWSO | Us | Why |
| --- | --- | --- |
| The server relays every message with `SendToAll`, including to unmodded clients, and the owner filters by `LocalSim`. | Relay only to the aircraft's owner (`aircraft.Identity.Owner`) and its seated crew, and only to peers that sent `CrewHello`. | An unknown message costs the receiver an error penalty (`SetError(60)`), and aim frames go out at 30 Hz. Targeted relay is also less traffic. |
| Handlers registered with `allowUnauthenticated: true`. No server checks. | Authenticated only. The server checks that the sender sits in that aircraft, that the station belongs to their seat, and the faction. | Otherwise any modded client could fire any aircraft's weapons. |
| One gunner per station, no seat roster ("I do not know if more than one person can be in an aircraft as a gunner"). | A server-owned roster: pilot plus WSO 1 and WSO 2. A station allocation table so the two WSOs never share a station, and the jammer pod split per mount. | Our core feature is two independent WSOs. |
| Spectate plus H. No consent. | Map click → "Join as WSO" → pilot accepts or declines. Spectate plus a key stays as a dev shortcut. | The pilot keeps control of who's aboard. |
| Every aircraft. | Aircraft in the `AircraftFilter` list (Medusa first). | Seats, stations and camera positions are tuned per aircraft. |
| Pilot presence is advertised every 5 s per aircraft. | Kept as-is, alongside the `CrewHello` handshake. | It's cheap and proves the owner can execute crew input. |

## NOMulticrew (Meteez)
[Meteez/NOMulticrew](https://github.com/Meteez/NOMulticrew) ships only a compiled `Multicrew.dll` (v0.1.2, July 2026), with no source and no license. **We can't reuse its code.** We can only try it as a player to compare features.
