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
        private static Vector2 buttonSize = new Vector2(48f, 48f);
        private static Vector2 gridSpacing = new Vector2(6f, 6f);
        private static int gridColumns = 8;
        private static LayoutElement viewportLayout;
        private static float panelHeight = 320f;
        private static TMP_Text titleStyle;
        private static Material highlight;
        private static VisualizationPanel hookedPanel;
        private static int signature = int.MinValue;
        private static bool painting;
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
            ProducerFocus.Clear();
        }

        internal static void Dismiss()
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

            ProducerFocus.Clear();
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
            viewportLayout = viewport.gameObject.AddComponent<LayoutElement>();
            viewportLayout.flexibleHeight = 0f;
            viewportLayout.preferredHeight = 260f;
            Stretch(viewport);

            content = CreateChild("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            GridLayoutGroup layout = content.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = buttonSize;
            layout.spacing = gridSpacing;
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Mathf.Max(1, gridColumns);
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = false;
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
                if (Rows[i].Root != null)
                {
                    UnityEngine.Object.Destroy(Rows[i].Root);
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
            FitToContent();
        }

        private static List<ProductCount> Collect()
        {
            var counts = new Dictionary<string, ProductCount>();
            SeedRecipeProducts(counts);

            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings != null)
            {
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
                        AddProduct(counts, products[i], 1);
                    }
                }
            }

            var list = new List<ProductCount>(counts.Values);
            list.Sort(delegate(ProductCount left, ProductCount right)
            {
                return string.Compare(left.Resource.GetNameInGame(), right.Resource.GetNameInGame(), StringComparison.CurrentCulture);
            });
            return list;
        }

        private static void SeedRecipeProducts(Dictionary<string, ProductCount> counts)
        {
            Dictionary<string, BuildingDescription> descriptions = Old.GetBuildingModule()?.buildingDescriptions;
            if (descriptions == null)
            {
                return;
            }

            foreach (BuildingDescription description in descriptions.Values)
            {
                if (!(description?.GetCachedProductionLogic() is FactoryProductionLogic factory))
                {
                    continue;
                }

                List<ResourceCost> products = factory.productionDefinition?.producables;
                if (products == null)
                {
                    continue;
                }

                for (int i = 0; i < products.Count; i++)
                {
                    ResourceCost cost = products[i];
                    Resource resource = cost.resource;
                    if (resource == null && !string.IsNullOrEmpty(cost.resourceName))
                    {
                        resource = Resource.Get(cost.resourceName);
                    }

                    AddProduct(counts, resource, 0);
                }
            }
        }

        private static void AddProduct(Dictionary<string, ProductCount> counts, Resource resource, int amount)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name))
            {
                return;
            }

            if (!counts.TryGetValue(resource.name, out ProductCount count))
            {
                count = new ProductCount { Resource = resource, Count = 0 };
                counts.Add(resource.name, count);
            }

            count.Count += amount;
        }

        private static ProductRow CreateRow(ProductCount product)
        {
            RectTransform cell = CreateChild("Product", content);
            ToggleButton template = hookedPanel != null ? hookedPanel.buttonTemplate : null;
            ToggleButton toggle = template == null
                ? null
                : UnityEngine.Object.Instantiate(template, cell);
            if (toggle == null)
            {
                return new ProductRow { Root = cell.gameObject, Resource = product.Resource };
            }

            toggle.gameObject.SetActive(true);
            toggle.isEscapable = false;
            RectTransform buttonRect = toggle.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = buttonSize;
            buttonRect.localScale = Vector3.one;
            if (toggle.image != null)
            {
                toggle.image.sprite = product.Resource.GetSprite();
                toggle.image.preserveAspect = true;
                toggle.image.raycastTarget = false;
            }

            if (toggle.text != null)
            {
                toggle.text.gameObject.SetActive(false);
            }

            RectTransform countRect = CreateChild("Count", buttonRect);
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 0f);
            countRect.anchoredPosition = new Vector2(0f, 0f);
            countRect.sizeDelta = new Vector2(-2f, 24f);
            TextMeshProUGUI count = countRect.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyTextStyle(count, titleStyle, false);
            count.fontSize = Mathf.Clamp(buttonSize.y * 0.42f, 18f, 26f);
            count.alignment = TextAlignmentOptions.Bottom;
            count.overflowMode = TextOverflowModes.Overflow;
            count.raycastTarget = false;
            count.text = product.Count.ToString();

            TooltipHoverable tooltip = toggle.GetComponent<TooltipHoverable>();
            if (tooltip != null)
            {
                tooltip.Init(product.Resource.GetNameInGame(), product.Count.ToString());
            }

            Resource resource = product.Resource;
            toggle.onChanged.AddListener(delegate { SyncRow(toggle, resource); });
            return new ProductRow { Root = cell.gameObject, Toggle = toggle, Resource = resource };
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

        private static void SyncRow(ToggleButton toggle, Resource resource)
        {
            if (painting || toggle == null)
            {
                return;
            }

            if (toggle.isPressed)
            {
                selected = resource;
                painting = true;
                for (int i = 0; i < Rows.Count; i++)
                {
                    ToggleButton other = Rows[i].Toggle;
                    if (other != null && other != toggle && other.isPressed)
                    {
                        other.SetIsPressed(false, false);
                    }
                }

                painting = false;
            }
            else if (Same(selected, resource))
            {
                selected = null;
            }

            WorldScripts.Inst?.visualizationManager?.MarkDirty();
        }

        private static void PaintSelection()
        {
            painting = true;
            for (int i = 0; i < Rows.Count; i++)
            {
                ToggleButton toggle = Rows[i].Toggle;
                if (toggle == null)
                {
                    continue;
                }

                bool chosen = selected != null && Same(Rows[i].Resource, selected);
                if (toggle.isPressed != chosen)
                {
                    toggle.SetIsPressed(chosen, false);
                }
            }

            painting = false;
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
                Vector2 size = template.GetComponent<RectTransform>().sizeDelta;
                if (size.x < 8f || size.y < 8f)
                {
                    size = new Vector2(Mathf.Max(height, 48f), Mathf.Max(height, 48f));
                }

                buttonSize = size;
                MeasureGrid(panel, size);
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

        private static void MeasureGrid(VisualizationPanel panel, Vector2 size)
        {
            gridSpacing = new Vector2(6f, 6f);
            gridColumns = 0;
            RectTransform group = panel.buttonGroup != null ? panel.buttonGroup.GetComponent<RectTransform>() : null;
            GridLayoutGroup layout = group != null ? group.GetComponent<GridLayoutGroup>() : null;
            if (layout != null)
            {
                if (layout.cellSize.x >= 8f && layout.cellSize.y >= 8f)
                {
                    buttonSize = layout.cellSize;
                }

                gridSpacing = layout.spacing;
                if (layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount && layout.constraintCount > 0)
                {
                    gridColumns = layout.constraintCount;
                }
            }

            if (group != null && gridColumns <= 0)
            {
                ToggleButton[] buttons = group.GetComponentsInChildren<ToggleButton>(false);
                float rowY = 0f;
                bool hasRow = false;
                int columns = 0;
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] == panel.buttonTemplate)
                    {
                        continue;
                    }

                    float y = buttons[i].GetComponent<RectTransform>().anchoredPosition.y;
                    if (!hasRow)
                    {
                        rowY = y;
                        hasRow = true;
                        columns = 1;
                        continue;
                    }

                    if (Mathf.Abs(y - rowY) > 2f)
                    {
                        break;
                    }

                    columns++;
                }

                if (columns > 0)
                {
                    gridColumns = columns;
                }
            }

            if (gridColumns <= 0)
            {
                float width = panel.GetComponent<RectTransform>().rect.width - 20f;
                gridColumns = Mathf.Max(1, Mathf.FloorToInt((width + gridSpacing.x) / (size.x + gridSpacing.x)));
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
            rootRect.sizeDelta = new Vector2(width, panelHeight);
            rootRect.anchoredPosition = local + new Vector2(0f, -4f);
            root.transform.SetAsLastSibling();
        }

        private static void FitToContent()
        {
            int columns = Mathf.Max(1, gridColumns);
            int rows = Mathf.Max(1, (Rows.Count + columns - 1) / columns);
            float gridHeight = rows * buttonSize.y + Mathf.Max(0, rows - 1) * gridSpacing.y;
            float header = titleStyle != null ? titleStyle.fontSize + 6f : 28f;
            panelHeight = 18f + header + 12f + 3f + gridHeight;
            if (viewportLayout != null)
            {
                viewportLayout.preferredHeight = gridHeight;
            }
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
            internal GameObject Root;
            internal ToggleButton Toggle;
            internal Resource Resource;
        }
    }
}
