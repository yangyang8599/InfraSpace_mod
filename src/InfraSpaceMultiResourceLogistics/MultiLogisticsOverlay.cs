using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    internal sealed class MultiResourceLogisticsOverlay : MonoBehaviour
    {
        private static readonly FieldInfo CollectionCenterField =
            AccessTools.Field(typeof(CollectionInfoPanel), "collectionCenter");

        private static readonly FieldInfo DistributionCenterField =
            AccessTools.Field(typeof(DistributionInfoPanel), "distributionCenter");

        private Vector2 scrollPosition;

        private void OnGUI()
        {
            if (WorldScripts.Inst == null)
            {
                return;
            }

            CollectionProductionLogic collection = GetCollectionCenter();
            DistributionProductionLogic distribution = collection == null ? GetDistributionCenter() : null;
            if (collection == null && distribution == null)
            {
                return;
            }

            Rect area = new Rect(Screen.width - 350f, 80f, 330f, Mathf.Min(Screen.height - 120f, 650f));
            GUILayout.BeginArea(area, GUI.skin.window);
            GUILayout.Label("<b>多物资物流中心</b>");
            GUILayout.Label("勾选该建筑要处理的物资；每种物资拥有面板所设的独立容量。");

            IReadOnlyList<Resource> selected = collection != null
                ? CenterState.GetResources(collection)
                : CenterState.GetResources(distribution);
            HashSet<Resource> selectedSet = new HashSet<Resource>(selected);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (Resource resource in GetAvailableResources(distribution != null))
            {
                bool wasSelected = selectedSet.Contains(resource);
                string label = resource.GetNameInGame();
                if (wasSelected)
                {
                    ConsumerProducer consumerProducer = collection != null
                        ? collection.consumerProducer
                        : distribution.consumerProducer;
                    int capacity = collection != null
                        ? collection.playerStorageLimit
                        : distribution.playerStorageLimit;
                    label += $"  {GetStorage(consumerProducer, resource)}/{capacity}";
                }

                bool isSelected = GUILayout.Toggle(wasSelected, label);
                if (isSelected != wasSelected)
                {
                    ApplyToggle(collection, distribution, selected, resource, isSelected);
                    break;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Label("至少保留一种物资。第一项为原版面板显示的主物资。");
            GUILayout.EndArea();
        }

        private static CollectionProductionLogic GetCollectionCenter()
        {
            CollectionInfoPanel panel = WorldScripts.Inst.collectionInfoPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                return null;
            }

            return CollectionCenterField.GetValue(panel) as CollectionProductionLogic;
        }

        private static DistributionProductionLogic GetDistributionCenter()
        {
            DistributionInfoPanel panel = WorldScripts.Inst.distributionInfoPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                return null;
            }

            return DistributionCenterField.GetValue(panel) as DistributionProductionLogic;
        }

        private static IEnumerable<Resource> GetAvailableResources(bool isDistribution)
        {
            return InfraSpaceCore.resourceModule.resources.Values
                .Where(resource => resource.canHaveExportRule && (!isDistribution || resource.name != "car"))
                .OrderBy(resource => resource.GetNameInGame());
        }

        private static void ApplyToggle(
            CollectionProductionLogic collection,
            DistributionProductionLogic distribution,
            IReadOnlyList<Resource> current,
            Resource changed,
            bool enabled)
        {
            List<Resource> resources = current.ToList();
            if (enabled)
            {
                resources.Add(changed);
            }
            else if (resources.Count > 1)
            {
                resources.Remove(changed);
            }
            else
            {
                return;
            }

            if (collection != null)
            {
                CenterState.SetResources(collection, resources);
            }
            else
            {
                CenterState.SetResources(distribution, resources);
            }

            WorldScripts.Inst.visualizationManager.Update(true);
        }

        private static int GetStorage(ConsumerProducer consumerProducer, Resource resource)
        {
            if (consumerProducer?.incomingResources == null || consumerProducer.incomingStorage == null)
            {
                return 0;
            }

            for (int i = 0; i < consumerProducer.incomingResources.Length; i++)
            {
                if (consumerProducer.incomingResources[i] == resource)
                {
                    return consumerProducer.incomingStorage[i];
                }
            }

            return 0;
        }
    }
}
