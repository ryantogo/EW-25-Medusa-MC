using HarmonyLib;
using MedusaMultiCrew.Crew;
using UnityEngine;

namespace MedusaMultiCrew.Patches
{
    // Cockpit camera tweaks while the local player sits in a crew seat.
    internal static class CrewCameraPatches
    {
        private static bool IsCrewCamera(CameraStateManager cam)
        {
            return CrewSession.InSession && cam != null && cam.followingUnit == CrewSession.Aircraft;
        }

        // FollowCheck is spectator logic that drops a followed non-local aircraft after a short
        // validation interval. While seated we follow on purpose. (SimpleWSO, CC0.)
        [HarmonyPatch(typeof(CameraStateManager), "FollowCheck")]
        private static class FollowCheckPatch
        {
            private static bool Prefix(CameraStateManager __instance) => !IsCrewCamera(__instance);
        }

        // Vanilla hides every pilot model in cockpit view. In a multi-crew aircraft, show the rest
        // of the crew and hide only the model sitting where our camera is: the seat for a crew
        // member, pilots[0] for the pilot.
        [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.EnterState))]
        private static class EnterStatePatch
        {
            private static void Postfix(CameraStateManager cam)
            {
                Aircraft aircraft = cam != null ? cam.followingUnit as Aircraft : null;
                int ownSeat;
                if (IsCrewCamera(cam))
                {
                    ownSeat = CrewSession.Seat;
                }
                else if (aircraft != null && GameManager.IsLocalAircraft(aircraft) && Plugin.IsMultiCrewAircraft(aircraft))
                {
                    ownSeat = CrewSeat.Pilot;
                }
                else
                {
                    return;
                }
                Pilot[] pilots = aircraft.pilots;
                for (int i = 0; i < pilots.Length; i++)
                {
                    if (pilots[i] != null)
                    {
                        pilots[i].TogglePilotVisibility(i != ownSeat);
                    }
                }
            }
        }

        // Vanilla puts the camera at cockpitViewPoint (the pilot's head). Shift it to the seat's
        // camera position, a fixed point in the aircraft frame, keeping vanilla's inertia and
        // shake offsets. UpdateState returns early without positioning the camera when flight
        // controls are disabled or the pilot is dead, so mirror those checks to avoid adding the
        // offset twice.
        [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
        private static class UpdateStatePatch
        {
            private static void Postfix(CameraStateManager cam)
            {
                if (!IsCrewCamera(cam) || !GameManager.flightControlsEnabled)
                {
                    return;
                }
                Aircraft aircraft = CrewSession.Aircraft;
                if (aircraft.pilots.Length > 0 && aircraft.pilots[0].dead)
                {
                    return;
                }
                Transform viewPoint = aircraft.cockpitViewPoint;
                if (viewPoint == null || cam.cameraPivot == null)
                {
                    return;
                }
                Vector3 seat = aircraft.transform.TransformPoint(Plugin.SeatCameraPosition(CrewSession.Seat));
                cam.cameraPivot.position += seat - viewPoint.position;
            }
        }

        // The cockpit camera turns changes in cockpit velocity into G-shake, camera inertia and
        // rattle sound. On an aircraft this client doesn't simulate, velocity jumps with every
        // network snapshot, so the crew seat rattles constantly (SimpleWSO notes the same on the
        // Medusa). Skip that simulation and just let damage/event shake decay.
        [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.FixedUpdateState))]
        private static class FixedUpdateStatePatch
        {
            private static readonly AccessTools.FieldRef<CameraCockpitState, Vector3> CamRelativePos =
                AccessTools.FieldRefAccess<CameraCockpitState, Vector3>("camRelativePos");
            private static readonly AccessTools.FieldRef<CameraCockpitState, Vector3> CamRelativeVel =
                AccessTools.FieldRefAccess<CameraCockpitState, Vector3>("camRelativeVel");
            private static readonly AccessTools.FieldRef<CameraCockpitState, float> LowFreqShake =
                AccessTools.FieldRefAccess<CameraCockpitState, float>("lowFreqShake");
            private static readonly AccessTools.FieldRef<CameraCockpitState, float> HighFreqShake =
                AccessTools.FieldRefAccess<CameraCockpitState, float>("highFreqShake");

            private static bool Prefix(CameraCockpitState __instance, CameraStateManager cam)
            {
                if (!Plugin.DampRemoteCockpitShake.Value || !IsCrewCamera(cam) || CrewSession.Aircraft.LocalSim)
                {
                    return true;
                }
                CamRelativePos(__instance) = Vector3.zero;
                CamRelativeVel(__instance) = Vector3.zero;
                LowFreqShake(__instance) = Mathf.Lerp(LowFreqShake(__instance), 0f, 5f * Time.fixedDeltaTime);
                HighFreqShake(__instance) = Mathf.Lerp(HighFreqShake(__instance), 0f, 4f * Time.fixedDeltaTime);
                if (cam.cockpitRattle != null && cam.cockpitRattle.isPlaying)
                {
                    cam.cockpitRattle.Stop();
                }
                return false;
            }
        }
    }
}
