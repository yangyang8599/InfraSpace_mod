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
            "只在勾选最终产物时出现。\n\n" +
            "工人：每生产 1 个最终产物占用的工人\n" +
            "耗电：每生产 1 个最终产物消耗的电\n" +
            "建筑：每生产 1 个最终产物占用的建筑\n\n" +
            "分母是当前可见的最终产物产量之和。\n" +
            "分子包含为这些产物配套的中间产物。\n" +
            "一座厂产出多种物资只算一座。\n" +
            "电力不计入。\n\n" +
            "鼠标放在右边三个数字上，可查看本次计入的建筑、分子、分母和算式。";

        private const int TipLineLimit = 10;

        private static readonly HashSet<string> VisibleNames = new HashSet<string>();

        private static readonly HashSet<string> FactoryInputs = new HashSet<string>();

        private static readonly Dictionary<string, double> Produced = new Dictionary<string, double>();

        private static readonly Dictionary<string, double> WorkersByName = new Dictionary<string, double>();

        private static readonly Dictionary<string, Dictionary<string, int>> ProducersByResource =
            new Dictionary<string, Dictionary<string, int>>();

        private static readonly Dictionary<string, int> ChainIndex = new Dictionary<string, int>();

        private static readonly List<Contributor> ChainLines = new List<Contributor>();

        private static readonly List<Contributor> WorkerLines = new List<Contributor>();

        private static readonly List<Contributor> FinalLines = new List<Contributor>();

        private static readonly List<Contributor> AnyLines = new List<Contributor>();

        private static readonly List<Contributor> Ranked = new List<Contributor>();

        private static readonly List<Contributor> NameRank = new List<Contributor>();

        private static readonly List<string> VisibleProducts = new List<string>();

        private static readonly StringBuilder TipText = new StringBuilder(768);

        private static readonly AccessTools.FieldRef<Tooltip, TooltipHoverable> OpenTip =
            AccessTools.FieldRefAccess<Tooltip, TooltipHoverable>("hoverable");

        private static PrioritizationItem summary;
        private static PrioritizationItem efficiency;
        private static TooltipHoverable workerFigureTip;
        private static TooltipHoverable powerFigureTip;
        private static TooltipHoverable buildingFigureTip;
        private static bool denominatorFallback;
        private static bool unavailable;
        private static bool workerTipReady;
        private static bool mineralsReady;

        private static readonly Dictionary<int, int> OriginalSibling = new Dictionary<int, int>();

        private static readonly List<PrioritizationItem> Ordered = new List<PrioritizationItem>();

        private static readonly HashSet<string> Minerals = new HashSet<string>();

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
            FactoryInputs.Clear();
            Produced.Clear();
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

            if (!FinalProductToggle.IsArmed)
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

            int chainBuildings;
            double chainPower;
            MeasureChain(district, out chainBuildings, out chainPower);
            double output = CollectDenominator();
            efficiency.employmentTotal.text = PerItem(itemWorkers, output);
            efficiency.productionText.text = PerItem(chainPower, output);
            efficiency.consumptionText.text = PerItem(chainBuildings, output);
            efficiency.productionText.color = Color.white;
            efficiency.transform.SetAsLastSibling();
            FillWorkerLines();
            SetFigureTips(
                itemWorkers,
                chainPower,
                chainBuildings,
                output,
                efficiency.employmentTotal.text,
                efficiency.productionText.text,
                efficiency.consumptionText.text);
        }

        private static void MeasureChain(byte district, out int buildings, out double power)
        {
            buildings = 0;
            power = 0d;
            ClearChain();
            IEnumerable<Building> all = WorldScripts.Inst?.registry?.builtBuildings;
            if (all == null)
            {
                return;
            }

            foreach (Building building in all)
            {
                if (!ProducerCounts.InDistrict(building, district))
                {
                    continue;
                }

                if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory)
                    || factory.productionDefinition == null)
                {
                    continue;
                }

                List<ResourceCost> costs = factory.productionDefinition.consumables;
                if (costs != null)
                {
                    for (int i = 0; i < costs.Count; i++)
                    {
                        Resource input = costs[i].resource;
                        if (input != null && !string.IsNullOrEmpty(input.name))
                        {
                            FactoryInputs.Add(input.name);
                        }
                    }
                }

                VisibleProducts.Clear();
                ProducerCounts.AppendMatchingProducts(building, VisibleNames, VisibleProducts);
                if (VisibleProducts.Count == 0)
                {
                    continue;
                }

                buildings++;
                double needed = 0d;
                if (factory.consumerProducer != null)
                {
                    needed = factory.consumerProducer.lastStepPowerNeeded;
                    power += needed;
                }

                string buildingName = BuildingLabel(building);
                AddChain(buildingName, needed);
                for (int product = 0; product < VisibleProducts.Count; product++)
                {
                    NoteProducer(VisibleProducts[product], buildingName);
                }
            }
        }

        private static double CollectDenominator()
        {
            FinalLines.Clear();
            AnyLines.Clear();
            double finals = 0d;
            double any = 0d;
            foreach (KeyValuePair<string, double> pair in Produced)
            {
                Contributor line = new Contributor
                {
                    Key = pair.Key,
                    Label = ResourceLabel(pair.Key),
                    Count = 1,
                    Amount = pair.Value
                };
                AnyLines.Add(line);
                any += pair.Value;
                if (!FactoryInputs.Contains(pair.Key))
                {
                    FinalLines.Add(line);
                    finals += pair.Value;
                }
            }

            denominatorFallback = finals <= 1e-6d;
            return denominatorFallback ? any : finals;
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
            if (IsFront(resource))
            {
                return 0;
            }

            double consumption;
            if (!TryParse(item.consumptionText.text, out consumption) || consumption <= 1e-4d)
            {
                return 2;
            }

            return 1;
        }

        private static bool IsFront(Resource resource)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name))
            {
                return false;
            }

            if (resource.name == "industrialRobot" || resource.name == "highTechTool")
            {
                return true;
            }

            EnsureMinerals();
            return Minerals.Contains(resource.name);
        }

        private static void EnsureMinerals()
        {
            if (mineralsReady)
            {
                return;
            }

            mineralsReady = true;
            ConstructionCategory mines = Old.GetGameUIModule()?.constructionCategories?.FindMainCategory("mines");
            if (mines == null)
            {
                return;
            }

            List<string> items = mines.GetAllItems();
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                BuildingDescription description = BuildingDescription.Get(items[i]);
                if (!(description?.GetCachedProductionLogic() is FactoryProductionLogic factory) ||
                    factory.productionDefinition?.producables == null)
                {
                    continue;
                }

                List<ResourceCost> products = factory.productionDefinition.producables;
                for (int product = 0; product < products.Count; product++)
                {
                    Resource resource = products[product].resource;
                    if (resource != null && !string.IsNullOrEmpty(resource.name))
                    {
                        Minerals.Add(resource.name);
                    }
                }
            }
        }

        private static void ClearChain()
        {
            ChainLines.Clear();
            ChainIndex.Clear();
            ProducersByResource.Clear();
        }

        private static void AddChain(string buildingName, double power)
        {
            if (string.IsNullOrEmpty(buildingName))
            {
                return;
            }

            if (ChainIndex.TryGetValue(buildingName, out int index))
            {
                Contributor existing = ChainLines[index];
                existing.Count++;
                existing.Amount += power;
                ChainLines[index] = existing;
                return;
            }

            ChainIndex[buildingName] = ChainLines.Count;
            ChainLines.Add(new Contributor
            {
                Key = buildingName,
                Label = buildingName,
                Count = 1,
                Amount = power,
            });
        }

        private static void NoteProducer(string resourceName, string buildingName)
        {
            if (string.IsNullOrEmpty(resourceName) || string.IsNullOrEmpty(buildingName))
            {
                return;
            }

            if (!ProducersByResource.TryGetValue(resourceName, out Dictionary<string, int> counts))
            {
                counts = new Dictionary<string, int>();
                ProducersByResource[resourceName] = counts;
            }

            counts.TryGetValue(buildingName, out int count);
            counts[buildingName] = count + 1;
        }

        private static string BuildingLabel(Building building)
        {
            if (building == null)
            {
                return "";
            }

            string label = building.GetDescription()?.GetNameInGame();
            return string.IsNullOrEmpty(label) ? building.buildingName : label;
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
            int buildings,
            double output,
            string workerResult,
            string powerResult,
            string buildingResult)
        {
            string denominatorName = denominatorFallback ? "当前可见产量" : "最终产物产量";
            List<Contributor> denominatorLines = denominatorFallback ? AnyLines : FinalLines;
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
                "每生产 1 个最终产物消耗的电",
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
            WriteTip(
                buildingFigureTip,
                "建筑",
                "每生产 1 个最终产物占用的建筑",
                buildings,
                "座建筑",
                output,
                denominatorName,
                buildingResult,
                "分子里的建筑",
                NameRank,
                true,
                null,
                null,
                false,
                denominatorLines);
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
