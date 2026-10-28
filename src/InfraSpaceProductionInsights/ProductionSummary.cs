using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class ProductionSummary
    {
        private static readonly AccessTools.FieldRef<PrioritizationItem, Resource> ResourceOf =
            AccessTools.FieldRefAccess<PrioritizationItem, Resource>("resource");

        private static readonly AccessTools.FieldRef<PrioritizationPanel, byte> DistrictOf =
            AccessTools.FieldRefAccess<PrioritizationPanel, byte>("selectedDistrict");

        private const string WorkerTip =
            "左边是这种物资的工人总数，右边是平均每座生产建筑的工人。例如 10/5 表示一共 10 人，每座建筑 5 人。";

        private const string EfficiencyTip =
            "地区统计和规划模式统计都会出现。全局统计没有这一行。\n\n" +
            "工人：每生产 1 个最终产物净产出占用的工人\n" +
            "耗电：每生产 1 个最终产物净产出消耗的电\n" +
            "原料：没有生产建筑的物品，数量不含居民消费\n\n" +
            "净产出 = 产量 − 不含居民的消费。磨损要扣。\n" +
            "居民吃掉的最终产物不减少净产出。\n" +
            "净产出小于或等于 0 的不进分母。\n" +
            "工业机器人、高科技工具、有机废料没有生产建筑时算原料。\n\n" +
            "鼠标放在右边三个数字上，可查看分子、分母和算式。";

        private const int TipLineLimit = 10;

        private static readonly HashSet<string> VisibleNames = new HashSet<string>();

        private static readonly Dictionary<string, double> Produced = new Dictionary<string, double>();

        private static readonly Dictionary<string, double> Operational = new Dictionary<string, double>();

        private static readonly Dictionary<string, double> WorkersByName = new Dictionary<string, double>();

        private static readonly Dictionary<string, double> InputRates = new Dictionary<string, double>();

        private static readonly List<string> NetNotes = new List<string>();

        private static readonly List<Contributor> ChainLines = new List<Contributor>();

        private static readonly List<Contributor> WorkerLines = new List<Contributor>();

        private static readonly List<Contributor> FinalLines = new List<Contributor>();

        private static readonly List<Contributor> Ranked = new List<Contributor>();

        private static readonly List<Contributor> NameRank = new List<Contributor>();

        private static readonly StringBuilder TipText = new StringBuilder(768);

        private static readonly AccessTools.FieldRef<Tooltip, TooltipHoverable> OpenTip =
            AccessTools.FieldRefAccess<Tooltip, TooltipHoverable>("hoverable");

        private static PrioritizationItem summary;
        private static PrioritizationItem efficiency;
        private static TooltipHoverable workerFigureTip;
        private static TooltipHoverable powerFigureTip;
        private static TooltipHoverable buildingFigureTip;
        private static bool unavailable;
        private static bool workerTipReady;
        private static readonly Dictionary<int, int> OriginalSibling = new Dictionary<int, int>();

        private static readonly List<PrioritizationItem> Ordered = new List<PrioritizationItem>();

        private struct Contributor
        {
            public string Key;
            public string Label;
            public int Count;
            public double Amount;
        }

        internal static void Refresh(PrioritizationPanel panel)
        {
            if (unavailable || panel == null || panel.prioItemParent == null)
            {
                return;
            }

            Ensure(panel);
            if (summary == null)
            {
                return;
            }

            EnsureWorkerTip(panel);
            VisibleNames.Clear();
            Produced.Clear();
            Operational.Clear();
            WorkersByName.Clear();
            double workers = 0d;
            double itemWorkers = 0d;
            double production = 0d;
            double consumption = 0d;
            int buildings = 0;
            byte district = DistrictOf(panel);
            // 只加当前显示出来的行。地区视图里被藏起的全零行，以及合计行、效率行，都不计入。
            PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(false);
            for (int i = 0; i < items.Length; i++)
            {
                PrioritizationItem item = items[i];
                if (item == null || item == summary || item == efficiency || !item.gameObject.activeSelf)
                {
                    continue;
                }

                Resource resource = ResourceOf(item);
                int producedBy = ProducerCounts.Count(resource, district);
                buildings += producedBy;
                SetCount(item, producedBy);

                double workerValue = 0d;
                if (TryParse(item.employmentTotal.text, out workerValue))
                {
                    workers += workerValue;
                }

                item.employmentTotal.text = WorkersText(workerValue, producedBy);

                double produced = 0d;
                if (TryParse(item.productionText.text, out produced))
                {
                    production += produced;
                }

                if (TryParse(item.consumptionText.text, out double consumed))
                {
                    consumption += consumed;
                }

                if (resource == null || IsPower(resource) || string.IsNullOrEmpty(resource.name))
                {
                    continue;
                }

                VisibleNames.Add(resource.name);
                Produced[resource.name] = produced;
                double habitat = 0d;
                if (district != 0 && !FinalProductToggle.IsArmed)
                {
                    habitat = ProductionSnapshots.HabitatUse(district, resource.name);
                }

                double operational = consumed - habitat;
                if (operational < 0d)
                {
                    operational = 0d;
                }

                Operational[resource.name] = operational;
                WorkersByName[resource.name] = workerValue;
                itemWorkers += workerValue;
            }

            SetCount(summary, buildings);

            summary.employmentTotal.text = WorkersText(workers, buildings);
            summary.productionText.text = production.ToString("#,##0.0");
            summary.consumptionText.text = consumption.ToString("#,##0.0");
            summary.productionText.color = ColorFor(production, consumption);
            SortRows(panel, district);
            summary.transform.SetAsLastSibling();
            RefreshEfficiency(district, itemWorkers);
        }

        private static void Ensure(PrioritizationPanel panel)
        {
            if (summary != null || panel.prioItemTemplate == null)
            {
                return;
            }

            GameObject clone = Object.Instantiate(panel.prioItemTemplate.gameObject, panel.prioItemParent);
            clone.name = "ProductionSummary";
            if (clone.activeSelf)
            {
                clone.SetActive(false);
            }

            summary = clone.GetComponent<PrioritizationItem>();
            if (summary == null
                || summary.employmentPercentage == null
                || summary.employmentTotal == null
                || summary.productionText == null
                || summary.consumptionText == null)
            {
                Object.Destroy(clone);
                summary = null;
                unavailable = true;
                Debug.LogWarning("[InfraSpaceProductionInsights] 生产概况合计行缺少文本字段");
                return;
            }

            // 不进入 prioItems，也不启用原版组件，避免合计行被当成某种物资去改优先级。
            summary.enabled = false;
            if (summary.tooltip != null)
            {
                summary.tooltip.enabled = false;
            }

            if (summary.icon != null)
            {
                summary.icon.enabled = false;
            }

            if (summary.slider != null)
            {
                summary.slider.interactable = false;
                CanvasGroup group = summary.slider.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = summary.slider.gameObject.AddComponent<CanvasGroup>();
                }

                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            Style(summary.employmentPercentage, "合计");
            Style(summary.employmentTotal, "0");
            Style(summary.productionText, "0.0");
            Style(summary.consumptionText, "0.0");
            summary.employmentPercentage.color = Color.white;
            summary.employmentTotal.color = Color.white;
            summary.consumptionText.color = Color.white;
            clone.SetActive(true);
            clone.transform.SetAsLastSibling();

            efficiency = CreateRow(panel, "ProductionEfficiency");
            if (efficiency == null)
            {
                return;
            }

            Style(efficiency.employmentPercentage, "效率");
            Style(efficiency.employmentTotal, "—");
            Style(efficiency.productionText, "—");
            Style(efficiency.consumptionText, "—");
            efficiency.employmentPercentage.color = Color.white;
            efficiency.employmentTotal.color = Color.white;
            efficiency.productionText.color = Color.white;
            efficiency.consumptionText.color = Color.white;
            efficiency.employmentPercentage.raycastTarget = true;
            TooltipHoverable efficiencyTip = efficiency.employmentPercentage.gameObject.AddComponent<TooltipHoverable>();
            efficiencyTip.Init("效率", EfficiencyTip);
            workerFigureTip = AttachTip(efficiency.employmentTotal);
            powerFigureTip = AttachTip(efficiency.productionText);
            buildingFigureTip = AttachTip(efficiency.consumptionText);
            efficiency.gameObject.SetActive(false);
        }

        private static PrioritizationItem CreateRow(PrioritizationPanel panel, string name)
        {
            GameObject clone = Object.Instantiate(panel.prioItemTemplate.gameObject, panel.prioItemParent);
            clone.name = name;
            if (clone.activeSelf)
            {
                clone.SetActive(false);
            }

            PrioritizationItem row = clone.GetComponent<PrioritizationItem>();
            if (row == null
                || row.employmentPercentage == null
                || row.employmentTotal == null
                || row.productionText == null
                || row.consumptionText == null)
            {
                Object.Destroy(clone);
                Debug.LogWarning("[InfraSpaceProductionInsights] 生产概况附加行缺少文本字段");
                return null;
            }

            row.enabled = false;
            if (row.tooltip != null)
            {
                row.tooltip.enabled = false;
            }

            if (row.icon != null)
            {
                row.icon.enabled = false;
            }

            if (row.slider != null)
            {
                row.slider.interactable = false;
                CanvasGroup group = row.slider.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = row.slider.gameObject.AddComponent<CanvasGroup>();
                }

                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            return row;
        }

        private static void RefreshEfficiency(byte district, double itemWorkers)
        {
            if (efficiency == null)
            {
                return;
            }

            if (district == 0)
            {
                if (efficiency.gameObject.activeSelf)
                {
                    efficiency.gameObject.SetActive(false);
                }

                return;
            }

            if (!efficiency.gameObject.activeSelf)
            {
                efficiency.gameObject.SetActive(true);
            }

            ClearChain();
            double chainPower = EfficiencyPower(district);
            FillRawRates(district);
            double output = CollectDenominator();
            efficiency.employmentTotal.text = PerItem(itemWorkers, output);
            efficiency.productionText.text = PerItem(chainPower, output);
            efficiency.consumptionText.text = InputCell(output);
            efficiency.productionText.color = Color.white;
            efficiency.transform.SetAsLastSibling();
            FillWorkerLines();
            SetFigureTips(
                itemWorkers,
                chainPower,
                output,
                efficiency.employmentTotal.text,
                efficiency.productionText.text,
                efficiency.consumptionText.text);
        }

        private static double EfficiencyPower(byte district)
        {
            if (FinalProductToggle.IsArmed)
            {
                return FinalProductDemand.PlannedPower(district);
            }

            Resource power = Resource.Get("power");
            return Old.GetSimulator()?.market?.GetConsumptionPerSecond(power, district) ?? 0d;
        }

        private static void FillRawRates(byte district)
        {
            InputRates.Clear();
            DistrictBalance.Ensure(district);
            foreach (KeyValuePair<string, double> pair in Operational)
            {
                if (pair.Value <= 1e-6d || pair.Key == "power")
                {
                    continue;
                }

                Resource resource = Resource.Get(pair.Key);
                if (DistrictBalance.HasProducer(resource))
                {
                    continue;
                }

                InputRates[pair.Key] = pair.Value;
            }
        }

        private static string InputCell(double output)
        {
            if (output <= 1e-6d || InputRates.Count == 0)
            {
                return "—";
            }

            if (InputRates.Count == 1)
            {
                foreach (double perMinute in InputRates.Values)
                {
                    return PerItem(perMinute, output);
                }
            }

            return InputRates.Count.ToString() + "种";
        }

        private static double CollectDenominator()
        {
            FinalLines.Clear();
            NetNotes.Clear();
            double finals = 0d;
            foreach (KeyValuePair<string, double> pair in Produced)
            {
                Resource resource = Resource.Get(pair.Key);
                if (!DistrictBalance.IsStatFinal(resource))
                {
                    continue;
                }

                Operational.TryGetValue(pair.Key, out double used);
                double net = pair.Value - used;
                if (pair.Value <= 1e-6d && used <= 1e-6d)
                {
                    continue;
                }

                string label = ResourceLabel(pair.Key);
                if (used > 1e-6d || net <= 1e-6d)
                {
                    string excluded = net <= 1e-6d ? "，不进入分母" : "";
                    NetNotes.Add(
                        label + " 产量 " + FormatAmount(pair.Value) +
                        "，不含居民的消耗 " + FormatAmount(used) +
                        "，净产出 " + FormatAmount(net) + excluded);
                }

                if (net <= 1e-6d)
                {
                    continue;
                }

                FinalLines.Add(new Contributor
                {
                    Key = pair.Key,
                    Label = label,
                    Count = 1,
                    Amount = net
                });
                finals += net;
            }

            return finals;
        }

        private static void EnsureWorkerTip(PrioritizationPanel panel)
        {
            if (workerTipReady || panel?.prioItemTemplate?.employmentTotal == null)
            {
                return;
            }

            RectTransform column = panel.prioItemTemplate.employmentTotal.rectTransform;
            Vector3 columnCenter = column.TransformPoint(column.rect.center);
            Transform list = panel.prioItemParent;
            TooltipHoverable match = null;
            float best = 40f;
            TooltipHoverable[] tips = panel.GetComponentsInChildren<TooltipHoverable>(true);
            for (int i = 0; i < tips.Length; i++)
            {
                TooltipHoverable tip = tips[i];
                if (tip == null || list == null || tip.transform.IsChildOf(list))
                {
                    continue;
                }

                RectTransform rect = tip.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                Vector3 center = rect.TransformPoint(rect.rect.center);
                float dx = Mathf.Abs(center.x - columnCenter.x);
                if (center.y <= columnCenter.y || dx >= best)
                {
                    continue;
                }

                best = dx;
                match = tip;
            }

            if (match == null)
            {
                return;
            }

            string title = match.GetTitle();
            if (string.IsNullOrEmpty(title))
            {
                title = "工人";
            }

            match.Init(title, WorkerTip);
            workerTipReady = true;
        }

        private static string WorkersText(double workers, int buildings)
        {
            string total = workers.ToString("#,##0");
            if (buildings <= 0 || workers <= 0d)
            {
                return total;
            }

            double each = System.Math.Round(workers / buildings, 1);
            string per = System.Math.Abs(each - System.Math.Round(each)) < 0.001d
                ? System.Math.Round(each).ToString("0")
                : each.ToString("0.0");
            return total + "/" + per;
        }

        private static string PerItem(double amount, double output)
        {
            if (output <= 1e-6d)
            {
                return "—";
            }

            double value = amount / output;
            if (value >= 100d)
            {
                return value.ToString("#,##0");
            }

            if (value >= 10d)
            {
                return value.ToString("0.0");
            }

            if (value >= 0.1d)
            {
                return value.ToString("0.00");
            }

            return value.ToString("0.###");
        }

        private static bool IsPower(Resource resource)
        {
            Resource power = Resource.Get("power");
            return power != null && (resource == power || resource.name == power.name);
        }

        private static void SortRows(PrioritizationPanel panel, byte district)
        {
            if (district != 0)
            {
                DistrictBalance.Ensure(district);
            }

            Ordered.Clear();
            PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(true);
            for (int i = 0; i < items.Length; i++)
            {
                PrioritizationItem item = items[i];
                if (item == null || item == summary || item == efficiency || item == panel.prioItemTemplate)
                {
                    continue;
                }

                int id = item.GetInstanceID();
                if (!OriginalSibling.ContainsKey(id))
                {
                    OriginalSibling[id] = item.transform.GetSiblingIndex();
                }

                Ordered.Add(item);
            }

            if (district == 0)
            {
                Ordered.Sort(CompareOriginal);
            }
            else
            {
                Ordered.Sort(CompareDistrict);
            }
            for (int i = 0; i < Ordered.Count; i++)
            {
                Ordered[i].transform.SetSiblingIndex(i);
            }
        }

        private static int CompareOriginal(PrioritizationItem left, PrioritizationItem right)
        {
            return OriginalSibling[left.GetInstanceID()].CompareTo(OriginalSibling[right.GetInstanceID()]);
        }

        private static int CompareDistrict(PrioritizationItem left, PrioritizationItem right)
        {
            int rank = Rank(left).CompareTo(Rank(right));
            if (rank != 0)
            {
                return rank;
            }

            return CompareOriginal(left, right);
        }

        private static int Rank(PrioritizationItem item)
        {
            Resource resource = ResourceOf(item);
            if (DistrictBalance.IsStatFinal(resource))
            {
                return 2;
            }

            if (IsRaw(resource))
            {
                return 0;
            }

            return 1;
        }

        private static bool IsRaw(Resource resource)
        {
            return resource != null &&
                   !string.IsNullOrEmpty(resource.name) &&
                   resource.name != "power" &&
                   !DistrictBalance.HasProducer(resource);
        }

        private static void ClearChain()
        {
            ChainLines.Clear();
            NetNotes.Clear();
        }

        private static string ResourceLabel(string name)
        {
            Resource resource = string.IsNullOrEmpty(name) ? null : Resource.Get(name);
            string label = resource?.GetNameInGame();
            return string.IsNullOrEmpty(label) ? name : label;
        }

        private static void FillWorkerLines()
        {
            WorkerLines.Clear();
            foreach (KeyValuePair<string, double> pair in WorkersByName)
            {
                if (pair.Value <= 0d)
                {
                    continue;
                }

                WorkerLines.Add(new Contributor
                {
                    Key = pair.Key,
                    Label = ResourceLabel(pair.Key),
                    Count = 1,
                    Amount = pair.Value,
                });
            }
        }

        private static TooltipHoverable AttachTip(TMP_Text text)
        {
            if (text == null)
            {
                return null;
            }

            text.raycastTarget = true;
            TooltipHoverable tip = text.GetComponent<TooltipHoverable>();
            if (tip == null)
            {
                tip = text.gameObject.AddComponent<TooltipHoverable>();
            }

            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init("效率", "");
            return tip;
        }

        private static void SetFigureTips(
            double workers,
            double power,
            double output,
            string workerResult,
            string powerResult,
            string inputResult)
        {
            string denominatorName = "最终产物净产出";
            List<Contributor> denominatorLines = FinalLines;
            NameRank.Clear();
            for (int i = 0; i < ChainLines.Count; i++)
            {
                Contributor line = ChainLines[i];
                line.Amount = 0d;
                NameRank.Add(line);
            }

            WriteTip(
                workerFigureTip,
                "工人",
                "每生产 1 个最终产物占用的工人",
                workers,
                "工人",
                output,
                denominatorName,
                workerResult,
                "分子里的物资",
                WorkerLines,
                false,
                "计入的建筑",
                NameRank,
                true,
                denominatorLines);
            WriteTip(
                powerFigureTip,
                "耗电",
                FinalProductToggle.IsArmed
                    ? "每生产 1 个最终产物的规划用电，按配方和规划效率"
                    : "每生产 1 个最终产物的原版电耗，含住宅用电",
                power,
                "电",
                output,
                denominatorName,
                powerResult,
                "分子里的建筑",
                ChainLines,
                true,
                null,
                null,
                false,
                denominatorLines);
            WriteInputTip(buildingFigureTip, output, denominatorName, inputResult, denominatorLines);
        }

        private static void WriteInputTip(
            TooltipHoverable tip,
            double output,
            string denominatorName,
            string result,
            List<Contributor> denominatorLines)
        {
            if (tip == null)
            {
                return;
            }

            TipText.Clear();
            TipText.Append("原料是本地区没有生产建筑的物品。\n");
            TipText.Append(FinalProductToggle.IsArmed
                ? "数量是规划消耗，不含居民消费。\n"
                : "数量已去掉居民消费，磨损仍计入。\n");
            TipText.Append("居民吃掉的最终产物不减少净产出。\n");
            TipText.Append("每件 = 每分钟消耗 / ").Append(denominatorName).Append("。\n");
            TipText.Append("结果：").Append(result).Append('\n');
            TipText.Append("分母：").Append(denominatorName).Append(' ').Append(FormatAmount(output)).Append('\n');
            Ranked.Clear();
            foreach (KeyValuePair<string, double> pair in InputRates)
            {
                if (pair.Value <= 0d)
                {
                    continue;
                }

                Ranked.Add(new Contributor
                {
                    Key = pair.Key,
                    Label = ResourceLabel(pair.Key),
                    Amount = pair.Value,
                });
            }

            Ranked.Sort(CompareAmount);
            int shown = Ranked.Count < TipLineLimit ? Ranked.Count : TipLineLimit;
            if (shown > 0)
            {
                TipText.Append('\n');
            }

            for (int i = 0; i < shown; i++)
            {
                Contributor line = Ranked[i];
                double perItem = output > 1e-6d ? line.Amount / output : 0d;
                TipText.Append(line.Label)
                    .Append("  ")
                    .Append(FormatAmount(line.Amount))
                    .Append("/分钟  ")
                    .Append(FormatAmount(perItem))
                    .Append("/件\n");
            }

            if (Ranked.Count > shown)
            {
                TipText.Append("其余 ").Append(Ranked.Count - shown).Append(" 种\n");
            }

            if (NetNotes.Count > 0)
            {
                TipText.Append('\n').Append("净产出：").Append('\n');
                for (int i = 0; i < NetNotes.Count; i++)
                {
                    TipText.Append(NetNotes[i]).Append('\n');
                }
            }

            AppendGroup("分母里的产物", denominatorLines, false);
            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init("原料", TipText.ToString().TrimEnd());
            RefreshOpenTip(tip);
        }

        private static void WriteTip(
            TooltipHoverable tip,
            string title,
            string meaning,
            double numerator,
            string numeratorName,
            double denominator,
            string denominatorName,
            string result,
            string firstHeading,
            List<Contributor> firstLines,
            bool firstByCount,
            string secondHeading,
            List<Contributor> secondLines,
            bool secondByCount,
            List<Contributor> denominatorLines)
        {
            if (tip == null)
            {
                return;
            }

            TipText.Clear();
            TipText.Append(meaning).Append('\n');
            TipText.Append("结果：").Append(result).Append('\n');
            TipText.Append("分子：").Append(numeratorName).Append(' ').Append(FormatAmount(numerator)).Append('\n');
            TipText.Append("分母：").Append(denominatorName).Append(' ').Append(FormatAmount(denominator)).Append('\n');
            if (denominator > 1e-6d)
            {
                TipText.Append("算式：")
                    .Append(FormatAmount(numerator))
                    .Append(" / ")
                    .Append(FormatAmount(denominator))
                    .Append(" = ")
                    .Append(result)
                    .Append('\n');
            }

            AppendGroup(firstHeading, firstLines, firstByCount);
            AppendGroup(secondHeading, secondLines, secondByCount);
            AppendGroup("分母里的产物", denominatorLines, false);
            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init(title, TipText.ToString().TrimEnd());
            RefreshOpenTip(tip);
        }

        private static void AppendGroup(string heading, List<Contributor> lines, bool byCount)
        {
            if (string.IsNullOrEmpty(heading) || lines == null || lines.Count == 0)
            {
                return;
            }

            Ranked.Clear();
            Ranked.AddRange(lines);
            Ranked.Sort(byCount ? (System.Comparison<Contributor>)CompareCount : CompareAmount);
            TipText.Append('\n').Append(heading).Append('\n');
            int shown = Ranked.Count < TipLineLimit ? Ranked.Count : TipLineLimit;
            for (int i = 0; i < shown; i++)
            {
                Contributor line = Ranked[i];
                TipText.Append(line.Label);
                if (byCount)
                {
                    TipText.Append(" ×").Append(line.Count);
                    if (line.Amount > 0d)
                    {
                        TipText.Append("  ").Append(FormatAmount(line.Amount));
                    }
                }
                else
                {
                    TipText.Append(' ').Append(FormatAmount(line.Amount));
                }

                TipText.Append('\n');
            }

            if (Ranked.Count > shown)
            {
                TipText.Append("其余 ").Append(Ranked.Count - shown).Append(" 项\n");
            }
        }

        private static string FormatAmount(double value)
        {
            if (System.Math.Abs(value) >= 100d)
            {
                return value.ToString("#,##0");
            }

            if (System.Math.Abs(value) >= 10d)
            {
                return value.ToString("0.0");
            }

            return value.ToString("0.00");
        }

        private static void RefreshOpenTip(TooltipHoverable tip)
        {
            Tooltip tooltip = WorldScripts.Inst?.tooltipManager?.tooltipPanel;
            if (tooltip == null || OpenTip(tooltip) != tip)
            {
                return;
            }

            OpenTip(tooltip) = null;
            tooltip.ShowTooltip(tip);
        }

        private static int CompareAmount(Contributor left, Contributor right)
        {
            return right.Amount.CompareTo(left.Amount);
        }

        private static int CompareCount(Contributor left, Contributor right)
        {
            int count = right.Count.CompareTo(left.Count);
            if (count != 0)
            {
                return count;
            }

            return string.CompareOrdinal(left.Label, right.Label);
        }

        private static void SetCount(PrioritizationItem item, int count)
        {
            if (item?.icon == null)
            {
                return;
            }

            Transform existing = item.icon.transform.Find("ProducerCount");
            TextMeshProUGUI label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
            if (label == null)
            {
                RectTransform rect = new GameObject("ProducerCount", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(item.icon.transform, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(4f, 16f);
                label = rect.gameObject.AddComponent<TextMeshProUGUI>();
                TMP_Text style = item.employmentTotal;
                if (style != null)
                {
                    label.font = style.font;
                    if (style.fontSharedMaterial != null)
                    {
                        label.fontMaterial = Object.Instantiate(style.fontSharedMaterial);
                    }
                }

                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Bottom;
                label.overflowMode = TextOverflowModes.Overflow;
                label.color = Color.white;
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color(0f, 0f, 0f, 0.95f);
                label.raycastTarget = false;
            }

            float height = item.icon.rectTransform.rect.height;
            if (height > 8f)
            {
                float size = Mathf.Clamp(height * 0.4f, 12f, 20f);
                RectTransform rect = label.rectTransform;
                if (Mathf.Abs(label.fontSize - size) > 0.5f)
                {
                    label.fontSize = size;
                }

                float bar = height * 0.48f;
                if (Mathf.Abs(rect.sizeDelta.y - bar) > 0.5f)
                {
                    rect.sizeDelta = new Vector2(4f, bar);
                }
            }

            string text = count.ToString();
            if (label.text != text)
            {
                label.text = text;
            }
        }

        private static void Style(TMP_Text text, string value)
        {
            text.fontStyle = FontStyles.Bold;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = value;
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out value);
        }

        private static Color ColorFor(double production, double consumption)
        {
            if (production < 0.75d * consumption)
            {
                return Dashboard.redTextColor;
            }

            if (production < 0.98d * consumption)
            {
                return Dashboard.yellowTextColor;
            }

            return Color.white;
        }
    }
}
