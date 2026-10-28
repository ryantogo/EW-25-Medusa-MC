using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MedusaMultiCrew.Patches
{
    // Stretches every jamming pod's range by Jamming.PodRangeMultiplier. A pod's strength at a
    // distance comes from its rangeFalloff curve (JammingPod.FixedUpdate, server only), so the
    // curve is replaced with one scaled along the distance axis: the strength vanilla had at
    // d metres now reaches d * multiplier. The pod's WeaponInfo max range (AI engagement and the
    // HUD's in-range cue) is scaled to match, once per WeaponInfo asset.
    // Joint Fires carries the same patch and skips it when this mod is installed, so the boost
    // never stacks.
    [HarmonyPatch(typeof(JammingPod), nameof(JammingPod.AttachToUnit))]
    internal static class JammingPodRangePatch
    {
        private static readonly AccessTools.FieldRef<JammingPod, AnimationCurve> RangeFalloff =
            AccessTools.FieldRefAccess<JammingPod, AnimationCurve>("rangeFalloff");

        // Curves we produced, so a pod re-attached to a unit isn't stretched twice.
        private static readonly HashSet<AnimationCurve> scaledCurves = new HashSet<AnimationCurve>();
        private static readonly HashSet<WeaponInfo> scaledInfos = new HashSet<WeaponInfo>();

        private static void Postfix(JammingPod __instance)
        {
            float multiplier = Plugin.JammingPodRangeMultiplier.Value;
            if (Mathf.Approximately(multiplier, 1f) || multiplier <= 0f)
            {
                return;
            }

            AnimationCurve curve = RangeFalloff(__instance);
            if (curve != null && !scaledCurves.Contains(curve))
            {
                AnimationCurve scaled = Stretch(curve, multiplier);
                scaledCurves.Add(scaled);
                RangeFalloff(__instance) = scaled;
            }

            WeaponInfo info = __instance.info;
            if (info != null && scaledInfos.Add(info))
            {
                info.targetRequirements.maxRange *= multiplier;
                Plugin.Log.LogInfo($"[Jamming] {info.weaponName} range x{multiplier:0.##} (max range now {info.targetRequirements.maxRange:0} m)");
            }
        }

        private static AnimationCurve Stretch(AnimationCurve source, float factor)
        {
            Keyframe[] keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time *= factor;
                // Same shape over a longer distance: slopes get shallower by the same factor.
                keys[i].inTangent /= factor;
                keys[i].outTangent /= factor;
            }
            return new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }
    }
}
