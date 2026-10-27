using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    [HarmonyPatch(typeof(FactoryInfoPanel), "Update")]
    internal static class FactoryRecipeRatePatch
    {
        private static void Postfix(FactoryInfoPanel __instance, FactoryProductionLogic ___factory)
        {
            FactoryRecipeRates.Apply(__instance, ___factory);
        }
    }

    internal static class FactoryRecipeRates
    {
        private static readonly Dictionary<int, RectTransform> Rows = new Dictionary<int, RectTransform>();

        private static readonly Dictionary<int, string> DirectPrefixes = new Dictionary<int, string>();

        internal static void Apply(FactoryInfoPanel panel, FactoryProductionLogic factory)
        {
            if (panel == null || factory?.productionDefinition == null)
            {
                return;
            }

            Transform storage = panel.storagePanel != null ? panel.storagePanel.transform : null;
            bool applied = false;
            TextLocalizationTMP[] tmpLabels = panel.GetComponentsInChildren<TextLocalizationTMP>(true);
            for (int i = 0; i < tmpLabels.Length; i++)
            {
                TextLocalizationTMP label = tmpLabels[i];
                TMP_Text header = label != null ? label.GetComponent<TMP_Text>() : null;
                if (header == null || Under(label.transform, storage) || !IsProductionHeader(Localized(label.key)))
                {
                    continue;
                }

                label.suffix = "";
                label.UpdateText();
                Show(header, Localized(label.key).Trim(), factory);
                applied = true;
            }

            TextLocalization[] legacyLabels = panel.GetComponentsInChildren<TextLocalization>(true);
            for (int i = 0; i < legacyLabels.Length; i++)
            {
                TextLocalization label = legacyLabels[i];
                if (label == null || Under(label.transform, storage) || !IsProductionHeader(Localized(label.key)))
                {
                    continue;
                }

                label.suffix = "";
                label.UpdateText();
            }

            if (applied)
            {
                return;
            }

            TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null || Under(text.transform, storage) || !IsProductionHeader(text.text))
                {
                    continue;
                }

                int id = text.GetInstanceID();
                if (!DirectPrefixes.TryGetValue(id, out string prefix))
                {
                    prefix = PrefixOf(text.text);
                    DirectPrefixes[id] = prefix;
                }

                text.text = prefix;
                Show(text, prefix, factory);
            }
        }

        private static void Show(TMP_Text header, string prefix, FactoryProductionLogic factory)
        {
            if (header.font == null || factory?.productionDefinition == null)
            {
                return;
            }

            RectTransform row = RowFor(header);
            Clear(row);
            ProductionDefinition definition = factory.productionDefinition;
            int timeSteps = definition.timeSteps;
            if (timeSteps <= 0)
            {
                row.gameObject.SetActive(false);
                return;
            }

            int speed = Mathf.RoundToInt(100f * factory.GetEfficiency()) * 10;
            float size = RateSize(header);
            float x = 0f;
            x = AddCosts(row, header, definition.consumables, timeSteps, speed, size, x);
            bool hasOutput = HasCost(definition.producables);
            if (x > 0f && hasOutput)
            {
                x = AddLabel(row, header, "→", size, x);
            }

            x = AddCosts(row, header, definition.producables, timeSteps, speed, size, x);
            if (x <= 0f)
            {
                row.gameObject.SetActive(false);
                return;
            }

            x = AddLabel(row, header, "/分", size, x);
            row.sizeDelta = new Vector2(x + 4f, size + 10f);
            row.gameObject.SetActive(true);
            Place(header, row, string.IsNullOrEmpty(prefix) ? "生产：" : prefix);
        }

        private static RectTransform RowFor(TMP_Text header)
        {
            int id = header.GetInstanceID();
            if (Rows.TryGetValue(id, out RectTransform existing) && existing != null)
            {
                return existing;
            }

            var root = new GameObject("RecipeRate");
            root.transform.SetParent(header.transform, false);
            RectTransform rect = root.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            Rows[id] = rect;
            return rect;
        }

        private static void Clear(RectTransform row)
        {
            for (int i = row.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(row.GetChild(i).gameObject);
            }
        }

        private static bool HasCost(List<ResourceCost> costs)
        {
            if (costs == null)
            {
                return false;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i].resource != null && costs[i].amount != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static float AddCosts(
            RectTransform row,
            TMP_Text header,
            List<ResourceCost> costs,
            int timeSteps,
            int speed,
            float size,
            float x)
        {
            if (costs == null)
            {
                return x;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                Resource resource = costs[i].resource;
                if (resource == null || costs[i].amount == 0)
                {
                    continue;
                }

                x = AddIcon(row, resource, size, x);
                x = AddLabel(row, header, Format(PerMinute(costs[i].amount, timeSteps, speed)), size, x);
            }

            return x;
        }

        private static float AddIcon(RectTransform row, Resource resource, float size, float x)
        {
            var root = new GameObject("icon");
            root.transform.SetParent(row, false);
            var image = root.AddComponent<Image>();
            image.sprite = resource.GetSprite();
            image.raycastTarget = true;
            var tip = root.AddComponent<TooltipHoverable>();
            tip.titleCode = "";
            tip.descriptionCode = "";
            tip.Init(resource.GetNameInGame(), "");
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(size + 6f, size + 6f);
            return x + size + 8f;
        }

        private static float AddLabel(RectTransform row, TMP_Text header, string value, float size, float x)
        {
            var root = new GameObject("rate");
            root.transform.SetParent(row, false);
            var text = root.AddComponent<TextMeshProUGUI>();
            text.font = header.font;
            text.fontSharedMaterial = header.fontSharedMaterial;
            text.fontSize = size;
            text.color = header.color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = value;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            float width = Mathf.Max(text.GetPreferredValues(value).x, value.Length * size * 0.55f) + 4f;
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, size + 8f);
            return x + width;
        }

        private static float RateSize(TMP_Text header)
        {
            if (header.fontSize <= 1f)
            {
                return 16f;
            }

            return Mathf.Clamp(header.fontSize * 0.75f, 15f, 18f);
        }

        private static void Place(TMP_Text header, RectTransform detail, string prefix)
        {
            float word = header.GetPreferredValues(prefix).x;
            float start = header.margin.x;
            int horizontal = (int)header.alignment & 0xFF;
            float width = header.rectTransform.rect.width;
            if (horizontal == 2)
            {
                start = width * 0.5f - word * 0.5f;
            }
            else if (horizontal == 4)
            {
                start = width - header.margin.z - word;
            }

            detail.anchoredPosition = new Vector2(start + word + 4f, 0f);
        }

        private static double PerMinute(int amount, int timeSteps, int speed)
        {
            return (double)(amount * 50 * 60 * speed) / timeSteps / 10d;
        }

        private static string Format(double value)
        {
            double abs = System.Math.Abs(value);
            if (abs >= 100d)
            {
                return value.ToString("#,##0");
            }

            if (abs >= 1d)
            {
                return value.ToString("0.0");
            }

            return value.ToString("0.00");
        }

        private static bool IsProductionHeader(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed == "生产：" || trimmed == "生产:" || trimmed == "生产")
            {
                return true;
            }

            return trimmed.StartsWith("生产：") || trimmed.StartsWith("生产:");
        }

        private static string PrefixOf(string value)
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.StartsWith("生产:"))
            {
                return "生产:";
            }

            if (trimmed == "生产")
            {
                return "生产";
            }

            return "生产：";
        }

        private static string Localized(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }

            return Localization.GetText(key);
        }

        private static bool Under(Transform transform, Transform root)
        {
            return root != null && transform != null && transform.IsChildOf(root);
        }
    }
}
