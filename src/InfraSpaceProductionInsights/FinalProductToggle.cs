using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class FinalProductToggle
    {
        private static Toggle toggle;

        internal static bool IsArmed
        {
            get { return toggle != null && toggle.isOn && toggle.interactable; }
        }

        internal static void Install(PrioritizationPanel panel)
        {
            if (toggle != null || panel == null || panel.districtToggle == null)
            {
                return;
            }

            Toggle original = panel.districtToggle;
            GameObject clone = Object.Instantiate(original.gameObject, original.transform.parent);
            clone.name = "FinalProductToggle";
            toggle = clone.GetComponent<Toggle>();
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(false);
            ApplyLabel(clone);
            ApplyTip(clone);

            RectTransform originalRect = original.GetComponent<RectTransform>();
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex());
            if (!HasLayout(original.transform.parent))
            {
                float extra = 96f;
                cloneRect.sizeDelta = new Vector2(originalRect.sizeDelta.x + extra, originalRect.sizeDelta.y);
                cloneRect.anchoredPosition = originalRect.anchoredPosition + new Vector2(-(originalRect.rect.width + extra + 8f), 0f);
            }

            Sync(panel);
        }

        internal static void Sync(PrioritizationPanel panel)
        {
            if (toggle == null || panel == null || panel.districtToggle == null)
            {
                return;
            }

            toggle.interactable = panel.districtToggle.isOn;
            ApplyLabel(toggle.gameObject);
        }

        private static void ApplyLabel(GameObject root)
        {
            foreach (TextLocalizationTMP localization in root.GetComponentsInChildren<TextLocalizationTMP>(true))
            {
                localization.key = "";
                localization.enabled = false;
            }

            foreach (TextLocalization localization in root.GetComponentsInChildren<TextLocalization>(true))
            {
                localization.key = "";
                localization.enabled = false;
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = "最终产物";
                text.overflowMode = TextOverflowModes.Overflow;
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                text.text = "最终产物";
            }
        }

        private static void ApplyTip(GameObject root)
        {
            const string Body =
                "勾选后，按这个地区最终产物的建筑数量反推原料需求。\n" +
                "每座最终产物建筑按满员配方计，不看当前开工率。\n" +
                "中间建筑多或少，都不会改这个需求。\n" +
                "某一级工厂还没建时，仍按配方往下推，这一级的产量可以是 0。\n" +
                "消费列改成反推出来的需求。\n" +
                "列表最下面多出效率行，表示每生产 1 个最终产物占用的工人、电和建筑。\n" +
                "要先勾选地区，这个开关才能用。";

            TooltipHoverable[] tips = root.GetComponentsInChildren<TooltipHoverable>(true);
            if (tips == null || tips.Length == 0)
            {
                tips = new[] { root.AddComponent<TooltipHoverable>() };
            }

            for (int i = 0; i < tips.Length; i++)
            {
                TooltipHoverable tip = tips[i];
                if (tip == null)
                {
                    continue;
                }

                tip.titleCode = "";
                tip.descriptionCode = "";
                tip.Init("最终产物", Body);
            }
        }

        private static bool HasLayout(Transform parent)
        {
            return parent != null
                && (parent.GetComponent<HorizontalLayoutGroup>() != null
                    || parent.GetComponent<VerticalLayoutGroup>() != null
                    || parent.GetComponent<GridLayoutGroup>() != null);
        }
    }
}
