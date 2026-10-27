using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceProductionInsights
{
    internal static class ProducerFocus
    {
        private static readonly AccessTools.FieldRef<PrioritizationItem, Resource> ResourceOf =
            AccessTools.FieldRefAccess<PrioritizationItem, Resource>("resource");

        private static readonly List<ProducerFocusButton> Hooked = new List<ProducerFocusButton>();

        private static Resource selected;
        private static VisualizationPanel hookedVisualization;
        private static int hookedChildren = -1;
        private static int paintedFrame = -1;

        internal static void Tick()
        {
            HookVisualization();
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
            if (selected == null)
            {
                return;
            }

            selected = null;
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

            Color clear = new Color(0f, 0f, 0f, 1f);
            foreach (Building building in buildings)
            {
                if (ProducerCounts.Produces(building, selected))
                {
                    building.SetOverrideMaterial(null, clear);
                }
            }
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
                Button button = item.icon.gameObject.GetComponent<Button>();
                if (button == null)
                {
                    button = item.icon.gameObject.AddComponent<Button>();
                }

                button.transition = Selectable.Transition.None;
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;
                button.navigation = navigation;
                button.onClick.AddListener(hook.Click);
                Hooked.Add(hook);
            }
        }

        private static void Toggle(PrioritizationItem item)
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

            if (Same(selected, resource))
            {
                Clear();
                return;
            }

            ProducerList.Dismiss();
            selected = resource;
            Paint();
            WorldScripts.Inst?.visualizationManager?.MarkDirty();
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
                bool chosen = selected != null && Same(ResourceOf(hook.Item), selected);
                Color color = selected == null || chosen
                    ? hook.OriginalIconColor
                    : hook.OriginalIconColor * new Color(0.4f, 0.4f, 0.4f, 1f);
                if (icon.color != color)
                {
                    icon.color = color;
                }

                Vector3 scale = chosen ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
                if (icon.rectTransform.localScale != scale)
                {
                    icon.rectTransform.localScale = scale;
                }
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

        private sealed class ProducerFocusButton : MonoBehaviour
        {
            internal PrioritizationItem Item;
            internal Color OriginalIconColor = Color.white;

            internal void Click()
            {
                Toggle(Item);
            }
        }
    }
}
