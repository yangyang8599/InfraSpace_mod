using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceStationTools
{
    [HarmonyPatch(typeof(Tooltip), "ConfigureTooltip")]
    internal static class CargoTooltipFit
    {
        private const float Slack = 18f;

        private static readonly AccessTools.FieldRef<Tooltip, TooltipHoverable> HoverableField =
            AccessTools.FieldRefAccess<Tooltip, TooltipHoverable>("hoverable");

        private static readonly Dictionary<LayoutElement, Width> widths = new Dictionary<LayoutElement, Width>();

        private static ContentSizeFitter fitter;

        private static ContentSizeFitter.FitMode horizontalFit;

        private static bool fitterCaptured;

        private static Text description;

        private static HorizontalWrapMode overflow;

        private static bool overflowCaptured;

        private struct Width
        {
            public float Preferred;
            public float Min;
        }

        private static void Postfix(Tooltip __instance, string title, string description)
        {
            if (__instance == null || __instance.descriptionText == null)
            {
                return;
            }

            TooltipHoverable hoverable = HoverableField(__instance);
            if (hoverable == null || !CargoRoutes.IsMarked(hoverable))
            {
                Restore();
                Rebuild(__instance);
                return;
            }

            float width = NeededWidth(__instance, description);
            if (width <= 0f)
            {
                return;
            }

            Apply(__instance, width);
            Rebuild(__instance);
        }

        private static float NeededWidth(Tooltip tooltip, string description)
        {
            float width = Measure(tooltip.descriptionText, description) + Slack;
            float scale = tooltip.transform.lossyScale.x;
            if (scale < 0.01f)
            {
                scale = 1f;
            }

            float cap = (Screen.width - 48f) / scale;
            return width > cap ? cap : width;
        }

        private static float Measure(Text text, string value)
        {
            if (text == null || string.IsNullOrEmpty(value) || text.pixelsPerUnit <= 0f)
            {
                return 0f;
            }

            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(8192f, 64f));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.updateBounds = true;
            string[] lines = value.Split('\n');
            float max = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Length == 0 ? " " : lines[i];
                float width = text.cachedTextGeneratorForLayout.GetPreferredWidth(line, settings) / text.pixelsPerUnit;
                if (width > max)
                {
                    max = width;
                }
            }

            return max;
        }

        private static void Apply(Tooltip tooltip, float width)
        {
            Text text = tooltip.descriptionText;
            if (!overflowCaptured)
            {
                description = text;
                overflow = text.horizontalOverflow;
                overflowCaptured = true;
            }

            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Transform node = text.transform;
            Transform root = tooltip.transform;
            while (node != null)
            {
                float target = node == text.transform ? width : width + 28f;
                LayoutElement element = node.GetComponent<LayoutElement>();
                if (element == null && (node == text.transform || node == root))
                {
                    element = node.gameObject.AddComponent<LayoutElement>();
                }

                if (element != null)
                {
                    Remember(element);
                    element.preferredWidth = target;
                    element.minWidth = target;
                }

                if (node == root)
                {
                    break;
                }

                node = node.parent;
            }

            ContentSizeFitter size = root.GetComponent<ContentSizeFitter>();
            if (size != null)
            {
                if (!fitterCaptured)
                {
                    fitter = size;
                    horizontalFit = size.horizontalFit;
                    fitterCaptured = true;
                }

                size.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private static void Remember(LayoutElement element)
        {
            if (widths.ContainsKey(element))
            {
                return;
            }

            widths[element] = new Width
            {
                Preferred = element.preferredWidth,
                Min = element.minWidth,
            };
        }

        private static void Restore()
        {
            foreach (KeyValuePair<LayoutElement, Width> pair in widths)
            {
                LayoutElement element = pair.Key;
                if (element == null)
                {
                    continue;
                }

                element.preferredWidth = pair.Value.Preferred;
                element.minWidth = pair.Value.Min;
            }

            if (fitterCaptured && fitter != null)
            {
                fitter.horizontalFit = horizontalFit;
            }

            if (overflowCaptured && description != null)
            {
                description.horizontalOverflow = overflow;
            }
        }

        private static void Rebuild(Tooltip tooltip)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip.descriptionText.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip.GetComponent<RectTransform>());
        }
    }
}
