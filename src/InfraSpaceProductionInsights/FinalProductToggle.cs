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

        private static bool HasLayout(Transform parent)
        {
            return parent != null
                && (parent.GetComponent<HorizontalLayoutGroup>() != null
                    || parent.GetComponent<VerticalLayoutGroup>() != null
                    || parent.GetComponent<GridLayoutGroup>() != null);
        }
    }
}
