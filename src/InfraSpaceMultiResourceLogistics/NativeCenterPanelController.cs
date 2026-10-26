using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InfraSpaceMultiResourceLogistics
{
    internal sealed class NativeCenterPanelController : MonoBehaviour, ICameraScrollBlocker
    {
        private static readonly FieldInfo CollectionCenterField =
            AccessTools.Field(typeof(CollectionInfoPanel), "collectionCenter");

        private static readonly FieldInfo DistributionCenterField =
            AccessTools.Field(typeof(DistributionInfoPanel), "distributionCenter");

        private static readonly Color RoadWarningColor = new Color(0.86f, 0.16f, 0.14f, 1f);

        private static readonly Color StorageOnlyColor = new Color(0.42f, 0.42f, 0.44f, 1f);

        private static readonly Dictionary<int, Sprite> grayscaleSprites = new Dictionary<int, Sprite>();

        private static readonly Dictionary<int, Texture2D> readableTextures = new Dictionary<int, Texture2D>();

        private static object pendingReopenCenter;
        private static int pendingReopenFrames;

        private readonly List<GameObject> resourceChips = new List<GameObject>();

        private CollectionInfoPanel collectionPanel;
        private DistributionInfoPanel distributionPanel;
        private ItemPickerButton picker;
        private TMP_Text storageTextTemplate;
        private Image storageImageTemplate;
        private Slider storageSliderTemplate;
        private RectTransform widthReference;
        private Scrollbar nativeScrollbarTemplate;
        private Sprite sliderBackgroundSprite;
        private Sprite sliderFillSprite;
        private Sprite sliderHandleSprite;
        private Color sliderBackgroundColor = Color.white;
        private Color sliderFillColor = Color.white;
        private Color sliderHandleColor = Color.white;
        private TMP_Text capacityTextTemplate;
        private RectTransform iconRow;
        private RectTransform iconRowSpacer;
        private RectTransform[] storageBoundsControls;
        private RectTransform resourceHeaderLabel;
        private RectTransform upstreamLabel;
        private RectTransform linkedCentersLabel;
        private RectTransform storageSpacer;
        private TMP_Text plusText;
        private readonly Dictionary<int, Vector2> homeAnchored = new Dictionary<int, Vector2>();
        private bool nativeLayoutFrozen;
        private bool radiusPlaced;
        private bool upstreamLifted;
        private bool labelsReady;
        private Vector2 storageScroll;
        private string storageHoverTip;
        private float scrollbarDragOffset;
        private int activeSliderControlId;
        private int activeScrollbarControlId;
        private long displayedBuildingId = -1;
        private string displayedResources = "";

        internal static void Attach(CollectionInfoPanel panel)
        {
            NativeCenterPanelController controller =
                panel.gameObject.GetComponent<NativeCenterPanelController>() ??
                panel.gameObject.AddComponent<NativeCenterPanelController>();
            controller.collectionPanel = panel;
            controller.Configure(
                panel.resourcePickerButton,
                panel.storageText,
                panel.storageResourceImage,
                panel.maxStorageSlider,
                panel.maxStorageText,
                panel.collectionRadiusSlider);
        }

        internal static void Attach(DistributionInfoPanel panel)
        {
            NativeCenterPanelController controller =
                panel.gameObject.GetComponent<NativeCenterPanelController>() ??
                panel.gameObject.AddComponent<NativeCenterPanelController>();
            controller.distributionPanel = panel;
            controller.Configure(
                panel.resourcePickerButton,
                panel.storageText,
                panel.storageResourceImage,
                panel.maxStorageSlider,
                panel.maxStorageText,
                panel.distributionRadiusSlider);
        }

        internal static void RefreshFor(ItemPickerButton picker)
        {
            picker?.GetComponentInParent<NativeCenterPanelController>()?.Refresh();
        }

        internal static void ProcessPendingReopen()
        {
            if (pendingReopenCenter == null || --pendingReopenFrames > 0 || WorldScripts.Inst == null)
            {
                return;
            }

            object center = pendingReopenCenter;
            pendingReopenCenter = null;
            if (center is CollectionProductionLogic collection)
            {
                CollectionInfoPanel panel = WorldScripts.Inst.collectionInfoPanel;
                object displayed = CollectionCenterField.GetValue(panel);
                if (!panel.gameObject.activeInHierarchy || displayed != center)
                {
                    panel.ShowForCollectionCenter(collection);
                }
            }
            else if (center is DistributionProductionLogic distribution)
            {
                DistributionInfoPanel panel = WorldScripts.Inst.distributionInfoPanel;
                object displayed = DistributionCenterField.GetValue(panel);
                if (!panel.gameObject.activeInHierarchy || displayed != center)
                {
                    panel.ShowForDistributionCenter(distribution);
                }
            }
        }

        internal void Refresh()
        {
            object center = GetCenter();
            if (center == null || picker == null)
            {
                return;
            }

            IReadOnlyList<Resource> resources = GetResources(center);
            displayedBuildingId = GetConsumerProducer(center).building.GetID();
            displayedResources = string.Join("|", resources.Select(resource => resource.name));
            RebuildResourceChips(resources);
            SetPlusVisual();
            ApplyRoadWarnings();
        }

        internal void RequestRemove(Resource resource)
        {
            object center = GetCenter();
            if (center == null)
            {
                return;
            }

            pendingReopenCenter = center;
            pendingReopenFrames = 2;
            IReadOnlyList<Resource> resources = GetResources(center);
            if (resources.Count <= 1 || !resources.Contains(resource))
            {
                return;
            }

            ConsumerProducer consumerProducer = GetConsumerProducer(center);
            int storage = GetStorage(consumerProducer, resource);
            int inTransit = consumerProducer.GetIncomingCarsResourceAmount(resource);
            if (storage > 0 || inTransit > 0)
            {
                MessageConfig confirmation = new MessageConfig(
                    "移除物资",
                    $"“{resource.GetNameInGame()}”仍有库存 {storage}、在途 {inTransit}。移除后这些物资将无法由该中心继续处理，确定要移除吗？",
                    null,
                    panel =>
                    {
                        panel.Close();
                        RemoveResource(resource);
                    },
                    "取消",
                    "确认移除",
                    true);
                confirmation.Show();
                return;
            }

            RemoveResource(resource);
        }

        internal void ToggleStorage(Resource resource)
        {
            object center = GetCenter();
            if (center == null || resource == null)
            {
                return;
            }

            if (center is CollectionProductionLogic collection)
            {
                CenterState.SetStorageOnly(collection, resource, !CenterState.IsStorageOnly(collection, resource));
            }
            else if (center is DistributionProductionLogic distribution)
            {
                CenterState.SetStorageOnly(distribution, resource, !CenterState.IsStorageOnly(distribution, resource));
            }

            ApplyRoadWarnings();
        }

        private void Configure(
            ItemPickerButton resourcePicker,
            TMP_Text storageText,
            Image storageImage,
            Slider storageSlider,
            TMP_Text capacityText,
            Slider panelWidthSlider)
        {
            if (picker != null)
            {
                return;
            }

            picker = resourcePicker;
            storageTextTemplate = storageText;
            storageImageTemplate = storageImage;
            storageSliderTemplate = storageSlider;
            capacityTextTemplate = capacityText;
            widthReference = panelWidthSlider.GetComponent<RectTransform>();
            CacheNativeControlStyle(panelWidthSlider);
            CreateIconRow();
            CreateStorageList();
            EnsurePanelLabels();
            if (!WorldScripts.Inst.cameraMovement.cameraScrollBlockers.Contains(this))
            {
                WorldScripts.Inst.cameraMovement.cameraScrollBlockers.Add(this);
            }
        }

        private void CacheNativeControlStyle(Slider panelWidthSlider)
        {
            Image background = panelWidthSlider.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(image => image.transform.name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0);
            Image fill = panelWidthSlider.fillRect == null
                ? null
                : panelWidthSlider.fillRect.GetComponent<Image>();
            Image handle = panelWidthSlider.handleRect == null
                ? null
                : panelWidthSlider.handleRect.GetComponent<Image>();
            sliderBackgroundSprite = background?.sprite;
            sliderFillSprite = fill?.sprite;
            sliderHandleSprite = handle?.sprite;
            sliderBackgroundColor = background?.color ?? Color.white;
            sliderFillColor = fill?.color ?? Color.white;
            sliderHandleColor = handle?.color ?? Color.white;

            nativeScrollbarTemplate = GetComponentsInChildren<Scrollbar>(true)
                .FirstOrDefault(scrollbar =>
                    scrollbar.direction == Scrollbar.Direction.TopToBottom ||
                    scrollbar.direction == Scrollbar.Direction.BottomToTop);
        }

        private void CreateIconRow()
        {
            RectTransform pickerRect = picker.GetComponent<RectTransform>();
            Transform originalParent = pickerRect.parent;
            GameObject rowObject = new GameObject("MultiResourceIconRow", typeof(RectTransform));
            iconRow = rowObject.GetComponent<RectTransform>();
            iconRow.SetParent(originalParent, false);
            iconRow.anchorMin = pickerRect.anchorMin;
            iconRow.anchorMax = pickerRect.anchorMax;
            float pickerHeight = pickerRect.rect.height > 1f ? pickerRect.rect.height : pickerRect.sizeDelta.y;
            float pickerTop = pickerRect.anchoredPosition.y + (1f - pickerRect.pivot.y) * pickerHeight;
            iconRow.pivot = new Vector2(0f, 1f);
            iconRow.anchoredPosition = new Vector2(pickerRect.anchoredPosition.x, pickerTop);
            iconRow.sizeDelta = new Vector2(Mathf.Max(260f, pickerRect.sizeDelta.x), 46f);
            iconRow.SetSiblingIndex(pickerRect.GetSiblingIndex());

            pickerRect.SetParent(iconRow, false);
            pickerRect.anchorMin = new Vector2(0f, 0.5f);
            pickerRect.anchorMax = new Vector2(0f, 0.5f);
            pickerRect.pivot = new Vector2(0.5f, 0.5f);
            pickerRect.sizeDelta = new Vector2(46f, 46f);
            LayoutElement pickerLayout = picker.gameObject.GetComponent<LayoutElement>() ??
                                         picker.gameObject.AddComponent<LayoutElement>();
            pickerLayout.preferredWidth = 46f;
            pickerLayout.preferredHeight = 46f;

            plusText = Instantiate(capacityTextTemplate, picker.transform);
            plusText.gameObject.name = "AddResourceText";
            plusText.text = "+";
            plusText.alignment = TextAlignmentOptions.Center;
            plusText.color = Color.white;
            plusText.enableAutoSizing = false;
            plusText.fontSize = 30f;
            plusText.fontStyle = FontStyles.Bold;
            plusText.raycastTarget = false;
            RectTransform plusRect = plusText.rectTransform;
            plusRect.anchorMin = Vector2.zero;
            plusRect.anchorMax = Vector2.one;
            plusRect.offsetMin = Vector2.zero;
            plusRect.offsetMax = Vector2.zero;
        }

        private void CreateStorageList()
        {
            storageBoundsControls = new[]
            {
                storageTextTemplate.rectTransform,
                storageImageTemplate.rectTransform,
                storageSliderTemplate.GetComponent<RectTransform>(),
                capacityTextTemplate.rectTransform
            };
            storageTextTemplate.gameObject.SetActive(false);
            storageImageTemplate.gameObject.SetActive(false);
            storageSliderTemplate.gameObject.SetActive(false);
            capacityTextTemplate.gameObject.SetActive(false);
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.text != null && text.text.Contains("最大存储"))
                {
                    text.gameObject.SetActive(false);
                }
            }
        }

        private void EnsurePanelLabels()
        {
            HideNativeStorageWidgets();
            if (labelsReady)
            {
                return;
            }

            bool foundHeader = resourceHeaderLabel != null;
            bool foundStorage = false;
            bool foundUpstream = upstreamLabel != null;
            bool foundLinked = linkedCentersLabel != null;
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text == plusText)
                {
                    continue;
                }

                string value = (text.text ?? "").Trim();
                if (value == "存储:" || value == "存储：")
                {
                    text.gameObject.SetActive(false);
                    foundStorage = true;
                }
                else if (value.Contains("最大存储"))
                {
                    text.gameObject.SetActive(false);
                }
                else if (value.Contains("收集资源") || value.Contains("资源分配"))
                {
                    resourceHeaderLabel = text.rectTransform;
                    foundHeader = true;
                }
                else if (value.Contains("互联供应方") || value.Contains("互联消费者"))
                {
                    if (!upstreamLifted)
                    {
                        text.rectTransform.anchoredPosition += new Vector2(0f, 10f);
                        upstreamLifted = true;
                    }

                    upstreamLabel = text.rectTransform;
                    foundUpstream = true;
                }
                else if (value.Contains("互联配送中心") || value.Contains("互联收集中心"))
                {
                    linkedCentersLabel = text.rectTransform;
                    foundLinked = true;
                }
            }

            labelsReady = foundHeader && foundStorage && foundUpstream && foundLinked;
        }

        private void HideNativeStorageWidgets()
        {
            if (storageTextTemplate != null)
            {
                storageTextTemplate.gameObject.SetActive(false);
            }

            if (storageImageTemplate != null)
            {
                storageImageTemplate.gameObject.SetActive(false);
            }

            if (storageSliderTemplate != null)
            {
                storageSliderTemplate.gameObject.SetActive(false);
            }

            if (capacityTextTemplate != null)
            {
                capacityTextTemplate.gameObject.SetActive(false);
            }
        }

        private void AlignResourceHeader()
        {
            if (resourceHeaderLabel == null || iconRow == null)
            {
                return;
            }

            RectTransform panel = (RectTransform)transform;
            if (panel.rect.width < 80f)
            {
                return;
            }

            if (resourceHeaderLabel.parent != iconRow)
            {
                resourceHeaderLabel.SetParent(iconRow, false);
            }

            resourceHeaderLabel.SetAsFirstSibling();
            TMP_Text label = resourceHeaderLabel.GetComponent<TMP_Text>();
            float labelWidth = 72f;
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                label.ForceMeshUpdate();
                labelWidth = Mathf.Max(24f, Mathf.Ceil(label.preferredWidth));
            }

            resourceHeaderLabel.sizeDelta = new Vector2(labelWidth, Mathf.Max(24f, resourceHeaderLabel.sizeDelta.y));
            LayoutElement labelLayout = resourceHeaderLabel.GetComponent<LayoutElement>() ??
                                        resourceHeaderLabel.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = labelWidth;
            labelLayout.minWidth = labelWidth;
            labelLayout.preferredHeight = resourceHeaderLabel.sizeDelta.y;
            labelLayout.flexibleWidth = 0f;

            PinIconRowToPanelLeft(panel);
            LayoutWrappedIcons(Mathf.Max(46f, iconRow.sizeDelta.x), labelWidth, 0f);
            if (iconRowSpacer != null)
            {
                LayoutElement spacerLayout = iconRowSpacer.GetComponent<LayoutElement>();
                float rowHeight = iconRow.sizeDelta.y;
                spacerLayout.minHeight = rowHeight;
                spacerLayout.preferredHeight = rowHeight;
                iconRowSpacer.sizeDelta = new Vector2(iconRowSpacer.sizeDelta.x, rowHeight);
            }
        }

        private void PinIconRowToPanelLeft(RectTransform panel)
        {
            const float margin = 10f;
            RectTransform parent = iconRow.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (iconRowSpacer == null)
            {
                GameObject spacerObject = new GameObject(
                    "MultiResourceIconSpacer",
                    typeof(RectTransform),
                    typeof(LayoutElement));
                iconRowSpacer = spacerObject.GetComponent<RectTransform>();
                iconRowSpacer.SetParent(parent, false);
                iconRowSpacer.SetSiblingIndex(iconRow.GetSiblingIndex());
                LayoutElement created = iconRowSpacer.GetComponent<LayoutElement>();
                created.flexibleWidth = 1f;
                created.flexibleHeight = 0f;
            }

            LayoutElement rowLayout = iconRow.GetComponent<LayoutElement>() ?? iconRow.gameObject.AddComponent<LayoutElement>();
            rowLayout.ignoreLayout = true;

            Camera camera = PanelCamera();
            float topY = ScreenEdge(iconRow, camera, true);
            if (iconRowSpacer.rect.height > 8f)
            {
                topY = ScreenEdge(iconRowSpacer, camera, true);
            }

            float width = Mathf.Max(160f, panel.rect.width - margin * 2f);
            float height = iconRow.sizeDelta.y > 1f ? iconRow.sizeDelta.y : 46f;
            iconRow.anchorMin = new Vector2(0f, 1f);
            iconRow.anchorMax = new Vector2(0f, 1f);
            iconRow.pivot = new Vector2(0f, 1f);
            iconRow.sizeDelta = new Vector2(width, height);

            if (iconRow.parent != panel)
            {
                iconRow.SetParent(panel, true);
            }

            Vector3 leftWorld = panel.TransformPoint(new Vector3(panel.rect.xMin + margin, panel.rect.yMax, 0f));
            float leftX = RectTransformUtility.WorldToScreenPoint(camera, leftWorld).x;
            MoveToScreen(iconRow, camera, new Vector2(leftX, topY));
        }

        private static float GlyphBottomGui(TMP_Text text, Camera camera)
        {
            if (text == null)
            {
                return -1f;
            }

            text.ForceMeshUpdate();
            Bounds bounds = text.textBounds;
            if (bounds.size.y < 1f)
            {
                return -1f;
            }

            Vector3 world = text.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, 0f));
            float screenY = RectTransformUtility.WorldToScreenPoint(camera, world).y;
            return Screen.height - screenY;
        }

        private void LayoutWrappedIcons(float rowWidth, float labelWidth, float leftPad)
        {
            const float spacing = 5f;
            const float preferredChip = 46f;
            const int maxRows = 3;
            float firstRowStart = leftPad + (labelWidth > 0f ? labelWidth + 6f : 0f);
            var items = new List<RectTransform>(resourceChips.Count + 1);
            foreach (GameObject chip in resourceChips)
            {
                if (chip == null)
                {
                    continue;
                }

                items.Add(chip.GetComponent<RectTransform>());
            }

            if (picker != null)
            {
                items.Add(picker.GetComponent<RectTransform>());
            }

            float chipSize = preferredChip;
            int rows = CountIconRows(items.Count, chipSize, rowWidth, firstRowStart, leftPad, spacing);
            while (rows > maxRows && chipSize > 28f)
            {
                chipSize = Mathf.Max(28f, chipSize - 2f);
                rows = CountIconRows(items.Count, chipSize, rowWidth, firstRowStart, leftPad, spacing);
            }

            KeepIconRowTopPivot();
            float cursor = firstRowStart;
            float yFromTop = 0f;
            bool firstRow = true;
            foreach (RectTransform item in items)
            {
                if (item == null)
                {
                    continue;
                }

                float rowStart = firstRow ? firstRowStart : leftPad;
                if (cursor > rowStart + 0.5f && cursor + chipSize > rowWidth + 0.5f)
                {
                    firstRow = false;
                    cursor = leftPad;
                    yFromTop += chipSize + spacing;
                }

                PlaceTopLeft(item, cursor, yFromTop, chipSize, chipSize);
                SetChipSize(item, item.GetComponent<LayoutElement>(), chipSize);
                cursor += chipSize + spacing;
            }

            if (resourceHeaderLabel != null)
            {
                float labelHeight = Mathf.Max(24f, resourceHeaderLabel.sizeDelta.y);
                PlaceTopLeft(
                    resourceHeaderLabel,
                    leftPad,
                    Mathf.Max(0f, (chipSize - labelHeight) * 0.5f),
                    labelWidth,
                    labelHeight);
            }

            float usedHeight = Mathf.Max(preferredChip, yFromTop + chipSize);
            float maxHeight = maxRows * preferredChip + (maxRows - 1) * spacing;
            float height = Mathf.Min(usedHeight, maxHeight);
            iconRow.sizeDelta = new Vector2(iconRow.sizeDelta.x, height);
            LayoutElement rowLayout = iconRow.GetComponent<LayoutElement>() ?? iconRow.gameObject.AddComponent<LayoutElement>();
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleHeight = 0f;
            rowLayout.flexibleWidth = 0f;
        }

        private static int CountIconRows(
            int count,
            float chipSize,
            float rowWidth,
            float firstRowStart,
            float leftPad,
            float spacing)
        {
            if (count <= 0)
            {
                return 1;
            }

            int firstCapacity = Mathf.Max(
                1,
                Mathf.FloorToInt((rowWidth - firstRowStart + spacing) / (chipSize + spacing)));
            int remaining = count - firstCapacity;
            if (remaining <= 0)
            {
                return 1;
            }

            int nextCapacity = Mathf.Max(
                1,
                Mathf.FloorToInt((rowWidth - leftPad + spacing) / (chipSize + spacing)));
            return 1 + Mathf.CeilToInt(remaining / (float)nextCapacity);
        }

        private void KeepIconRowTopPivot()
        {
            if (iconRow == null || iconRow.pivot.y >= 0.999f)
            {
                return;
            }

            float height = iconRow.rect.height > 1f ? iconRow.rect.height : iconRow.sizeDelta.y;
            float top = iconRow.anchoredPosition.y + (1f - iconRow.pivot.y) * height;
            iconRow.pivot = new Vector2(0f, 1f);
            iconRow.anchoredPosition = new Vector2(iconRow.anchoredPosition.x, top);
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float yFromTop, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -yFromTop);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetChipSize(RectTransform rect, LayoutElement layout, float size)
        {
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(size, size);
            }

            if (layout == null)
            {
                return;
            }

            layout.preferredWidth = size;
            layout.preferredHeight = size;
        }

        private void RebuildResourceChips(IReadOnlyList<Resource> resources)
        {
            foreach (GameObject chip in resourceChips)
            {
                chip.SetActive(false);
                Destroy(chip);
            }

            resourceChips.Clear();
            foreach (Resource resource in resources)
            {
                GameObject chip = CreateResourceChip(resource);
                resourceChips.Add(chip);
            }

            picker.transform.SetAsLastSibling();
            AlignResourceHeader();
        }

        private void ApplyRoadWarnings()
        {
            object center = GetCenter();
            if (center == null)
            {
                return;
            }

            IReadOnlyList<Resource> resources = GetResources(center);
            int count = Math.Min(resourceChips.Count, resources.Count);
            for (int i = 0; i < count; i++)
            {
                GameObject chip = resourceChips[i];
                if (chip == null)
                {
                    continue;
                }

                List<string> partners = PartnerNames(center, resources[i]);
                bool storageOnly = IsStorageOnly(center, resources[i]);
                bool missing = partners.Count == 0;
                bool blocked = !storageOnly && !missing && IsRoadBlocked(center, resources[i]);
                bool warning = !storageOnly && (missing || blocked);
                string tip = DescribeResourceTip(center, partners, storageOnly, missing, blocked);
                ResourceChipButton button = chip.GetComponent<ResourceChipButton>();
                if (button != null &&
                    button.roadBlocked == warning &&
                    button.storageOnly == storageOnly &&
                    button.linkTip == tip)
                {
                    continue;
                }

                if (button != null)
                {
                    button.roadBlocked = warning;
                    button.storageOnly = storageOnly;
                    button.linkTip = tip;
                }

                Image background = chip.GetComponent<Image>();
                if (background != null)
                {
                    background.color = storageOnly
                        ? StorageOnlyColor
                        : warning ? RoadWarningColor : Color.white;
                }

                Image icon = chip.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null)
                {
                    Sprite sprite = resources[i].GetSprite();
                    icon.sprite = storageOnly ? GrayscaleSprite(sprite) : sprite;
                    icon.color = Color.white;
                }

                chip.GetComponent<TooltipHoverable>()?.Init(resources[i].GetNameInGame(), tip);
            }
        }

        private GameObject CreateResourceChip(Resource resource)
        {
            ToggleButton pickerToggle = picker.GetComponent<ToggleButton>();
            GameObject chip = new GameObject(
                $"ResourceChip_{resource.name}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                typeof(TooltipHoverable),
                typeof(ResourceChipButton));
            chip.transform.SetParent(iconRow, false);

            RectTransform rect = chip.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(46f, 46f);
            Image background = chip.GetComponent<Image>();
            background.sprite = pickerToggle.buttonDefault;
            background.type = Image.Type.Sliced;

            LayoutElement layout = chip.GetComponent<LayoutElement>();
            layout.preferredWidth = 46f;
            layout.preferredHeight = 46f;

            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(3f, 3f);
            iconRect.offsetMax = new Vector2(-3f, -3f);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = resource.GetSprite();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            chip.GetComponent<TooltipHoverable>().Init(
                resource.GetNameInGame(),
                "右键移除此物资");
            ResourceChipButton button = chip.GetComponent<ResourceChipButton>();
            button.controller = this;
            button.resource = resource;
            return chip;
        }

        private void SetPlusVisual()
        {
            ToggleButton toggle = picker.GetComponent<ToggleButton>();
            if (toggle?.image != null)
            {
                toggle.image.sprite = null;
                toggle.image.color = Color.clear;
            }

            if (plusText != null)
            {
                plusText.gameObject.SetActive(true);
                plusText.text = "+";
                plusText.transform.SetAsLastSibling();
            }

            picker.GetComponent<TooltipHoverable>()?.Init("添加物资", "点击打开物资列表");
        }

        private void RemoveResource(Resource resource)
        {
            object center = GetCenter();
            if (center == null)
            {
                return;
            }

            List<Resource> remaining = GetResources(center)
                .Where(selected => selected != resource)
                .ToList();
            if (remaining.Count == 0)
            {
                return;
            }

            SetResources(center, remaining);
            Refresh();
            MultiResourcePickerPatch.RefreshButtonVisuals(picker.itemPickerPanel);
            WorldScripts.Inst.visualizationManager.Update(true);
        }

        private void Update()
        {
            object center = GetCenter();
            if (center == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            EnsurePanelLabels();
            AlignResourceHeader();
            SetPlusVisual();
            IReadOnlyList<Resource> resources = GetResources(center);
            string signature = string.Join("|", resources.Select(resource => resource.name));
            long buildingId = GetConsumerProducer(center).building.GetID();
            if (buildingId != displayedBuildingId || signature != displayedResources)
            {
                Refresh();
                return;
            }

            ApplyRoadWarnings();
        }

        private void LateUpdate()
        {
            if (picker == null || !isActiveAndEnabled)
            {
                return;
            }

            ApplyRoom();
        }

        private void OnGUI()
        {
            ReleaseFinishedPointerControls();
            object center = GetCenter();
            if (center == null ||
                storageBoundsControls == null ||
                !gameObject.activeInHierarchy)
            {
                return;
            }

            Rect area = CalculateStorageScreenRect();
            if (area.width < 50f || area.height < 30f)
            {
                return;
            }

            storageHoverTip = null;
            GUI.depth = -1000;
            if (TryGetItemPickerRect(out Rect pickerRect) &&
                area.Overlaps(pickerRect))
            {
                DrawStorageBehindPicker(area, pickerRect, center);
            }
            else
            {
                DrawStorageArea(area, center);
            }

            DrawStorageHoverTip();
        }

        private void DrawStorageBehindPicker(Rect area, Rect pickerRect, object center)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float left = Mathf.Max(area.xMin, pickerRect.xMin);
            float right = Mathf.Min(area.xMax, pickerRect.xMax);
            float top = Mathf.Max(area.yMin, pickerRect.yMin);
            float bottom = Mathf.Min(area.yMax, pickerRect.yMax);
            DrawStorageClip(new Rect(area.xMin, area.yMin, area.width, top - area.yMin), area, center);
            DrawStorageClip(new Rect(area.xMin, bottom, area.width, area.yMax - bottom), area, center);
            DrawStorageClip(new Rect(area.xMin, top, left - area.xMin, bottom - top), area, center);
            DrawStorageClip(new Rect(right, top, area.xMax - right, bottom - top), area, center);
        }

        private void DrawStorageClip(Rect clip, Rect area, object center)
        {
            if (clip.width <= 0f || clip.height <= 0f)
            {
                return;
            }

            GUI.BeginGroup(clip);
            area.position -= clip.position;
            DrawStorageArea(area, center);
            GUI.EndGroup();
        }

        private void DrawStorageHoverTip()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(storageHoverTip))
            {
                return;
            }

            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(14f * scale),
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                normal = { textColor = Color.white }
            };
            string[] lines = storageHoverTip.Split('\n');
            float width = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                width = Mathf.Max(width, MeasureTextWidth(style, lines[i]));
            }

            float lineHeight = style.fontSize + 5f * scale;
            float pad = 8f * scale;
            width += pad * 2f;
            float height = lines.Length * lineHeight + pad * 2f;
            Vector2 mouse = Event.current.mousePosition;
            float x = mouse.x + 16f * scale;
            float y = mouse.y + 16f * scale;
            if (x + width > Screen.width - 6f)
            {
                x = mouse.x - width - 12f * scale;
            }

            if (y + height > Screen.height - 6f)
            {
                y = Screen.height - height - 6f;
            }

            x = Mathf.Max(6f, x);
            y = Mathf.Max(6f, y);
            int previousDepth = GUI.depth;
            Color previous = GUI.color;
            GUI.depth = -2000;
            GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.96f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < lines.Length; i++)
            {
                style.Draw(
                    new Rect(x + pad, y + pad + i * lineHeight, width - pad * 2f, lineHeight),
                    lines[i],
                    false,
                    false,
                    false,
                    false);
            }

            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        private static float MeasureTextWidth(GUIStyle style, string text)
        {
            float measured = style.CalcSize(new GUIContent(text ?? string.Empty)).x;
            float size = Mathf.Max(12f, style.fontSize);
            float estimated = 0f;
            foreach (char character in text ?? string.Empty)
            {
                estimated += character <= 0x7f ? size * 0.56f : size;
            }

            return Mathf.Max(measured, estimated) + 4f;
        }

        private void DrawStorageArea(Rect area, object center)
        {
            IReadOnlyList<Resource> resources = GetResources(center);
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            float rowHeight = 31f * scale;
            float padding = 7f * scale;
            GUI.color = new Color(0.075f, 0.17f, 0.25f, 0.96f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            Rect scrollArea = new Rect(
                area.x + padding,
                area.y + padding,
                area.width - padding * 2f,
                area.height - padding * 2f);
            float contentHeight = Mathf.Max(scrollArea.height, resources.Count * rowHeight);
            float scrollbarWidth = 5f * scale;
            float scrollbarGap = 5f * scale;
            float contentWidth = scrollArea.width - scrollbarWidth - scrollbarGap;
            Rect viewport = new Rect(scrollArea.x, scrollArea.y, contentWidth, scrollArea.height);
            float maxScroll = Mathf.Max(0f, contentHeight - viewport.height);
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.ScrollWheel && scrollArea.Contains(currentEvent.mousePosition))
            {
                storageScroll.y = Mathf.Clamp(
                    storageScroll.y + currentEvent.delta.y * rowHeight,
                    0f,
                    maxScroll);
                currentEvent.Use();
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(14f * scale),
                normal = { textColor = Color.white }
            };
            GUIStyle capacityStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };

            GUI.BeginGroup(viewport);
            GUI.BeginGroup(new Rect(0f, -storageScroll.y, contentWidth, contentHeight));
            for (int i = 0; i < resources.Count; i++)
            {
                Resource resource = resources[i];
                float y = i * rowHeight;
                List<string> partners = PartnerNames(center, resource);
                bool storageOnly = IsStorageOnly(center, resource);
                bool missing = partners.Count == 0;
                bool blocked = !storageOnly && !missing && IsRoadBlocked(center, resource);
                if (!storageOnly && (missing || blocked))
                {
                    Color previous = GUI.color;
                    GUI.color = RoadWarningColor;
                    GUI.DrawTexture(
                        new Rect(1f, y + 1f, rowHeight - 2f, rowHeight - 2f),
                        Texture2D.whiteTexture);
                    GUI.color = previous;
                }

                Rect iconRect = new Rect(3f, y + 3f, rowHeight - 6f, rowHeight - 6f);
                Sprite iconSprite = storageOnly ? GrayscaleSprite(resource.GetSprite()) : resource.GetSprite();
                DrawSprite(iconSprite, iconRect);
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    iconRect.Contains(Event.current.mousePosition))
                {
                    ToggleStorage(resource);
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint &&
                    iconRect.Contains(Event.current.mousePosition))
                {
                    Building building = GetConsumerProducer(center)?.building;
                    string link = DescribeResourceTip(center, partners, storageOnly, missing, blocked);
                    storageHoverTip = building == null
                        ? link
                        : link + "\n" + TransitTip.Describe(building, resource);
                }

                int storage = GetStorage(GetConsumerProducer(center), resource);
                string caption = resource.GetNameInGame() + " " + storage;
                float captionLeft = rowHeight + 2f * scale;
                float captionWidth = MeasureTextWidth(labelStyle, caption);
                int capacity = GetCapacity(center, resource);
                int maxStorage = CollectionProductionLogic.GetMaxStorage();
                float capacityWidth = MeasureTextWidth(capacityStyle, maxStorage.ToString());
                float gap = 6f * scale;
                float capacityLeft = contentWidth - capacityWidth;
                float maxCaption = Mathf.Max(36f * scale, capacityLeft - captionLeft - gap * 2f - 48f * scale);
                captionWidth = Mathf.Min(captionWidth, maxCaption);
                GUI.Label(new Rect(captionLeft, y, captionWidth, rowHeight), caption, labelStyle);

                float sliderLeft = captionLeft + captionWidth + gap;
                float sliderWidth = Mathf.Max(36f * scale, capacityLeft - gap - sliderLeft);
                float value = DrawNativeSlider(
                    new Rect(sliderLeft, y + 5f * scale, sliderWidth, 21f * scale),
                    capacity,
                    CollectionProductionLogic.minStorageLimit,
                    maxStorage,
                    resource.name.GetHashCode());
                int rounded = Mathf.RoundToInt(value / 5f) * 5;
                if (rounded != capacity)
                {
                    SetCapacity(center, resource, rounded);
                }

                GUI.Label(new Rect(capacityLeft, y, capacityWidth, rowHeight), rounded.ToString(), capacityStyle);
            }

            GUI.EndGroup();
            GUI.EndGroup();
            storageScroll.y = Mathf.Clamp(storageScroll.y, 0f, maxScroll);
            DrawNativeScrollbar(
                new Rect(
                    scrollArea.xMax - scrollbarWidth,
                    scrollArea.y,
                    scrollbarWidth,
                    scrollArea.height),
                maxScroll);
        }

        private bool TryGetItemPickerRect(out Rect rect)
        {
            rect = default(Rect);
            if (picker == null ||
                picker.itemPickerPanel == null ||
                !picker.itemPickerPanel.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = picker.itemPickerPanel.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            RectTransform panelRect = picker.itemPickerPanel.GetComponent<RectTransform>();
            rect = GetGuiRect(panelRect, camera);
            if (rect.width <= Screen.width * 0.8f)
            {
                return true;
            }

            Image bestBackground = picker.itemPickerPanel.GetComponentsInChildren<Image>(true)
                .Where(image => image.gameObject.activeInHierarchy && image.color.a > 0.05f)
                .Select(image => new
                {
                    Image = image,
                    Rect = GetGuiRect(image.rectTransform, camera)
                })
                .Where(candidate =>
                    candidate.Rect.width > 150f &&
                    candidate.Rect.height > 150f &&
                    candidate.Rect.width < Screen.width * 0.8f)
                .OrderByDescending(candidate => candidate.Rect.width * candidate.Rect.height)
                .Select(candidate => candidate.Image)
                .FirstOrDefault();
            if (bestBackground != null)
            {
                rect = GetGuiRect(bestBackground.rectTransform, camera);
            }

            return true;
        }

        private float DrawNativeSlider(
            Rect rect,
            float value,
            float minimum,
            float maximum,
            int hint)
        {
            int controlId = GUIUtility.GetControlID(hint, FocusType.Passive, rect);
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                activeSliderControlId = controlId;
                currentEvent.Use();
            }

            if (GUIUtility.hotControl == controlId &&
                activeSliderControlId == controlId)
            {
                if (currentEvent.type == EventType.MouseDrag ||
                    currentEvent.type == EventType.MouseDown)
                {
                    float normalized = Mathf.InverseLerp(rect.x, rect.xMax, currentEvent.mousePosition.x);
                    value = Mathf.Lerp(minimum, maximum, normalized);
                    currentEvent.Use();
                }
                else if (currentEvent.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    activeSliderControlId = 0;
                    currentEvent.Use();
                }
            }

            float handleSize = Mathf.Min(rect.height, 20f * Mathf.Max(0.75f, Screen.height / 1080f));
            float trackHeight = Mathf.Max(5f, rect.height * 0.32f);
            Rect track = new Rect(
                rect.x + handleSize * 0.5f,
                rect.center.y - trackHeight * 0.5f,
                Mathf.Max(1f, rect.width - handleSize),
                trackHeight);
            float amount = Mathf.InverseLerp(minimum, maximum, value);
            DrawStyledSprite(sliderBackgroundSprite, track, sliderBackgroundColor);
            if (amount > 0f)
            {
                DrawStyledSprite(
                    sliderFillSprite,
                    new Rect(track.x, track.y, track.width * amount, track.height),
                    sliderFillColor);
            }

            Rect handle = new Rect(
                track.x + track.width * amount - handleSize * 0.5f,
                rect.center.y - handleSize * 0.5f,
                handleSize,
                handleSize);
            DrawStyledSprite(sliderHandleSprite, handle, sliderHandleColor);
            return value;
        }

        private void DrawNativeScrollbar(Rect rect, float maxScroll)
        {
            Image trackImage = nativeScrollbarTemplate?.GetComponent<Image>();
            Image handleImage = nativeScrollbarTemplate?.targetGraphic as Image;
            float handleHeight = Mathf.Clamp(
                rect.height * rect.height / (rect.height + maxScroll),
                rect.width * 1.5f,
                rect.height);
            float travel = Mathf.Max(1f, rect.height - handleHeight);
            float normalized = maxScroll <= 0f ? 0f : storageScroll.y / maxScroll;
            Rect handle = new Rect(rect.x, rect.y + travel * normalized, rect.width, handleHeight);

            int controlId = GUIUtility.GetControlID(
                "MultiLogisticsScrollbar".GetHashCode(),
                FocusType.Passive,
                rect);
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                activeScrollbarControlId = controlId;
                scrollbarDragOffset = handle.Contains(currentEvent.mousePosition)
                    ? currentEvent.mousePosition.y - handle.y
                    : handleHeight * 0.5f;
                UpdateScrollbarFromMouse(rect, handleHeight, maxScroll, currentEvent.mousePosition.y);
                currentEvent.Use();
            }
            else if (GUIUtility.hotControl == controlId &&
                     activeScrollbarControlId == controlId &&
                     currentEvent.type == EventType.MouseDrag)
            {
                UpdateScrollbarFromMouse(rect, handleHeight, maxScroll, currentEvent.mousePosition.y);
                currentEvent.Use();
            }
            else if (GUIUtility.hotControl == controlId && currentEvent.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                activeScrollbarControlId = 0;
                currentEvent.Use();
            }

            DrawStyledSprite(trackImage?.sprite, rect, trackImage?.color ?? new Color(0.04f, 0.1f, 0.15f));
            normalized = maxScroll <= 0f ? 0f : storageScroll.y / maxScroll;
            handle.y = rect.y + travel * normalized;
            DrawStyledSprite(handleImage?.sprite, handle, handleImage?.color ?? new Color(0.45f, 0.65f, 0.75f));
        }

        private void ReleaseFinishedPointerControls()
        {
            if (Event.current.rawType != EventType.MouseUp)
            {
                return;
            }

            if (GUIUtility.hotControl == activeSliderControlId ||
                GUIUtility.hotControl == activeScrollbarControlId)
            {
                GUIUtility.hotControl = 0;
            }

            activeSliderControlId = 0;
            activeScrollbarControlId = 0;
        }

        private void UpdateScrollbarFromMouse(Rect rect, float handleHeight, float maxScroll, float mouseY)
        {
            float travel = Mathf.Max(1f, rect.height - handleHeight);
            float handleY = Mathf.Clamp(mouseY - scrollbarDragOffset, rect.y, rect.y + travel);
            storageScroll.y = (handleY - rect.y) / travel * maxScroll;
        }

        private static void DrawStyledSprite(Sprite sprite, Rect target, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            if (sprite != null)
            {
                DrawSprite(sprite, target);
            }
            else
            {
                GUI.DrawTexture(target, Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        private void ApplyRoom()
        {
            if (iconRow == null || storageBoundsControls == null)
            {
                return;
            }

            RestoreNativeLayout();
            EnsureStorageSpacer();
            LiftRadiusByTwoRows();
            HideEmptyStorageChrome();
        }

        private void HideEmptyStorageChrome()
        {
            RectTransform frame = FindNativeStorageFrame();
            if (frame != null)
            {
                foreach (Image child in frame.GetComponentsInChildren<Image>(true))
                {
                    child.enabled = false;
                    child.raycastTarget = false;
                }
            }

            if (upstreamLabel == null || linkedCentersLabel == null)
            {
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            float textBottom = GlyphBottomGui(upstreamLabel.GetComponent<TMP_Text>(), camera);
            Rect linked = GetGuiRect(linkedCentersLabel, camera);
            if (textBottom < 0f || linked.yMin <= textBottom + 12f)
            {
                return;
            }

            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                if (iconRow != null && image.transform.IsChildOf(iconRow))
                {
                    continue;
                }

                if (widthReference != null &&
                    (image.transform == widthReference || image.transform.IsChildOf(widthReference)))
                {
                    continue;
                }

                if (image.GetComponentInParent<CollectionDistributionCenterEntryPanel>() != null)
                {
                    continue;
                }

                Rect rect = GetGuiRect(image.rectTransform, camera);
                float middle = (rect.yMin + rect.yMax) * 0.5f;
                if (rect.width < 80f || rect.height < 16f || rect.height > 220f || rect.Overlaps(linked))
                {
                    continue;
                }

                if (middle > textBottom && middle < linked.yMin)
                {
                    image.enabled = false;
                    image.raycastTarget = false;
                }
            }
        }

        private RectTransform FindNativeStorageFrame()
        {
            if (storageSliderTemplate == null)
            {
                return null;
            }

            Transform current = storageSliderTemplate.transform.parent;
            RectTransform best = null;
            float bestHeight = 0f;
            while (current != null && current != transform)
            {
                RectTransform rect = current as RectTransform;
                if (rect != null && current.GetComponent<Image>() != null)
                {
                    float height = rect.rect.height;
                    if (height >= 24f && height <= 220f && height >= bestHeight)
                    {
                        best = rect;
                        bestHeight = height;
                    }
                }

                current = current.parent;
            }

            return best;
        }

        private void LiftRadiusByTwoRows()
        {
            if (radiusPlaced || widthReference == null || iconRow == null || FindVerticalLayout(widthReference) != null)
            {
                return;
            }

            radiusPlaced = true;

            Camera camera = PanelCamera();
            float lift = LocalHeightToScreen(iconRow, camera, (46f + 5f) * 2f);
            LiftFromHome(widthReference, camera, lift);
            if (collectionPanel != null)
            {
                LiftFromHome(collectionPanel.collectionRadiusText, camera, lift);
            }

            if (distributionPanel != null)
            {
                LiftFromHome(distributionPanel.distributionRadiusText, camera, lift);
            }

            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                string value = text.text ?? "";
                if (!value.Contains("分配半径") && !value.Contains("收集半径"))
                {
                    continue;
                }

                if (text.transform == widthReference || text.transform.IsChildOf(widthReference))
                {
                    continue;
                }

                LiftFromHome(text, camera, lift);
                break;
            }
        }

        private void LiftFromHome(Component component, Camera camera, float screenUp)
        {
            if (component == null)
            {
                return;
            }

            RectTransform rect = component.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            int id = rect.GetInstanceID();
            if (!homeAnchored.ContainsKey(id))
            {
                homeAnchored[id] = rect.anchoredPosition;
            }

            rect.anchoredPosition = homeAnchored[id];
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
            float iconBottom = ScreenEdge(iconRow, camera, false);
            float liftedTop = ScreenEdge(rect, camera, true) + screenUp;
            float allowedTop = iconBottom - 6f;
            float applied = liftedTop > allowedTop ? screenUp - (liftedTop - allowedTop) : screenUp;
            if (applied < 0f)
            {
                applied = 0f;
            }

            MoveToScreen(rect, camera, screen + new Vector2(0f, applied));
        }

        private void RestoreNativeLayout()
        {
            if (nativeLayoutFrozen)
            {
                return;
            }

            EnableLayoutAncestors(upstreamLabel);
            EnableLayoutAncestors(linkedCentersLabel);
            EnableLayoutAncestors(widthReference);
            EnableLayoutAncestors(EntryTemplateParent());
            EnableLayoutOn(transform);
            nativeLayoutFrozen = true;
        }

        private Transform EntryTemplateParent()
        {
            if (collectionPanel != null && collectionPanel.distributionCenterTemplate != null)
            {
                return collectionPanel.distributionCenterTemplate.transform.parent;
            }

            if (distributionPanel != null && distributionPanel.collectionCenterTemplate != null)
            {
                return distributionPanel.collectionCenterTemplate.transform.parent;
            }

            return null;
        }

        private void EnableLayoutAncestors(Transform start)
        {
            Transform current = start;
            while (current != null)
            {
                EnableLayoutOn(current);
                if (current == transform)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private static void EnableLayoutOn(Component component)
        {
            if (component == null)
            {
                return;
            }

            LayoutGroup group = component.GetComponent<LayoutGroup>();
            if (group != null)
            {
                group.enabled = true;
            }

            ContentSizeFitter fitter = component.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = true;
            }
        }

        private void EnsureStorageSpacer()
        {
            if (linkedCentersLabel == null)
            {
                return;
            }

            VerticalLayoutGroup layout = FindVerticalLayout(linkedCentersLabel);
            if (layout == null)
            {
                return;
            }

            Transform host = layout.transform;
            Transform block = linkedCentersLabel;
            while (block.parent != null && block.parent != host)
            {
                block = block.parent;
            }

            if (storageSpacer == null)
            {
                GameObject spacerObject = new GameObject(
                    "MultiResourceStorageSpacer",
                    typeof(RectTransform),
                    typeof(LayoutElement));
                storageSpacer = spacerObject.GetComponent<RectTransform>();
                storageSpacer.SetParent(host, false);
            }

            if (storageSpacer.parent != host)
            {
                storageSpacer.SetParent(host, false);
            }

            int spacerIndex = storageSpacer.GetSiblingIndex();
            int blockIndex = block.GetSiblingIndex();
            int desiredIndex = spacerIndex < blockIndex ? blockIndex - 1 : blockIndex;
            if (spacerIndex != desiredIndex)
            {
                storageSpacer.SetSiblingIndex(desiredIndex);
            }

            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            float localHeight = ScreenHeightToLocal(host as RectTransform, PanelCamera(), MinimumStorageHeight(scale));
            LayoutElement element = storageSpacer.GetComponent<LayoutElement>();
            if (Mathf.Abs(element.preferredHeight - localHeight) > 0.5f)
            {
                element.minHeight = localHeight;
                element.preferredHeight = localHeight;
                element.flexibleHeight = 0f;
                storageSpacer.sizeDelta = new Vector2(Mathf.Max(10f, storageSpacer.sizeDelta.x), localHeight);
            }
        }

        private VerticalLayoutGroup FindVerticalLayout(Transform start)
        {
            Transform current = start;
            while (current != null)
            {
                VerticalLayoutGroup layout = current.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    return layout;
                }

                if (current == transform)
                {
                    break;
                }

                current = current.parent;
            }

            return null;
        }

        private static float ScreenHeightToLocal(RectTransform rect, Camera camera, float screenHeight)
        {
            if (rect == null || screenHeight <= 0f)
            {
                return screenHeight;
            }

            float screen = Mathf.Max(1f, ScreenEdgeSpan(rect, camera));
            return rect.rect.height / screen * screenHeight;
        }

        private static float MinimumStorageHeight(float scale)
        {
            return 5f * 31f * scale + 14f * scale;
        }

        private static float LocalHeightToScreen(RectTransform rect, Camera camera, float localHeight)
        {
            if (rect == null || localHeight <= 0f)
            {
                return 0f;
            }

            float local = Mathf.Max(1f, rect.rect.height);
            return ScreenEdgeSpan(rect, camera) * (localHeight / local);
        }

        private static float ScreenEdgeSpan(RectTransform rect, Camera camera)
        {
            return ScreenEdge(rect, camera, true) - ScreenEdge(rect, camera, false);
        }

        private static float ScreenEdge(RectTransform rect, Camera camera, bool top)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float edge = top ? float.MinValue : float.MaxValue;
            foreach (Vector3 corner in corners)
            {
                float y = RectTransformUtility.WorldToScreenPoint(camera, corner).y;
                edge = top ? Mathf.Max(edge, y) : Mathf.Min(edge, y);
            }

            return edge;
        }

        private static void MoveToScreen(RectTransform rect, Camera camera, Vector2 desiredScreen)
        {
            RectTransform parent = rect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Vector2 currentScreen = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
            if ((currentScreen - desiredScreen).sqrMagnitude < 0.25f)
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, currentScreen, camera, out Vector2 currentLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, desiredScreen, camera, out Vector2 desiredLocal);
            rect.anchoredPosition += desiredLocal - currentLocal;
        }

        private Camera PanelCamera()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private Rect CalculateStorageScreenRect()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            Vector3[] corners = new Vector3[4];
            foreach (RectTransform control in storageBoundsControls)
            {
                control.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                }
            }

            Rect result = new Rect(
                min.x - 4f,
                Screen.height - max.y - 4f,
                max.x - min.x + 8f,
                max.y - min.y + 8f);
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            bool usingSpacer = false;
            if (storageSpacer != null && storageSpacer.gameObject.activeInHierarchy)
            {
                Rect spacer = GetGuiRect(storageSpacer, camera);
                if (spacer.height > 40f)
                {
                    result.yMin = spacer.yMin + 4f * scale;
                    result.yMax = spacer.yMax - 4f * scale;
                    usingSpacer = true;
                    IncludeStorageGap(ref result, camera, scale);
                }
            }

            if (!usingSpacer)
            {
                IncludeStorageGap(ref result, camera, scale);
            }

            if (widthReference != null)
            {
                Rect width = GetGuiRect(widthReference, camera);
                result.xMin = width.xMin - 4f * scale;
                result.xMax = width.xMax + 4f * scale;
            }

            return result;
        }

        private void IncludeStorageGap(ref Rect result, Camera camera, float scale)
        {
            if (upstreamLabel != null)
            {
                float textBottom = GlyphBottomGui(upstreamLabel.GetComponent<TMP_Text>(), camera);
                if (textBottom > 0f)
                {
                    result.yMin = Mathf.Min(result.yMin, textBottom + 4f * scale);
                }
            }

            RectTransform frame = FindNativeStorageFrame();
            if (frame != null)
            {
                Rect native = GetGuiRect(frame, camera);
                if (native.height > 16f && native.width > 40f)
                {
                    result.yMin = Mathf.Min(result.yMin, native.yMin);
                    result.yMax = Mathf.Max(result.yMax, native.yMax);
                }
            }

            if (linkedCentersLabel != null)
            {
                Rect linked = GetGuiRect(linkedCentersLabel, camera);
                result.yMax = Mathf.Min(result.yMax, linked.yMin - 6f * scale);
            }
        }

        public bool IsCameraScrollBlocked()
        {
            if (!isActiveAndEnabled || storageBoundsControls == null)
            {
                return false;
            }

            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return CalculateStorageScreenRect().Contains(mouse);
        }

        private void OnDisable()
        {
            if (GUIUtility.hotControl == activeSliderControlId ||
                GUIUtility.hotControl == activeScrollbarControlId)
            {
                GUIUtility.hotControl = 0;
            }

            activeSliderControlId = 0;
            activeScrollbarControlId = 0;
            radiusPlaced = false;
        }

        private void OnDestroy()
        {
            if (GUIUtility.hotControl == activeSliderControlId ||
                GUIUtility.hotControl == activeScrollbarControlId)
            {
                GUIUtility.hotControl = 0;
            }

            activeSliderControlId = 0;
            activeScrollbarControlId = 0;
            if (WorldScripts.Inst != null &&
                WorldScripts.Inst.cameraMovement != null)
            {
                WorldScripts.Inst.cameraMovement.cameraScrollBlockers.Remove(this);
            }
        }

        private static Rect GetGuiRect(RectTransform transform, Camera camera)
        {
            Vector3[] corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            return new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
        }

        private static Sprite GrayscaleSprite(Sprite source)
        {
            if (source == null || source.texture == null)
            {
                return source;
            }

            int key = source.GetInstanceID();
            if (grayscaleSprites.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            try
            {
                Sprite created = CreateGrayscaleSprite(source);
                if (created != null)
                {
                    grayscaleSprites[key] = created;
                    return created;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[InfraSpaceMultiResourceLogistics] 物资图标转灰度失败。 " + exception.Message);
            }

            return source;
        }

        private static Sprite CreateGrayscaleSprite(Sprite source)
        {
            Texture2D readable = ReadableCopy(source.texture);
            Rect textureRect = source.textureRect;
            int x = Mathf.Clamp(Mathf.FloorToInt(textureRect.x), 0, readable.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(textureRect.y), 0, readable.height - 1);
            int width = Mathf.Clamp(Mathf.RoundToInt(textureRect.width), 1, readable.width - x);
            int height = Mathf.Clamp(Mathf.RoundToInt(textureRect.height), 1, readable.height - y);
            Color[] pixels = readable.GetPixels(x, y, width, height);
            pixels = Upright(pixels, width, height, source.packingRotation, out width, out height);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                float gray = (pixel.r * 0.299f) + (pixel.g * 0.587f) + (pixel.b * 0.114f);
                pixels[i] = new Color(gray, gray, gray, pixel.a);
            }

            Texture2D grayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            grayTexture.SetPixels(pixels);
            grayTexture.Apply(false, true);
            grayTexture.filterMode = source.texture.filterMode;
            grayTexture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                grayTexture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit <= 0f ? 100f : source.pixelsPerUnit);
        }

        private static Texture2D ReadableCopy(Texture source)
        {
            int key = source.GetInstanceID();
            if (readableTextures.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            Graphics.Blit(source, temporary);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            readableTextures[key] = copy;
            return copy;
        }

        private static Color[] Upright(
            Color[] pixels,
            int width,
            int height,
            SpritePackingRotation rotation,
            out int uprightWidth,
            out int uprightHeight)
        {
            uprightWidth = width;
            uprightHeight = height;
            if (rotation == SpritePackingRotation.None || pixels == null)
            {
                return pixels;
            }

            if (rotation == SpritePackingRotation.Rotate180)
            {
                return Rotate180(pixels, width, height);
            }

            if (rotation == SpritePackingRotation.FlipHorizontal)
            {
                return FlipHorizontal(pixels, width, height);
            }

            if (rotation == SpritePackingRotation.FlipVertical)
            {
                return FlipVertical(pixels, width, height);
            }

            uprightWidth = height;
            uprightHeight = width;
            return Rotate90CounterClockwise(pixels, width, height);
        }

        private static Color[] Rotate90CounterClockwise(Color[] pixels, int width, int height)
        {
            Color[] rotated = new Color[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    rotated[((width - 1 - x) * height) + y] = pixels[(y * width) + x];
                }
            }

            return rotated;
        }

        private static Color[] Rotate180(Color[] pixels, int width, int height)
        {
            Color[] rotated = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                rotated[pixels.Length - 1 - i] = pixels[i];
            }

            return rotated;
        }

        private static Color[] FlipHorizontal(Color[] pixels, int width, int height)
        {
            Color[] flipped = new Color[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    flipped[(y * width) + (width - 1 - x)] = pixels[(y * width) + x];
                }
            }

            return flipped;
        }

        private static Color[] FlipVertical(Color[] pixels, int width, int height)
        {
            Color[] flipped = new Color[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    flipped[((height - 1 - y) * width) + x] = pixels[(y * width) + x];
                }
            }

            return flipped;
        }

        private static void DrawSprite(Sprite sprite, Rect target)
        {
            if (sprite == null || sprite.texture == null)
            {
                return;
            }

            Rect textureRect = sprite.textureRect;
            Rect uv = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(target, sprite.texture, uv, true);
        }

        private object GetCenter()
        {
            if (collectionPanel != null)
            {
                return CollectionCenterField.GetValue(collectionPanel) as CollectionProductionLogic;
            }

            return distributionPanel == null
                ? null
                : DistributionCenterField.GetValue(distributionPanel) as DistributionProductionLogic;
        }

        private static IReadOnlyList<Resource> GetResources(object center)
        {
            return center is CollectionProductionLogic collection
                ? CenterState.GetResources(collection)
                : CenterState.GetResources((DistributionProductionLogic)center);
        }

        private static ConsumerProducer GetConsumerProducer(object center)
        {
            return center is CollectionProductionLogic collection
                ? collection.consumerProducer
                : ((DistributionProductionLogic)center).consumerProducer;
        }

        private static int GetCapacity(object center, Resource resource)
        {
            return center is CollectionProductionLogic collection
                ? CenterState.GetCapacity(collection, resource)
                : CenterState.GetCapacity((DistributionProductionLogic)center, resource);
        }

        private static void SetCapacity(object center, Resource resource, int capacity)
        {
            if (center is CollectionProductionLogic collection)
            {
                CenterState.SetCapacity(collection, resource, capacity);
            }
            else
            {
                CenterState.SetCapacity((DistributionProductionLogic)center, resource, capacity);
            }
        }

        private static void SetResources(object center, IEnumerable<Resource> resources)
        {
            if (center is CollectionProductionLogic collection)
            {
                CenterState.SetResources(collection, resources);
            }
            else
            {
                CenterState.SetResources((DistributionProductionLogic)center, resources);
            }
        }

        private static bool IsRoadBlocked(object center, Resource resource)
        {
            if (center is CollectionProductionLogic collection)
            {
                return MultiResourceLogisticsEngine.IsResourceRoadBlocked(collection, resource);
            }

            if (center is DistributionProductionLogic distribution)
            {
                return MultiResourceLogisticsEngine.IsResourceRoadBlocked(distribution, resource);
            }

            return false;
        }

        private static List<string> PartnerNames(object center, Resource resource)
        {
            List<string> names = new List<string>();
            if (resource == null)
            {
                return names;
            }

            if (center is CollectionProductionLogic collection)
            {
                AppendPartners(collection.connectedDistributionCenters, names, resource, true);
            }
            else if (center is DistributionProductionLogic distribution)
            {
                AppendPartners(distribution.connectedCollectionCenters, names, resource, false);
            }

            return names;
        }

        private static void AppendPartners(
            List<Reference<Building>> links,
            List<string> names,
            Resource resource,
            bool partnerIsDistribution)
        {
            if (links == null)
            {
                return;
            }

            for (int i = 0; i < links.Count && names.Count < 8; i++)
            {
                Building building = links[i]?.Get();
                if (building == null || !building.IsValidAndBuilt())
                {
                    continue;
                }

                bool matches = partnerIsDistribution
                    ? building.consumerProducer?.productionLogic is DistributionProductionLogic distribution &&
                      CenterState.Moves(distribution, resource)
                    : building.consumerProducer?.productionLogic is CollectionProductionLogic collection &&
                      CenterState.Moves(collection, resource);
                if (matches)
                {
                    names.Add(TransitTip.BuildingName(building));
                }
            }
        }

        private static string DescribeLink(
            object center,
            List<string> partners,
            bool missing,
            bool blocked)
        {
            string partnerLabel = center is DistributionProductionLogic ? "搜集中心" : "配送中心";
            StringBuilder text = new StringBuilder();
            if (missing || partners == null || partners.Count == 0)
            {
                text.Append("没有配对的");
                text.Append(partnerLabel);
            }
            else
            {
                text.Append("配对");
                text.Append(partnerLabel);
                for (int i = 0; i < partners.Count; i++)
                {
                    text.Append("\n  ");
                    text.Append(partners[i]);
                }
            }

            if (blocked)
            {
                text.Append("\n货车无法从搜集中心开到配送中心");
            }

            return text.ToString();
        }

        private static string DescribeResourceTip(
            object center,
            List<string> partners,
            bool storageOnly,
            bool missing,
            bool blocked)
        {
            if (storageOnly)
            {
                return "仅存储，不搜集也不配送\n左键恢复搜集和配送\n右键移除此物资";
            }

            return DescribeLink(center, partners, missing, blocked) + "\n左键改为仅存储\n右键移除此物资";
        }

        private static bool IsStorageOnly(object center, Resource resource)
        {
            if (resource == null)
            {
                return false;
            }

            if (center is CollectionProductionLogic collection)
            {
                return CenterState.IsStorageOnly(collection, resource);
            }

            if (center is DistributionProductionLogic distribution)
            {
                return CenterState.IsStorageOnly(distribution, resource);
            }

            return false;
        }

        private static int GetStorage(ConsumerProducer consumerProducer, Resource resource)
        {
            int index = Array.IndexOf(consumerProducer.incomingResources, resource);
            return index < 0 ? 0 : consumerProducer.incomingStorage[index];
        }

    }

    internal sealed class ResourceChipButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        internal NativeCenterPanelController controller;
        internal Resource resource;
        internal bool roadBlocked;
        internal bool storageOnly;
        internal string linkTip;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                eventData.Use();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                eventData.Use();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                controller?.RequestRemove(resource);
                eventData.Use();
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                controller?.ToggleStorage(resource);
                eventData.Use();
            }
        }
    }
}
