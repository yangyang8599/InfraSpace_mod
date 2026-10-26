using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace InfraSpaceMultiResourceLogistics
{
    [HarmonyPatch(typeof(FactoryInfoPanel), "Update")]
    internal static class FactoryRecipeTipPatch
    {
        private static readonly FieldInfo LeftInfos =
            AccessTools.Field(typeof(ProductionInfoPanel), "resourceInfosLeft");

        private static readonly FieldInfo RightInfos =
            AccessTools.Field(typeof(ProductionInfoPanel), "resourceInfosRight");

        private static void Postfix(FactoryInfoPanel __instance, FactoryProductionLogic ___factory)
        {
            ConsumerProducer producer = ___factory?.consumerProducer;
            Building building = producer?.building;
            ProductionDefinition definition = ___factory?.productionDefinition;
            ProductionInfoPanel storage = __instance?.storagePanel;
            if (building == null || definition == null || storage == null ||
                LeftInfos == null || RightInfos == null)
            {
                return;
            }

            ApplyIncoming(LeftInfos.GetValue(storage) as List<ResourceInfoPanel>, building, definition.consumables);
            ApplyOutgoing(
                RightInfos.GetValue(storage) as List<ResourceInfoPanel>,
                producer,
                definition.producables);
        }

        private static void ApplyIncoming(
            List<ResourceInfoPanel> panels,
            Building building,
            List<ResourceCost> costs)
        {
            if (panels == null || costs == null)
            {
                return;
            }

            int count = panels.Count < costs.Count ? panels.Count : costs.Count;
            for (int i = 0; i < count; i++)
            {
                Resource resource = costs[i].resource;
                SetTip(panels[i], resource, TransitTip.DescribeIncoming(building, resource));
            }
        }

        private static void ApplyOutgoing(
            List<ResourceInfoPanel> panels,
            ConsumerProducer producer,
            List<ResourceCost> costs)
        {
            if (panels == null || costs == null)
            {
                return;
            }

            int count = panels.Count < costs.Count ? panels.Count : costs.Count;
            for (int i = 0; i < count; i++)
            {
                Resource resource = costs[i].resource;
                SetTip(panels[i], resource, DescribeOutput(producer, resource));
            }
        }

        private static string DescribeOutput(ConsumerProducer producer, Resource resource)
        {
            StringBuilder text = new StringBuilder();
            Building building = producer.building;
            if (building.GetPipeComponentForResource(resource) != null)
            {
                text.Append("通过管道输出");
                return text.ToString();
            }

            text.Append(TransitTip.DescribeOutgoing(building, resource));
            List<string> centers = CollectionTargets(producer, resource);
            if (centers.Count > 0)
            {
                text.Append("\n送往搜集中心");
                for (int i = 0; i < centers.Count; i++)
                {
                    text.Append("\n  ");
                    text.Append(centers[i]);
                }

                text.Append("\n装满后交给全局处理");
                return text.ToString();
            }

            if (WorldScripts.Inst.districtsManager.IsDistrictExporting(
                    resource,
                    WorldScripts.Inst.districtsManager.GetDistrict(building.GetPosition())))
            {
                text.Append("\n按地区出口");
                return text.ToString();
            }

            text.Append("\n交给公共供需");
            return text.ToString();
        }

        private static List<string> CollectionTargets(ConsumerProducer producer, Resource resource)
        {
            List<string> names = new List<string>();
            if (producer.connectedCollectionCenters == null ||
                !producer.connectedCollectionCenters.TryGetValue(
                    resource,
                    out List<CollectionProductionLogic> centers) ||
                centers == null)
            {
                return names;
            }

            for (int i = 0; i < centers.Count && names.Count < 8; i++)
            {
                Building center = centers[i]?.consumerProducer?.building;
                if (center != null && center.IsValidAndBuilt())
                {
                    names.Add(TransitTip.BuildingName(center));
                }
            }

            return names;
        }

        private static void SetTip(ResourceInfoPanel panel, Resource resource, string body)
        {
            if (panel == null || resource == null || panel.tooltip == null)
            {
                return;
            }

            panel.tooltip.Init(resource.GetNameInGame(), body ?? string.Empty);
        }
    }

    [HarmonyPatch(typeof(ResourceInfoPanel), nameof(ResourceInfoPanel.SetDeliveryMethod))]
    internal static class CollectionDeliveryMethodTipPatch
    {
        private static void Postfix(ResourceInfoPanel __instance, DeliveryMethod method)
        {
            if (method != DeliveryMethod.CollectionCenters || __instance?.deliveryTooltip == null)
            {
                return;
            }

            __instance.deliveryTooltip.Init(
                Localization.GetText("deliveryMethod"),
                "优先派送给搜集中心，其次给全局处理。");
        }
    }
}
