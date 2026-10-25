using System.Collections.Generic;
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
                Station = FirstHeldStation();
                view.Refresh(Aircraft, Station);
            }
            KeepCameraOnAircraft();
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
