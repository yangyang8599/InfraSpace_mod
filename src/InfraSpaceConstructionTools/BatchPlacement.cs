using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal enum BatchCommitResult
    {
        Success,
        Invalid
    }

    internal interface IBatchPlacementSession
    {
        bool IsPreviewValid { get; }

        int RoadNodeCount { get; }

        void UpdatePreview(Vector2 mousePosition);

        BatchCommitResult TryCommit();

        void Cancel();
    }

    internal static class BatchPlacement
    {
        internal static Vector3 GetRawDelta(Vector3 anchor, Vector3 current)
        {
            return current - anchor;
        }

        internal static bool ShouldUseRawTranslation()
        {
            return Input.GetKey(KeyCode.LeftShift) ||
                   Input.GetKey(KeyCode.RightShift) ||
                   WorldScripts.Inst.gridManager.GetGridState() == GridStates.Off;
        }

        internal static bool CanPlaceBuildings(IReadOnlyList<Building> buildings)
        {
            return CanPlaceBuildings(buildings, null);
        }

        internal static bool CanPlaceBuildings(
            IReadOnlyList<Building> buildings,
            IReadOnlyList<Building> ignoredBuildings)
        {
            SetRepresentationsActive(ignoredBuildings, false);
            SetRepresentationsActive(buildings, false);
            try
            {
                foreach (Building building in buildings)
                {
                    building.SetRepresentationActive(true);
                    Physics.SyncTransforms();
                    if (!building.CanPlace(checkCollision: true))
                    {
                        return false;
                    }

                    building.SetRepresentationActive(false);
                }

                return true;
            }
            finally
            {
                SetRepresentationsActive(buildings, true);
                SetRepresentationsActive(ignoredBuildings, true);
            }
        }

        internal static void AddCosts(
            IDictionary<Resource, int> totals,
            IEnumerable<ResourceCost> costs)
        {
            foreach (ResourceCost cost in costs)
            {
                totals[cost.resource] = totals.TryGetValue(cost.resource, out int amount)
                    ? amount + cost.amount
                    : cost.amount;
            }
        }

        internal static bool CanPay(IReadOnlyCollection<ResourceCost> costs)
        {
            return CanSkipPay() || WorldScripts.Inst.resourceManager.CanPay(costs);
        }

        internal static void Pay(IEnumerable<ResourceCost> costs)
        {
            if (CanSkipPay())
            {
                return;
            }

            foreach (ResourceCost cost in costs)
            {
                WorldScripts.Inst.resourceManager.AddResource(cost.resource, -cost.amount);
            }
        }

        private static bool CanSkipPay()
        {
            return WorldScripts.Inst.worldSettings.noBuildingCosts ||
                   !WorldScripts.Inst.debugPanel.buildCostsButton.isOn;
        }

        private static void SetRepresentationsActive(
            IEnumerable<Building> buildings,
            bool active)
        {
            if (buildings == null)
            {
                return;
            }

            foreach (Building building in buildings)
            {
                building.SetRepresentationActive(active);
            }
        }
    }
}
