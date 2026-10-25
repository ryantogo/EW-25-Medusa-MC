using Mirage.Serialization;
using UnityEngine;

namespace MedusaMultiCrew.Net
{
    // Hand-written Mirage serializers (no weaver at mod build time). Pattern from SimpleWSO (CC0).
    // Readers clamp array lengths so a malformed packet can't allocate unbounded memory.
    internal static class CrewSerializers
    {
        public const int MaxTargets = 32;
        public const int MaxStations = 32;

        private static bool registered;

        public static void Register()
        {
            if (registered)
            {
                return;
            }
            registered = true;

            Writer<CrewHelloMsg>.Write = (w, m) => { w.WriteByte(m.Protocol); w.WriteUInt16(m.Version); };
            Reader<CrewHelloMsg>.Read = r => new CrewHelloMsg { Protocol = r.ReadByte(), Version = r.ReadUInt16() };

            Writer<CrewHelloAckMsg>.Write = (w, m) => { w.WriteByte(m.Protocol); w.WriteUInt16(m.Version); };
            Reader<CrewHelloAckMsg>.Read = r => new CrewHelloAckMsg { Protocol = r.ReadByte(), Version = r.ReadUInt16() };

            Writer<CrewJoinRequestMsg>.Write = (w, m) => { w.WriteUInt32(m.AircraftNetId); w.WriteByte(m.Seat); };
            Reader<CrewJoinRequestMsg>.Read = r => new CrewJoinRequestMsg { AircraftNetId = r.ReadUInt32(), Seat = r.ReadByte() };

            Writer<CrewJoinPromptMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.RequestId);
                w.WriteUInt32(m.AircraftNetId);
                w.WriteByte(m.Seat);
                w.WriteUInt32(m.RequesterNetId);
            };
            Reader<CrewJoinPromptMsg>.Read = r => new CrewJoinPromptMsg
            {
                RequestId = r.ReadUInt32(),
                AircraftNetId = r.ReadUInt32(),
                Seat = r.ReadByte(),
                RequesterNetId = r.ReadUInt32(),
            };

            Writer<CrewJoinDecisionMsg>.Write = (w, m) => { w.WriteUInt32(m.RequestId); w.WriteBoolean(m.Accept); };
            Reader<CrewJoinDecisionMsg>.Read = r => new CrewJoinDecisionMsg { RequestId = r.ReadUInt32(), Accept = r.ReadBoolean() };

            Writer<CrewJoinResultMsg>.Write = (w, m) => { w.WriteUInt32(m.AircraftNetId); w.WriteByte(m.Seat); w.WriteByte(m.Result); };
            Reader<CrewJoinResultMsg>.Read = r => new CrewJoinResultMsg { AircraftNetId = r.ReadUInt32(), Seat = r.ReadByte(), Result = r.ReadByte() };

            Writer<CrewLeaveMsg>.Write = (w, m) => w.WriteUInt32(m.AircraftNetId);
            Reader<CrewLeaveMsg>.Read = r => new CrewLeaveMsg { AircraftNetId = r.ReadUInt32() };

            Writer<CrewRosterMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.AircraftNetId);
                w.WriteUInt32(m.Wso1NetId);
                w.WriteUInt32(m.Wso2NetId);
                w.WriteByte(m.Reason);
                int count = m.Stations != null ? Mathf.Min(m.Stations.Length, MaxStations) : 0;
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteByte((byte)m.Stations[i].Mode);
                    w.WriteByte(m.Stations[i].SeatA);
                    w.WriteByte(m.Stations[i].SeatB);
                }
            };
            Reader<CrewRosterMsg>.Read = r =>
            {
                var m = new CrewRosterMsg
                {
                    AircraftNetId = r.ReadUInt32(),
                    Wso1NetId = r.ReadUInt32(),
                    Wso2NetId = r.ReadUInt32(),
                    Reason = r.ReadByte(),
                };
                int count = Mathf.Min(r.ReadByte(), MaxStations);
                m.Stations = new StationAllocation[count];
                for (int i = 0; i < count; i++)
                {
                    m.Stations[i] = new StationAllocation
                    {
                        Mode = (AllocationMode)r.ReadByte(),
                        SeatA = r.ReadByte(),
                        SeatB = r.ReadByte(),
                    };
                }
                return m;
            };

            Writer<CrewControlFrameMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.AircraftNetId);
                w.WriteByte(m.Seat);
                w.WriteByte(m.Station);
                w.WriteBoolean(m.Firing);
                w.WriteByte(m.RadarSeq);
                w.WriteSingle(m.AimX);
                w.WriteSingle(m.AimY);
                w.WriteSingle(m.AimZ);
                int count = m.Targets != null ? Mathf.Min(m.Targets.Length, MaxTargets) : 0;
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteUInt32(m.Targets[i]);
                }
            };
            Reader<CrewControlFrameMsg>.Read = r =>
            {
                var m = new CrewControlFrameMsg
                {
                    AircraftNetId = r.ReadUInt32(),
                    Seat = r.ReadByte(),
                    Station = r.ReadByte(),
                    Firing = r.ReadBoolean(),
                    RadarSeq = r.ReadByte(),
                    AimX = r.ReadSingle(),
                    AimY = r.ReadSingle(),
                    AimZ = r.ReadSingle(),
                };
                int count = Mathf.Min(r.ReadByte(), MaxTargets);
                m.Targets = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    m.Targets[i] = r.ReadUInt32();
                }
                return m;
            };
        }
    }
}
