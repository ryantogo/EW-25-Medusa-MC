using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MedusaMultiCrew.Core;
using MedusaMultiCrew.Crew;

namespace MedusaMultiCrew.Patches
{
    // Keep the crew HUD on the crew's own target list and stations. Vanilla CombatHUD selection
    // writes through WeaponManager into the aircraft's (pilot's) target list and current station;
    // on a host that would even replicate to everyone. Adapted from SimpleWSO's
    // GunnerTargetListPatch (CC0, Bongus/Motmir).
    internal static class CrewHudPatches
    {
        private static int hudSelectionDepth;
        private static int hudStationDepth;

        private static readonly AccessTools.FieldRef<WeaponManager, Aircraft> WeaponManagerAircraft =
            AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");

        private static bool IsCrewHud(CombatHUD hud) => CrewSession.InSession && hud != null && hud.aircraft == CrewSession.Aircraft;

        private static bool IsCrewWeaponManager(WeaponManager wm) => CrewSession.InSession && wm != null && WeaponManagerAircraft(wm) == CrewSession.Aircraft;

        private static bool InCrewSelection(WeaponManager wm) => hudSelectionDepth > 0 && IsCrewWeaponManager(wm);

        private static void BindCrewList(CombatHUD hud)
        {
            if (IsCrewHud(hud))
            {
                Reflect.SetField(hud, "targetList", CrewSession.TargetList);
            }
        }

        private static void BeginSelection(CombatHUD hud)
        {
            if (IsCrewHud(hud))
            {
                hudSelectionDepth++;
                BindCrewList(hud);
            }
        }

        private static void EndSelection(CombatHUD hud)
        {
            if (IsCrewHud(hud))
            {
                BindCrewList(hud);
                if (hudSelectionDepth > 0)
                {
                    hudSelectionDepth--;
                }
            }
        }

        // The HUD may only show stations the seat holds (the pilot's station changes replicate
        // to us and would otherwise switch our display).
        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.ShowWeaponStation))]
        private static class ShowWeaponStationPatch
        {
            private static bool Prefix(CombatHUD __instance, WeaponStation weaponStation, ref bool __state)
            {
                __state = false;
                if (!IsCrewHud(__instance))
                {
                    return true;
                }
                if (weaponStation == null || !CrewSession.SeatHolds(weaponStation.Number))
                {
                    return false;
                }
                hudStationDepth++;
                BindCrewList(__instance);
                __state = true;
                return true;
            }

            private static void Postfix(CombatHUD __instance, bool __state)
            {
                if (__state)
                {
                    BindCrewList(__instance);
                    hudStationDepth--;
                }
            }
        }

        [HarmonyPatch(typeof(CombatHUD), "TargetSelect")]
        private static class TargetSelectPatch
        {
            private static void Prefix(CombatHUD __instance) => BeginSelection(__instance);
            private static void Postfix(CombatHUD __instance) => EndSelection(__instance);
        }

        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.SelectUnit))]
        private static class SelectUnitPatch
        {
            private static void Prefix(CombatHUD __instance) => BeginSelection(__instance);
            private static void Postfix(CombatHUD __instance) => EndSelection(__instance);
        }

        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.DeSelectUnit))]
        private static class DeSelectUnitPatch
        {
            private static void Prefix(CombatHUD __instance) => BeginSelection(__instance);
            private static void Postfix(CombatHUD __instance) => EndSelection(__instance);
        }

        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.DeselectAll))]
        private static class DeselectAllPatch
        {
            private static void Prefix(CombatHUD __instance) => BeginSelection(__instance);
            private static void Postfix(CombatHUD __instance) => EndSelection(__instance);
        }

        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.AddTargetList))]
        private static class AddTargetListPatch
        {
            private static bool Prefix(WeaponManager __instance, Unit target)
            {
                if (!InCrewSelection(__instance))
                {
                    return true;
                }
                if (target != null && !CrewSession.TargetList.Contains(target))
                {
                    CrewSession.TargetList.Insert(0, target);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.RemoveTargetList))]
        private static class RemoveTargetListPatch
        {
            private static bool Prefix(WeaponManager __instance, Unit target)
            {
                if (!InCrewSelection(__instance))
                {
                    return true;
                }
                CrewSession.TargetList.Remove(target);
                return false;
            }
        }

        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.ClearTargetList))]
        private static class ClearTargetListPatch
        {
            private static bool Prefix(WeaponManager __instance)
            {
                if (!InCrewSelection(__instance))
                {
                    return true;
                }
                CrewSession.TargetList.Clear();
                return false;
            }
        }

        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.GetTargetList))]
        private static class GetTargetListPatch
        {
            private static bool Prefix(WeaponManager __instance, ref List<Unit> __result)
            {
                if (hudStationDepth == 0 || !IsCrewWeaponManager(__instance))
                {
                    return true;
                }
                __result = CrewSession.TargetList;
                return false;
            }
        }

        // The replicated pilot target-list path must never land in the crew's private selection.
        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SetTargetList))]
        private static class SetTargetListPatch
        {
            private static bool Prefix(WeaponManager __instance) => !IsCrewWeaponManager(__instance);
        }

        // Stop vanilla pushing the crew's selection into the aircraft's current (pilot's) station.
        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.TargetListChanged))]
        private static class TargetListChangedPatch
        {
            private static bool Prefix(WeaponManager __instance) => !InCrewSelection(__instance);
        }

        // Looping missile alarms only stop when ThreatList.itemLookup still has the missile, and
        // the HUD rebinding above can clear that map. (SimpleWSO ThreatListHelper, CC0.)
        [HarmonyPatch(typeof(ThreatList), "ThreatList_OffMissileWarning")]
        private static class OffMissileWarningPatch
        {
            private static void Postfix(ThreatList __instance, MissileWarning.OffMissileWarning e)
            {
                Missile missile = e.missile;
                if (missile == null)
                {
                    return;
                }
                string seekerType = missile.GetSeekerType();
                if (string.IsNullOrEmpty(seekerType))
                {
                    return;
                }
                if (Reflect.TryGetField(__instance, "alarmLookup", out IDictionary lookup) && lookup != null && lookup.Contains(seekerType))
                {
                    Reflect.TryCall(lookup[seekerType], "RemoveMissile", missile);
                }
            }
        }
    }
}
