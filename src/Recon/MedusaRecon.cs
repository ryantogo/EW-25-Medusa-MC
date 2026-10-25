using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MedusaMultiCrew.Recon
{
    // Logs how a multi-crew aircraft is laid out (seats, weapon stations, mounts, cameras)
    // so seat positions and station splits can be designed against the real prefab.
    internal static class MedusaRecon
    {
        private static readonly AccessTools.FieldRef<WeaponManager, Aircraft> WeaponManagerAircraft =
            AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");

        private static readonly AccessTools.FieldRef<TargetCam, Transform> CamMountForward =
            AccessTools.FieldRefAccess<TargetCam, Transform>("camMountForward");

        private static readonly AccessTools.FieldRef<TargetCam, Transform> CamMountRear =
            AccessTools.FieldRefAccess<TargetCam, Transform>("camMountRear");

        internal static void Dump(Aircraft aircraft, string reason)
        {
            Transform root = aircraft.transform;
            var sb = new StringBuilder();
            sb.AppendLine($"=== Recon ({reason}): {aircraft.definition?.unitName} [key={aircraft.definition?.jsonKey}] '{aircraft.unitName}' ===");
            sb.AppendLine($"server={aircraft.IsServer} authority={aircraft.HasAuthority} localSim={aircraft.LocalSim} player={(aircraft.Player != null ? aircraft.Player.ToString() : "AI")}");

            sb.AppendLine($"cockpitViewPoint: {Describe(root, aircraft.cockpitViewPoint)}");

            Pilot[] pilots = aircraft.pilots ?? new Pilot[0];
            sb.AppendLine($"pilots: {pilots.Length}");
            for (int i = 0; i < pilots.Length; i++)
            {
                Pilot pilot = pilots[i];
                if (pilot == null)
                {
                    sb.AppendLine($"  [{i}] null");
                    continue;
                }
                sb.AppendLine($"  [{i}] {Describe(root, pilot.transform)} type={pilot.pilotType} ejectionSeat={pilot.HasEjectionSeat()} state={pilot.GetCurrentState()}");
            }

            sb.AppendLine($"weaponStations: {aircraft.weaponStations.Count}");
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                WeaponInfo info = station.WeaponInfo;
                sb.AppendLine($"  station {station.Number}: {info?.weaponName} gun={info?.gun} cargo={station.Cargo} weapons={station.Weapons.Count} turrets={station.Turrets.Count} ammo={station.Ammo}/{station.FullAmmo}");
                for (int w = 0; w < station.Weapons.Count; w++)
                {
                    Weapon weapon = station.Weapons[w];
                    if (weapon == null)
                    {
                        continue;
                    }
                    sb.AppendLine($"    weapon {w}: {weapon.GetType().Name} {Describe(root, weapon.transform)}");
                }
            }

            if (aircraft.targetCam != null)
            {
                sb.AppendLine($"targetCam: {Describe(root, aircraft.targetCam.transform)}");
                sb.AppendLine($"  mountForward: {Describe(root, CamMountForward(aircraft.targetCam))}");
                sb.AppendLine($"  mountRear: {Describe(root, CamMountRear(aircraft.targetCam))}");
            }
            else
            {
                sb.AppendLine("targetCam: none");
            }

            sb.AppendLine($"radar: {(aircraft.radar != null ? aircraft.radar.GetType().Name : "none")}");
            // Pods aren't found as children of the aircraft transform, so count them from the stations.
            int jammingPods = 0;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                foreach (Weapon weapon in station.Weapons)
                {
                    if (weapon is JammingPod)
                    {
                        jammingPods++;
                    }
                }
            }
            sb.AppendLine($"jammingPods: {jammingPods} radarJammers: {aircraft.GetComponentsInChildren<RadarJammer>(true).Length}");

            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Describe(Transform root, Transform t)
        {
            if (t == null)
            {
                return "none";
            }
            Vector3 local = root.InverseTransformPoint(t.position);
            return $"'{t.name}' (parent '{(t.parent != null ? t.parent.name : "-")}') at ({local.x:F2}, {local.y:F2}, {local.z:F2})";
        }

        [HarmonyPatch(typeof(WeaponManager), "OrganizeWeaponStations")]
        private static class OrganizeWeaponStationsPatch
        {
            private static void Postfix(WeaponManager __instance)
            {
                if (!Plugin.ReconLogging.Value)
                {
                    return;
                }
                Aircraft aircraft = WeaponManagerAircraft(__instance);
                if (!Plugin.IsMultiCrewAircraft(aircraft))
                {
                    return;
                }
                try
                {
                    Dump(aircraft, "weapons loaded");
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Recon dump failed: {e}");
                }
            }
        }
    }
}
