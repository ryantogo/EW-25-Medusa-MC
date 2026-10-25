# Testing

Everything logs to `BepInEx/LogOutput.log` with prefixes `[Net]`, `[Server]`, `[Client]`, `[Session]` and `[View]`. On-screen messages appear in the game's action report text (the same place as "All Guns Linked").

Run tests with only this mod enabled in NOMM. MultiMissile changes loadouts, and SimpleWSO patches the same code.

## Phase 4: WSO weapons and targeting (v0.4.0)

While seated, the WSO uses the **vanilla controls**: **Select** to pick targets on the HUD, **Next/Previous Weapon** to cycle through stations your seat holds, **Fire** to fire, and **Radar** to toggle the radar. The pilot's game carries out each action, so missiles, the laser and jamming show up for everyone exactly as vanilla.

Expected split with both WSOs seated: WSO 1 = Laser plus the left jamming pods, WSO 2 = ARAD-116 plus the right pods. A lone WSO holds everything. The pilot holds nothing while any WSO is aboard.

### A4. One PC, AI Medusa (you're WSO, the host's game fires)
1. Seat yourself in a friendly AI Medusa as in A3. You're WSO 1 and hold all three stations.
2. Select an enemy radar or ground target with **Select**, then hold **Fire** on the **Laser**. Expect the beam on the target.
3. **Next Weapon** to the **ARAD-116**, target an emitting radar, press **Fire**. Expect one missile launch (and a salvo if you've selected several targets).
4. **Next Weapon** to the **Radar Jamming Pod**, target a radar, hold **Fire**. Expect it to be jammed.
5. **Radar** is ignored on an AI aircraft (the AI runs its own radar). The log says so.

### B4. Two or three players
1. Player 1 flies, player 2 is WSO 1, player 3 is WSO 2.
2. WSO 1 fires the Laser, WSO 2 fires ARADs at a different target. Both should work at the same time.
3. Both WSOs pick **different radars** and hold Fire on the pods. Each side's pods should point at their own WSO's target.
4. The pilot presses **Next/Previous Weapon**. WSO-held stations are skipped, and on the Medusa (where WSOs hold every station) the pilot sees "Weapons are with your WSO". If the pilot's HUD still shows a WSO station (it was selected before the WSO joined), pressing Fire does nothing and shows "<weapon> is held by WSO 1" (or WSO 2). Test this both with the **host as pilot** and with a **client as pilot**.
5. The pilot selecting targets on their HUD must not change what the WSOs' weapons are aimed at.
6. WSO presses **Radar**. The Medusa's radar toggles for everyone.
7. A WSO leaves. Their stations go to the other WSO. When both leave, the pilot can fire everything again.

**Known limit:** the game reports firing per station, so while either WSO holds Fire on the pod station, all pods jam. Each pod still jams its own WSO's target, and if a WSO has no target, their pods jam the other WSO's target.

## Phase 3: crew cockpit view (v0.3.0)

Taking a seat now puts your camera in the Medusa's cockpit at that seat, with the aircraft's HUD. Weapons and targeting controls come in phase 4, so you can look around but can't fire yet.

### A3. One PC, AI Medusa
1. Same setup as test A below: host a mission with a friendly AI Medusa, join the faction, don't spawn.
2. Follow the Medusa and press **F6**. Expect:
   - Your view moves into the **front-right seat** (WSO 1), with the pilot model visible to your left and the HUD showing the **120kw Laser** (the first station a lone WSO holds).
   - Log lines: `[Session] WSO 1 in EW-25 Medusa, station 120kw Laser` and `[View] Entered crew cockpit view`.
   - Mouse look and zoom work as in your own cockpit. **Switch View** goes to the external view and back.
   - No constant rattle or shaking. That damping is on by default via `Seats.DampRemoteCockpitShake`.
3. Press **F6**. Expect your previous spectator camera back, the HUD gone, and `[View] Left crew view; camera and HUD restored`.
4. Seat again, then click another unit on the map. You should leave the seat ("camera left the aircraft").
5. Seat again and let the AI Medusa get shot down. You should leave the seat, and **the wreck should still despawn about 30 s later**. If it never disappears, report it; that's the teardown trap we guard against.

**If the camera is in the wrong place:** change `Seats.Wso1CameraPosition` / `Wso2CameraPosition` in the config (aircraft-local metres: X right, Y up, Z forward), then re-seat.

### B3. Two or three players
As in tests B and C below, but the WSOs now get the cockpit view. WSO 2 sits in the **rear-left** seat. When the second WSO joins, WSO 1's HUD should stay on the Laser, and WSO 2's should show the ARAD-116. The pilot's own HUD and weapon selection must not change when a WSO selects targets.

## Phase 2: networking and seating (no crew camera yet)

Seating only changes who the server thinks is in each seat. You won't see a cockpit view until phase 3. Look for the log lines and on-screen messages below.

Default keys (configurable under `[Keys]` in `com.ryantogo.medusamulticrew.cfg`):

| Key | Who | Action |
| --- | --- | --- |
| F6 | Spectator | Ask to join the friendly Medusa the camera is following |
| F6 | WSO | Leave your seat |
| F7 | Pilot | Accept a join request |
| F8 | Pilot | Decline a join request |

If F6–F8 already do something in your control bindings, change them in the config.

### A. One PC, AI Medusa (tests the whole message path over loopback)
1. Host a mission (single player is fine) with a **friendly AI-flown EW-25 Medusa** in it. Use the mission editor if needed.
2. Join the faction, but **don't spawn an aircraft**.
3. On the map, click the friendly AI Medusa so the camera follows it, then press **F6**.
4. Expect:
   - `[Net] Server handlers registered`, `[Net] Client handlers registered`, `[Net] Sent hello to server`, `[Server] ... has the mod`, `[Client] Server has the mod`
   - `[Server] ... seated as WSO 1 in ...`
   - `[Client] Roster for aircraft N (Update): WSO 1=<you> WSO 2=0 stations [0=WSO 1, 1=WSO 1, 2=WSO 1]` (a lone WSO gets everything)
   - On screen: "You are WSO 1. F6 = leave seat"
5. Press **F6** again. Expect "You left your seat" and a roster with `WSO 1=0`.
6. Seat again, then let the AI Medusa get shot down or land. Expect "Aircraft lost; you left your seat" within a second.

### B. Two players (pilot prompt)
1. Both players have the mod. The host can be either one. Same faction.
2. Player 1 flies a Medusa. Player 2 spectates it and presses **F6**.
3. Player 1 sees a **CREW REQUEST** box at the top-center of the screen (pulsing orange border, "<name> wants to join as WSO 1", key hints, a countdown, and Accept/Decline buttons). Player 2 sees a blue **WAITING FOR THE PILOT** box. Player 1 presses **F7** (or clicks Accept).
4. Expect both logs to show the roster. Player 1 sees "Crew: WSO 1 <name>, WSO 2 empty".
5. Also try:
   - **F8**: player 2 sees "Couldn't join: the pilot declined".
   - **No answer for 20 s**: "the pilot didn't answer".
   - **Player 2 on a different faction**: "join the aircraft's faction first".
   - **Player 2 flying their own aircraft**: "you're flying an aircraft".

### C. Three players (both WSO seats)
With player 1 flying, players 2 and 3 join in turn. The roster should read `0=WSO 1, 1=WSO 2, 2=left WSO 1/right WSO 2` (laser, pylons, split pods). When either WSO leaves, the other gets every station again.

### D. Mod missing somewhere
- **Host without the mod:** the client logs `Sent hello to server` and never `Server has the mod`. F6 shows "Multi-crew needs the mod on the host too".
- **Pilot without the mod** (host and joiner have it): the join is refused with "the pilot doesn't have the mod".
