using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class ProducerFocus
    {
        private const string Tip =
            "单击：用绿色标出生产该物品的建筑\n双击：用蓝色标出需要该物品的建筑\n右键或 Esc：退出";

        private static readonly Color SupplyColor = new Color(0.22f, 0.86f, 0.38f, 1f);
        private static readonly Color DemandColor = new Color(0.25f, 0.55f, 1f, 1f);

        private static readonly AccessTools.FieldRef<PrioritizationItem, Resource> ResourceOf =
            AccessTools.FieldRefAccess<PrioritizationItem, Resource>("resource");

        private static readonly AccessTools.FieldRef<PrioritizationPanel, byte> DistrictOf =
            AccessTools.FieldRefAccess<PrioritizationPanel, byte>("selectedDistrict");

        private static readonly List<ProducerFocusButton> Hooked = new List<ProducerFocusButton>();

        private static readonly Dictionary<int, Sprite> GrayIcons = new Dictionary<int, Sprite>();

        private static readonly HashSet<int> GrayIds = new HashSet<int>();

        private static readonly FocusExit ExitHook = new FocusExit();

        private static Resource selected;
        private static bool demand;
        private static PrioritizationItem pending;
        private static float pendingAt;
        private static Material supplyMaterial;
        private static Material demandMaterial;
        private static VisualizationPanel hookedVisualization;
        private static int hookedChildren = -1;
        private static int paintedFrame = -1;
        private static bool escapeRegistered;

        internal static void Tick()
        {
            HookVisualization();
            FlushPendingClick();
            PrioritizationPanel panel = WorldScripts.Inst?.productionPanel;
            if (panel == null || panel.prioItemParent == null || !panel.gameObject.activeInHierarchy)
            {
                return;
            }

            int childCount = panel.prioItemParent.childCount;
            if (childCount != hookedChildren)
            {
                Hook(panel);
                hookedChildren = panel.prioItemParent.childCount;
            }

            Paint();
        }

        internal static void Clear()
        {
            UnregisterEscape();
            if (selected == null)
            {
                pending = null;
                return;
            }

            selected = null;
            demand = false;
            pending = null;
            Paint();
            WorldScripts.Inst?.visualizationManager?.MarkDirty();
        }

        internal static void OverrideVisualType(ref VisualType visualType)
        {
            if (selected == null || IsProtected(visualType))
            {
                return;
            }

            visualType = VisualType.Blank;
        }

        private static void HookVisualization()
        {
            VisualizationPanel panel = WorldScripts.Inst?.visualizationPanel;
            if (panel == null || panel == hookedVisualization || panel.buttonGroup?.onButtonPressed == null)
            {
                return;
            }

            panel.buttonGroup.onButtonPressed.AddListener(Clear);
            hookedVisualization = panel;
        }

        internal static void NotePainted()
        {
            if (selected != null)
            {
                paintedFrame = Time.frameCount;
            }
        }

        internal static void RestoreProducers()
        {
            if (selected == null || paintedFrame != Time.frameCount)
            {
                return;
            }

            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return;
            }

            Material material = HighlightMaterial();
            if (material == null)
            {
                return;
            }

            byte district = SelectedDistrict();
            foreach (Building building in buildings)
            {
                if (!ProducerCounts.InDistrict(building, district))
                {
                    continue;
                }

                bool match = demand
                    ? ProducerCounts.Demands(building, selected)
                    : ProducerCounts.Produces(building, selected);
                if (match)
                {
                    building.SetOverrideMaterial(material, material.color);
                }
            }
        }

        private static byte SelectedDistrict()
        {
            PrioritizationPanel panel = WorldScripts.Inst?.productionPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                return 0;
            }

            return DistrictOf(panel);
        }

        private static Material HighlightMaterial()
        {
            if (demand)
            {
                if (demandMaterial == null)
                {
                    demandMaterial = CreateMaterial(DemandColor);
                }

                return demandMaterial;
            }

            if (supplyMaterial == null)
            {
                supplyMaterial = CreateMaterial(SupplyColor);
            }

            return supplyMaterial;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                return WorldScripts.Inst?.visualizationManager?.selectableDefaultMat;
            }

            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.enableInstancing = true;
            material.color = color;
            return material;
        }

        private static void Hook(PrioritizationPanel panel)
        {
            PrioritizationItem[] items = panel.prioItemParent.GetComponentsInChildren<PrioritizationItem>(true);
            for (int i = 0; i < items.Length; i++)
            {
                PrioritizationItem item = items[i];
                if (item == null || item == panel.prioItemTemplate || !item.enabled || item.icon == null)
                {
                    continue;
                }

                if (item.icon.GetComponent<ProducerFocusButton>() != null)
                {
                    continue;
                }

                item.icon.raycastTarget = true;
                ProducerFocusButton hook = item.icon.gameObject.AddComponent<ProducerFocusButton>();
                hook.Item = item;
                hook.OriginalIconColor = item.icon.color;
                hook.OriginalSprite = item.icon.sprite;
                EnsureTip(item);
                Hooked.Add(hook);
            }
        }

        private static void NotifyClick(PrioritizationItem item, int clickCount)
        {
            if (item == null)
            {
                return;
            }

            if (clickCount >= 2)
            {
                pending = null;
                Apply(item, true);
                return;
            }

            pending = item;
            pendingAt = Time.unscaledTime;
        }

        private static void FlushPendingClick()
        {
            if (pending == null || Time.unscaledTime - pendingAt < 0.28f)
            {
                return;
            }

            PrioritizationItem item = pending;
            pending = null;
            Apply(item, false);
        }

        private static void Apply(PrioritizationItem item, bool showDemand)
        {
            if (item == null)
            {
                return;
            }

            Resource resource = ResourceOf(item);
            if (resource == null)
            {
                return;
            }

            if (Same(selected, resource) && demand == showDemand)
            {
                Clear();
                return;
            }

            ProducerList.Dismiss();
            selected = resource;
            demand = showDemand;
            RegisterEscape();
            Paint();
            WorldScripts.Inst?.visualizationManager?.MarkDirty();
        }

        private static void EnsureTip(PrioritizationItem item)
        {
            if (item?.icon == null)
            {
                return;
            }

            TooltipHoverable tip = item.icon.GetComponent<TooltipHoverable>();
            if (tip == null && item.tooltip != null && item.tooltip.gameObject == item.icon.gameObject)
            {
                tip = item.tooltip;
            }

            if (tip == null)
            {
                tip = item.icon.gameObject.AddComponent<TooltipHoverable>();
            }

            Resource resource = ResourceOf(item);
            string title = resource != null ? resource.GetNameInGame() : "";
            if (tip.GetTitle() != title || tip.GetDescription() != Tip)
            {
                tip.Init(title, Tip);
            }
        }

        private static void Paint()
        {
            for (int i = Hooked.Count - 1; i >= 0; i--)
            {
                ProducerFocusButton hook = Hooked[i];
                if (hook == null || hook.Item == null || hook.Item.icon == null)
                {
                    Hooked.RemoveAt(i);
                    continue;
                }

                Image icon = hook.Item.icon;
                EnsureTip(hook.Item);
                bool chosen = selected != null && Same(ResourceOf(hook.Item), selected);
                if (selected == null || chosen)
                {
                    if (hook.OriginalSprite != null && icon.sprite != hook.OriginalSprite)
                    {
                        icon.sprite = hook.OriginalSprite;
                    }

                    if (icon.color != hook.OriginalIconColor)
                    {
                        icon.color = hook.OriginalIconColor;
                    }
                }
                else
                {
                    if (icon.sprite != null && !IsGrayIcon(icon.sprite))
                    {
                        hook.OriginalSprite = icon.sprite;
                    }

                    if (hook.OriginalSprite != null && !IsGrayIcon(hook.OriginalSprite))
                    {
                        Sprite gray = GrayIcon(hook.OriginalSprite);
                        if (gray != null && icon.sprite != gray)
                        {
                            icon.sprite = gray;
                        }

                        Color flat = new Color(1f, 1f, 1f, hook.OriginalIconColor.a);
                        if (icon.color != flat)
                        {
                            icon.color = flat;
                        }
                    }
                }

                Vector3 scale = chosen ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
                if (icon.rectTransform.localScale != scale)
                {
                    icon.rectTransform.localScale = scale;
                }
            }
        }

        private static void RegisterEscape()
        {
            if (escapeRegistered)
            {
                return;
            }

            EscapeHandler handler = DionicGame.GetEscapeHandler();
            if (handler == null)
            {
                return;
            }

            handler.AddEscapable(ExitHook);
            escapeRegistered = true;
        }

        private static void UnregisterEscape()
        {
            if (!escapeRegistered)
            {
                return;
            }

            DionicGame.GetEscapeHandler()?.RemoveEscapable(ExitHook);
            escapeRegistered = false;
        }

        private static bool IsGrayIcon(Sprite sprite)
        {
            return sprite != null && GrayIds.Contains(sprite.GetInstanceID());
        }

        private static Sprite GrayIcon(Sprite source)
        {
            if (source == null || source.texture == null)
            {
                return source;
            }

            int id = source.GetInstanceID();
            if (GrayIcons.TryGetValue(id, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Rect rect = source.textureRect;
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            RenderTexture target = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source.texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
                Color32[] pixels = copy.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    byte gray = (byte)(pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f);
                    pixels[i] = new Color32(gray, gray, gray, pixel.a);
                }

                copy.SetPixels32(pixels);
                copy.Apply(false, true);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            if (copy == null)
            {
                return source;
            }

            Vector2 pivot = new Vector2(
                rect.width <= 0f ? 0.5f : source.pivot.x / rect.width,
                rect.height <= 0f ? 0.5f : source.pivot.y / rect.height);
            Sprite graySprite = Sprite.Create(copy, new Rect(0f, 0f, width, height), pivot, source.pixelsPerUnit);
            graySprite.hideFlags = HideFlags.HideAndDontSave;
            GrayIcons[id] = graySprite;
            GrayIds.Add(graySprite.GetInstanceID());
            return graySprite;
        }

        private sealed class FocusExit : IEscapable
        {
            public bool Escape()
            {
                Clear();
                return true;
            }
        }

        private static bool Same(Resource left, Resource right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left == right || left.name == right.name;
        }

        private static bool IsProtected(VisualType visualType)
        {
            return visualType == VisualType.Selection
                || visualType == VisualType.CustomBuilding
                || visualType == VisualType.TerraformDustParticleFiltering
                || visualType == VisualType.TerraformFertilization
                || visualType == VisualType.TerraformGroundAcidity
                || visualType == VisualType.TerraformAmmoniaExtraction
                || visualType == VisualType.TerraformMagneticField
                || visualType == VisualType.TerraformProgress;
        }

        private sealed class ProducerFocusButton : MonoBehaviour, IPointerClickHandler
        {
            internal PrioritizationItem Item;
            internal Color OriginalIconColor = Color.white;
            internal Sprite OriginalSprite;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                NotifyClick(Item, eventData.clickCount);
            }
        }
    }
}
