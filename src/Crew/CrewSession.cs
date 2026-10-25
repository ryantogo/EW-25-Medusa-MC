using System.Collections.Generic;
using MedusaMultiCrew.Core;
using MedusaMultiCrew.Net;
using Mirage;
using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // The local player's crew seat as seen on screen: drives CrewView from seat changes, keeps
    // the camera on the aircraft, and leaves when the seat stops being viable. Seat changes
    // arrive inside Mirage message handlers, so they're queued and applied in Update.
    internal static class CrewSession
    {
        private static readonly CrewView view = new CrewView();

        // Set before the view is entered so patches recognise the aircraft during Enter.
        public static Aircraft Aircraft { get; private set; }
        public static byte Seat { get; private set; }
        public static WeaponStation Station { get; private set; }
        public static readonly List<Unit> TargetList = new List<Unit>();

        public static bool InSession => Aircraft != null;

        private static bool seatChangePending;
        private static uint pendingAircraftNetId;
        private static byte pendingSeat;
        private static bool rosterChanged;
        private static string pendingLeaveReason;
        private static CameraBaseState lastCameraState;
        private static Aircraft subscribedAircraft;

        // WSO intent, streamed to the aircraft owner (CrewOwner applies it).
        private const float FrameInterval = 0.05f;
        private const float HeartbeatInterval = 0.25f;
        private static bool firing;
        private static byte radarSeq;
        private static float nextFrame;
        private static float nextHeartbeat;
        private static int sentStation = -1;
        private static bool sentFiring;
        private static byte sentRadarSeq;
        private static uint[] sentTargets = new uint[0];

        public static void Init()
        {
            CrewClient.LocalSeatChanged += (aircraftNetId, seat) =>
            {
                seatChangePending = true;
                pendingAircraftNetId = aircraftNetId;
                pendingSeat = seat;
            };
            CrewClient.SeatedRosterChanged += () => rosterChanged = true;
        }

        public static bool SeatHolds(int stationIndex)
        {
            return InSession
                && CrewClient.TryGetRoster(Aircraft.NetId, out CrewRosterMsg roster)
                && stationIndex >= 0 && stationIndex < roster.Stations.Length
                && CrewAllocation.SeatHoldsStation(roster.Stations[stationIndex], Seat);
        }

        public static void Update()
        {
            if (seatChangePending)
            {
                seatChangePending = false;
                if (pendingAircraftNetId == 0)
                {
                    End();
                }
                else
                {
                    Begin(pendingAircraftNetId, pendingSeat);
                }
            }
            if (!InSession)
            {
                return;
            }
            if (pendingLeaveReason != null)
            {
                Leave(pendingLeaveReason);
                return;
            }
            if (!IsViable(Aircraft))
            {
                Leave("pilot ejected or the aircraft is down");
                return;
            }
            if (rosterChanged)
            {
                rosterChanged = false;
                if (Station == null || !SeatHolds(Station.Number))
                {
                    Station = FirstHeldStation();
                }
                view.Refresh(Aircraft, Station);
            }
            KeepCameraOnAircraft();
            if (!InSession)
            {
                return;
            }
            ReadInput();
            SendControlFrame();
        }

        private static void ReadInput()
        {
            if (RewiredInput.GetActionTimedPressUp("Next Weapon"))
            {
                CycleStation(+1);
            }
            else if (RewiredInput.GetActionTimedPressUp("Previous Weapon"))
            {
                CycleStation(-1);
            }
            if (RewiredInput.GetActionTimedPressUp("Radar"))
            {
                radarSeq++;
            }
            // Same menu safety as vanilla: no firing while the cursor is up.
            firing = Station != null && RewiredInput.GetAction("Fire") && (!PlayerSettings.menuWeaponSafety || !Cursor.visible);
        }

        private static void CycleStation(int delta)
        {
            var held = new List<WeaponStation>();
            for (int i = 0; i < Aircraft.weaponStations.Count; i++)
            {
                if (SeatHolds(i))
                {
                    held.Add(Aircraft.weaponStations[i]);
                }
            }
            if (held.Count < 2)
            {
                return;
            }
            int index = Mathf.Max(0, held.IndexOf(Station));
            Station = held[(index + delta + held.Count) % held.Count];
            view.Refresh(Aircraft, Station);
        }

        private static void SendControlFrame()
        {
            uint[] targets = TargetIds();
            int station = Station != null ? Station.Number : -1;
            bool changed = station != sentStation || firing != sentFiring || radarSeq != sentRadarSeq || !SameIds(targets, sentTargets);
            float now = Time.unscaledTime;
            if (!(changed && now >= nextFrame) && now < nextHeartbeat)
            {
                return;
            }
            if (station < 0)
            {
                return;
            }
            nextFrame = now + FrameInterval;
            nextHeartbeat = now + HeartbeatInterval;
            sentStation = station;
            sentFiring = firing;
            sentRadarSeq = radarSeq;
            sentTargets = targets;

            Vector3 aim = CameraStateManager.i != null && CameraStateManager.i.mainCamera != null
                ? CameraStateManager.i.mainCamera.transform.forward
                : Aircraft.transform.forward;
            CrewNet.SendToServer(new CrewControlFrameMsg
            {
                AircraftNetId = Aircraft.NetId,
                Seat = Seat,
                Station = (byte)station,
                Firing = firing,
                RadarSeq = radarSeq,
                AimX = aim.x,
                AimY = aim.y,
                AimZ = aim.z,
                Targets = targets,
            });
        }

        private static uint[] TargetIds()
        {
            var ids = new List<uint>();
            foreach (Unit unit in TargetList)
            {
                if (unit != null && !unit.disabled && unit.persistentID.Id != 0 && !ids.Contains(unit.persistentID.Id))
                {
                    ids.Add(unit.persistentID.Id);
                    if (ids.Count >= CrewSerializers.MaxTargets)
                    {
                        break;
                    }
                }
            }
            return ids.ToArray();
        }

        private static bool SameIds(uint[] a, uint[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static void Reset()
        {
            seatChangePending = false;
            pendingLeaveReason = null;
            End();
        }

        private static void Begin(uint aircraftNetId, byte seat)
        {
            if (InSession)
            {
                End();
            }
            NetworkClient client = CrewNet.Client;
            if (client?.World == null || !client.World.TryGetIdentity(aircraftNetId, out NetworkIdentity identity) || identity == null)
            {
                Plugin.Log.LogWarning($"[Session] Seated in aircraft {aircraftNetId}, but it isn't spawned here");
                return;
            }
            Aircraft aircraft = identity.GetComponent<Aircraft>();
            if (!IsViable(aircraft))
            {
                CrewClient.LeaveSeat("aircraft unavailable");
                return;
            }

            Aircraft = aircraft;
            Seat = seat;
            TargetList.Clear();
            firing = false;
            sentStation = -1;
            nextHeartbeat = 0f;
            Station = FirstHeldStation();
            rosterChanged = false;
            pendingLeaveReason = null;

            aircraft.onDisableUnit += OnAircraftDisabled;
            subscribedAircraft = aircraft;

            view.Enter(aircraft, Station);
            lastCameraState = CameraStateManager.i != null ? CameraStateManager.i.currentState : null;
            Plugin.Log.LogInfo($"[Session] {CrewSeat.Name(seat)} in {aircraft.unitName}, station {(Station != null ? Station.WeaponInfo?.weaponName : "none")}");
        }

        private static void End()
        {
            if (subscribedAircraft != null)
            {
                subscribedAircraft.onDisableUnit -= OnAircraftDisabled;
                subscribedAircraft = null;
            }
            if (!InSession)
            {
                return;
            }
            view.Exit();
            TargetList.Clear();
            Aircraft = null;
            Seat = 0;
            Station = null;
            lastCameraState = null;
            pendingLeaveReason = null;
        }

        private static void Leave(string reason)
        {
            Plugin.Log.LogInfo($"[Session] Leaving seat: {reason}");
            End();
            CrewClient.LeaveSeat(reason);
        }

        // Vanilla raises onDisableUnit inside Unit.UnitDisabled during crash / return-to-inventory
        // teardown; doing the camera/HUD teardown here (or throwing) aborts vanilla's sequence and
        // the airframe never despawns. Only record the intent.
        private static void OnAircraftDisabled(Unit unit)
        {
            pendingLeaveReason = "aircraft lost";
        }

        private static void KeepCameraOnAircraft()
        {
            CameraStateManager csm = CameraStateManager.i;
            if (csm == null)
            {
                return;
            }
            if (csm.followingUnit != Aircraft)
            {
                // The player picked something else to look at (map click, spawn menu).
                Leave("camera left the aircraft");
                return;
            }

            CameraBaseState state = csm.currentState;
            bool allowed = state == csm.cockpitState || state == csm.orbitState || state == csm.TVState || state == csm.chaseState;
            if (!allowed)
            {
                view.Reattach(Aircraft, Station);
            }
            else if (state == csm.cockpitState && lastCameraState != csm.cockpitState)
            {
                // Back in the cockpit after cycling views: the cockpit state rebinds FlightHud.
                view.Refresh(Aircraft, Station);
            }
            lastCameraState = csm.currentState;
        }

        private static WeaponStation FirstHeldStation()
        {
            for (int i = 0; i < Aircraft.weaponStations.Count; i++)
            {
                if (SeatHolds(i))
                {
                    return Aircraft.weaponStations[i];
                }
            }
            return null;
        }

        // The cockpit camera bails to free cam when pilots[0] dies, and ejected airframes
        // break cockpit follow; leave once instead of fighting it.
        private static bool IsViable(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled || aircraft.HasEjected())
            {
                return false;
            }
            Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
            return pilot == null || !pilot.dead;
        }
    }
}
