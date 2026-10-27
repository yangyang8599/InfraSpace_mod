using System.Globalization;
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
            int buildings = 0;
            // 只加当前显示出来的行。地区视图里被藏起的全零行，以及合计行本身，都不计入。
            PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(false);
            for (int i = 0; i < items.Length; i++)
            {
                PrioritizationItem item = items[i];
                if (item == null || item == summary || !item.gameObject.activeSelf)
                {
                    continue;
                }

                int producedBy = ProducerCounts.Count(ResourceOf(item));
                buildings += producedBy;
                SetCount(item, producedBy);

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

            SetCount(summary, buildings);

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
