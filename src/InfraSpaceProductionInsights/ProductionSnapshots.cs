using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class ProductionSnapshots
    {
        // 满员配方。产线还没配平时，已经建好的建筑也按一座计，不看当前开工率。
        private const int FullRecipeSpeed = 1000;

        private static readonly Dictionary<string, RecipePick> Recipes = new Dictionary<string, RecipePick>();

        private static readonly Dictionary<string, double> HabitatUses = new Dictionary<string, double>();

        private static int habitatFrame = -1;

        private static byte habitatDistrict;

        private static double lastPowerOutput;

        private static bool recipesReady;

        internal static double LastPowerOutput
        {
            get { return lastPowerOutput; }
        }

        internal static double HabitatUse(byte district, string resourceName)
        {
            EnsureHabitat(district);
            if (string.IsNullOrEmpty(resourceName) || !HabitatUses.TryGetValue(resourceName, out double amount))
            {
                return 0d;
            }

            return amount;
        }

        internal static bool IsPlanningRaw(byte district, Resource resource, bool stopAtNoProduction)
        {
            if (resource == null || resource.name == "power" || resource.name == "organicWaste")
            {
                return true;
            }

            DistrictBalance.Ensure(district);
            if (resource.name == "methane" && !DistrictBalance.HasFermenter(district))
            {
                return true;
            }

            bool produced = DistrictBalance.HasProducer(resource);
            if ((resource.name == "industrialRobot" || resource.name == "highTechTool") && !produced)
            {
                return true;
            }

            return stopAtNoProduction && !produced;
        }

        internal static List<DemandPull.Node<Resource>> NominalNodes(byte district, bool includeHighTech, out double power)
        {
            power = 0d;
            lastPowerOutput = 0d;
            var nodes = new List<DemandPull.Node<Resource>>();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings == null)
            {
                return nodes;
            }

            foreach (Building building in buildings)
            {
                if (!ProducerCounts.InDistrict(building, district) || !building.IsValidAndBuilt())
                {
                    continue;
                }

                if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory) ||
                    factory.productionDefinition == null ||
                    factory.productionDefinition.timeSteps <= 0)
                {
                    continue;
                }

                int timeSteps = factory.productionDefinition.timeSteps;
                int speed = EquippedSpeed(factory, includeHighTech);
                if (factory.productionDefinition.powerNeeded > 0f && speed > 0)
                {
                    power += factory.productionDefinition.powerNeeded * speed / 1000d;
                }

                if (factory.productionDefinition.powerOutput > 0f && speed > 0)
                {
                    lastPowerOutput += factory.productionDefinition.powerOutput * speed / 1000d;
                }

                var node = new DemandPull.Node<Resource>();
                AddCosts(node.Inputs, factory.productionDefinition.consumables, timeSteps, speed);
                AddCosts(node.Outputs, factory.productionDefinition.producables, timeSteps, speed);
                if (node.Inputs.Count == 0 && node.Outputs.Count == 0)
                {
                    continue;
                }

                nodes.Add(node);
            }

            return nodes;
        }

        // 同一次反推里，一种建筑的配方只补一次。调用方每个地区、每一帧用一个新的委托。
        internal static System.Func<Resource, DemandPull.Node<Resource>> MissingRecipeScope()
        {
            EnsureRecipes();
            var used = new HashSet<string>();
            return resource =>
            {
                if (resource == null || string.IsNullOrEmpty(resource.name) ||
                    !Recipes.TryGetValue(resource.name, out RecipePick pick))
                {
                    return null;
                }

                if (!used.Add(pick.BuildingName))
                {
                    return null;
                }

                DemandPull.Node<Resource> node = NodeFromRecipe(pick.BuildingName);
                if (node == null)
                {
                    used.Remove(pick.BuildingName);
                    return null;
                }

                node.Gap = true;
                return node;
            };
        }

        private static void EnsureRecipes()
        {
            if (recipesReady)
            {
                return;
            }

            Dictionary<string, BuildingDescription> descriptions = Old.GetBuildingModule()?.buildingDescriptions;
            if (descriptions == null || descriptions.Count == 0)
            {
                return;
            }

            foreach (BuildingDescription description in descriptions.Values)
            {
                if (description == null || string.IsNullOrEmpty(description.name))
                {
                    continue;
                }

                if (!(description.GetCachedProductionLogic() is FactoryProductionLogic factory) ||
                    factory.productionDefinition == null ||
                    factory.productionDefinition.timeSteps <= 0 ||
                    factory.logicOverride == "terraformer")
                {
                    continue;
                }

                List<ResourceCost> products = factory.productionDefinition.producables;
                if (products == null)
                {
                    continue;
                }

                int outputs = 0;
                for (int i = 0; i < products.Count; i++)
                {
                    if (ResourceOf(products[i]) != null && products[i].amount > 0)
                    {
                        outputs++;
                    }
                }

                if (outputs == 0)
                {
                    continue;
                }

                for (int i = 0; i < products.Count; i++)
                {
                    Resource resource = ResourceOf(products[i]);
                    if (resource == null || string.IsNullOrEmpty(resource.name) || products[i].amount <= 0)
                    {
                        continue;
                    }

                    var pick = new RecipePick
                    {
                        BuildingName = description.name,
                        Outputs = outputs,
                        Amount = products[i].amount,
                    };
                    if (!Recipes.TryGetValue(resource.name, out RecipePick current) || Prefer(pick, current))
                    {
                        Recipes[resource.name] = pick;
                    }
                }
            }

            recipesReady = true;
        }

        private static bool Prefer(RecipePick candidate, RecipePick current)
        {
            if (candidate.Outputs != current.Outputs)
            {
                return candidate.Outputs < current.Outputs;
            }

            if (candidate.Amount != current.Amount)
            {
                return candidate.Amount > current.Amount;
            }

            return string.CompareOrdinal(candidate.BuildingName, current.BuildingName) < 0;
        }

        private static DemandPull.Node<Resource> NodeFromRecipe(string buildingName)
        {
            ProductionDefinitionManager manager = Old.GetBuildingModule()?.productionDefinitionManager;
            if (manager == null || string.IsNullOrEmpty(buildingName))
            {
                return null;
            }

            ProductionDefinition definition = manager.GetProductionDefinition(buildingName);
            if (definition == null || definition.timeSteps <= 0)
            {
                return null;
            }

            var node = new DemandPull.Node<Resource>();
            AddCosts(node.Inputs, definition.consumables, definition.timeSteps);
            AddCosts(node.Outputs, definition.producables, definition.timeSteps);
            if (node.Outputs.Count == 0)
            {
                return null;
            }

            return node;
        }

        // 满员配方加上已安装的产率加成。不看当前工人、电力或中间设备效率。
        private static void EnsureHabitat(byte district)
        {
            if (habitatFrame == Time.frameCount && habitatDistrict == district)
            {
                return;
            }

            habitatFrame = Time.frameCount;
            habitatDistrict = district;
            HabitatUses.Clear();
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

                if (!(building.consumerProducer?.productionLogic is Habitat habitat))
                {
                    continue;
                }

                AddHabitat(habitat.currentRequiredResources, habitat);
                AddHabitat(habitat.nextRequiredResources, habitat);
            }
        }

        private static void AddHabitat(List<HabitatResourceInfo> resources, Habitat habitat)
        {
            if (resources == null || habitat?.storage == null)
            {
                return;
            }

            for (int i = 0; i < resources.Count; i++)
            {
                HabitatResourceInfo info = resources[i];
                if (info?.resource == null || info.consumptionFactor <= 0d || !habitat.storage.ContainsKey(info.resource))
                {
                    continue;
                }

                double perMinute = habitat.GetConsumptionPerMinute(info);
                if (perMinute <= 0d || string.IsNullOrEmpty(info.resource.name))
                {
                    continue;
                }

                HabitatUses.TryGetValue(info.resource.name, out double current);
                HabitatUses[info.resource.name] = current + perMinute;
            }
        }

        private static int EquippedSpeed(FactoryProductionLogic factory, bool includeHighTech)
        {
            Building building = factory.consumerProducer.building;
            float wind = 1f;
            if (building.GetDescription()?.windEfficiencyRadius > 0f)
            {
                wind = building.GetWindEfficiency();
            }

            float neighbor = 1f;
            if (factory.productionDefinition.powerOutput <= 0f && building.efficiencyBoostedBy != null)
            {
                int count = 0;
                foreach (Building item in building.efficiencyBoostedBy)
                {
                    if (item != null && item.IsValid() && item.DoesWork())
                    {
                        count++;
                    }
                }

                neighbor += Building.GetEfficiencyBoost() * count;
            }

            float bonus = 1f;
            float tech = WorldScripts.Inst?.upgradeManager?.GetBonus("productionTimeUpgrade") ?? 0f;
            if (tech >= 1f)
            {
                bonus = tech;
            }

            float upgrades = 1f;
            List<BuildingUpgrade> installed = building.upgrades;
            if (installed != null)
            {
                for (int i = 0; i < installed.Count; i++)
                {
                    BuildingUpgrade upgrade = installed[i];
                    if (upgrade == null)
                    {
                        continue;
                    }

                    if (!includeHighTech && upgrade.upgradeType == BuildingUpgrade.highTechTools)
                    {
                        continue;
                    }

                    upgrades *= upgrade.GetProductionSpeedModifier(ignoreActive: true);
                }
            }

            int speed = (int)System.Math.Round(100d * wind * neighbor * bonus * upgrades) * 10;
            return speed > 0 ? speed : FullRecipeSpeed;
        }

        private static void AddCosts(Dictionary<Resource, double> values, List<ResourceCost> costs, int timeSteps, int speed = FullRecipeSpeed)
        {
            if (costs == null)
            {
                return;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                ResourceCost cost = costs[i];
                Resource resource = ResourceOf(cost);
                if (resource == null || cost.amount == 0)
                {
                    continue;
                }

                values.TryGetValue(resource, out double current);
                values[resource] = current + (double)(cost.amount * 50 * 60 * speed) / timeSteps / 10d;
            }
        }

        private static Resource ResourceOf(ResourceCost cost)
        {
            if (cost.resource != null)
            {
                return cost.resource;
            }

            if (string.IsNullOrEmpty(cost.resourceName))
            {
                return null;
            }

            return Resource.Get(cost.resourceName);
        }

        private sealed class RecipePick
        {
            public string BuildingName;

            public int Outputs;

            public int Amount;
        }
    }
}
