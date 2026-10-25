using HarmonyLib;
using MedusaMultiCrew.Crew;
using MedusaMultiCrew.Net;
using UnityEngine;

namespace MedusaMultiCrew.Patches
{
    // On the client that simulates a crewed aircraft, stations held by a WSO only fire and take
    // targets through CrewOwner. The pilot's trigger, the AI pilot and the pilot's HUD target
    // list are kept off them.
    internal static class CrewWeaponPatches
    {
        private static readonly AccessTools.FieldRef<WeaponManager, Aircraft> WeaponManagerAircraft =
            AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");

        private static float nextNotice;

        private static bool Blocked(Aircraft aircraft, int stationIndex)
        {
            return !CrewOwner.Authorized && CrewOwner.IsWsoStation(aircraft, stationIndex);
        }

        // The pilot's trigger: explain why nothing fires instead of failing silently.
        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.Fire))]
        private static class WeaponManagerFirePatch
        {
            private static bool Prefix(WeaponManager __instance)
            {
                Aircraft aircraft = WeaponManagerAircraft(__instance);
                WeaponStation station = __instance.currentWeaponStation;
                if (station == null || !Blocked(aircraft, station.Number))
                {
                    return true;
                }
                Notice(aircraft, $"{station.WeaponInfo?.weaponName} is held by {HolderName(aircraft, station.Number)}");
                return false;
            }
        }

        // The pilot's weapon cycling skips stations a WSO holds, so the pilot's HUD never offers
        // weapons they can't fire.
        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.NextWeaponStation))]
        private static class NextWeaponStationPatch
        {
            private static bool Prefix(WeaponManager __instance) => CyclePilotStation(__instance, +1);
        }

        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.PreviousWeaponStation))]
        private static class PreviousWeaponStationPatch
        {
            private static bool Prefix(WeaponManager __instance) => CyclePilotStation(__instance, -1);
        }

        // Returns false (skip vanilla) when a WSO holds any station of this aircraft.
        private static bool CyclePilotStation(WeaponManager manager, int delta)
        {
            Aircraft aircraft = WeaponManagerAircraft(manager);
            int count = aircraft != null ? aircraft.weaponStations.Count : 0;
            bool anyWso = false;
            for (int i = 0; i < count; i++)
            {
                anyWso |= CrewOwner.IsWsoStation(aircraft, i);
            }
            if (!anyWso)
            {
                return true;
            }

            int start = manager.currentWeaponStation != null ? manager.currentWeaponStation.Number : 0;
            for (int step = 1; step <= count; step++)
            {
                int index = ((start + delta * step) % count + count) % count;
                if (!CrewOwner.IsWsoStation(aircraft, index))
                {
                    if (index != start || CrewOwner.IsWsoStation(aircraft, start))
                    {
                        // Same steps as vanilla NextWeaponStation.
                        manager.currentWeaponStation = aircraft.weaponStations[index];
                        aircraft.SetActiveStation((byte)index);
                        SceneSingleton<CombatHUD>.i?.ShowWeaponStation(manager.currentWeaponStation);
                    }
                    return false;
                }
            }
            Notice(aircraft, "Weapons are with your WSO");
            return false;
        }

        private static void Notice(Aircraft aircraft, string text)
        {
            if (GameManager.IsLocalAircraft(aircraft) && Time.unscaledTime >= nextNotice)
            {
                nextNotice = Time.unscaledTime + 3f;
                SceneSingleton<AircraftActionsReport>.i?.ReportText(text, 3f);
            }
        }

        // Catches every other fire path on the owner (FireGuns, AI combat).
        [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.Fire))]
        private static class StationFirePatch
        {
            private static bool Prefix(WeaponStation __instance, Unit owner) => !Blocked(owner as Aircraft, __instance.Number);
        }

        [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
        private static class LaunchMountPatch
        {
            private static bool Prefix(WeaponStation __instance, Unit owner) => !Blocked(owner as Aircraft, __instance.Number);
        }

        // WeaponManager.TargetListChanged pushes the pilot's HUD targets into the current station;
        // a WSO station keeps its WSO's targets.
        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetStationTargets))]
        private static class SetStationTargetsPatch
        {
            private static bool Prefix(Aircraft __instance, byte stationIndex) => !Blocked(__instance, stationIndex);
        }

        private static string HolderName(Aircraft aircraft, int stationIndex)
        {
            if (!CrewOwner.TryGetAllocation(aircraft.NetId, out StationAllocation[] allocation) || stationIndex >= allocation.Length)
            {
                return "a WSO";
            }
            StationAllocation a = allocation[stationIndex];
            return a.Mode == AllocationMode.SplitBySide
                ? $"{CrewSeat.Name(a.SeatA)} and {CrewSeat.Name(a.SeatB)}"
                : CrewSeat.Name(a.SeatA);
        }
    }
}
