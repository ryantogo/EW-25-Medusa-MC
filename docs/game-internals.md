# Nuclear Option internals relevant to multi-crew

Notes from decompiling `Assembly-CSharp.dll` and `Mirage.dll` (game build `cb745d6c44f1`, BepInEx 5.4.23).
Game code is **not** committed here. To regenerate the reference sources locally:

```
dotnet tool install -g ilspycmd --version 9.1.0.7988
ilspycmd -p -o <out>/Assembly-CSharp -r "<game>/NuclearOption_Data/Managed" "<game>/NuclearOption_Data/Managed/Assembly-CSharp.dll"
```

File references below are relative to that decompiled project.

## Ownership and simulation
- `Aircraft : Unit : NetworkBehaviour` (Mirage). Its owner is `playerRef.Player` (`Aircraft.Player`).
- Only the owning client simulates physics: `CheckIfLocalSim()` returns `Player.IsLocalPlayer`, or server-active for AI aircraft (`Aircraft.cs` ~1330). Everyone else runs simple physics (`remoteSim`).
- `SetLocalSim` logs *"Invalid transition: Cannot transition from remote simulation to local simulation after spawn!"* when going remote → local. It still proceeds. This is one reason handing the flight controls to another seat is out of scope.
- `OnAuthorityChanged(false)` on a client turns local sim off.
- On spawn (`OnStartClient`), the owner calls `SetupLocalPlayerAndUI()`. That runs `Player.AttachToAircraft`, puts `pilots[0]` into `playerState`, creates the status display and HUD extras, sets the camera to follow plus cockpit state, and enables the map. `CombatHUD.SetAircraft(this)` runs for the local player.

## Seats
- `Aircraft.pilots : Pilot[]`. Every pilot gets `AssignPilotNumber(index)`, but only `pilots[0]` is ever driven. `Pilot_OnInitialize` returns early for any other pilot, and `Player.AttachToAircraft` only sets `pilots[0].player`.
- Ejection spawns one `PilotDismounted` per pilot (`SpawnEjectingPilot(n)`). Only pilot 0 gets the `Player`.
- When pilot 0 dies, the server disables the aircraft and ejects the survivors after 1 s (`Pilot.ApplyDamage` → `CommandEjection`).
- Seat camera: `Unit.cockpitViewPoint` (public `Transform`). Pilot transforms give the other seat positions. Run the recon dump (below) to get the Medusa's real values.

## Player input → actions (`PilotPlayerState.cs`)
Runs on the owner only, using Rewired `ReInput.players.GetPlayer(0)`:
- Axes: Pitch, Roll, Yaw, Throttle and Custom Axis 1 go to `aircraft.GetInputs()` (`ControlInputs`). They're then compressed and sent (`CompressedInputs`, `ApplySetInputs`).
- Buttons: Next/Previous Weapon go to `WeaponManager.Next/PreviousWeaponStation`. Fire goes to `Pilot.Fire` → `WeaponManager.Fire`. Turret Control goes to `CombatHUD.ToggleAutoControl`. Radar goes to `Aircraft.CmdToggleRadar`. Gear, Countermeasures, Next Countermeasure, Eject, Flight Assist, Nav Lights, Toggle Engine and Link Guns map to their obvious calls.

**Seat ownership of inputs (target design):**
- Pilot: axes, gear, countermeasures, flight assist, eject, engine, nav lights, plus any stations it still owns.
- WSO 1 / WSO 2: weapons, targeting, TargetCam, radar/EW, jammers. Each has its own stations and targets.

## Weapons (`WeaponManager.cs`, `WeaponStation.cs`)
- Stations are created per `WeaponInfo` (`RegisterWeapon`), and `OrganizeWeaponStations` numbers them `0..n-1`. One station can hold several `Weapon`s on different hardpoints (e.g. a pair of jammer pods).
- `WeaponManager.currentWeaponStation` and `targetList` are single-selection, pilot-oriented state.
- **Any station can be fired directly**, without touching that selection:
  - `WeaponStation.Fire(owner, target)` fires all weapons in the station (guns, jammers, zero-interval weapons).
  - `WeaponStation.LaunchMount(owner, target, aimpoint)` launches the next mount (missiles and bombs).
  - Each `Weapon.Fire(owner, target, inheritedVelocity, station, aimpoint)` can be called alone, which is how a jammer station gets split per pod.
  - Firing must happen on the simulating client (`!aircraft.remoteSim`). Missile launches replicate through `CmdLaunchMissile` / `RpcLaunchMissile`.
- Per-station targets already exist: `Aircraft.SetStationTargets(stationIndex, PersistentID[])` → `WeaponStation.SetStationTargets`, which feeds turrets and weapons.
- `Aircraft.SetActiveStation`, `SetTurretVector` and `SetStationTurretTarget` are networked. They need `IsServer` or `HasAuthority`, so only the owner or the server can call them.

## Targeting UI (`CombatHUD.cs`, `TargetCam.cs`)
- `CombatHUD` is a scene singleton bound to one aircraft (`SetAircraft`). Its target list feeds `weaponManager.AddTargetList` / `TargetListChanged` (`SelectUnit`, `DeSelectUnit`, `TargetSelect(paint)`).
- `TargetCam` is per aircraft. Private mounts are `camMountForward`, `camMountRear` and `camMountLanding`, with `AimCamera` and `SetTargetCam`. There's one physical camera per aircraft.
- For crew seats, the plan is to bind the crew client's `CombatHUD` to the shared aircraft. Their selections get redirected into a per-seat target list, so they never touch the pilot's `weaponManager.targetList`.

## Networking (Mirage)
- Custom messages: register with `NetworkServer.MessageHandler` / `NetworkClient.MessageHandler` (`RegisterHandler<T>`). Message IDs are `typeof(T).FullName.GetStableHashCode() & 0xFFFF` (`MessagePacker.GetId`).
- Serializers: `Mirage.Serialization.Writer<T>.Write` and `Reader<T>.Read` have **public setters**, so the plugin can register hand-written serializers without the Weaver.
- An unknown message ID makes the receiver call `player.SetError(60, RpcSync)` (`MessageHandler.InvokeHandler`). It doesn't disconnect on its own, but errors add up. **Rule: never send crew messages to a peer that hasn't confirmed it has the mod.** The client sends exactly one `CrewHello` after connecting, and the server only answers clients that said hello.
- Existing aircraft RPCs require authority (`ServerRpcSender.ShouldInvokeLocally(this, requireAuthority: true, ...)`). A crew client can't call them, so crew actions are relayed through the server to the owner client.

## Disconnects (`NetworkManagerNuclearOption.OnServerDisconnect`)
- When a player disconnects, the server sets `unit.Networkdisabled = true` on their aircraft (killing it), removes them from their HQ, and runs `RemoveAllOwnedObject` plus destroying their Player.
- So when the pilot disconnects, the shared aircraft dies with them. The WSOs have to be unseated cleanly (returned to the map or respawn flow) when the aircraft is disabled. There's no hand-over of the flight controls.
- `Player.SetAircraft` on the server removes authority from the player's previous aircraft and ejects from it. WSO seating must never call it for the shared aircraft.

## Recon dump
The plugin logs the layout of every matching aircraft (config `General.AircraftFilter`, default `Medusa`) when its weapons load. Press **F10** in flight to dump your current aircraft. Output goes to `BepInEx/LogOutput.log`:

```
=== Recon (weapons loaded): <unitName> [key=<jsonKey>] ... ===
cockpitViewPoint: ...
pilots: N
  [0] 'Pilot' ... at (x, y, z) ...
weaponStations: ...
targetCam: ... mountForward / mountRear ...
```

Paste the Medusa's dump into the "Medusa layout" section below once it's captured.

## Medusa layout
_TBD: fill in from the recon dump (seat count and positions, stations, jammer pods, TargetCam mounts)._
