using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceConstructionTools
{
    [HarmonyPatch(typeof(NetworkConstructionManager), "ReplaceSelectedEdge")]
    internal static class RailChainUpgradePatch
    {
        private const int MaxEdges = 4096;

        private static readonly MethodInfo ReplaceMethod =
            AccessTools.Method(typeof(NetworkConstructionManager), "ReplaceSelectedEdge");

        private static readonly AccessTools.FieldRef<NetworkConstructionManager, NetEdge> SelectedEdge =
            AccessTools.FieldRefAccess<NetworkConstructionManager, NetEdge>("selectedEdge");

        private static readonly AccessTools.FieldRef<NetworkConstructionManager, string> CurrentEdgeName =
            AccessTools.FieldRefAccess<NetworkConstructionManager, string>("currentEdgeName");

        private static readonly AccessTools.FieldRef<NetworkConstructionManager, NetworkType> CurrentNetworkType =
            AccessTools.FieldRefAccess<NetworkConstructionManager, NetworkType>("currentNetworkType");

        private static readonly AccessTools.FieldRef<NetworkConstructionManager, bool> IsReplacingEdge =
            AccessTools.FieldRefAccess<NetworkConstructionManager, bool>("isReplacingEdge");

        private static bool inside;

        internal static bool ShowHint
        {
            get
            {
                NetworkConstructionManager manager = WorldScripts.Inst?.networkConstructionManager;
                return manager != null &&
                       IsReplacingEdge(manager) &&
                       CurrentNetworkType(manager) == NetworkType.Rail;
            }
        }

        private static bool Prefix(NetworkConstructionManager __instance, bool onClick)
        {
            if (inside || !IsShiftHeld())
            {
                return true;
            }

            NetEdge selected = SelectedEdge(__instance);
            string targetName = CurrentEdgeName(__instance);
            if (!IsRailUpgrade(__instance, selected, targetName))
            {
                return true;
            }

            List<NetEdge> chain = Collect(selected);
            inside = true;
            try
            {
                foreach (NetEdge edge in chain)
                {
                    if (!ShouldUpgrade(edge, targetName))
                    {
                        continue;
                    }

                    SelectedEdge(__instance) = edge;
                    ReplaceMethod.Invoke(__instance, new object[] { onClick });
                    NetEdge replaced = SelectedEdge(__instance);
                    if (replaced == null || !replaced.IsValid() || replaced.GetEdgeName() != targetName)
                    {
                        break;
                    }
                }
            }
            finally
            {
                inside = false;
            }

            return false;
        }

        internal static void UpdatePrice(NetworkConstructionManager manager)
        {
            if (!IsShiftHeld() || !IsReplacingEdge(manager))
            {
                return;
            }

            NetEdge selected = SelectedEdge(manager);
            string targetName = CurrentEdgeName(manager);
            Text priceText = manager.floatingPriceText;
            if (!IsRailUpgrade(manager, selected, targetName) ||
                priceText == null ||
                !priceText.gameObject.activeSelf)
            {
                return;
            }

            List<ResourceCost> costs = TotalPrice(Collect(selected), targetName);
            if (costs.Count == 0)
            {
                return;
            }

            priceText.text = Resource.PriceToString(costs);
            priceText.color = WorldScripts.Inst.resourceManager.CanPay(costs)
                ? Color.white
                : WorldScripts.Inst.canvasHandler.redTextColor;
        }

        private static bool IsRailUpgrade(NetworkConstructionManager manager, NetEdge selected, string targetName)
        {
            return selected != null &&
                   targetName != null &&
                   CurrentNetworkType(manager) == NetworkType.Rail &&
                   selected.networkType == NetworkType.Rail;
        }

        private static List<NetEdge> Collect(NetEdge start)
        {
            List<NetEdge> result = new List<NetEdge>();
            HashSet<NetEdge> seen = new HashSet<NetEdge>();
            Queue<NetEdge> queue = new Queue<NetEdge>();
            queue.Enqueue(start);
            seen.Add(start);
            while (queue.Count > 0 && result.Count < MaxEdges)
            {
                NetEdge edge = queue.Dequeue();
                if (edge == null || !edge.IsValid() || edge.networkType != NetworkType.Rail)
                {
                    continue;
                }

                result.Add(edge);
                Enqueue(edge.startNode, seen, queue);
                Enqueue(edge.endNode, seen, queue);
            }

            return result;
        }

        private static void Enqueue(NetNode node, HashSet<NetEdge> seen, Queue<NetEdge> queue)
        {
            if (node == null || !node.IsValid())
            {
                return;
            }

            foreach (NetEdge next in node.GetEdges())
            {
                if (next != null &&
                    next.networkType == NetworkType.Rail &&
                    seen.Count < MaxEdges &&
                    seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        private static bool ShouldUpgrade(NetEdge edge, string targetName)
        {
            return edge != null &&
                   edge.IsValid() &&
                   edge.IsBuilt() &&
                   edge.IsEditable() &&
                   edge.networkType == NetworkType.Rail &&
                   edge.GetEdgeName() != targetName;
        }

        private static List<ResourceCost> TotalPrice(List<NetEdge> edges, string targetName)
        {
            Dictionary<Resource, int> totals = new Dictionary<Resource, int>();
            foreach (NetEdge edge in edges)
            {
                if (!ShouldUpgrade(edge, targetName))
                {
                    continue;
                }

                foreach (ResourceCost cost in RoadDescription.GetPrice(targetName, edge.GetPriceLength()))
                {
                    int amount = Mathf.CeilToInt(cost.amount / 2f);
                    totals[cost.resource] = totals.TryGetValue(cost.resource, out int have)
                        ? have + amount
                        : amount;
                }
            }

            return totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
        }

        private static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), "CustomUpdate")]
    internal static class RailChainPricePatch
    {
        private static void Postfix(NetworkConstructionManager __instance)
        {
            RailChainUpgradePatch.UpdatePrice(__instance);
        }
    }
}
