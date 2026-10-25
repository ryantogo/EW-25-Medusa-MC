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

## Mod networking (phase 2, `src/Net`, `src/Crew`)
- **Startup.** `CrewNet.Tick` finds `NetworkManagerNuclearOption` (via an `Awake` postfix, with `FindObjectOfType` as a fallback). It re-registers handlers whenever `Server.MessageHandler` or `Client.MessageHandler` is a new object, because Mirage creates a new one on every start.
- **Handshake.** Once a local `Player` exists, the client sends one `CrewHelloMsg`. The server always replies with `CrewHelloAckMsg` (that layout is frozen) and only treats the peer as modded if the protocol matches. A client sends nothing else until the ack arrives, so a vanilla host costs it a single `SetError(60)`.
- **Join.** `CrewJoinRequestMsg` → server checks: aircraft eligible and alive, requester in the same faction, not flying, not already crew, seat free, owner modded, one pending request per aircraft. Then `CrewJoinPromptMsg` goes to the owner, who replies with `CrewJoinDecisionMsg`. Requests time out after 20 s. AI aircraft (`Identity.Owner == null`) auto-accept when `AllowAiAircraft` is on.
- **Roster.** `CrewRosterMsg` (WSO player NetIds plus the per-station allocation) goes only to the owner and the crew, and to a leaver one last time. A roster with both WSO slots set to 0 means the crew was disbanded.
- **Control frames.** `CrewControlFrameMsg` is accepted only from the seated player, for a station their seat holds, and at most every 20 ms. It's forwarded to the owner only, or raised on the server for AI aircraft (`CrewNet.ControlFrameReceived`). Phase 4 applies them and must sanitize the aim vector (NaN or zero).
- **Upkeep.** `CrewServer.Tick` (every 0.25 s) times out requests and disbands crews whose aircraft is gone, disabled, ejected or has a dead pilot, or whose owner changed. A disconnect removes the player's seat, or disbands the crew if the player was the pilot.

## Disconnects (`NetworkManagerNuclearOption.OnServerDisconnect`)
- When a player disconnects, the server sets `unit.Networkdisabled = true` on their aircraft (killing it), removes them from their HQ, and runs `RemoveAllOwnedObject` plus destroying their Player.
- So when the pilot disconnects, the shared aircraft dies with them. The WSOs have to be unseated cleanly (returned to the map or respawn flow) when the aircraft is disabled. There's no hand-over of the flight controls.
- `Player.SetAircraft` on the server removes authority from the player's previous aircraft and ejects from it. WSO seating must never call it for the shared aircraft.

## Recon dump
The plugin logs the layout of every matching aircraft (config `General.AircraftFilter`, default `Medusa`) when its weapons load. Press **F10** in flight (polled through Rewired) to dump your current aircraft. Output goes to `BepInEx/LogOutput.log`:

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
From recon dumps on 2026-10-25 (EW-25 Medusa, `jsonKey=EW1`). Positions are aircraft-local metres: X right, Y up, Z forward.

**Seats.** There are 4 pilot models in two tandem cockpits, all with ejection seats:

| `pilots[i]` | Name | Cockpit | Position | Use |
| --- | --- | --- | --- | --- |
| 0 | `pilot` | `cockpit_F` (front) | (-0.40, 0.52, 7.62) | Pilot (vanilla player seat) |
| 1 | `WSO` | `cockpit_F` (front) | (0.40, 0.52, 7.62) | WSO 1 |
| 2 | `RearSeater1` | `cockpit_R` (rear) | (-0.44, 0.54, 6.08) | WSO 2 |
| 3 | `RearSeater2` | `cockpit_R` (rear) | (0.44, 0.54, 6.08) | Spare |

The camera sits at `cockpitViewPoint` = `helmetCamPoint` on the pilot's head, at (-0.37, 1.17, 7.42). That's about (+0.03, +0.65, -0.20) from the pilot model. Applying the same head offset to the other seats gives camera offsets from `cockpitViewPoint` (used by the camera-offset patch):

| Seat | Camera position | Offset from `cockpitViewPoint` |
| --- | --- | --- |
| WSO 1 (front right) | ≈ (0.43, 1.17, 7.42) | (+0.80, 0.00, 0.00) |
| WSO 2 (rear left) | ≈ (-0.41, 1.19, 5.88) | (-0.04, +0.02, -1.54) |
| Spare (rear right) | ≈ (0.47, 1.19, 5.88) | (+0.84, +0.02, -1.54) |

These are starting values to tune in-game.

**Weapon stations.** There are 3, and the mount counts depend on the loadout:

| Station | Weapon | Mounts | Notes |
| --- | --- | --- | --- |
| 0 | 120kw Laser (`Laser`) | 1, nose `laserMount` (0, 0.07, 9.68) | A single mount, so it belongs to one seat |
| 1 | ARAD-116 (`MountedMissile`) | 2 internal bay (±0.20, -0.46, 0.66), plus 4 on wing pylons (±5.59 / ±6.51, -0.25, -0.63) in the heavier loadout | Anti-radiation missiles. Mounts can be split left/right. |
| 2 | Radar Jamming Pod (`JammingPod`) | 2 at (±3.98, -0.34, 0.00), plus 2 at (±6.05, -0.36, -0.71) in the heavier loadout | Each pod aims at its own target (`JammingPod.SetTarget`), so splitting them lets each WSO jam a different radar |

Two loadouts were observed: 6 ARAD + 2 pods, and 2 ARAD + 4 pods. Neither has turrets.

**Default station allocation (decided 2026-10-25).**
Stations are grouped by weapon type, not by hardpoint, so the rules match on what's mounted rather than on station numbers.
- **Pilot**: no weapon stations while any WSO is seated. Keeps countermeasures, including the built-in `RadarJammer`.
- **WSO 1** (front right, `pilots[1]`): the station holding the nose `Laser`, plus the **left** jamming pods (local X < 0).
- **WSO 2** (rear left, `pilots[2]`): every other non-pod station. That's the ARAD-116 pylons and internal bay, or whatever else the loadout puts there. Plus the **right** jamming pods (local X > 0).
- With only one WSO seated, that WSO gets every station. With none, the pilot has everything (vanilla).
- The table lives on the server, so a "free pick" mode (claim any free station or pod) can be added later as an option without changing the network design.

**Other systems.**
- `radar`: `Radar`.
- One built-in `RadarJammer`. It's a `Countermeasure` (ARH/SARH, draws on the power supply), not a weapon station. It fires through the countermeasure system, so it stays with the **pilot**.
- TargetCam: one physical camera on `nose` (0, 0.14, 9.18), with mounts `targetCamForward` (0, 0.29, 9.40) and `targetCamRear` (0, 0.71, -7.42). Two of the three remote AI dumps showed `targetCam: none`, so don't assume it exists on aircraft this client doesn't simulate.
- `GetComponentsInChildren<JammingPod>` on the aircraft found 0 pods even though station 2 lists them, so always reach the pods through `WeaponStation.Weapons`.
