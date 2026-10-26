using System.Globalization;
using TMPro;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class ProductionSummary
    {
        private static PrioritizationItem summary;
        private static bool unavailable;

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

            double workers = 0d;
            double production = 0d;
            double consumption = 0d;
            // 只加当前显示出来的行。地区视图里被藏起的全零行，以及合计行本身，都不计入。
            PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(false);
            for (int i = 0; i < items.Length; i++)
            {
                PrioritizationItem item = items[i];
                if (item == null || item == summary || !item.gameObject.activeSelf)
                {
                    continue;
                }

                if (TryParse(item.employmentTotal.text, out double workerValue))
                {
                    workers += workerValue;
                }

                if (TryParse(item.productionText.text, out double produced))
                {
                    production += produced;
                }

                if (TryParse(item.consumptionText.text, out double consumed))
                {
                    consumption += consumed;
                }
            }

            summary.employmentTotal.text = workers.ToString("#,##0");
            summary.productionText.text = production.ToString("#,##0.0");
            summary.consumptionText.text = consumption.ToString("#,##0.0");
            summary.productionText.color = ColorFor(production, consumption);
            summary.transform.SetAsLastSibling();
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
