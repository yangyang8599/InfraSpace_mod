using System.Globalization;
using HarmonyLib;
using TMPro;
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

        private static void Postfix(PrioritizationPanel __instance)
        {
            ProductionSummary.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(PrioritizationItem), nameof(PrioritizationItem.CustomUpdate))]
    internal static class HideIdleDistrictResourcePatch
    {
        private static readonly Color FinalProductBlue = new Color(0.45f, 0.75f, 1f, 1f);

        private static readonly AccessTools.FieldRef<PrioritizationItem, Resource> ResourceOf =
            AccessTools.FieldRefAccess<PrioritizationItem, Resource>("resource");

        private static void Postfix(PrioritizationItem __instance, byte district)
        {
            ApplyFinalProductDemand(__instance, district);
            ApplyDistrictBalance(__instance, district);
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

        private static void ApplyDistrictBalance(PrioritizationItem item, byte district)
        {
            if (district == 0 || item == null)
            {
                if (item?.productionText != null)
                {
                    ClearColorTip(item.productionText);
                }

                return;
            }

            Resource resource = ResourceOf(item);
            if (resource == null)
            {
                ClearColorTip(item.productionText);
                return;
            }

            DistrictBalance.Ensure(district);
            bool maintenance = DistrictBalance.IsMaintenance(resource);
            if (!item.gameObject.activeSelf && !maintenance)
            {
                return;
            }

            double production;
            if (maintenance || !TryParse(item.productionText.text, out production))
            {
                production = Old.GetSimulator().market.GetProductionPerMinute(resource, district);
                double scanned = DistrictBalance.ScannedProduction(resource);
                if (scanned > production)
                {
                    production = scanned;
                }
            }

            if (!TryParse(item.consumptionText.text, out double consumption))
            {
                consumption = Old.GetSimulator().market.GetConsumptionPerSecond(resource, district);
            }

            if (maintenance)
            {
                consumption += DistrictBalance.MaintenanceUse(resource);
                item.productionText.text = FormatRate(production);
                item.consumptionText.text = FormatRate(consumption);
                if (production > 0d || consumption > 0d)
                {
                    item.gameObject.SetActive(true);
                }
            }

            if (!item.gameObject.activeSelf)
            {
                return;
            }

            if (production > consumption &&
                consumption > 1e-4d &&
                DistrictBalance.TryLargest(resource, out double oneBuilding) &&
                production - consumption > oneBuilding)
            {
                item.productionText.color = Dashboard.greenTextColor;
                SetColorTip(item.productionText, "产量富余", DescribeSurplus(resource, production, consumption, oneBuilding));
                return;
            }

            if (consumption <= 1e-4d)
            {
                item.productionText.color = FinalProductBlue;
                SetColorTip(item.productionText, "最终产物", DescribeFinal(resource, production));
                return;
            }

            ClearColorTip(item.productionText);

            if (!maintenance)
            {
                return;
            }

            if (production < 0.75d * consumption)
            {
                item.productionText.color = Dashboard.redTextColor;
            }
            else if (production < 0.98d * consumption)
            {
                item.productionText.color = Dashboard.yellowTextColor;
            }
            else
            {
                item.productionText.color = new Color(1f, 1f, 1f, 1f);
            }
        }

        private static string DescribeFinal(Resource resource, double production)
        {
            return resource.GetNameInGame() + "\n" +
                   "这个地区的消费是 0，\n" +
                   "所以把它当作最终产物。\n" +
                   "产量固定为蓝色，\n" +
                   "不会因为富余变成绿色。\n\n" +
                   "产量：" + FormatRate(production) + "\n" +
                   "消费：0";
        }

        private static string DescribeSurplus(Resource resource, double production, double consumption, double oneBuilding)
        {
            return resource.GetNameInGame() + "\n" +
                   "产量比消费多出的部分，\n" +
                   "已经超过这个地区\n" +
                   "产量最高的一座建筑。\n" +
                   "所以产量显示为绿色。\n\n" +
                   "产量：" + FormatRate(production) + "\n" +
                   "消费：" + FormatRate(consumption) + "\n" +
                   "多出：" + FormatRate(production - consumption) + "\n" +
                   "单座最高产量：" + FormatRate(oneBuilding);
        }

        private static void SetColorTip(TMP_Text text, string title, string body)
        {
            if (text == null)
            {
                return;
            }

            text.raycastTarget = true;
            TooltipHoverable tip = text.GetComponent<TooltipHoverable>();
            if (tip == null)
            {
                tip = text.gameObject.AddComponent<TooltipHoverable>();
            }

            tip.enabled = true;
            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init(title, body);
            RefreshOpenTip(tip);
        }

        private static void ClearColorTip(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TooltipHoverable tip = text.GetComponent<TooltipHoverable>();
            if (tip == null)
            {
                return;
            }

            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init("", "");
            tip.enabled = false;
        }

        private static readonly AccessTools.FieldRef<Tooltip, TooltipHoverable> OpenTip =
            AccessTools.FieldRefAccess<Tooltip, TooltipHoverable>("hoverable");

        private static void RefreshOpenTip(TooltipHoverable tip)
        {
            Tooltip tooltip = WorldScripts.Inst?.tooltipManager?.tooltipPanel;
            if (tooltip == null || tip == null || OpenTip(tooltip) != tip)
            {
                return;
            }

            OpenTip(tooltip) = null;
            tooltip.ShowTooltip(tip);
        }

        private static string FormatRate(double value)
        {
            if (value <= 0d)
            {
                return "0.0";
            }

            if (value >= 10d)
            {
                return value.ToString("#,##0.0");
            }

            if (value >= 0.1d)
            {
                return value.ToString("0.00");
            }

            return value.ToString("0.000");
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out value);
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
            ProducerFocus.RestoreProducers();
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
            ProducerFocus.OverrideVisualType(ref __result);
        }
    }

    [HarmonyPatch(typeof(VisualizationManager), "GetBuildingMat")]
    internal static class HighlightProducersPatch
    {
        private static void Postfix(Building building, VisualType visualType, ref Material __result)
        {
            ProducerList.ApplyHighlight(building, visualType, ref __result);
            ProducerFocus.NotePainted();
        }
    }
}
