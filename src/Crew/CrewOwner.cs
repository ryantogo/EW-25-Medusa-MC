using System.Collections;
using System.Collections.Generic;
using MedusaMultiCrew.Net;
using Mirage;
using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // Runs on the client that simulates a crewed aircraft (the pilot, or the host for AI
    // aircraft) and carries out the WSOs' intent through the game's own weapon paths, so
    // firing, missile launches and station targets replicate exactly as if the pilot did it.
    //
    // Vanilla replicates "firing" per station and hands a station's target list out across its
    // weapons by index (WeaponStation.SetStationTargets). So a split station (the jamming pods)
    // fires while either holder fires, and each side's pods get that seat's target.
    internal static class CrewOwner
    {
        private const float StaleAfter = 0.75f;

        private class SeatIntent
        {
            public byte Station;
            public bool Firing;
            public byte RadarSeq;
            public bool RadarSeen;
            public uint[] Targets = new uint[0];
            public float Received;
        }

        private class OwnedCrew
        {
            public Aircraft Aircraft;
            public readonly SeatIntent[] Seats = new SeatIntent[3];
            public readonly Dictionary<int, uint[]> AppliedTargets = new Dictionary<int, uint[]>();
        }

        private static readonly Dictionary<uint, OwnedCrew> owned = new Dictionary<uint, OwnedCrew>();
        private static readonly HashSet<WeaponStation> salvos = new HashSet<WeaponStation>();
        private static int salvoToken;
        private static int authorizeDepth;

        // True while CrewOwner itself is firing or targeting, so the block patches let it through.
        public static bool Authorized => authorizeDepth > 0;

        public static void Init()
        {
            CrewNet.ControlFrameReceived += OnFrame;
        }

        public static void Reset()
        {
            owned.Clear();
            CancelSalvos();
        }

        // The seat allocation for an aircraft this process knows about: the server's roster when
        // we host, otherwise the roster the server sent us as the aircraft's pilot.
        public static bool TryGetAllocation(uint aircraftNetId, out StationAllocation[] allocation)
        {
            if (CrewNet.Server != null && CrewServer.TryGetAllocation(aircraftNetId, out allocation))
            {
                return true;
            }
            if (CrewClient.TryGetRoster(aircraftNetId, out CrewRosterMsg roster))
            {
                allocation = roster.Stations;
                return true;
            }
            allocation = null;
            return false;
        }

        // True if a seated WSO holds this station of an aircraft we simulate.
        public static bool IsWsoStation(Aircraft aircraft, int stationIndex)
        {
            if (aircraft == null || !aircraft.LocalSim || !TryGetAllocation(aircraft.NetId, out StationAllocation[] allocation))
            {
                return false;
            }
            return stationIndex >= 0 && stationIndex < allocation.Length && allocation[stationIndex].Mode != AllocationMode.Pilot;
        }

        private static void OnFrame(CrewControlFrameMsg msg)
        {
            Aircraft aircraft = ResolveAircraft(msg.AircraftNetId);
            if (aircraft == null || !aircraft.LocalSim || !CrewSeat.IsWso(msg.Seat))
            {
                return;
            }
            if (!owned.TryGetValue(msg.AircraftNetId, out OwnedCrew crew))
            {
                crew = new OwnedCrew { Aircraft = aircraft };
                owned[msg.AircraftNetId] = crew;
            }
            SeatIntent intent = crew.Seats[msg.Seat] ?? (crew.Seats[msg.Seat] = new SeatIntent());
            intent.Station = msg.Station;
            intent.Firing = msg.Firing;
            intent.Targets = msg.Targets ?? new uint[0];
            intent.Received = Time.unscaledTime;

            if (intent.RadarSeen && msg.RadarSeq != intent.RadarSeq)
            {
                ToggleRadar(aircraft);
            }
            intent.RadarSeq = msg.RadarSeq;
            intent.RadarSeen = true;
        }

        public static void Tick()
        {
            if (owned.Count == 0)
            {
                return;
            }
            var gone = new List<uint>();
            foreach (KeyValuePair<uint, OwnedCrew> pair in owned)
            {
                OwnedCrew crew = pair.Value;
                if (crew.Aircraft == null || crew.Aircraft.disabled || !crew.Aircraft.LocalSim
                    || !TryGetAllocation(pair.Key, out StationAllocation[] allocation) || allocation.Length == 0)
                {
                    gone.Add(pair.Key);
                    continue;
                }
                Apply(crew, allocation);
            }
            foreach (uint netId in gone)
            {
                owned.Remove(netId);
            }
        }

        private static void Apply(OwnedCrew crew, StationAllocation[] allocation)
        {
            Aircraft aircraft = crew.Aircraft;
            int count = Mathf.Min(allocation.Length, aircraft.weaponStations.Count);
            for (int i = 0; i < count; i++)
            {
                StationAllocation a = allocation[i];
                if (a.Mode == AllocationMode.Pilot)
                {
                    continue;
                }
                WeaponStation station = aircraft.weaponStations[i];
                SeatIntent first = ActiveOn(crew, a.SeatA, i);
                SeatIntent second = a.Mode == AllocationMode.SplitBySide ? ActiveOn(crew, a.SeatB, i) : null;
                if (first == null && second == null)
                {
                    continue;
                }

                ApplyTargets(crew, aircraft, station, a, first, second);

                SeatIntent shooter = first != null && first.Firing ? first : second != null && second.Firing ? second : null;
                if (shooter != null)
                {
                    Fire(aircraft, station, ResolveTargets(shooter.Targets));
                }
            }
        }

        // The seat's intent, if it's fresh and the seat currently has this station selected.
        private static SeatIntent ActiveOn(OwnedCrew crew, byte seat, int stationIndex)
        {
            SeatIntent intent = CrewSeat.IsWso(seat) ? crew.Seats[seat] : null;
            if (intent == null || intent.Station != stationIndex || Time.unscaledTime - intent.Received > StaleAfter)
            {
                return null;
            }
            return intent;
        }

        private static void ApplyTargets(OwnedCrew crew, Aircraft aircraft, WeaponStation station, StationAllocation a, SeatIntent first, SeatIntent second)
        {
            uint[] ids;
            if (a.Mode == AllocationMode.SplitBySide)
            {
                // One entry per weapon, by index: each pod tracks its own side's seat, or the
                // other seat's target when its own seat has none.
                uint firstTarget = PrimaryOf(first);
                uint secondTarget = PrimaryOf(second);
                ids = new uint[station.Weapons.Count];
                for (int w = 0; w < ids.Length; w++)
                {
                    Weapon weapon = station.Weapons[w];
                    bool left = weapon == null || aircraft.transform.InverseTransformPoint(weapon.transform.position).x <= 0f;
                    uint own = left ? firstTarget : secondTarget;
                    ids[w] = own != 0 ? own : (left ? secondTarget : firstTarget);
                }
                if (System.Array.TrueForAll(ids, id => id == 0))
                {
                    ids = new uint[0];
                }
            }
            else
            {
                ids = first.Targets;
            }

            if (crew.AppliedTargets.TryGetValue(station.Number, out uint[] applied) && Same(applied, ids))
            {
                return;
            }
            crew.AppliedTargets[station.Number] = ids;

            var persistent = new PersistentID[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                persistent[i] = new PersistentID { Id = ids[i] };
            }
            authorizeDepth++;
            try
            {
                // Networked: sets each weapon's target here and on every other client.
                aircraft.SetStationTargets(station.Number, persistent);
            }
            finally
            {
                authorizeDepth--;
            }
        }

        // Same rules as WeaponManager.Fire, for one station and one seat's targets.
        private static void Fire(Aircraft aircraft, WeaponStation station, List<Unit> targets)
        {
            WeaponInfo info = station.WeaponInfo;
            if (info == null || station.SafetyIsOn(aircraft) || !station.Ready() || station.SalvoInProgress)
            {
                return;
            }
            Unit target = targets.Count > 0 ? targets[0] : null;
            authorizeDepth++;
            try
            {
                if (info.gun || info.fireInterval == 0f || info.sling)
                {
                    station.Fire(aircraft, target);
                }
                else if (targets.Count > 1 && Plugin.Instance != null)
                {
                    station.SalvoInProgress = true;
                    salvos.Add(station);
                    Plugin.Instance.StartCoroutine(Salvo(aircraft, station, targets, info.fireInterval * 1.1f, salvoToken));
                }
                else
                {
                    station.LaunchMount(aircraft, target, aircraft.GlobalPosition() + aircraft.transform.forward * 50000f);
                }
            }
            finally
            {
                authorizeDepth--;
            }
        }

        private static IEnumerator Salvo(Aircraft aircraft, WeaponStation station, List<Unit> targets, float interval, int token)
        {
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (token != salvoToken || aircraft == null || aircraft.disabled)
                    {
                        yield break;
                    }
                    Unit target = targets[i];
                    if (target != null && !target.disabled)
                    {
                        authorizeDepth++;
                        try
                        {
                            station.LaunchMount(aircraft, target, default(GlobalPosition));
                        }
                        finally
                        {
                            authorizeDepth--;
                        }
                    }
                    if (i < targets.Count - 1)
                    {
                        yield return new WaitForSeconds(interval);
                    }
                }
            }
            finally
            {
                station.SalvoInProgress = false;
                salvos.Remove(station);
            }
        }

        private static void CancelSalvos()
        {
            salvoToken++;
            foreach (WeaponStation station in salvos)
            {
                if (station != null)
                {
                    station.SalvoInProgress = false;
                }
            }
            salvos.Clear();
        }

        private static void ToggleRadar(Aircraft aircraft)
        {
            if (aircraft.radar == null)
            {
                return;
            }
            // CmdToggleRadar requires the owning player's authority; an AI pilot runs its own radar.
            if (aircraft.Player == null)
            {
                Plugin.Log.LogInfo("[Owner] WSO radar toggle ignored: AI pilots manage their own radar");
                return;
            }
            aircraft.CmdToggleRadar();
        }

        private static uint PrimaryOf(SeatIntent intent)
        {
            if (intent == null)
            {
                return 0;
            }
            foreach (uint id in intent.Targets)
            {
                if (id != 0)
                {
                    return id;
                }
            }
            return 0;
        }

        private static List<Unit> ResolveTargets(uint[] ids)
        {
            var units = new List<Unit>();
            foreach (uint id in ids)
            {
                if (id != 0 && new PersistentID { Id = id }.TryGetUnit(out Unit unit) && unit != null && !unit.disabled && !units.Contains(unit))
                {
                    units.Add(unit);
                }
            }
            return units;
        }

        private static bool Same(uint[] a, uint[] b)
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

        private static Aircraft ResolveAircraft(uint netId)
        {
            NetworkIdentity identity = null;
            NetworkServer server = CrewNet.Server;
            NetworkClient client = CrewNet.Client;
            bool found = (server?.World != null && server.World.TryGetIdentity(netId, out identity))
                || (client?.World != null && client.World.TryGetIdentity(netId, out identity));
            return found && identity != null ? identity.GetComponent<Aircraft>() : null;
        }
    }
}
