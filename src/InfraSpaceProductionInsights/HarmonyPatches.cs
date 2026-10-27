using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
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

                if (item != null && district == 0)
                {
                    ApplyGlobalMaintenance(item);
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
                SetColorTip(item.consumptionText, "维护消耗", DescribeMaintenance(resource, district));
                if (production > 0d || consumption > 0d)
                {
                    item.gameObject.SetActive(true);
                }
            }
            else if (FinalProductToggle.IsArmed)
            {
                SetColorTip(item.consumptionText, "倒推需求", DescribeDemand(resource, consumption));
            }
            else
            {
                ClearColorTip(item.consumptionText);
            }

            if (!item.gameObject.activeSelf)
            {
                return;
            }

            if (DistrictBalance.IsRecipeFinal(resource))
            {
                item.productionText.color = FinalProductBlue;
                SetColorTip(item.productionText, "最终产物", DescribeFinal(resource, production, consumption));
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

        private static void ApplyGlobalMaintenance(PrioritizationItem item)
        {
            Resource resource = ResourceOf(item);
            if (!DistrictBalance.IsMaintenance(resource) || !item.gameObject.activeSelf)
            {
                ClearColorTip(item.consumptionText);
                return;
            }

            DistrictBalance.Ensure(0);
            if (!TryParse(item.consumptionText.text, out double consumption))
            {
                consumption = Old.GetSimulator().market.GetConsumptionPerSecond(resource, 0);
            }

            consumption += DistrictBalance.MaintenanceUse(resource);
            item.consumptionText.text = FormatRate(consumption);
            SetColorTip(item.consumptionText, "维护消耗", DescribeMaintenance(resource, 0));
            if (!TryParse(item.productionText.text, out double production))
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

        private static string DescribeMaintenance(Resource resource, byte district)
        {
            string scope = district == 0 ? "全图" : "当前地区";
            int count = DistrictBalance.MaintenanceBuildings(resource);
            double use = DistrictBalance.MaintenanceUse(resource);
            return "原版不把升级磨损记入消费。\n\n" +
                   "只统计" + scope + "里、落在有效配送覆盖内的建筑：\n" +
                   "配送中心正在配送这种物资，\n" +
                   "覆盖到这座建筑，中心能出车，建筑有入口。\n\n" +
                   "每座每分钟 0.048。\n" +
                   "本次 " + count.ToString() + " 座，合计 " + FormatRate(use) + "。";
        }

        private static string DescribeFinal(Resource resource, double production, double consumption)
        {
            return resource.GetNameInGame() + "\n" +
                   "这个地区没有别的工厂配方把它当作原料。\n" +
                   "同一条产线消耗自己产出的部分不算。\n" +
                   "所以它是最终产物，产量固定为蓝色。\n\n" +
                   "倒推从这些最终产物建筑的配方开始。\n" +
                   "下一级要多少，就乘上配方里的原料数量。\n" +
                   "中间建筑有几台、效率多少，都不改变这个比例。\n" +
                   "缺工人或缺电只让产量变低，需求仍按配方算。\n" +
                   "回收中心还没有，副产物不抵消上游需求。\n\n" +
                   "其他行的消费是这条链拆出来的需求。\n" +
                   "其他行的产量仍是这些建筑当前效率下的产量。\n" +
                   "有机废料只记需求，不再往下拆配方。\n\n" +
                   "产量：" + FormatRate(production) + "\n" +
                   "消费：" + FormatRate(consumption);
        }

        private static string DescribeDemand(Resource resource, double consumption)
        {
            return resource.GetNameInGame() + "\n" +
                   "这是从最终产物按配方倒推的需求，\n" +
                   "不是当前实际运进工厂的数量。\n\n" +
                   "起点是最终产物建筑的配方产量。\n" +
                   "再按每一级配方的原料比例往回乘。\n" +
                   "中间设备的效率不参与。\n\n" +
                   "本次需求：" + FormatRate(consumption);
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
