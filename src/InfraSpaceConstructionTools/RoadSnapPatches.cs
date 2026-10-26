using System;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.CanBuildIntoNode))]
    internal static class IgnoreRoadNodePatch
    {
        private static bool Prefix(NetworkConstructionManager __instance, ref bool __result)
        {
            if (!RoadSnapFeature.MagnetSuppressed || !RoadSnapFeature.IsBuildingRoad(__instance))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.CanBuildIntoEdge))]
    internal static class IgnoreRoadEdgePatch
    {
        private static bool Prefix(NetworkConstructionManager __instance, ref bool __result)
        {
            if (!RoadSnapFeature.MagnetSuppressed || !RoadSnapFeature.IsBuildingRoad(__instance))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.HandleLeftMouseClick))]
    internal static class AllowPlaceOverFullNodePatch
    {
        private static void Prefix(NetworkConstructionManager __instance)
        {
            if (!RoadSnapFeature.MagnetSuppressed ||
                !RoadSnapFeature.IsBuildingRoad(__instance) ||
                RoadSnapFeature.FullNodeField == null)
            {
                return;
            }

            RoadSnapFeature.FullNodeField.SetValue(__instance, false);
        }
    }

    [HarmonyPatch(typeof(Helper), "GetNearbyColliders", new Type[] { typeof(Vector3), typeof(float), typeof(float) })]
    internal static class IgnoreNearbyRoadPatch
    {
        private static void Postfix(float radius, float height, ref Collider[] __result)
        {
            if (__result == null ||
                __result.Length == 0 ||
                radius != 0.7f ||
                height != 0.7f ||
                !RoadSnapFeature.MagnetSuppressed ||
                !RoadSnapFeature.IsBuildingRoad())
            {
                return;
            }

            int kept = 0;
            Collider[] filtered = new Collider[__result.Length];
            foreach (Collider collider in __result)
            {
                if (!IsRoadCollider(collider))
                {
                    filtered[kept++] = collider;
                }
            }

            if (kept == __result.Length)
            {
                return;
            }

            Collider[] trimmed = new Collider[kept];
            Array.Copy(filtered, trimmed, kept);
            __result = trimmed;
        }

        private static bool IsRoadCollider(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            ColliderInfo info = Helper.GetComponentInHierarchy<ColliderInfo>(collider.gameObject);
            return info != null && (info.collidable is NetEdge || info.collidable is NetNode);
        }
    }
}
