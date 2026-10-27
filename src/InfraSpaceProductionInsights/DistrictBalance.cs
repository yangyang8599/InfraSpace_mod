using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class DistrictBalance
    {
        // 原版每帧扣 1.6e-5 耐久。面板上的产量和消耗是每分钟，50 帧/秒。
        private const double MaintenancePerMinute = 1.6e-5d * 50d * 60d;

        private static readonly Dictionary<string, Output> Outputs = new Dictionary<string, Output>();

        private static int frame = -1;
        private static byte cachedDistrict;
        private static double robotUse;
        private static double toolUse;

        internal static void Ensure(byte district)
        {
            if (cachedDistrict == district && frame >= 0 && Time.frameCount - frame < 15)
            {
                return;
            }

            frame = Time.frameCount;
            cachedDistrict = district;
            robotUse = 0d;
            toolUse = 0d;
            Outputs.Clear();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return;
            }

            foreach (Building building in buildings)
            {
                if (!ProducerCounts.InDistrict(building, district) || !building.IsValidAndBuilt())
                {
                    continue;
                }

                CountMaintenance(building);
                CountOutputs(building);
            }
        }

        internal static bool IsMaintenance(Resource resource)
        {
            return resource != null &&
                   (resource.name == "industrialRobot" || resource.name == "highTechTool");
        }

        internal static double MaintenanceUse(Resource resource)
        {
            if (resource == null)
            {
                return 0d;
            }

            if (resource.name == "industrialRobot")
            {
                return robotUse;
            }

            if (resource.name == "highTechTool")
            {
                return toolUse;
            }

            return 0d;
        }

        internal static double ScannedProduction(Resource resource)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name) ||
                !Outputs.TryGetValue(resource.name, out Output output))
            {
                return 0d;
            }

            return output.Total;
        }

        internal static bool TryLargest(Resource resource, out double largest)
        {
            largest = 0d;
            if (resource == null || string.IsNullOrEmpty(resource.name) ||
                !Outputs.TryGetValue(resource.name, out Output output) ||
                output.Largest <= 0d)
            {
                return false;
            }

            largest = output.Largest;
            return true;
        }

        private static void CountMaintenance(Building building)
        {
            List<BuildingUpgrade> upgrades = building.upgrades;
            if (upgrades == null)
            {
                return;
            }

            for (int i = 0; i < upgrades.Count; i++)
            {
                BuildingUpgrade upgrade = upgrades[i];
                if (upgrade == null)
                {
                    continue;
                }

                if (upgrade.upgradeType == BuildingUpgrade.industrialRobots)
                {
                    robotUse += MaintenancePerMinute;
                }
                else if (upgrade.upgradeType == BuildingUpgrade.highTechTools)
                {
                    toolUse += MaintenancePerMinute;
                }
            }
        }

        private static void CountOutputs(Building building)
        {
            if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory) ||
                factory.productionDefinition == null ||
                factory.productionDefinition.timeSteps <= 0)
            {
                return;
            }

            List<ResourceCost> products = factory.productionDefinition.producables;
            if (products == null)
            {
                return;
            }

            int speed = Mathf.RoundToInt(100f * factory.GetEfficiency()) * 10;
            int timeSteps = factory.productionDefinition.timeSteps;
            for (int i = 0; i < products.Count; i++)
            {
                Resource product = products[i].resource;
                int amount = products[i].amount;
                if (product == null || string.IsNullOrEmpty(product.name) || amount <= 0 || speed <= 0)
                {
                    continue;
                }

                double rate = (double)(amount * 50 * 60 * speed) / timeSteps / 10d;
                if (rate <= 0d)
                {
                    continue;
                }

                if (!Outputs.TryGetValue(product.name, out Output output))
                {
                    output = new Output();
                    Outputs[product.name] = output;
                }

                output.Total += rate;
                if (rate > output.Largest)
                {
                    output.Largest = rate;
                }
            }
        }

        private sealed class Output
        {
            public double Total;

            public double Largest;
        }
    }
}
