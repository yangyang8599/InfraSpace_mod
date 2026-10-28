using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class FinalProductToggle
    {
        private const string FinalTip =
            "勾选后进入规划模式统计。要先勾选地区。\n" +
            "产出列按当前建筑数量和配方计算，不看当前开工。\n" +
            "消耗列从最终产物往原料倒推。\n" +
            "没勾选「无产原矿」时，一直倒到开采物。\n" +
            "有机废料是终点，不再进入农场。\n" +
            "没有沼气发酵池时，天然气是终点；有发酵池时，终点改成有机废料。\n" +
            "机器人或高科没有生产建筑时是原料；有生产建筑则继续倒推。\n" +
            "效率行用这些规划数字，不计入居民消费。";

        private const string StopTip =
            "只在规划模式里可用。\n" +
            "倒推只走本地区已经建好的生产建筑。\n" +
            "没有生产建筑的物品当作原料，不再补没建的配方。\n" +
            "三个特殊物品、天然气和有机废料的规则与规划模式相同。";

        private const string TechTip =
            "只在规划模式里可用。\n" +
            "打开后，已安装的高科技工具按 1.5 倍产率计入规划产量，不管工具有没有送到。\n" +
            "没安装的建筑不会因此多出产率，也不追加磨损。\n" +
            "关闭时，规划产量不含高科技工具的产率。\n" +
            "土壤富集站覆盖始终计入。";

        private static readonly Color DisabledTint = new Color(0.72f, 0.76f, 0.80f, 1f);

        private static readonly Color DisabledFill = new Color(0.55f, 0.58f, 0.62f, 1f);

        private static readonly Dictionary<int, BoxState> Boxes = new Dictionary<int, BoxState>();

        private static Sprite solid;

        private static bool renderHooked;

        private static bool layingOut;

        private static bool layoutReleased;

        private static PrioritizationPanel boundPanel;

        private static Toggle toggle;

        private static Toggle stopToggle;

        private static Toggle techToggle;

        private static Color enabledLabel = Color.white;

        private static bool enabledLabelReady;

        internal static bool IsArmed
        {
            get { return toggle != null && toggle.isOn && toggle.interactable; }
        }

        internal static bool StopAtNoProduction
        {
            get { return stopToggle != null && stopToggle.isOn && stopToggle.interactable; }
        }

        internal static bool IncludeHighTech
        {
            get { return techToggle != null && techToggle.isOn && techToggle.interactable; }
        }

        internal static void Install(PrioritizationPanel panel)
        {
            if (toggle != null || panel == null || panel.districtToggle == null)
            {
                return;
            }

            Toggle original = panel.districtToggle;
            RememberEnabledLabel(original.gameObject);
            GameObject clone = Object.Instantiate(original.gameObject, original.transform.parent);
            clone.name = "FinalProductToggle";
            toggle = clone.GetComponent<Toggle>();
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(false);
            ApplyLabel(clone, "规划模式");
            KeepOurPlace(toggle);
            clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex());

            GameObject stop = Object.Instantiate(original.gameObject, original.transform.parent);
            stop.name = "NoProductionRawToggle";
            stopToggle = stop.GetComponent<Toggle>();
            stopToggle.onValueChanged.RemoveAllListeners();
            stopToggle.SetIsOnWithoutNotify(false);
            ApplyLabel(stop, "无产原矿");
            KeepOurPlace(stopToggle);
            stop.transform.SetSiblingIndex(clone.transform.GetSiblingIndex());

            GameObject tech = Object.Instantiate(original.gameObject, original.transform.parent);
            tech.name = "HighTechPlanningToggle";
            techToggle = tech.GetComponent<Toggle>();
            techToggle.onValueChanged.RemoveAllListeners();
            techToggle.SetIsOnWithoutNotify(false);
            ApplyLabel(tech, "高科工具");
            KeepOurPlace(techToggle);
            tech.transform.SetSiblingIndex(stop.transform.GetSiblingIndex());

            EnsureRenderHook();
            Sync(panel);
        }

        private static void EnsureRenderHook()
        {
            if (renderHooked)
            {
                return;
            }

            Canvas.willRenderCanvases += BeforeRender;
            renderHooked = true;
        }

        private static void BeforeRender()
        {
            if (layingOut || boundPanel == null || boundPanel.districtToggle == null)
            {
                return;
            }

            layingOut = true;
            try
            {
                Layout(boundPanel.districtToggle);
            }
            finally
            {
                layingOut = false;
            }
        }

        internal static void Sync(PrioritizationPanel panel)
        {
            if (toggle == null || panel == null || panel.districtToggle == null)
            {
                return;
            }

            boundPanel = panel;
            EnsureRenderHook();

            ApplyInteractable(toggle, panel.districtToggle.isOn);
            bool planning = toggle.interactable && toggle.isOn;
            ApplyInteractable(stopToggle, planning);
            ApplyInteractable(techToggle, planning);

            ApplyLabel(toggle.gameObject, "规划模式");
            if (stopToggle != null)
            {
                ApplyLabel(stopToggle.gameObject, "无产原矿");
            }

            if (techToggle != null)
            {
                ApplyLabel(techToggle.gameObject, "高科工具");
            }

            Layout(panel.districtToggle);
            ApplyTip(toggle.gameObject, "规划模式", FinalTip);
            if (stopToggle != null)
            {
                ApplyTip(stopToggle.gameObject, "无产原矿", StopTip);
            }

            if (techToggle != null)
            {
                ApplyTip(techToggle.gameObject, "高科工具", TechTip);
            }
        }

        private static void ApplyLabel(GameObject root, string label)
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
                text.text = label;
                text.overflowMode = TextOverflowModes.Overflow;
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                text.text = label;
            }
        }

        private static void ApplyTip(GameObject root, string title, string body)
        {
            if (root == null)
            {
                return;
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            if (graphics == null || graphics.Length == 0)
            {
                TooltipHoverable created = root.GetComponent<TooltipHoverable>();
                if (created == null)
                {
                    created = root.AddComponent<TooltipHoverable>();
                }

                WriteTip(created, title, body);
                return;
            }

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                graphic.raycastTarget = true;
                TooltipHoverable tip = graphic.GetComponent<TooltipHoverable>();
                if (tip == null)
                {
                    tip = graphic.gameObject.AddComponent<TooltipHoverable>();
                }

                WriteTip(tip, title, body);
            }
        }

        private static void WriteTip(TooltipHoverable tip, string title, string body)
        {
            if (tip == null)
            {
                return;
            }

            tip.titleCode = "";
            tip.descriptionCode = "";
            if (tip.GetTitle() != title || tip.GetDescription() != body)
            {
                tip.Init(title, body);
            }
        }

        private static void Layout(Toggle district)
        {
            if (district == null)
            {
                return;
            }

            Glue(toggle);
            Glue(stopToggle);
            Glue(techToggle);
            KeepOurPlace(toggle);
            KeepOurPlace(stopToggle);
            KeepOurPlace(techToggle);
            if (!layoutReleased)
            {
                layoutReleased = true;
                RectTransform parent = district.transform.parent as RectTransform;
                if (parent != null)
                {
                    LayoutRebuilder.MarkLayoutForRebuild(parent);
                }
            }

            PlaceLeft(toggle, district);
            PlaceLeft(stopToggle, toggle);
            PlaceLeft(techToggle, stopToggle);
        }

        private static void KeepOurPlace(Toggle item)
        {
            if (item == null)
            {
                return;
            }

            LayoutElement element = item.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = item.gameObject.AddComponent<LayoutElement>();
            }

            element.ignoreLayout = true;
        }

        private static void Glue(Toggle item)
        {
            if (item == null)
            {
                return;
            }

            RectTransform root = item.transform as RectTransform;
            RectTransform box = CheckBox(item, root);
            RectTransform label = FindLabel(root, box);
            if (root == null || box == null || label == null)
            {
                return;
            }

            StopLayout(label, root);
            float textWidth = PreferredWidth(label);
            float height = label.rect.height > 1f ? label.rect.height : box.rect.height;
            if (label.anchorMin != new Vector2(0.5f, 0.5f) || label.anchorMax != new Vector2(0.5f, 0.5f))
            {
                label.anchorMin = new Vector2(0.5f, 0.5f);
                label.anchorMax = new Vector2(0.5f, 0.5f);
            }

            if (label.pivot != new Vector2(1f, 0.5f))
            {
                label.pivot = new Vector2(1f, 0.5f);
            }

            float usedHeight = height > 1f ? height : 22f;
            if (Mathf.Abs(label.sizeDelta.x - textWidth) > 0.5f || Mathf.Abs(label.sizeDelta.y - usedHeight) > 0.5f)
            {
                label.sizeDelta = new Vector2(textWidth, usedHeight);
            }

            Align(label, true);

            float scale = Mathf.Abs(box.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }

            Vector3 edge = box.TransformPoint(new Vector3(box.rect.xMin, box.rect.center.y, 0f));
            edge.x -= 8f * scale;
            edge.z = label.position.z;
            if ((label.position - edge).sqrMagnitude > 0.25f)
            {
                label.position = edge;
            }
        }

        private static void StopLayout(RectTransform label, RectTransform root)
        {
            Transform cursor = label;
            while (cursor != null)
            {
                if (cursor == label)
                {
                    LayoutElement element = cursor.GetComponent<LayoutElement>();
                    if (element == null)
                    {
                        element = cursor.gameObject.AddComponent<LayoutElement>();
                    }

                    element.ignoreLayout = true;
                }

                if (cursor == root)
                {
                    HorizontalLayoutGroup row = cursor.GetComponent<HorizontalLayoutGroup>();
                    if (row != null)
                    {
                        row.enabled = false;
                    }

                    break;
                }

                cursor = cursor.parent;
            }
        }

        private static void PlaceLeft(Toggle item, Toggle rightNeighbor)
        {
            if (item == null || rightNeighbor == null)
            {
                return;
            }

            RectTransform rect = item.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            float scale = Mathf.Abs(rect.lossyScale.x);
            float gap = 14f * (scale < 0.0001f ? 1f : scale);
            float delta = ContentLeft(rightNeighbor) - gap - ContentRight(item);
            if (Mathf.Abs(delta) > 0.5f)
            {
                rect.position += new Vector3(delta, 0f, 0f);
            }
        }

        private static float ContentLeft(Toggle item)
        {
            return ContentEdge(item, true);
        }

        private static float ContentRight(Toggle item)
        {
            return ContentEdge(item, false);
        }

        private static float ContentEdge(Toggle item, bool left)
        {
            RectTransform root = item.transform as RectTransform;
            RectTransform box = CheckBox(item, root);
            RectTransform label = FindLabel(root, box);
            float edge = left ? float.MaxValue : float.MinValue;
            if (box != null)
            {
                edge = left ? Mathf.Min(edge, WorldLeft(box)) : Mathf.Max(edge, WorldRight(box));
            }

            if (label != null)
            {
                edge = left ? Mathf.Min(edge, WorldLeft(label)) : Mathf.Max(edge, WorldRight(label));
            }

            if (left && edge == float.MaxValue)
            {
                return root != null ? WorldLeft(root) : 0f;
            }

            if (!left && edge == float.MinValue)
            {
                return root != null ? WorldRight(root) : 0f;
            }

            return edge;
        }

        private static RectTransform CheckBox(Toggle item, RectTransform root)
        {
            if (item == null || root == null)
            {
                return null;
            }

            Image image = CheckBoxImage(item);
            if (image != null)
            {
                return image.rectTransform;
            }

            RectTransform mark = item.graphic != null ? item.graphic.rectTransform : null;
            if (mark != null && mark.rect.width >= 8f && mark.rect.width <= 56f && mark.rect.height <= 56f)
            {
                return mark;
            }

            return null;
        }

        private static Image CheckBoxImage(Toggle item)
        {
            if (item == null)
            {
                return null;
            }

            Image mark = item.graphic as Image;
            if (mark != null && mark.transform.parent is RectTransform parent && parent != item.transform)
            {
                Image background = parent.GetComponent<Image>();
                if (background != null &&
                    parent.rect.width >= 8f && parent.rect.width <= 56f &&
                    parent.rect.height >= 8f && parent.rect.height <= 56f)
                {
                    return background;
                }
            }

            Image target = item.targetGraphic as Image;
            if (target != null && target != mark)
            {
                Rect area = target.rectTransform.rect;
                if (area.width >= 8f && area.width <= 56f && area.height >= 8f && area.height <= 56f)
                {
                    return target;
                }
            }

            Image best = null;
            float bestArea = 1600f;
            Image[] images = item.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image == mark || image.GetComponent<TMP_Text>() != null)
                {
                    continue;
                }

                Rect area = image.rectTransform.rect;
                if (area.width < 8f || area.height < 8f || area.width > 56f || area.height > 56f)
                {
                    continue;
                }

                float size = area.width * area.height;
                if (size > 4f && size < bestArea)
                {
                    best = image;
                    bestArea = size;
                }
            }

            return best;
        }

        private static RectTransform FindLabel(RectTransform root, RectTransform box)
        {
            if (root == null)
            {
                return null;
            }

            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].rectTransform != box)
                {
                    return texts[i].rectTransform;
                }
            }

            Text[] legacy = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (legacy[i] != null && legacy[i].rectTransform != box)
                {
                    return legacy[i].rectTransform;
                }
            }

            return null;
        }

        private static float PreferredWidth(RectTransform label)
        {
            TMP_Text tmp = label.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                float width = tmp.GetPreferredValues(string.IsNullOrEmpty(tmp.text) ? " " : tmp.text).x;
                return Mathf.Clamp(width + 6f, 12f, 220f);
            }

            Text text = label.GetComponent<Text>();
            if (text != null)
            {
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                return Mathf.Max(8f, text.preferredWidth);
            }

            return Mathf.Max(8f, label.rect.width);
        }

        private static void Align(RectTransform label, bool textOnLeft)
        {
            TMP_Text tmp = label.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.alignment = textOnLeft ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            }

            Text text = label.GetComponent<Text>();
            if (text != null)
            {
                text.alignment = textOnLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            }
        }

        private static float WorldLeft(RectTransform rect)
        {
            return rect.TransformPoint(new Vector3(rect.rect.xMin, 0f, 0f)).x;
        }

        private static float WorldRight(RectTransform rect)
        {
            return rect.TransformPoint(new Vector3(rect.rect.xMax, 0f, 0f)).x;
        }

        private static void RememberEnabledLabel(GameObject root)
        {
            if (enabledLabelReady || root == null)
            {
                return;
            }

            TMP_Text tmp = root.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                enabledLabel = tmp.color;
                enabledLabelReady = true;
                return;
            }

            Text legacy = root.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                enabledLabel = legacy.color;
                enabledLabelReady = true;
            }
        }

        private static void Capture(Toggle item)
        {
            if (item == null)
            {
                return;
            }

            int id = item.GetInstanceID();
            if (Boxes.TryGetValue(id, out BoxState existing) && existing.Image != null)
            {
                return;
            }

            Image image = CheckBoxImage(item);
            if (image == null)
            {
                return;
            }
            Image mark = item.graphic as Image;
            Boxes[id] = new BoxState
            {
                Image = image,
                Sprite = image != null ? image.sprite : null,
                Color = image != null ? image.color : Color.white,
                Type = image != null ? image.type : Image.Type.Simple,
                Mark = mark,
                MarkColor = mark != null ? mark.color : Color.white
            };
        }

        private static void ApplyInteractable(Toggle item, bool enabled)
        {
            if (item == null)
            {
                return;
            }

            ColorBlock colors = item.colors;
            colors.disabledColor = colors.normalColor;
            colors.fadeDuration = 0f;
            item.colors = colors;
            item.interactable = enabled;

            Color ink = enabled ? enabledLabel : DisabledTint;
            foreach (TMP_Text text in item.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = ink;
            }

            foreach (Text text in item.GetComponentsInChildren<Text>(true))
            {
                text.color = ink;
            }

            PaintBox(item, enabled);
        }

        private static void PaintBox(Toggle item, bool enabled)
        {
            Capture(item);
            if (!Boxes.TryGetValue(item.GetInstanceID(), out BoxState state) || state.Image == null)
            {
                return;
            }

            if (enabled)
            {
                state.Image.sprite = state.Sprite;
                state.Image.type = state.Type;
                state.Image.color = state.Color;
                if (state.Mark != null)
                {
                    state.Mark.color = state.MarkColor;
                    state.Mark.canvasRenderer.SetAlpha(item.isOn ? 1f : 0f);
                }

                return;
            }

            state.Image.sprite = Solid();
            state.Image.type = Image.Type.Simple;
            state.Image.color = DisabledFill;
            if (state.Mark != null)
            {
                Color hidden = state.MarkColor;
                hidden.a = 0f;
                state.Mark.color = hidden;
                state.Mark.canvasRenderer.SetAlpha(0f);
            }
        }

        private static Sprite Solid()
        {
            if (solid != null)
            {
                return solid;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            solid = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            solid.hideFlags = HideFlags.HideAndDontSave;
            return solid;
        }

        private sealed class BoxState
        {
            public Image Image;
            public Sprite Sprite;
            public Color Color;
            public Image.Type Type;
            public Image Mark;
            public Color MarkColor;
        }

    }
}
