using MedusaMultiCrew.Net;

namespace MedusaMultiCrew.Crew
{
    // Default seat -> station split (docs/game-internals.md, "Default station allocation").
    // Stations are grouped by weapon type, so rules match on what is mounted, not station numbers.
    // Mounts in a split station are told apart by side (local X), which every client can
    // compute from geometry; station.Weapons order isn't guaranteed to match across machines.
    internal static class CrewAllocation
    {
        public static StationAllocation[] Compute(Aircraft aircraft, bool wso1Seated, bool wso2Seated)
        {
            int count = aircraft.weaponStations.Count;
            var result = new StationAllocation[count];
            if (!wso1Seated && !wso2Seated)
            {
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                WeaponStation station = aircraft.weaponStations[i];
                if (IsFixedGun(station))
                {
                    // Forward-firing guns need the pilot's aim.
                    result[i] = new StationAllocation { Mode = AllocationMode.Pilot };
                    continue;
                }
                if (wso1Seated != wso2Seated)
                {
                    byte only = wso1Seated ? CrewSeat.Wso1 : CrewSeat.Wso2;
                    result[i] = new StationAllocation { Mode = AllocationMode.Whole, SeatA = only };
                    continue;
                }
                if (Holds<JammingPod>(station))
                {
                    result[i] = new StationAllocation { Mode = AllocationMode.SplitBySide, SeatA = CrewSeat.Wso1, SeatB = CrewSeat.Wso2 };
                }
                else if (Holds<Laser>(station))
                {
                    result[i] = new StationAllocation { Mode = AllocationMode.Whole, SeatA = CrewSeat.Wso1 };
                }
                else
                {
                    result[i] = new StationAllocation { Mode = AllocationMode.Whole, SeatA = CrewSeat.Wso2 };
                }
            }
            return result;
        }

        public static bool SeatHoldsStation(StationAllocation allocation, byte seat)
        {
            switch (allocation.Mode)
            {
                case AllocationMode.Whole:
                    return allocation.SeatA == seat;
                case AllocationMode.SplitBySide:
                    return allocation.SeatA == seat || allocation.SeatB == seat;
                default:
                    return false;
            }
        }

        // True if the mount at this aircraft-local X belongs to the seat.
        public static bool SeatHoldsMount(StationAllocation allocation, byte seat, float localX)
        {
            if (allocation.Mode != AllocationMode.SplitBySide)
            {
                return SeatHoldsStation(allocation, seat);
            }
            return (localX <= 0f ? allocation.SeatA : allocation.SeatB) == seat;
        }

        private static bool IsFixedGun(WeaponStation station)
        {
            return station.WeaponInfo != null && station.WeaponInfo.gun && station.Turrets.Count == 0;
        }

        private static bool Holds<T>(WeaponStation station) where T : Weapon
        {
            foreach (Weapon weapon in station.Weapons)
            {
                if (weapon is T)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
