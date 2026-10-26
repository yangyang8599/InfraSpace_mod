using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceMultiResourceLogistics
{
    [HarmonyPatch(typeof(ItemPickerPanel), nameof(ItemPickerPanel.ItemPicked))]
    internal static class MultiResourcePickerPatch
    {
        private static readonly FieldInfo ItemPickerButtonField =
            AccessTools.Field(typeof(ItemPickerPanel), "itemPickerButton");

        private static readonly FieldInfo ConfigListField =
            AccessTools.Field(typeof(ItemPickerPanel), "configList");

        private static readonly FieldInfo ButtonListField =
            AccessTools.Field(typeof(ItemPickerPanel), "buttonList");

        private static readonly FieldInfo CollectionCenterField =
            AccessTools.Field(typeof(CollectionInfoPanel), "collectionCenter");

        private static readonly FieldInfo DistributionCenterField =
            AccessTools.Field(typeof(DistributionInfoPanel), "distributionCenter");

        private static bool Prefix(ItemPickerPanel __instance, int i)
        {
            if (!TryGetCenter(
                    __instance,
                    out ItemPickerButton picker,
                    out CollectionProductionLogic collection,
                    out DistributionProductionLogic distribution))
            {
                return true;
            }

            List<IItemPickerConfig> configs =
                (List<IItemPickerConfig>)ConfigListField.GetValue(__instance);
            if (i < 0 || i >= configs.Count)
            {
                return true;
            }

            Resource changed = configs[i].GetItemObject() as Resource;
            IReadOnlyList<Resource> current = collection != null
                ? CenterState.GetResources(collection)
                : CenterState.GetResources(distribution);
            List<Resource> updated = current.ToList();

            if (updated.Contains(changed))
            {
                return false;
            }

            updated.Add(changed);

            if (collection != null)
            {
                CenterState.SetResources(collection, updated);
            }
            else
            {
                CenterState.SetResources(distribution, updated);
            }

            RefreshButtonVisuals(__instance, updated);
            NativeCenterPanelController.RefreshFor(picker);
            WorldScripts.Inst.visualizationManager.Update(true);
            return false;
        }

        internal static void RefreshButtonVisuals(ItemPickerPanel panel)
        {
            if (!TryGetCenter(
                    panel,
                    out ItemPickerButton picker,
                    out CollectionProductionLogic collection,
                    out DistributionProductionLogic distribution))
            {
                return;
            }

            IReadOnlyList<Resource> selected = collection != null
                ? CenterState.GetResources(collection)
                : CenterState.GetResources(distribution);
            RefreshButtonVisuals(panel, selected);
        }

        private static bool TryGetCenter(
            ItemPickerPanel panel,
            out ItemPickerButton picker,
            out CollectionProductionLogic collection,
            out DistributionProductionLogic distribution)
        {
            picker = ItemPickerButtonField.GetValue(panel) as ItemPickerButton;
            collection = null;
            distribution = null;
            if (picker == null || WorldScripts.Inst == null)
            {
                return false;
            }

            CollectionInfoPanel collectionPanel = WorldScripts.Inst.collectionInfoPanel;
            if (collectionPanel != null && picker == collectionPanel.resourcePickerButton)
            {
                collection = CollectionCenterField.GetValue(collectionPanel) as CollectionProductionLogic;
                return collection != null;
            }

            DistributionInfoPanel distributionPanel = WorldScripts.Inst.distributionInfoPanel;
            if (distributionPanel != null && picker == distributionPanel.resourcePickerButton)
            {
                distribution =
                    DistributionCenterField.GetValue(distributionPanel) as DistributionProductionLogic;
                return distribution != null;
            }

            return false;
        }

        private static void RefreshButtonVisuals(
            ItemPickerPanel panel,
            IReadOnlyCollection<Resource> selected)
        {
            List<IItemPickerConfig> configs =
                (List<IItemPickerConfig>)ConfigListField.GetValue(panel);
            List<Button> buttons = (List<Button>)ButtonListField.GetValue(panel);
            for (int i = 0; i < buttons.Count && i < configs.Count; i++)
            {
                Resource resource = configs[i].GetItemObject() as Resource;
                buttons[i].image.color = selected.Contains(resource)
                    ? new Color(0.45f, 0.9f, 0.65f, 1f)
                    : Color.white;
            }
        }
    }

    [HarmonyPatch(typeof(ItemPickerPanel), "OnEnable")]
    internal static class MultiResourcePickerOpenPatch
    {
        private static void Postfix(ItemPickerPanel __instance)
        {
            MultiResourcePickerPatch.RefreshButtonVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(CollectionInfoPanel), "Awake")]
    internal static class CollectionNativeUiSetupPatch
    {
        private static void Postfix(CollectionInfoPanel __instance)
        {
            NativeCenterPanelController.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(DistributionInfoPanel), "Awake")]
    internal static class DistributionNativeUiSetupPatch
    {
        private static void Postfix(DistributionInfoPanel __instance)
        {
            NativeCenterPanelController.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(CollectionInfoPanel), nameof(CollectionInfoPanel.ShowForCollectionCenter))]
    internal static class CollectionNativeUiRefreshPatch
    {
        private static void Postfix(CollectionInfoPanel __instance)
        {
            NativeCenterPanelController.Attach(__instance);
            __instance.GetComponent<NativeCenterPanelController>()?.Refresh();
        }
    }

    [HarmonyPatch(typeof(DistributionInfoPanel), nameof(DistributionInfoPanel.ShowForDistributionCenter))]
    internal static class DistributionNativeUiRefreshPatch
    {
        private static void Postfix(DistributionInfoPanel __instance)
        {
            NativeCenterPanelController.Attach(__instance);
            __instance.GetComponent<NativeCenterPanelController>()?.Refresh();
        }
    }
}
