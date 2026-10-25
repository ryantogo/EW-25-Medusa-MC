# Testing

Everything logs to `BepInEx/LogOutput.log` with prefixes `[Net]`, `[Server]` and `[Client]`. On-screen messages appear in the game's action report text (the same place as "All Guns Linked").

Run tests with only this mod enabled in NOMM. MultiMissile changes loadouts, and SimpleWSO patches the same code.

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
3. Player 1 sees "<name> wants to join as WSO 1. F7 = accept, F8 = decline". Press **F7**.
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
