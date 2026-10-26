using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    internal static class BlueprintStationState
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
                LogOnce("蓝图车站配置挂钩失败：找不到复制方法。");
                return;
            }

            harmony.Patch(
                copy,
                postfix: new HarmonyMethod(typeof(BlueprintStationState), nameof(AfterCopy)));
        }

        private static void AfterCopy(Building source, Building target)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (mod == null || source == null || target == null || source.GetID() == target.GetID())
            {
                return;
            }

            long sourceId = source.GetID();
            long targetId = target.GetID();
            CopyValue(mod.mirroredTrainStations, sourceId, targetId);
            CopyList(mod.trainStationRoadPorts, sourceId, targetId);
            CopyList(mod.trainStationRailPorts, sourceId, targetId);
        }

        private static void CopyValue(
            Dictionary<long, bool> records,
            long sourceId,
            long targetId)
        {
            if (records != null && records.TryGetValue(sourceId, out bool value))
            {
                records[targetId] = value;
            }
        }

        private static void CopyList(
            Dictionary<long, List<int>> records,
            long sourceId,
            long targetId)
        {
            if (records != null &&
                records.TryGetValue(sourceId, out List<int> values) &&
                values != null &&
                values.Count > 0)
            {
                records[targetId] = new List<int>(values);
            }
        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogWarning("[StationTools] " + message);
        }
    }
}
