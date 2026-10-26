using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class ProducerList
    {
        private static readonly Color HighlightColor = new Color(0.22f, 0.86f, 0.38f, 1f);
        private static readonly Color FallbackDivider = new Color(0.35f, 0.78f, 0.95f, 1f);

        private static readonly List<ProductRow> Rows = new List<ProductRow>();
        private static Resource selected;
        private static GameObject root;
        private static RectTransform rootRect;
        private static RectTransform content;
        private static Sprite panelSprite;
        private static Color panelTint = Color.white;
        private static Material panelMaterial;
        private static Sprite rowSprite;
        private static Sprite rowSelectedSprite;
        private static Sprite rowHoverSprite;
        private static Color rowTint = Color.white;
        private static Sprite dividerSprite;
        private static Color dividerColor;
        private static float rowHeight = 40f;
        private static TMP_Text titleStyle;
        private static Material highlight;
        private static VisualizationPanel hookedPanel;
        private static int signature = int.MinValue;
        private static bool loggedFailure;

        internal static bool HasSelection
        {
            get { return selected != null; }
        }

        internal static void Tick()
        {
            try
            {
                VisualizationPanel panel = WorldScripts.Inst?.visualizationPanel;
                if (panel == null)
                {
                    return;
                }

                bool open = panel.gameObject.activeInHierarchy;
                if (!open)
                {
                    if (root != null)
                    {
                        root.SetActive(false);
                    }

                    if (selected != null)
                    {
                        ClearSelection();
                    }

                    return;
                }

                EnsureUi(panel);
                if (root == null)
                {
                    return;
                }

                root.SetActive(true);
                Place(panel);
                if (Time.frameCount % 30 == 0)
                {
                    Refresh();
                }
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
        }

        internal static void OnVisualizationButton()
        {
            ClearSelection();
        }

        internal static void ApplyHighlight(Building building, VisualType visualType, ref Material material)
        {
            if (selected == null || visualType != VisualType.Blank || building == null || material == null)
            {
                return;
            }

            if (!building.IsValidAndBuilt() || !Produces(building, selected))
            {
                return;
            }

            Material replacement = HighlightMaterial();
            if (replacement != null)
            {
                material = replacement;
            }
        }

        internal static bool OverrideVisualType(ref VisualType visualType)
        {
            if (selected == null)
            {
                return false;
            }

            if (visualType == VisualType.Selection || visualType == VisualType.CustomBuilding || IsTerraform(visualType))
            {
                return false;
            }

            visualType = VisualType.Blank;
            return true;
        }

        private static void Toggle(Resource resource)
        {
            if (resource == null)
            {
                return;
            }

            selected = selected == resource ? null : resource;
            PaintSelection();
            WorldScripts.Inst?.visualizationManager?.MarkDirty();
        }

        private static void ClearSelection()
        {
            if (selected == null)
            {
                return;
            }

            selected = null;
            PaintSelection();
            WorldScripts.Inst?.visualizationManager?.MarkDirty();
        }

        private static void EnsureUi(VisualizationPanel panel)
        {
            if (root != null)
            {
                Hook(panel);
                return;
            }

            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            CaptureStyle(panel);
            root = new GameObject("ProducerList", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            root.transform.SetParent(canvas.transform, false);
            rootRect = root.GetComponent<RectTransform>();
            Image background = root.GetComponent<Image>();
            ApplySprite(background, panelSprite, panelTint);
            background.material = panelMaterial;
            background.raycastTarget = true;
            VerticalLayoutGroup rootLayout = root.GetComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(10, 10, 8, 10);
            rootLayout.spacing = 6f;
            rootLayout.childAlignment = TextAnchor.UpperLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            CreateHeader(rootRect);
            CreateDivider(rootRect);

            RectTransform viewport = CreateChild("Viewport", rootRect);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            LayoutElement viewportLayout = viewport.gameObject.AddComponent<LayoutElement>();
            viewportLayout.flexibleHeight = 1f;
            viewportLayout.preferredHeight = 260f;
            Stretch(viewport);

            content = CreateChild("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            Hook(panel);
            Refresh();
        }

        private static void Hook(VisualizationPanel panel)
        {
            if (hookedPanel == panel || panel?.buttonGroup?.onButtonPressed == null)
            {
                return;
            }

            panel.buttonGroup.onButtonPressed.AddListener(OnVisualizationButton);
            hookedPanel = panel;
        }

        private static void Refresh()
        {
            if (content == null)
            {
                return;
            }

            List<ProductCount> products = Collect();
            int nextSignature = Signature(products);
            if (nextSignature == signature)
            {
                return;
            }

            signature = nextSignature;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Button != null)
                {
                    UnityEngine.Object.Destroy(Rows[i].Button.gameObject);
                }
            }

            Rows.Clear();
            bool stillThere = false;
            for (int i = 0; i < products.Count; i++)
            {
                Rows.Add(CreateRow(products[i]));
                if (selected != null && Same(products[i].Resource, selected))
                {
                    selected = products[i].Resource;
                    stillThere = true;
                }
            }

            if (selected != null && !stillThere)
            {
                selected = null;
                WorldScripts.Inst?.visualizationManager?.MarkDirty();
            }

            PaintSelection();
        }

        private static List<ProductCount> Collect()
        {
            var counts = new Dictionary<string, ProductCount>();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return new List<ProductCount>();
            }

            foreach (Building building in buildings)
            {
                if (building == null || !building.IsValidAndBuilt())
                {
                    continue;
                }

                if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory))
                {
                    continue;
                }

                List<Resource> products = factory.GetProducables();
                if (products == null)
                {
                    continue;
                }

                for (int i = 0; i < products.Count; i++)
                {
                    Resource resource = products[i];
                    if (resource == null || string.IsNullOrEmpty(resource.name))
                    {
                        continue;
                    }

                    if (!counts.TryGetValue(resource.name, out ProductCount count))
                    {
                        count = new ProductCount { Resource = resource, Count = 0 };
                        counts.Add(resource.name, count);
                    }

                    count.Count++;
                }
            }

            var list = new List<ProductCount>(counts.Values);
            list.Sort(delegate(ProductCount left, ProductCount right)
            {
                return string.Compare(left.Resource.GetNameInGame(), right.Resource.GetNameInGame(), StringComparison.CurrentCulture);
            });
            return list;
        }

        private static ProductRow CreateRow(ProductCount product)
        {
            RectTransform row = CreateChild("Row", content);
            Image image = row.gameObject.AddComponent<Image>();
            ApplySprite(image, rowSprite, rowTint);
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState sprites = button.spriteState;
            sprites.highlightedSprite = rowHoverSprite;
            sprites.pressedSprite = rowSelectedSprite;
            sprites.selectedSprite = rowSelectedSprite;
            button.spriteState = sprites;
            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = rowHeight;
            layout.minHeight = rowHeight;

            float iconSize = Mathf.Clamp(rowHeight - 12f, 22f, 32f);
            RectTransform icon = CreateChild("Icon", row);
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.anchoredPosition = new Vector2(8f, 0f);
            icon.sizeDelta = new Vector2(iconSize, iconSize);
            Image iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.sprite = product.Resource.GetSprite();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            RectTransform label = CreateChild("Label", row);
            label.anchorMin = new Vector2(0f, 0f);
            label.anchorMax = new Vector2(1f, 1f);
            label.offsetMin = new Vector2(iconSize + 16f, 2f);
            label.offsetMax = new Vector2(-10f, -2f);
            TextMeshProUGUI text = label.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyTextStyle(text, titleStyle, false);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = product.Resource.GetNameInGame() + "  " + product.Count;

            Resource resource = product.Resource;
            button.onClick.AddListener(delegate { Toggle(resource); });
            return new ProductRow { Button = button, Background = image, Resource = resource };
        }

        private static void CreateHeader(RectTransform parent)
        {
            RectTransform header = CreateChild("Header", parent);
            LayoutElement layout = header.gameObject.AddComponent<LayoutElement>();
            float size = titleStyle != null ? titleStyle.fontSize : 22f;
            layout.preferredHeight = size + 6f;
            layout.minHeight = size + 6f;
            TextMeshProUGUI text = header.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyTextStyle(text, titleStyle, true);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            text.text = "生产建筑";
        }

        private static void CreateDivider(RectTransform parent)
        {
            RectTransform line = CreateChild("Divider", parent);
            LayoutElement layout = line.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 3f;
            layout.minHeight = 3f;
            Image image = line.gameObject.AddComponent<Image>();
            ApplySprite(image, dividerSprite, dividerColor);
            image.raycastTarget = false;
        }

        private static void PaintSelection()
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                ProductRow row = Rows[i];
                if (row.Background == null || row.Button == null)
                {
                    continue;
                }

                bool chosen = selected != null && Same(row.Resource, selected);
                row.Button.transition = chosen ? Selectable.Transition.None : Selectable.Transition.SpriteSwap;
                ApplySprite(row.Background, chosen ? rowSelectedSprite ?? rowSprite : rowSprite, rowTint);
            }
        }

        private static void CaptureStyle(VisualizationPanel panel)
        {
            Image background = FindPanelBackground(panel);
            if (background != null)
            {
                panelSprite = background.sprite;
                panelTint = background.color;
                panelMaterial = background.material;
            }

            ToggleButton template = panel.buttonTemplate;
            Image frame = template != null ? template.GetComponent<Image>() : null;
            if (template != null)
            {
                rowSprite = template.buttonDefault != null ? template.buttonDefault : frame?.sprite;
                rowSelectedSprite = template.buttonDown != null ? template.buttonDown : rowSprite;
                rowHoverSprite = template.buttonHighlighted != null ? template.buttonHighlighted : rowSprite;
                if (frame != null)
                {
                    rowTint = frame.color;
                }

                float height = template.GetComponent<RectTransform>().rect.height;
                if (height >= 28f && height <= 72f)
                {
                    rowHeight = height;
                }
            }

            titleStyle = FindTitle(panel);
            Image divider = FindDivider(panel);
            if (divider != null)
            {
                dividerSprite = divider.sprite;
                dividerColor = divider.color;
            }
            else
            {
                dividerSprite = null;
                dividerColor = FallbackDivider;
            }
        }

        private static Image FindPanelBackground(VisualizationPanel panel)
        {
            Image own = panel.GetComponent<Image>();
            if (own != null && own.sprite != null)
            {
                return own;
            }

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Image parentImage = panel.transform.parent != null ? panel.transform.parent.GetComponent<Image>() : null;
            if (parentImage != null && parentImage.sprite != null && panelRect != null)
            {
                Rect parent = parentImage.rectTransform.rect;
                Rect current = panelRect.rect;
                if (parent.width <= current.width * 1.4f && parent.height <= current.height * 2.5f)
                {
                    return parentImage;
                }
            }

            return own;
        }

        private static TMP_Text FindTitle(VisualizationPanel panel)
        {
            TMP_Text best = null;
            foreach (TMP_Text text in Enumerate<TMP_Text>(panel))
            {
                if (text.GetComponentInParent<ToggleButton>() != null)
                {
                    continue;
                }

                if (best == null || text.fontSize > best.fontSize)
                {
                    best = text;
                }
            }

            return best;
        }

        private static Image FindDivider(VisualizationPanel panel)
        {
            Image best = null;
            float bestWidth = 0f;
            foreach (Image image in Enumerate<Image>(panel))
            {
                if (image.GetComponent<ToggleButton>() != null || image.GetComponentInParent<ToggleButton>() != null)
                {
                    continue;
                }

                Rect rect = image.rectTransform.rect;
                if (rect.height <= 0f || rect.height > 8f || rect.width < 40f || rect.width <= bestWidth)
                {
                    continue;
                }

                best = image;
                bestWidth = rect.width;
            }

            return best;
        }

        private static IEnumerable<T> Enumerate<T>(VisualizationPanel panel) where T : Component
        {
            foreach (T item in panel.GetComponentsInChildren<T>(true))
            {
                yield return item;
            }

            Transform parent = panel.transform.parent;
            if (parent == null)
            {
                yield break;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == panel.transform)
                {
                    continue;
                }

                foreach (T item in child.GetComponentsInChildren<T>(true))
                {
                    yield return item;
                }
            }
        }

        private static void ApplySprite(Image image, Sprite sprite, Color tint)
        {
            image.sprite = sprite;
            image.color = sprite == null && tint.a <= 0f ? FallbackDivider : tint;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        }

        private static void ApplyTextStyle(TMP_Text text, TMP_Text sample, bool title)
        {
            if (sample != null)
            {
                text.font = sample.font;
                text.fontSharedMaterial = sample.fontSharedMaterial;
                text.color = sample.color;
                text.fontStyle = sample.fontStyle;
                text.fontSize = title ? sample.fontSize : Mathf.Max(15f, sample.fontSize - 2f);
                text.characterSpacing = sample.characterSpacing;
            }
            else
            {
                text.color = Color.white;
                text.fontSize = title ? 22f : 18f;
            }

            text.enableWordWrapping = false;
        }

        private static void Place(VisualizationPanel panel)
        {
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (panelRect == null || canvas == null || rootRect == null)
            {
                return;
            }

            RectTransform canvasRect = canvas.transform as RectTransform;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            panelRect.GetWorldCorners(corners);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    RectTransformUtility.WorldToScreenPoint(camera, corners[0]),
                    camera,
                    out Vector2 local))
            {
                return;
            }

            float width = Mathf.Max(panelRect.rect.width, 240f);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.sizeDelta = new Vector2(width, 320f);
            rootRect.anchoredPosition = local + new Vector2(0f, -4f);
            root.transform.SetAsLastSibling();
        }

        private static bool Produces(Building building, Resource resource)
        {
            if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory))
            {
                return false;
            }

            List<Resource> products = factory.GetProducables();
            if (products == null)
            {
                return false;
            }

            for (int i = 0; i < products.Count; i++)
            {
                if (Same(products[i], resource))
                {
                    return true;
                }
            }

            return false;
        }

        private static Material HighlightMaterial()
        {
            if (highlight != null)
            {
                return highlight;
            }

            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                return WorldScripts.Inst?.visualizationManager?.selectableDefaultMat;
            }

            highlight = new Material(shader);
            highlight.hideFlags = HideFlags.HideAndDontSave;
            highlight.enableInstancing = true;
            highlight.color = HighlightColor;
            return highlight;
        }

        private static bool Same(Resource left, Resource right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left == right || left.name == right.name;
        }

        private static bool IsTerraform(VisualType visualType)
        {
            return visualType == VisualType.TerraformDustParticleFiltering
                || visualType == VisualType.TerraformFertilization
                || visualType == VisualType.TerraformGroundAcidity
                || visualType == VisualType.TerraformAmmoniaExtraction
                || visualType == VisualType.TerraformMagneticField
                || visualType == VisualType.TerraformProgress;
        }

        private static int Signature(List<ProductCount> products)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < products.Count; i++)
                {
                    hash = (hash * 397) ^ products[i].Resource.name.GetHashCode();
                    hash = (hash * 397) ^ products[i].Count;
                }

                return hash;
            }
        }

        private static RectTransform CreateChild(string name, RectTransform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void LogFailure(Exception exception)
        {
            if (loggedFailure)
            {
                return;
            }

            loggedFailure = true;
            Debug.LogWarning("[InfraSpaceProductionInsights] Producer list skipped: " + exception.Message);
        }

        private sealed class ProductCount
        {
            internal Resource Resource;
            internal int Count;
        }

        private struct ProductRow
        {
            internal Button Button;
            internal Image Background;
            internal Resource Resource;
        }
    }
}
