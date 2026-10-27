using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    internal static class BlueprintLogistics
    {
        private static bool logged;

        internal static void TryPatch(Harmony harmony)
        {
            System.Type session = AccessTools.TypeByName(
                "InfraSpaceConstructionTools.BlueprintCopySession");
            if (session == null)
            {
                return;
            }

            MethodInfo copy = AccessTools.Method(session, "CopyBuildingConfiguration");
            if (copy == null)
            {
                LogOnce("蓝图物资挂钩失败：找不到复制方法。");
                return;
            }

            harmony.Patch(
                copy,
                postfix: new HarmonyMethod(typeof(BlueprintLogistics), nameof(AfterCopy)));
        }

        private static void AfterCopy(Building source, Building target)
        {
            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || source == null || target == null)
            {
                return;
            }

            long sourceId = source.GetID();
            long targetId = target.GetID();
            if (sourceId == targetId)
            {
                return;
            }

            if (mod.centers != null &&
                mod.centers.TryGetValue(sourceId, out CenterSettings settings) &&
                settings?.resourceNames != null &&
                settings.resourceNames.Count > 0)
            {
                mod.centers[targetId] = new CenterSettings
                {
                    resourceNames = new List<string>(settings.resourceNames),
                    capacities = settings.capacities == null
                        ? new Dictionary<string, int>()
                        : new Dictionary<string, int>(settings.capacities),
                    radii = settings.radii == null
                        ? new Dictionary<string, float>()
                        : new Dictionary<string, float>(settings.radii),
                    storageOnly = settings.storageOnly == null
                        ? new List<string>()
                        : new List<string>(settings.storageOnly)
                };
                target.consumerProducer?.ResetConsumablesProducables();
            }

        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogWarning(message);
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    internal static class DestroyedCenterCleanupPatch
    {
        private static void Postfix(Building __instance)
        {
            MultiResourceLogisticsMod.Instance?.centers?.Remove(__instance.GetID());
        }
    }
}
