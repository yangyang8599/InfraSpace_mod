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

    [HarmonyPatch(typeof(FactoryInfoPanel), "Update")]
    internal static class UpgradeDeliveryTipPatch
    {
        // 机器人和高科技工具不在配方图标上。提示里补上在途货车，以及当前是否在请求配送。
        private static void Postfix(
            FactoryProductionLogic ___factory,
            List<UpgradeItemPanel> ___upgradeItems)
        {
            ConsumerProducer producer = ___factory?.consumerProducer;
            Building building = producer?.building;
            if (building == null || ___upgradeItems == null)
            {
                return;
            }

            for (int i = 0; i < ___upgradeItems.Count; i++)
            {
                UpgradeItemPanel panel = ___upgradeItems[i];
                if (panel == null || !panel.isActiveAndEnabled || panel.upgrade == null || panel.tooltip == null)
                {
                    continue;
                }

                string body = DescribeUpgrade(producer, panel.upgrade);
                if (body == null)
                {
                    continue;
                }

                panel.tooltip.Init(panel.upgrade.GetNameInGame(), body);
            }
        }

        private static string DescribeUpgrade(ConsumerProducer producer, BuildingUpgrade upgrade)
        {
            Resource resource = MaintenanceResource(upgrade.upgradeType);
            Building building = producer.building;
            if (resource == null || building == null)
            {
                return null;
            }

            StringBuilder text = new StringBuilder();
            string description = upgrade.GetDescInGame();
            if (!string.IsNullOrEmpty(description))
            {
                text.Append(description);
                text.Append('\n');
            }

            text.Append(TransitTip.DescribeIncoming(building, resource));
            if (RequestedAmount(producer, resource) <= 0)
            {
                text.Append("\n暂不请求配送");
                return text.ToString();
            }

            if (building.GetPipeComponentForResource(resource) != null)
            {
                text.Append("\n通过管道配送");
                return text.ToString();
            }

            List<string> centers = DistributionSources(producer, resource);
            if (centers.Count > 0)
            {
                text.Append("\n由配送中心配送");
                for (int i = 0; i < centers.Count; i++)
                {
                    text.Append("\n  ");
                    text.Append(centers[i]);
                }

                return text.ToString();
            }

            if (WorldScripts.Inst.districtsManager.IsDistrictImporting(
                    resource,
                    WorldScripts.Inst.districtsManager.GetDistrict(building.GetPosition())))
            {
                text.Append("\n按地区进口");
                return text.ToString();
            }

            text.Append("\n由公共供需配送");
            return text.ToString();
        }

        private static Resource MaintenanceResource(string upgradeType)
        {
            if (upgradeType == BuildingUpgrade.industrialRobots)
            {
                return Resource.Get("industrialRobot");
            }

            if (upgradeType == BuildingUpgrade.highTechTools)
            {
                return Resource.Get("highTechTool");
            }

            return null;
        }

        private static int RequestedAmount(ConsumerProducer producer, Resource resource)
        {
            List<ResourceCost> costs = producer.GetConsumables(false);
            if (costs == null)
            {
                return 0;
            }

            int amount = 0;
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i].resource == resource)
                {
                    amount += costs[i].amount;
                }
            }

            return amount;
        }

        private static List<string> DistributionSources(ConsumerProducer producer, Resource resource)
        {
            List<string> names = new List<string>();
            if (producer.connectedDistributionCenters == null ||
                !producer.connectedDistributionCenters.TryGetValue(
                    resource,
                    out List<DistributionProductionLogic> centers) ||
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
    }
}
