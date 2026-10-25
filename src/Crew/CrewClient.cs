using System.Collections.Generic;
using MedusaMultiCrew.Core;
using MedusaMultiCrew.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // Client mirror of the server's crew state for the local player: the seat we hold (as WSO),
    // rosters of our own aircraft (as pilot), the pending join prompt, and the dev keys.
    // Phase 3 hangs the crew view off LocalSeatChanged.
    internal static class CrewClient
    {
        private const float PromptLifetime = 20f;

        public static uint SeatedAircraftNetId { get; private set; }
        public static byte SeatedSeat { get; private set; }
        public static bool IsSeated => SeatedAircraftNetId != 0;

        // (aircraftNetId, seat); (0, 0) when we leave.
        public static event System.Action<uint, byte> LocalSeatChanged;

        private static readonly Dictionary<uint, CrewRosterMsg> rosters = new Dictionary<uint, CrewRosterMsg>();
        private static readonly HashSet<byte> loggedFrameSeats = new HashSet<byte>();
        private static CrewJoinPromptMsg? prompt;
        private static float promptExpires;
        private static bool warnedNoMod;

        public static void Reset()
        {
            if (IsSeated)
            {
                SetLocalSeat(0, 0);
            }
            rosters.Clear();
            loggedFrameSeats.Clear();
            prompt = null;
            warnedNoMod = false;
        }

        public static bool TryGetRoster(uint aircraftNetId, out CrewRosterMsg roster) => rosters.TryGetValue(aircraftNetId, out roster);

        // ---- handlers ----

        public static void OnHelloAck(INetworkPlayer _, CrewHelloAckMsg msg)
        {
            if (msg.Protocol != CrewNet.Protocol)
            {
                Plugin.Log.LogWarning($"[Client] Server runs multi-crew protocol {msg.Protocol}, we run {CrewNet.Protocol}; multi-crew disabled");
                Notify("Multi-crew disabled: the server runs a different version of the mod");
                return;
            }
            CrewNet.SetServerHasMod(true);
            Plugin.Log.LogInfo($"[Client] Server has the mod (v{msg.Version}); multi-crew enabled");
        }

        public static void OnJoinPrompt(INetworkPlayer _, CrewJoinPromptMsg msg)
        {
            prompt = msg;
            promptExpires = Time.unscaledTime + PromptLifetime;
            string who = PlayerName(msg.RequesterNetId);
            Notify($"{who} wants to join as {CrewSeat.Name(msg.Seat)}. {Plugin.AcceptKey.Value} = accept, {Plugin.DeclineKey.Value} = decline", PromptLifetime);
        }

        public static void OnJoinResult(INetworkPlayer _, CrewJoinResultMsg msg)
        {
            var result = (JoinResult)msg.Result;
            Plugin.Log.LogInfo($"[Client] Join result for aircraft {msg.AircraftNetId}: {result}");
            if (result != JoinResult.Accepted)
            {
                Notify($"Couldn't join: {Describe(result)}");
            }
        }

        public static void OnRoster(INetworkPlayer _, CrewRosterMsg msg)
        {
            bool empty = msg.Wso1NetId == 0 && msg.Wso2NetId == 0;
            if (empty)
            {
                rosters.Remove(msg.AircraftNetId);
            }
            else
            {
                rosters[msg.AircraftNetId] = msg;
            }
            LogRoster(msg);

            uint me = LocalPlayerNetId();
            byte mySeat = me == 0 ? (byte)0 : me == msg.Wso1NetId ? CrewSeat.Wso1 : me == msg.Wso2NetId ? CrewSeat.Wso2 : (byte)0;
            if (mySeat != 0)
            {
                if (SeatedAircraftNetId != msg.AircraftNetId || SeatedSeat != mySeat)
                {
                    SetLocalSeat(msg.AircraftNetId, mySeat);
                    Notify($"You are {CrewSeat.Name(mySeat)}. {Plugin.JoinLeaveKey.Value} = leave seat");
                }
            }
            else if (SeatedAircraftNetId == msg.AircraftNetId)
            {
                SetLocalSeat(0, 0);
                Notify(LeaveText((RosterReason)msg.Reason));
            }
            else if (IsLocalAircraft(msg.AircraftNetId))
            {
                // We're the pilot of this aircraft.
                if (empty)
                {
                    Notify("Your crew has left");
                }
                else
                {
                    Notify($"Crew: WSO 1 {SeatHolder(msg.Wso1NetId)}, WSO 2 {SeatHolder(msg.Wso2NetId)}");
                }
            }
        }

        // Owner side. Phase 4 applies these; for now just prove they arrive.
        public static void OnControlFrame(INetworkPlayer _, CrewControlFrameMsg msg)
        {
            if (loggedFrameSeats.Add(msg.Seat))
            {
                Plugin.Log.LogInfo($"[Client] First control frame from {CrewSeat.Name(msg.Seat)} (station {msg.Station})");
            }
            CrewNet.RaiseControlFrame(msg);
        }

        // ---- input ----

        public static void Update()
        {
            if (prompt.HasValue && Time.unscaledTime > promptExpires)
            {
                prompt = null;
            }

            if (RewiredInput.GetKeyDown(Plugin.JoinLeaveKey.Value))
            {
                ToggleSeat();
            }
            if (prompt.HasValue && RewiredInput.GetKeyDown(Plugin.AcceptKey.Value))
            {
                Answer(true);
            }
            else if (prompt.HasValue && RewiredInput.GetKeyDown(Plugin.DeclineKey.Value))
            {
                Answer(false);
            }
        }

        private static void ToggleSeat()
        {
            if (!CrewNet.ServerHasMod)
            {
                if (!warnedNoMod)
                {
                    warnedNoMod = true;
                    Notify("Multi-crew needs the mod on the host too");
                }
                return;
            }
            if (IsSeated)
            {
                CrewNet.SendToServer(new CrewLeaveMsg { AircraftNetId = SeatedAircraftNetId });
                return;
            }

            // Dev entry: join the aircraft the spectator camera is following.
            Aircraft followed = CameraStateManager.i != null ? CameraStateManager.i.followingUnit as Aircraft : null;
            if (followed == null || !Plugin.IsMultiCrewAircraft(followed))
            {
                Notify("Spectate a friendly Medusa first, then press " + Plugin.JoinLeaveKey.Value);
                return;
            }
            if (IsLocalAircraft(followed.NetId))
            {
                Notify("You're flying this aircraft");
                return;
            }
            CrewNet.SendToServer(new CrewJoinRequestMsg { AircraftNetId = followed.NetId, Seat = 0 });
            Notify("Asking the pilot to join...");
        }

        private static void Answer(bool accept)
        {
            CrewJoinPromptMsg msg = prompt.Value;
            prompt = null;
            CrewNet.SendToServer(new CrewJoinDecisionMsg { RequestId = msg.RequestId, Accept = accept });
            Notify(accept ? "Crew request accepted" : "Crew request declined");
        }

        // ---- helpers ----

        private static void SetLocalSeat(uint aircraftNetId, byte seat)
        {
            SeatedAircraftNetId = aircraftNetId;
            SeatedSeat = seat;
            try
            {
                LocalSeatChanged?.Invoke(aircraftNetId, seat);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Client] Seat change handler failed: {e}");
            }
        }

        private static void LogRoster(CrewRosterMsg msg)
        {
            var parts = new List<string>();
            for (int i = 0; i < msg.Stations.Length; i++)
            {
                StationAllocation a = msg.Stations[i];
                switch (a.Mode)
                {
                    case AllocationMode.Whole:
                        parts.Add($"{i}={CrewSeat.Name(a.SeatA)}");
                        break;
                    case AllocationMode.SplitBySide:
                        parts.Add($"{i}=left {CrewSeat.Name(a.SeatA)}/right {CrewSeat.Name(a.SeatB)}");
                        break;
                    default:
                        parts.Add($"{i}=Pilot");
                        break;
                }
            }
            Plugin.Log.LogInfo($"[Client] Roster for aircraft {msg.AircraftNetId} ({(RosterReason)msg.Reason}): WSO 1={msg.Wso1NetId} WSO 2={msg.Wso2NetId} stations [{string.Join(", ", parts)}]");
        }

        private static bool IsLocalAircraft(uint aircraftNetId)
        {
            return GameManager.GetLocalAircraft(out Aircraft local) && local != null && local.NetId == aircraftNetId;
        }

        private static uint LocalPlayerNetId()
        {
            return GameManager.GetLocalPlayer<Player>(out Player player) ? player.NetId : 0u;
        }

        private static string SeatHolder(uint playerNetId) => playerNetId == 0 ? "empty" : PlayerName(playerNetId);

        private static string PlayerName(uint playerNetId)
        {
            NetworkClient client = CrewNet.Client;
            if (client?.World != null && client.World.TryGetIdentity(playerNetId, out NetworkIdentity identity) && identity != null)
            {
                Player player = identity.GetComponent<Player>();
                if (player != null)
                {
                    return player.GetDisplayName(PlayerNameContext.Other);
                }
            }
            return "A player";
        }

        private static string Describe(JoinResult result)
        {
            switch (result)
            {
                case JoinResult.Declined: return "the pilot declined";
                case JoinResult.TimedOut: return "the pilot didn't answer";
                case JoinResult.SeatTaken: return "no free WSO seat";
                case JoinResult.AircraftNotEligible: return "that aircraft can't take crew";
                case JoinResult.WrongFaction: return "join the aircraft's faction first";
                case JoinResult.YouAreFlying: return "you're flying an aircraft";
                case JoinResult.AlreadyCrew: return "you're already in a crew seat";
                case JoinResult.PilotLacksMod: return "the pilot doesn't have the mod";
                case JoinResult.PilotBusy: return "the pilot is answering another request";
                default: return result.ToString();
            }
        }

        private static string LeaveText(RosterReason reason)
        {
            switch (reason)
            {
                case RosterReason.AircraftLost: return "Aircraft lost; you left your seat";
                case RosterReason.PilotLeft: return "The pilot left; you left your seat";
                default: return "You left your seat";
            }
        }

        private static void Notify(string text, float seconds = 5f)
        {
            Plugin.Log.LogInfo($"[Client] {text}");
            try
            {
                SceneSingleton<AircraftActionsReport>.i?.ReportText(text, seconds);
            }
            catch (System.Exception)
            {
                // The report panel only exists in missions; the log line above is enough.
            }
        }
    }
}
