using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    internal static class DemandScores
    {
        internal struct ReportedDemand
        {
            public ConsumerProducer Consumer;
            public Resource Resource;
            public float Score;
        }

        private struct DemandId : IEquatable<DemandId>
        {
            public long ConsumerId;
            public string ResourceName;

            public bool Equals(DemandId other)
            {
                return ConsumerId == other.ConsumerId && ResourceName == other.ResourceName;
            }

            public override bool Equals(object obj)
            {
                return obj is DemandId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ConsumerId.GetHashCode() * 397) ^
                           (ResourceName == null ? 0 : ResourceName.GetHashCode());
                }
            }
        }

        private sealed class ScoreTable
        {
            public readonly Dictionary<DemandId, ReportedDemand> Scores =
                new Dictionary<DemandId, ReportedDemand>();
        }

        private static readonly object Gate = new object();

        private static readonly Dictionary<long, ScoreTable> Tables = new Dictionary<long, ScoreTable>();

        internal static void Report(
            DistributionProductionLogic center,
            ConsumerProducer consumer,
            float priority,
            Resource resource)
        {
            if (center != null)
            {
                center.UpdateRequestStatus(consumer, priority);
            }

            if (center?.consumerProducer?.building == null ||
                consumer?.building == null ||
                resource == null)
            {
                return;
            }

            long centerId = center.consumerProducer.building.GetID();
            DemandId id = new DemandId
            {
                ConsumerId = consumer.building.GetID(),
                ResourceName = resource.name,
            };
            lock (Gate)
            {
                ScoreTable table;
                if (!Tables.TryGetValue(centerId, out table))
                {
                    if (priority <= 0f)
                    {
                        return;
                    }

                    table = new ScoreTable();
                    Tables[centerId] = table;
                }

                if (priority <= 0f)
                {
                    table.Scores.Remove(id);
                    return;
                }

                table.Scores[id] = new ReportedDemand
                {
                    Consumer = consumer,
                    Resource = resource,
                    Score = priority,
                };
            }
        }

        internal static List<ReportedDemand> Copy(DistributionProductionLogic center)
        {
            List<ReportedDemand> copy = new List<ReportedDemand>();
            if (center?.consumerProducer?.building == null)
            {
                return copy;
            }

            lock (Gate)
            {
                ScoreTable table;
                if (!Tables.TryGetValue(center.consumerProducer.building.GetID(), out table))
                {
                    return copy;
                }

                List<DemandId> stale = null;
                foreach (KeyValuePair<DemandId, ReportedDemand> pair in table.Scores)
                {
                    ReportedDemand demand = pair.Value;
                    if (demand.Score <= 0f ||
                        demand.Consumer?.building == null ||
                        !demand.Consumer.building.IsValid())
                    {
                        if (stale == null)
                        {
                            stale = new List<DemandId>();
                        }

                        stale.Add(pair.Key);
                        continue;
                    }

                    copy.Add(demand);
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        table.Scores.Remove(stale[i]);
                    }
                }
            }

            return copy;
        }
    }

    [HarmonyPatch(typeof(ConsumerProducer), "RequestResources")]
    internal static class DemandScoreReportPatch
    {
        private static bool logged;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo update = AccessTools.Method(
                typeof(DistributionProductionLogic),
                nameof(DistributionProductionLogic.UpdateRequestStatus));
            MethodInfo getResource = AccessTools.PropertyGetter(typeof(ResourceCost), "resource");
            MethodInfo report = AccessTools.Method(typeof(DemandScores), nameof(DemandScores.Report));
            if (update == null || getResource == null || report == null)
            {
                LogOnce("配送分数挂钩失败：找不到原版方法。");
                return codes;
            }

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(update))
                {
                    continue;
                }

                int loader = FindResourceLoad(codes, i, getResource);
                if (loader < 0)
                {
                    LogOnce("配送分数挂钩失败：找不到物资参数。");
                    return codes;
                }

                CodeInstruction resourceLoad = codes[loader].Clone();
                CodeInstruction resourceField = codes[loader + 1].Clone();
                CodeInstruction resourceGet = codes[loader + 2].Clone();
                resourceLoad.labels.Clear();
                resourceLoad.blocks.Clear();
                resourceField.labels.Clear();
                resourceField.blocks.Clear();
                resourceGet.labels.Clear();
                resourceGet.blocks.Clear();
                resourceLoad.labels.AddRange(codes[i].labels);
                resourceLoad.blocks.AddRange(codes[i].blocks);
                codes[i].labels.Clear();
                codes[i].blocks.Clear();
                codes[i] = new CodeInstruction(OpCodes.Call, report);
                codes.Insert(i, resourceLoad);
                codes.Insert(i + 1, resourceField);
                codes.Insert(i + 2, resourceGet);
                return codes;
            }

            LogOnce("配送分数挂钩失败：找不到上报调用。");
            return codes;
        }

        private static int FindResourceLoad(
            List<CodeInstruction> codes,
            int callIndex,
            MethodInfo getResource)
        {
            for (int i = callIndex - 1; i >= 2; i--)
            {
                if (!codes[i].Calls(getResource) || codes[i - 1].opcode != OpCodes.Ldflda)
                {
                    continue;
                }

                return i - 2;
            }

            return -1;
        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogError("[InfraSpaceMultiResourceLogistics] " + message);
        }
    }
}
