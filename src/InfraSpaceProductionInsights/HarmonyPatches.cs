using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    [HarmonyPatch(typeof(FactoryProductionLogic), nameof(FactoryProductionLogic.UpdateSimulationThread))]
    internal static class RecordFactoryRatesPatch
    {
        private static void Postfix(FactoryProductionLogic __instance)
        {
            ProductionSnapshots.RecordFactory(__instance);
        }
    }

    [HarmonyPatch(typeof(Habitat), nameof(Habitat.UpdateSimulationThread))]
    internal static class RecordHabitatRatesPatch
    {
        private static void Postfix(Habitat __instance)
        {
            ProductionSnapshots.RecordHabitat(__instance);
        }
    }

    [HarmonyPatch(typeof(PrioritizationPanel), "Start")]
    internal static class InstallFinalProductTogglePatch
    {
        private static void Postfix(PrioritizationPanel __instance)
        {
            FinalProductToggle.Install(__instance);
        }
    }

    [HarmonyPatch(typeof(PrioritizationPanel), "Update")]
    internal static class SyncFinalProductTogglePatch
    {
        private static void Prefix(PrioritizationPanel __instance)
        {
            FinalProductToggle.Install(__instance);
            FinalProductToggle.Sync(__instance);
        }
    }

    [HarmonyPatch(typeof(PrioritizationItem), nameof(PrioritizationItem.CustomUpdate))]
    internal static class HideIdleDistrictResourcePatch
    {
        private static readonly AccessTools.FieldRef<PrioritizationItem, Resource> ResourceOf =
            AccessTools.FieldRefAccess<PrioritizationItem, Resource>("resource");

        private static void Postfix(PrioritizationItem __instance, byte district)
        {
            ApplyFinalProductDemand(__instance, district);
            HideIfIdle(__instance, district);
        }

        private static void ApplyFinalProductDemand(PrioritizationItem item, byte district)
        {
            if (district == 0 || !item.gameObject.activeSelf)
            {
                return;
            }

            Resource resource = ResourceOf(item);
            if (!FinalProductDemand.TryGet(district, resource, out double demand))
            {
                return;
            }

            item.consumptionText.text = demand.ToString("#,##0.0");
            double production = Old.GetSimulator().market.GetProductionPerMinute(resource, district);
            if (production < 0.75d * demand)
            {
                item.productionText.color = Dashboard.redTextColor;
            }
            else if (production < 0.98d * demand)
            {
                item.productionText.color = Dashboard.yellowTextColor;
            }
            else
            {
                item.productionText.color = new Color(1f, 1f, 1f, 1f);
            }
        }

        private static void HideIfIdle(PrioritizationItem item, byte district)
        {
            if (district == 0 || !item.gameObject.activeSelf)
            {
                return;
            }

            if (IsDisplayedZero(item.employmentTotal.text)
                && IsDisplayedZero(item.productionText.text)
                && IsDisplayedZero(item.consumptionText.text))
            {
                item.gameObject.SetActive(false);
            }
        }

        internal static bool IsDisplayedZero(string text)
        {
            return double.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture,
                    out double value)
                && value == 0d;
        }
    }

    [HarmonyPatch(typeof(VisualizationManager), nameof(VisualizationManager.Update))]
    internal static class PreparePowerRangeColorsPatch
    {
        // 不跳过原版 Update。Prefix 只准备电网配色；Postfix 关掉原版那张不分电网的范围贴花。
        private static void Prefix(VisualizationManager __instance, bool force)
        {
            PowerGridColors.Prepare(__instance, force);
        }

        private static void Postfix()
        {
            PowerGridColors.HideUnifiedRange();
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.UpdateDecalsVisible))]
    internal static class ColorPowerRangePatch
    {
        private static void Postfix(Building __instance)
        {
            PowerGridColors.ShowRange(__instance);
        }
    }

    [HarmonyPatch(typeof(VisualizationManager), "GetVisualTypeToDisplay")]
    internal static class ProducerVisualTypePatch
    {
        // 不跳过原版。选中产物时，把面板上的普通可视化改成空白底，方便只突出生产建筑。
        private static void Postfix(ref VisualType __result)
        {
            ProducerList.OverrideVisualType(ref __result);
        }
    }

    [HarmonyPatch(typeof(VisualizationManager), "GetBuildingMat")]
    internal static class HighlightProducersPatch
    {
        private static void Postfix(Building building, VisualType visualType, ref Material __result)
        {
            ProducerList.ApplyHighlight(building, visualType, ref __result);
        }
    }
}
