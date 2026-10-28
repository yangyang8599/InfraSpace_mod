using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class LiveConsumption
    {
        private const int SlotCount = 300;

        private const float SampleInterval = 0.2f;

        private const float WindowSeconds = 60f;

        private const string PlanningTitle = "规划模式";

        private const string PlanningBody =
            "规划模式按已建配方统计，不能切换这一拍或最近一分钟。\n" +
            "关掉规划模式后，再点这个图标。";

        private static readonly Color InstantTint = new Color(0.55f, 0.95f, 0.75f, 1f);

        private static readonly Color MinuteTint = new Color(0.95f, 0.82f, 0.45f, 1f);

        private enum ColumnMode
        {
            Vanilla,
            Instant,
            Minute
        }

        private static readonly Dictionary<string, double> ConsumptionRates = new Dictionary<string, double>();

        private static readonly Dictionary<string, double> ProductionRates = new Dictionary<string, double>();

        private static readonly Dictionary<byte, DistrictHistory> Histories = new Dictionary<byte, DistrictHistory>();

        private static readonly AccessTools.FieldRef<PrioritizationPanel, byte> DistrictOf =
            AccessTools.FieldRefAccess<PrioritizationPanel, byte>("selectedDistrict");

        private static int frame = -1;

        private static byte cachedDistrict;

        private static ColumnMode consumptionMode;

        private static ColumnMode productionMode;

        private static RectTransform consumptionIcon;

        private static RectTransform productionIcon;

        private static Color consumptionColor = Color.white;

        private static Color productionColor = Color.white;

        private static bool consumptionColorReady;

        private static bool productionColorReady;

        private static bool productionSearched;

        internal static bool ConsumptionEnabled
        {
            get { return consumptionMode != ColumnMode.Vanilla; }
        }

        internal static bool ProductionEnabled
        {
            get { return productionMode != ColumnMode.Vanilla; }
        }

        internal static string ConsumptionCellNote()
        {
            if (consumptionMode == ColumnMode.Minute)
            {
                return "最近连续 1 分钟：把这一拍的消耗速率取平均，停下的时段按 0 计入，再加上当前居民消耗。";
            }

            return "这一拍约 0.2 秒。只统计这一次采样里正在运转的工厂，再加上居民消耗。缺料或出货堵住的不计。";
        }

        internal static string ProductionCellNote()
        {
            if (productionMode == ColumnMode.Minute)
            {
                return "最近连续 1 分钟：把这一拍的产量速率取平均，停下的时段按 0 计入。";
            }

            return "这一拍约 0.2 秒。只统计这一次采样里正在运转的工厂。缺料或出货堵住的不计。";
        }

        internal static void Install(PrioritizationPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            Hook(panel.consumptionTitleIcon, false, ref consumptionIcon, ref consumptionColor, ref consumptionColorReady);
            if (!productionSearched || productionIcon == null)
            {
                productionSearched = productionIcon != null;
                Hook(FindProductionIcon(panel), true, ref productionIcon, ref productionColor, ref productionColorReady);
                productionSearched = productionIcon != null;
            }

            byte district = panel.gameObject.activeInHierarchy ? DistrictOf(panel) : (byte)0;
            Tick(panel, district);
            RefreshIcon(consumptionIcon, consumptionColor, consumptionColorReady, false, district);
            RefreshIcon(productionIcon, productionColor, productionColorReady, true, district);
        }

        internal static void CycleConsumption()
        {
            consumptionMode = Next(consumptionMode);
        }

        internal static void CycleProduction()
        {
            productionMode = Next(productionMode);
        }

        internal static bool TryGet(byte district, Resource resource, out double perMinute)
        {
            perMinute = 0d;
            if (!ConsumptionEnabled || resource == null || string.IsNullOrEmpty(resource.name) || resource.name == "power")
            {
                return false;
            }

            perMinute = Rate(district, resource.name, false, consumptionMode == ColumnMode.Minute);
            perMinute += ProductionSnapshots.HabitatUse(district, resource.name);
            return true;
        }

        internal static bool TryGetProduction(byte district, Resource resource, out double perMinute)
        {
            perMinute = 0d;
            if (!ProductionEnabled || resource == null || string.IsNullOrEmpty(resource.name) || resource.name == "power")
            {
                return false;
            }

            perMinute = Rate(district, resource.name, true, productionMode == ColumnMode.Minute);
            return true;
        }

        internal static void ShowPlanningNotice()
        {
            TooltipManager manager = WorldScripts.Inst?.tooltipManager;
            Tooltip tooltip = manager?.tooltipPanel;
            if (tooltip == null)
            {
                return;
            }

            tooltip.ShowTooltip(PlanningTitle, PlanningBody, true);
            manager.oneFrameTooltipLastUpdate = Time.frameCount;
        }

        private static void Hook(
            RectTransform icon,
            bool production,
            ref RectTransform hooked,
            ref Color color,
            ref bool colorReady)
        {
            if (icon == null || icon == hooked)
            {
                return;
            }

            hooked = icon;
            Image image = icon.GetComponent<Image>();
            if (image == null)
            {
                image = icon.GetComponentInChildren<Image>(true);
            }

            GameObject target = image != null ? image.gameObject : icon.gameObject;
            if (image != null)
            {
                image.raycastTarget = true;
                if (!colorReady)
                {
                    color = image.color;
                    colorReady = true;
                }
            }

            LiveRateButton button = target.GetComponent<LiveRateButton>();
            if (button == null)
            {
                button = target.AddComponent<LiveRateButton>();
            }

            button.Production = production;
        }

        private static void RefreshIcon(RectTransform icon, Color color, bool colorReady, bool production, byte district)
        {
            if (icon == null)
            {
                return;
            }

            Image image = icon.GetComponent<Image>();
            if (image == null)
            {
                image = icon.GetComponentInChildren<Image>(true);
            }

            ColumnMode mode = production ? productionMode : consumptionMode;
            if (image != null && colorReady)
            {
                if (FinalProductToggle.IsArmed || mode == ColumnMode.Vanilla)
                {
                    image.color = color;
                }
                else
                {
                    image.color = mode == ColumnMode.Minute ? MinuteTint : InstantTint;
                }
            }

            GameObject target = image != null ? image.gameObject : icon.gameObject;
            TooltipHoverable tip = target.GetComponent<TooltipHoverable>();
            if (tip == null)
            {
                tip = target.AddComponent<TooltipHoverable>();
            }

            string title;
            string body;
            if (FinalProductToggle.IsArmed)
            {
                title = PlanningTitle;
                body = PlanningBody;
            }
            else
            {
                title = Title(production, mode);
                body = Body(production, mode, district);
            }

            tip.titleCode = "";
            tip.descriptionCode = "";
            if (tip.GetTitle() != title || tip.GetDescription() != body)
            {
                tip.Init(title, body);
            }
        }

        private static RectTransform FindProductionIcon(PrioritizationPanel panel)
        {
            RectTransform consumption = panel.consumptionTitleIcon;
            if (consumption == null || !panel.gameObject.activeInHierarchy || consumption.position.sqrMagnitude < 1f)
            {
                return null;
            }

            float targetX = consumption.position.x;
            if (panel.prioItemParent != null)
            {
                PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(true);
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i]?.productionText == null)
                    {
                        continue;
                    }

                    targetX = items[i].productionText.rectTransform.position.x;
                    break;
                }
            }

            float yTolerance = Mathf.Max(36f, consumption.rect.height * Mathf.Abs(consumption.lossyScale.y) * 1.5f);
            float xTolerance = Mathf.Max(120f, consumption.rect.width * Mathf.Abs(consumption.lossyScale.x) * 6f);
            RectTransform best = null;
            float bestDistance = xTolerance;
            Image[] images = panel.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                RectTransform rect = image != null ? image.rectTransform : null;
                if (rect == null || rect == consumption || IsUnder(rect, consumption))
                {
                    continue;
                }

                if (panel.scaleBarTitleIcon != null &&
                    (rect == panel.scaleBarTitleIcon || IsUnder(rect, panel.scaleBarTitleIcon)))
                {
                    continue;
                }

                if (panel.districtToggle != null && IsUnder(rect, panel.districtToggle.transform))
                {
                    continue;
                }

                if (rect.GetComponentInParent<PrioritizationItem>() != null)
                {
                    continue;
                }

                if (Mathf.Abs(rect.position.y - consumption.position.y) > yTolerance)
                {
                    continue;
                }

                float width = rect.rect.width * Mathf.Abs(rect.lossyScale.x);
                float height = rect.rect.height * Mathf.Abs(rect.lossyScale.y);
                if (width < 4f || height < 4f || width > 96f || height > 96f)
                {
                    continue;
                }

                float distance = Mathf.Abs(rect.position.x - targetX);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = rect;
                }
            }

            return best;
        }

        private static bool IsUnder(Transform child, Transform parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }

            Transform cursor = child;
            while (cursor != null)
            {
                if (cursor == parent)
                {
                    return true;
                }

                cursor = cursor.parent;
            }

            return false;
        }

        private static void Ensure(byte district)
        {
            if (frame == Time.frameCount && cachedDistrict == district)
            {
                return;
            }

            frame = Time.frameCount;
            cachedDistrict = district;
            ConsumptionRates.Clear();
            ProductionRates.Clear();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return;
            }

            foreach (Building building in buildings)
            {
                if (!ProducerCounts.InDistrict(building, district) || !building.IsValidAndBuilt() || !building.DoesWork())
                {
                    continue;
                }

                if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory) ||
                    !factory.couldWork ||
                    factory.productionDefinition == null ||
                    factory.productionDefinition.timeSteps <= 0)
                {
                    continue;
                }

                int speed = Mathf.RoundToInt(100f * factory.GetEfficiency()) * 10;
                if (speed <= 0)
                {
                    continue;
                }

                int timeSteps = factory.productionDefinition.timeSteps;
                AddCosts(ConsumptionRates, factory.productionDefinition.consumables, timeSteps, speed);
                AddCosts(ProductionRates, factory.productionDefinition.producables, timeSteps, speed);
                if (factory.HasTerraformingLogicOverride())
                {
                    AddRate(ProductionRates, Resource.Get("terraform"), (3000d * speed) / timeSteps / 10d);
                }
            }
        }

        private static void AddCosts(Dictionary<string, double> rates, List<ResourceCost> costs, int timeSteps, int speed)
        {
            if (costs == null)
            {
                return;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                int amount = costs[i].amount;
                if (amount <= 0)
                {
                    continue;
                }

                AddRate(rates, costs[i].resource, (amount * 50d * 60d * speed) / timeSteps / 10d);
            }
        }

        private static void AddRate(Dictionary<string, double> rates, Resource resource, double rate)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name) || rate <= 0d)
            {
                return;
            }

            rates.TryGetValue(resource.name, out double current);
            rates[resource.name] = current + rate;
        }

        private static ColumnMode Next(ColumnMode mode)
        {
            if (mode == ColumnMode.Vanilla)
            {
                return ColumnMode.Instant;
            }

            if (mode == ColumnMode.Instant)
            {
                return ColumnMode.Minute;
            }

            return ColumnMode.Vanilla;
        }

        private static void Tick(PrioritizationPanel panel, byte district)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                return;
            }

            DistrictHistory history = HistoryOf(district);
            float now = Time.unscaledTime;
            bool due = now - history.LastSample >= SampleInterval;
            bool instant = !FinalProductToggle.IsArmed &&
                (consumptionMode == ColumnMode.Instant || productionMode == ColumnMode.Instant);
            if (!due && !instant)
            {
                return;
            }

            Ensure(district);
            if (due)
            {
                Push(history, now);
            }
        }

        private static double Rate(byte district, string resourceName, bool production, bool minute)
        {
            if (!minute)
            {
                Ensure(district);
                Dictionary<string, double> latest = production ? ProductionRates : ConsumptionRates;
                latest.TryGetValue(resourceName, out double current);
                return current;
            }

            return Average(HistoryOf(district), resourceName, production, Time.unscaledTime);
        }

        private static void Push(DistrictHistory history, float now)
        {
            int slot = history.Next;
            history.Consumption[slot] = Copy(ConsumptionRates, history.Consumption[slot]);
            history.Production[slot] = Copy(ProductionRates, history.Production[slot]);
            history.SampledAt[slot] = now;
            history.Next = (slot + 1) % SlotCount;
            if (history.Filled < SlotCount)
            {
                history.Filled++;
            }

            history.LastSample = now;
        }

        private static Dictionary<string, double> Copy(Dictionary<string, double> source, Dictionary<string, double> target)
        {
            if (target == null)
            {
                target = new Dictionary<string, double>();
            }
            else
            {
                target.Clear();
            }

            foreach (KeyValuePair<string, double> pair in source)
            {
                target[pair.Key] = pair.Value;
            }

            return target;
        }

        private static double Average(DistrictHistory history, string resourceName, bool production, float now)
        {
            int count = 0;
            double sum = 0d;
            int filled = history.Filled;
            for (int i = 0; i < filled; i++)
            {
                if (now - history.SampledAt[i] > WindowSeconds)
                {
                    continue;
                }

                count++;
                Dictionary<string, double> sample = production ? history.Production[i] : history.Consumption[i];
                if (sample != null && sample.TryGetValue(resourceName, out double value))
                {
                    sum += value;
                }
            }

            if (count == 0)
            {
                return 0d;
            }

            return sum / count;
        }

        private static float Coverage(byte district)
        {
            DistrictHistory history = HistoryOf(district);
            float now = Time.unscaledTime;
            float oldest = now;
            int count = 0;
            for (int i = 0; i < history.Filled; i++)
            {
                float sampledAt = history.SampledAt[i];
                if (now - sampledAt > WindowSeconds)
                {
                    continue;
                }

                count++;
                if (sampledAt < oldest)
                {
                    oldest = sampledAt;
                }
            }

            if (count == 0)
            {
                return 0f;
            }

            return Mathf.Min(WindowSeconds, now - oldest + SampleInterval);
        }

        private static string Title(bool production, ColumnMode mode)
        {
            if (mode == ColumnMode.Minute)
            {
                return production ? "连续 1 分钟产量" : "连续 1 分钟消耗";
            }

            if (mode == ColumnMode.Instant)
            {
                return production ? "这一拍产量" : "这一拍消耗";
            }

            return production ? "原版产量" : "原版消耗";
        }

        private static string Body(bool production, ColumnMode mode, byte district)
        {
            string column = production ? "产量" : "消耗";
            string residents = production ? "" : "再加上居民消耗。\n";
            if (mode == ColumnMode.Vanilla)
            {
                return "原版" + column + "：按当前效率估算。\n" +
                    "缺料或出货堵住的工厂也会计入。\n\n" +
                    "点击改为这一拍。只在全局统计和地区统计中使用。";
            }

            if (mode == ColumnMode.Instant)
            {
                return "这一拍约 0.2 秒。\n" +
                    "模拟每秒 50 帧，每个工厂每 10 帧才更新一次是否在运转。\n" +
                    "只统计这一次采样里正在运转的工厂。\n" +
                    residents +
                    "缺料或出货堵住的不计。电力仍是原版。\n\n" +
                    "点击改为最近连续 1 分钟。";
            }

            int seconds = Mathf.Clamp(Mathf.RoundToInt(Coverage(district)), 0, 60);
            string covered = seconds >= 60
                ? "已经记满最近 60 秒。\n"
                : "面板打开后已累计 " + seconds.ToString() + " 秒，记满后是完整的一分钟。\n";
            return "最近连续 1 分钟：把这一拍的速率取平均。\n" +
                "停下的时段按 0 计入，所以不会因为某一拍停了就消失。\n" +
                residents +
                "电力仍是原版。\n" +
                covered +
                "\n点击改回原版。";
        }

        private static DistrictHistory HistoryOf(byte district)
        {
            if (!Histories.TryGetValue(district, out DistrictHistory history))
            {
                history = new DistrictHistory();
                Histories[district] = history;
            }

            return history;
        }

        private sealed class DistrictHistory
        {
            public readonly Dictionary<string, double>[] Consumption = new Dictionary<string, double>[SlotCount];

            public readonly Dictionary<string, double>[] Production = new Dictionary<string, double>[SlotCount];

            public readonly float[] SampledAt = new float[SlotCount];

            public int Next;

            public int Filled;

            public float LastSample = -1000f;
        }

        private sealed class LiveRateButton : MonoBehaviour, IPointerClickHandler
        {
            private bool noticePending;

            internal bool Production { get; set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                if (FinalProductToggle.IsArmed)
                {
                    noticePending = true;
                    return;
                }

                if (Production)
                {
                    CycleProduction();
                }
                else
                {
                    CycleConsumption();
                }
            }

            private void LateUpdate()
            {
                if (!noticePending)
                {
                    return;
                }

                noticePending = false;
                ShowPlanningNotice();
            }
        }
    }
}
