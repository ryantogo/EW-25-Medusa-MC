namespace MedusaMultiCrew.Net
{
    // Wire messages. Flat structs of primitives; serializers are registered by hand in
    // CrewSerializers because the mod is built without the Mirage weaver.
    // Player and aircraft references are Mirage NetIds; targets are Unit persistent IDs.

    // client -> server, once per session. Nothing else is sent until the server acks.
    public struct CrewHelloMsg
    {
        public byte Protocol;
        public ushort Version;
    }

    // server -> client, only to clients that sent CrewHelloMsg.
    public struct CrewHelloAckMsg
    {
        public byte Protocol;
        public ushort Version;
    }

    // crew -> server. Seat 0 means "first free WSO seat".
    public struct CrewJoinRequestMsg
    {
        public uint AircraftNetId;
        public byte Seat;
    }

    // server -> aircraft owner.
    public struct CrewJoinPromptMsg
    {
        public uint RequestId;
        public uint AircraftNetId;
        public byte Seat;
        public uint RequesterNetId;
    }

    // aircraft owner -> server.
    public struct CrewJoinDecisionMsg
    {
        public uint RequestId;
        public bool Accept;
    }

    // server -> requester.
    public struct CrewJoinResultMsg
    {
        public uint AircraftNetId;
        public byte Seat;
        public byte Result;
    }

    // crew -> server.
    public struct CrewLeaveMsg
    {
        public uint AircraftNetId;
    }

    // server -> aircraft owner and crew (and anyone just unseated). An empty roster
    // (both seats 0) means the crew was disbanded.
    public struct CrewRosterMsg
    {
        public uint AircraftNetId;
        public uint Wso1NetId;
        public uint Wso2NetId;
        public byte Reason;
        public StationAllocation[] Stations;
    }

    // crew -> server -> aircraft owner: the WSO's current intent, sent on change and as a
    // heartbeat while seated. The owner stops acting on a seat whose frames go stale.
    public struct CrewControlFrameMsg
    {
        public uint AircraftNetId;
        public byte Seat;
        public byte Station;
        public bool Firing;
        // Incremented each time the WSO presses Radar; the owner toggles on change.
        public byte RadarSeq;
        public float AimX;
        public float AimY;
        public float AimZ;
        public uint[] Targets;
    }

    public struct StationAllocation
    {
        public AllocationMode Mode;
        // Whole: the owning seat. Split: the seat holding mounts on the left (local X <= 0).
        public byte SeatA;
        // Split: the seat holding mounts on the right (local X > 0).
        public byte SeatB;
    }

    public enum AllocationMode : byte
    {
        Pilot = 0,
        Whole = 1,
        SplitBySide = 2,
    }

    public enum JoinResult : byte
    {
        Accepted = 0,
        Declined = 1,
        TimedOut = 2,
        SeatTaken = 3,
        AircraftNotEligible = 4,
        WrongFaction = 5,
        YouAreFlying = 6,
        AlreadyCrew = 7,
        PilotLacksMod = 8,
        PilotBusy = 9,
    }

    public enum RosterReason : byte
    {
        Update = 0,
        AircraftLost = 1,
        PilotLeft = 2,
        LeftSeat = 3,
    }
}
