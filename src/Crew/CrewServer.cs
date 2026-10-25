using System.Collections.Generic;
using MedusaMultiCrew.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // Server-authoritative crew state: who has the mod, who sits where, pending join requests,
    // and the seat -> station allocation. Every crew message is validated here and relayed
    // only to the aircraft owner and that aircraft's crew, never broadcast.
    internal static class CrewServer
    {
        private const float JoinTimeout = 20f;
        private const float MinFrameInterval = 0.02f;
        private const float ValidateInterval = 0.25f;

        private class AircraftCrew
        {
            public uint NetId;
            public Aircraft Aircraft;
            // Null for AI aircraft, which the server simulates itself.
            public INetworkPlayer Owner;
            public readonly INetworkPlayer[] Seats = new INetworkPlayer[3];
            public readonly float[] LastFrame = new float[3];
            public StationAllocation[] Allocation = new StationAllocation[0];
        }

        private class PendingJoin
        {
            public uint Id;
            public INetworkPlayer Requester;
            public uint AircraftNetId;
            public byte Seat;
            public float Expires;
        }

        private static readonly HashSet<INetworkPlayer> modded = new HashSet<INetworkPlayer>();
        private static readonly Dictionary<uint, AircraftCrew> crews = new Dictionary<uint, AircraftCrew>();
        private static readonly Dictionary<uint, PendingJoin> pending = new Dictionary<uint, PendingJoin>();
        private static uint nextRequestId = 1;
        private static float nextValidate;

        public static void Reset()
        {
            modded.Clear();
            crews.Clear();
            pending.Clear();
            nextRequestId = 1;
        }

        // ---- handlers ----

        public static void OnHello(INetworkPlayer player, CrewHelloMsg msg)
        {
            // Always ack: the ack layout is frozen, so a mismatched client can still read it and warn.
            CrewNet.SendToPlayer(player, new CrewHelloAckMsg { Protocol = CrewNet.Protocol, Version = CrewNet.Version });
            if (msg.Protocol != CrewNet.Protocol)
            {
                Plugin.Log.LogWarning($"[Server] {player} runs protocol {msg.Protocol}, server runs {CrewNet.Protocol}; multi-crew disabled for them");
                return;
            }
            modded.Add(player);
            Plugin.Log.LogInfo($"[Server] {player} has the mod (v{msg.Version})");
        }

        public static void OnJoinRequest(INetworkPlayer player, CrewJoinRequestMsg msg)
        {
            if (!modded.Contains(player))
            {
                return;
            }

            byte seat = msg.Seat;
            JoinResult? refusal = CheckJoin(player, msg.AircraftNetId, ref seat, out Aircraft aircraft);
            if (refusal.HasValue)
            {
                SendResult(player, msg.AircraftNetId, msg.Seat, refusal.Value);
                return;
            }

            INetworkPlayer owner = aircraft.Identity.Owner;
            if (owner == null)
            {
                // AI aircraft: nobody to ask.
                if (Plugin.AllowAiAircraft.Value)
                {
                    Seat(player, aircraft, seat);
                }
                else
                {
                    SendResult(player, msg.AircraftNetId, seat, JoinResult.AircraftNotEligible);
                }
                return;
            }

            var request = new PendingJoin
            {
                Id = nextRequestId++,
                Requester = player,
                AircraftNetId = msg.AircraftNetId,
                Seat = seat,
                Expires = Time.unscaledTime + JoinTimeout,
            };
            pending[request.Id] = request;

            uint requesterNetId = player.TryGetPlayer<Player>(out Player requester) ? requester.NetId : 0u;
            CrewNet.SendToPlayer(owner, new CrewJoinPromptMsg
            {
                RequestId = request.Id,
                AircraftNetId = msg.AircraftNetId,
                Seat = seat,
                RequesterNetId = requesterNetId,
            });
            Plugin.Log.LogInfo($"[Server] Join request {request.Id}: {player} -> {CrewSeat.Name(seat)} of {aircraft.unitName}, asking owner");
        }

        public static void OnJoinDecision(INetworkPlayer player, CrewJoinDecisionMsg msg)
        {
            if (!pending.TryGetValue(msg.RequestId, out PendingJoin request))
            {
                return;
            }
            Aircraft aircraft = ResolveAircraft(request.AircraftNetId);
            // Only the aircraft's current owner may answer.
            if (aircraft == null || aircraft.Identity.Owner != player)
            {
                return;
            }
            pending.Remove(msg.RequestId);

            if (!msg.Accept)
            {
                SendResult(request.Requester, request.AircraftNetId, request.Seat, JoinResult.Declined);
                return;
            }

            // Things may have changed while the pilot was deciding.
            byte seat = request.Seat;
            JoinResult? refusal = CheckJoin(request.Requester, request.AircraftNetId, ref seat, out aircraft, request.Id);
            if (refusal.HasValue)
            {
                SendResult(request.Requester, request.AircraftNetId, request.Seat, refusal.Value);
                return;
            }
            Seat(request.Requester, aircraft, seat);
        }

        public static void OnLeave(INetworkPlayer player, CrewLeaveMsg msg)
        {
            if (crews.TryGetValue(msg.AircraftNetId, out AircraftCrew crew))
            {
                Unseat(crew, player, RosterReason.LeftSeat, notifyLeaver: true);
            }
        }

        public static void OnControlFrame(INetworkPlayer player, CrewControlFrameMsg msg)
        {
            if (!crews.TryGetValue(msg.AircraftNetId, out AircraftCrew crew))
            {
                return;
            }
            if (!CrewSeat.IsWso(msg.Seat) || crew.Seats[msg.Seat] != player)
            {
                return;
            }
            if (msg.Station >= crew.Allocation.Length || !CrewAllocation.SeatHoldsStation(crew.Allocation[msg.Station], msg.Seat))
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now - crew.LastFrame[msg.Seat] < MinFrameInterval)
            {
                return;
            }
            crew.LastFrame[msg.Seat] = now;

            if (crew.Owner == null)
            {
                // The server simulates AI aircraft, so apply here.
                CrewNet.RaiseControlFrame(msg);
            }
            else
            {
                CrewNet.SendToPlayer(crew.Owner, msg);
            }
        }

        public static void OnDisconnected(INetworkPlayer player)
        {
            modded.Remove(player);

            var expired = new List<uint>();
            foreach (PendingJoin request in pending.Values)
            {
                if (request.Requester == player)
                {
                    expired.Add(request.Id);
                }
            }
            foreach (uint id in expired)
            {
                pending.Remove(id);
            }

            foreach (AircraftCrew crew in new List<AircraftCrew>(crews.Values))
            {
                if (crew.Owner == player)
                {
                    Disband(crew, RosterReason.PilotLeft);
                }
                else
                {
                    // They're gone, so only the rest of the crew hears about it.
                    Unseat(crew, player, RosterReason.Update, notifyLeaver: false);
                }
            }
        }

        // ---- upkeep ----

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < nextValidate)
            {
                return;
            }
            nextValidate = now + ValidateInterval;

            var expired = new List<PendingJoin>();
            foreach (PendingJoin request in pending.Values)
            {
                if (now > request.Expires)
                {
                    expired.Add(request);
                }
            }
            foreach (PendingJoin request in expired)
            {
                pending.Remove(request.Id);
                SendResult(request.Requester, request.AircraftNetId, request.Seat, JoinResult.TimedOut);
            }

            foreach (AircraftCrew crew in new List<AircraftCrew>(crews.Values))
            {
                if (!IsCrewable(crew.Aircraft))
                {
                    Disband(crew, RosterReason.AircraftLost);
                }
                else if (crew.Aircraft.Identity.Owner != crew.Owner)
                {
                    Disband(crew, RosterReason.PilotLeft);
                }
            }
        }

        // ---- seating ----

        // Returns null when the join is allowed; picks a free WSO seat when seat is 0.
        private static JoinResult? CheckJoin(INetworkPlayer player, uint aircraftNetId, ref byte seat, out Aircraft aircraft, uint ignoreRequestId = 0)
        {
            aircraft = ResolveAircraft(aircraftNetId);
            if (aircraft == null || !IsCrewable(aircraft) || !Plugin.IsMultiCrewAircraft(aircraft))
            {
                return JoinResult.AircraftNotEligible;
            }
            if (!player.TryGetPlayer<Player>(out Player requester))
            {
                return JoinResult.AircraftNotEligible;
            }
            if (requester.Aircraft != null && !requester.Aircraft.disabled)
            {
                return JoinResult.YouAreFlying;
            }
            if (requester.HQ == null || requester.HQ != aircraft.NetworkHQ)
            {
                return JoinResult.WrongFaction;
            }
            foreach (AircraftCrew other in crews.Values)
            {
                if (System.Array.IndexOf(other.Seats, player) >= 0)
                {
                    return JoinResult.AlreadyCrew;
                }
            }

            INetworkPlayer owner = aircraft.Identity.Owner;
            if (owner != null && !modded.Contains(owner))
            {
                return JoinResult.PilotLacksMod;
            }

            crews.TryGetValue(aircraftNetId, out AircraftCrew crew);
            if (seat == 0)
            {
                if (IsSeatFree(crew, aircraftNetId, CrewSeat.Wso1, ignoreRequestId))
                {
                    seat = CrewSeat.Wso1;
                }
                else if (IsSeatFree(crew, aircraftNetId, CrewSeat.Wso2, ignoreRequestId))
                {
                    seat = CrewSeat.Wso2;
                }
                else
                {
                    return JoinResult.SeatTaken;
                }
            }
            else if (!CrewSeat.IsWso(seat) || !IsSeatFree(crew, aircraftNetId, seat, ignoreRequestId))
            {
                return JoinResult.SeatTaken;
            }

            if (owner != null && ignoreRequestId == 0)
            {
                // One question at a time per pilot.
                foreach (PendingJoin request in pending.Values)
                {
                    if (request.AircraftNetId == aircraftNetId)
                    {
                        return JoinResult.PilotBusy;
                    }
                }
            }
            return null;
        }

        private static bool IsSeatFree(AircraftCrew crew, uint aircraftNetId, byte seat, uint ignoreRequestId)
        {
            if (crew != null && crew.Seats[seat] != null)
            {
                return false;
            }
            foreach (PendingJoin request in pending.Values)
            {
                if (request.Id != ignoreRequestId && request.AircraftNetId == aircraftNetId && request.Seat == seat)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Seat(INetworkPlayer player, Aircraft aircraft, byte seat)
        {
            if (!crews.TryGetValue(aircraft.NetId, out AircraftCrew crew))
            {
                crew = new AircraftCrew { NetId = aircraft.NetId, Aircraft = aircraft, Owner = aircraft.Identity.Owner };
                crews[crew.NetId] = crew;
            }
            crew.Seats[seat] = player;
            crew.LastFrame[seat] = 0f;
            Reallocate(crew);
            SendResult(player, crew.NetId, seat, JoinResult.Accepted);
            BroadcastRoster(crew, RosterReason.Update, null);
            Plugin.Log.LogInfo($"[Server] {player} seated as {CrewSeat.Name(seat)} in {aircraft.unitName}");
        }

        private static void Unseat(AircraftCrew crew, INetworkPlayer player, RosterReason reason, bool notifyLeaver)
        {
            int seat = System.Array.IndexOf(crew.Seats, player);
            if (seat <= 0)
            {
                return;
            }
            crew.Seats[seat] = null;
            Plugin.Log.LogInfo($"[Server] {player} left {CrewSeat.Name((byte)seat)} of {(crew.Aircraft != null ? crew.Aircraft.unitName : crew.NetId.ToString())}");
            if (crew.Seats[CrewSeat.Wso1] == null && crew.Seats[CrewSeat.Wso2] == null)
            {
                crews.Remove(crew.NetId);
                crew.Allocation = new StationAllocation[0];
            }
            else
            {
                Reallocate(crew);
            }
            // The leaver gets one last roster without them so their client unseats.
            BroadcastRoster(crew, reason, notifyLeaver ? player : null);
        }

        private static void Disband(AircraftCrew crew, RosterReason reason)
        {
            var former = new List<INetworkPlayer>();
            for (int i = 1; i < crew.Seats.Length; i++)
            {
                if (crew.Seats[i] != null)
                {
                    former.Add(crew.Seats[i]);
                    crew.Seats[i] = null;
                }
            }
            crews.Remove(crew.NetId);
            crew.Allocation = new StationAllocation[0];
            var msg = BuildRoster(crew, reason);
            foreach (INetworkPlayer player in former)
            {
                CrewNet.SendToPlayer(player, msg);
            }
            if (crew.Owner != null && modded.Contains(crew.Owner))
            {
                CrewNet.SendToPlayer(crew.Owner, msg);
            }
            Plugin.Log.LogInfo($"[Server] Crew of aircraft {crew.NetId} disbanded ({reason})");
        }

        private static void Reallocate(AircraftCrew crew)
        {
            crew.Allocation = CrewAllocation.Compute(crew.Aircraft, crew.Seats[CrewSeat.Wso1] != null, crew.Seats[CrewSeat.Wso2] != null);
        }

        private static void BroadcastRoster(AircraftCrew crew, RosterReason reason, INetworkPlayer extra)
        {
            var msg = BuildRoster(crew, reason);
            var sent = new HashSet<INetworkPlayer>();
            if (crew.Owner != null && modded.Contains(crew.Owner))
            {
                sent.Add(crew.Owner);
            }
            for (int i = 1; i < crew.Seats.Length; i++)
            {
                if (crew.Seats[i] != null)
                {
                    sent.Add(crew.Seats[i]);
                }
            }
            if (extra != null)
            {
                sent.Add(extra);
            }
            foreach (INetworkPlayer player in sent)
            {
                CrewNet.SendToPlayer(player, msg);
            }
        }

        private static CrewRosterMsg BuildRoster(AircraftCrew crew, RosterReason reason)
        {
            return new CrewRosterMsg
            {
                AircraftNetId = crew.NetId,
                Wso1NetId = PlayerNetId(crew.Seats[CrewSeat.Wso1]),
                Wso2NetId = PlayerNetId(crew.Seats[CrewSeat.Wso2]),
                Reason = (byte)reason,
                Stations = crew.Allocation,
            };
        }

        private static void SendResult(INetworkPlayer player, uint aircraftNetId, byte seat, JoinResult result)
        {
            CrewNet.SendToPlayer(player, new CrewJoinResultMsg { AircraftNetId = aircraftNetId, Seat = seat, Result = (byte)result });
            if (result != JoinResult.Accepted)
            {
                Plugin.Log.LogInfo($"[Server] Join by {player} for aircraft {aircraftNetId} refused: {result}");
            }
        }

        // ---- helpers ----

        private static Aircraft ResolveAircraft(uint netId)
        {
            NetworkServer server = CrewNet.Server;
            if (server?.World == null || !server.World.TryGetIdentity(netId, out NetworkIdentity identity) || identity == null)
            {
                return null;
            }
            return identity.GetComponent<Aircraft>();
        }

        private static bool IsCrewable(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled || aircraft.HasEjected())
            {
                return false;
            }
            Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
            return pilot == null || !pilot.dead;
        }

        private static uint PlayerNetId(INetworkPlayer player)
        {
            return player != null && player.TryGetPlayer<Player>(out Player p) ? p.NetId : 0u;
        }
    }
}
