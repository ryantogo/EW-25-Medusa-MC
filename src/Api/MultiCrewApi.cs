using System;
using MedusaMultiCrew.Crew;
using MedusaMultiCrew.Net;
using Mirage;

namespace MedusaMultiCrew
{
    // Public surface for other plugins (Joint Fires). Everything else in this assembly is
    // internal and may change between versions; this class only grows.
    // Seats: 0 = pilot, 1 = WSO 1, 2 = WSO 2.
    public static class MultiCrewApi
    {
        // Bumped when members are added. Check it before calling anything newer.
        public const int ApiVersion = 1;

        public static bool IsMultiCrewAircraft(Aircraft aircraft) => Plugin.IsMultiCrewAircraft(aircraft);

        // ---- server ----

        // Adds a join check run on the server after the built-in ones (faction, free seat, ...).
        // Return false to refuse; the joiner sees "this server doesn't allow you to crew aircraft".
        // A policy that throws refuses the join.
        public static void AddJoinPolicy(Func<INetworkPlayer, Aircraft, byte, bool> policy)
        {
            if (policy != null && !CrewServer.JoinPolicies.Contains(policy))
            {
                CrewServer.JoinPolicies.Add(policy);
            }
        }

        public static void RemoveJoinPolicy(Func<INetworkPlayer, Aircraft, byte, bool> policy) => CrewServer.JoinPolicies.Remove(policy);

        // Server only: the WSOs seated in an aircraft. False when it has no crew.
        public static bool TryGetCrew(Aircraft aircraft, out INetworkPlayer wso1, out INetworkPlayer wso2)
        {
            if (aircraft == null)
            {
                wso1 = null;
                wso2 = null;
                return false;
            }
            return CrewServer.TryGetSeats(aircraft.NetId, out wso1, out wso2);
        }

        // Server only: raised with the aircraft's NetId after anyone is seated or unseated,
        // or the crew is disbanded. The aircraft may already be gone.
        public static event Action<uint> ServerCrewChanged
        {
            add => CrewServer.CrewChanged += value;
            remove => CrewServer.CrewChanged -= value;
        }

        // ---- client ----

        // True once the host has answered our hello with a matching protocol.
        public static bool ServerHasMod => CrewNet.ServerHasMod;

        // The aircraft the local player is crewing as a WSO, or null. Set a frame or so after
        // LocalSeatChanged, once the crew view has resolved the aircraft.
        public static Aircraft LocalCrewAircraft => CrewSession.Aircraft;

        // The local player's WSO seat (1 or 2), or 0 when not seated.
        public static byte LocalSeat => CrewClient.SeatedSeat;

        // NetId of the aircraft the local player is seated in, or 0.
        public static uint LocalCrewAircraftNetId => CrewClient.SeatedAircraftNetId;

        // Raised with (aircraft NetId, seat) when the local player takes a seat, and (0, 0) on leaving.
        public static event Action<uint, byte> LocalSeatChanged
        {
            add => CrewClient.LocalSeatChanged += value;
            remove => CrewClient.LocalSeatChanged -= value;
        }

        // Client: the Player NetIds in each WSO seat (0 = empty). Known only for the aircraft the
        // local player flies or is seated in, since rosters go only to that crew.
        public static bool TryGetCrewNetIds(uint aircraftNetId, out uint wso1PlayerNetId, out uint wso2PlayerNetId)
        {
            if (CrewClient.TryGetRoster(aircraftNetId, out CrewRosterMsg roster))
            {
                wso1PlayerNetId = roster.Wso1NetId;
                wso2PlayerNetId = roster.Wso2NetId;
                return true;
            }
            wso1PlayerNetId = 0;
            wso2PlayerNetId = 0;
            return false;
        }

        // Client: raised with the aircraft's NetId whenever a roster arrives for it.
        public static event Action<uint> RosterReceived
        {
            add => CrewClient.RosterReceived += value;
            remove => CrewClient.RosterReceived -= value;
        }
    }
}
