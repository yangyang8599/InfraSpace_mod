using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class DistrictBalance
    {
        // 原版每帧扣 1.6e-5 耐久。面板上的产量和消耗是每分钟，50 帧/秒。
        private const double MaintenancePerMinute = 1.6e-5d * 50d * 60d;

        private static readonly Dictionary<string, Output> Outputs = new Dictionary<string, Output>();

        private static readonly HashSet<string> ProducedByRecipe = new HashSet<string>();

        private static readonly HashSet<string> UsedByOtherRecipe = new HashSet<string>();

        private static readonly HashSet<string> OwnOutputs = new HashSet<string>();

        private static int frame = -1;
        private static byte cachedDistrict;
        private static double robotUse;
        private static double toolUse;
        private static int robotBuildings;
        private static int toolBuildings;

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
            robotBuildings = 0;
            toolBuildings = 0;
            Outputs.Clear();
            ProducedByRecipe.Clear();
            UsedByOtherRecipe.Clear();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return;
            }

            Resource robots = Resource.Get("industrialRobot");
            Resource tools = Resource.Get("highTechTool");
            foreach (Building building in buildings)
            {
                if (!ProducerCounts.InDistrict(building, district) || !building.IsValidAndBuilt())
                {
                    continue;
                }

                CountMaintenance(building, robots, tools);
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

        internal static int MaintenanceBuildings(Resource resource)
        {
            if (resource == null)
            {
                return 0;
            }

            if (resource.name == "industrialRobot")
            {
                return robotBuildings;
            }

            if (resource.name == "highTechTool")
            {
                return toolBuildings;
            }

            return 0;
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

        // 本地区有配方产出，且没有别的配方把它当原料。同一条产线消耗自己的产出不算。
        internal static bool IsRecipeFinal(Resource resource)
        {
            return resource != null &&
                   !string.IsNullOrEmpty(resource.name) &&
                   ProducedByRecipe.Contains(resource.name) &&
                   !UsedByOtherRecipe.Contains(resource.name);
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

        private static void CountMaintenance(Building building, Resource robots, Resource tools)
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

                if (upgrade.upgradeType == BuildingUpgrade.industrialRobots &&
                    InEffectiveCoverage(building, robots))
                {
                    robotUse += MaintenancePerMinute;
                    robotBuildings++;
                }
                else if (upgrade.upgradeType == BuildingUpgrade.highTechTools &&
                         InEffectiveCoverage(building, tools))
                {
                    toolUse += MaintenancePerMinute;
                    toolBuildings++;
                }
            }
        }

        // 游戏把落在配送半径内的消费者写进 connectedDistributionCenters。
        // 物流 Mod 会去掉只存储、以及半径外的链接，所以这里读到的就是当前有效覆盖。
        private static bool InEffectiveCoverage(Building building, Resource resource)
        {
            if (building == null || resource == null || !building.HasAnyIncomingLane())
            {
                return false;
            }

            Dictionary<Resource, List<DistributionProductionLogic>> links =
                building.consumerProducer?.connectedDistributionCenters;
            if (links == null ||
                !links.TryGetValue(resource, out List<DistributionProductionLogic> centers) ||
                centers == null)
            {
                return false;
            }

            for (int i = 0; i < centers.Count; i++)
            {
                Building source = centers[i]?.consumerProducer?.building;
                if (source != null &&
                    source.IsValidAndBuilt() &&
                    source.DoesWork() &&
                    source.HasAnyOutgoingLane())
                {
                    return true;
                }
            }

            return false;
        }

        private static void CountOutputs(Building building)
        {
            if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory) ||
                factory.productionDefinition == null)
            {
                return;
            }

            List<ResourceCost> products = factory.productionDefinition.producables;
            OwnOutputs.Clear();
            if (products != null)
            {
                for (int i = 0; i < products.Count; i++)
                {
                    Resource product = products[i].resource;
                    if (product == null || string.IsNullOrEmpty(product.name))
                    {
                        continue;
                    }

                    OwnOutputs.Add(product.name);
                    ProducedByRecipe.Add(product.name);
                }
            }

            List<ResourceCost> inputs = factory.productionDefinition.consumables;
            if (inputs != null)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    Resource input = inputs[i].resource;
                    if (input == null || string.IsNullOrEmpty(input.name) || OwnOutputs.Contains(input.name))
                    {
                        continue;
                    }

                    UsedByOtherRecipe.Add(input.name);
                }
            }

            if (products == null || factory.productionDefinition.timeSteps <= 0)
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
