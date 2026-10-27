using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class ProducerCounts
    {
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly List<Resource> Products = new List<Resource>();
        private static readonly List<ResourceCost> Costs = new List<ResourceCost>();
        private static readonly HashSet<string> Seen = new HashSet<string>();

        private static int censusFrame = -1;
        private static byte censusDistrict;

        internal static int Count(Resource resource, byte district)
        {
            EnsureFresh(district);
            if (resource == null || string.IsNullOrEmpty(resource.name))
            {
                return 0;
            }

            return Counts.TryGetValue(resource.name, out int count) ? count : 0;
        }

        internal static bool Produces(Building building, Resource resource)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name))
            {
                return false;
            }

            Collect(building);
            for (int i = 0; i < Products.Count; i++)
            {
                Resource product = Products[i];
                if (product == resource || (product != null && product.name == resource.name))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool Demands(Building building, Resource resource)
        {
            if (building == null || resource == null || string.IsNullOrEmpty(resource.name) || !building.IsValidAndBuilt())
            {
                return false;
            }

            ProductionLogic logic = building.consumerProducer?.productionLogic;
            if (logic == null
                || logic is CollectionProductionLogic
                || logic is DistributionProductionLogic
                || logic is StorehouseProductionLogic
                || logic is LiquidTankProductionLogic)
            {
                return false;
            }

            if (logic is FactoryProductionLogic factory)
            {
                if (IsPower(resource)
                    && factory.productionDefinition != null
                    && factory.productionDefinition.powerNeeded > 0f)
                {
                    return true;
                }

                return Contains(factory.productionDefinition?.consumables, resource);
            }

            if (logic is Habitat habitat)
            {
                if (IsPower(resource) && habitat.powerNeededForTenPeople > 0f)
                {
                    return true;
                }

                return Contains(habitat.consumables, resource);
            }

            Costs.Clear();
            logic.AddConsumablesToList(Costs);
            return Contains(Costs, resource);
        }

        internal static bool InDistrict(Building building, byte district)
        {
            if (district == 0)
            {
                return true;
            }

            DistrictsManager districts = WorldScripts.Inst?.districtsManager;
            if (building == null || districts == null)
            {
                return false;
            }

            return districts.GetDistrict(building.GetPosition()) == district;
        }

        private static void EnsureFresh(byte district)
        {
            if (censusFrame >= 0 && censusDistrict == district && Time.frameCount - censusFrame < 20)
            {
                return;
            }

            Counts.Clear();
            censusDistrict = district;
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings != null)
            {
                foreach (Building building in buildings)
                {
                    if (!InDistrict(building, district))
                    {
                        continue;
                    }

                    Collect(building);
                    for (int i = 0; i < Products.Count; i++)
                    {
                        Resource product = Products[i];
                        if (product == null || string.IsNullOrEmpty(product.name))
                        {
                            continue;
                        }

                        Counts.TryGetValue(product.name, out int count);
                        Counts[product.name] = count + 1;
                    }
                }
            }

            censusFrame = Time.frameCount;
        }

        private static bool IsPower(Resource resource)
        {
            Resource power = Resource.Get("power");
            return power != null && (resource == power || resource.name == power.name);
        }

        private static bool Contains(List<ResourceCost> costs, Resource resource)
        {
            if (costs == null)
            {
                return false;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                Resource item = costs[i].resource;
                if (item == resource || (item != null && item.name == resource.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Collect(Building building)
        {
            Products.Clear();
            Seen.Clear();
            if (building == null || !building.IsValidAndBuilt())
            {
                return;
            }

            if (!(building.consumerProducer?.productionLogic is FactoryProductionLogic factory))
            {
                return;
            }

            if (factory.productionDefinition != null && factory.productionDefinition.powerOutput > 0f)
            {
                Add(Resource.Get("power"));
            }

            List<Resource> products = factory.GetProducables();
            if (products == null)
            {
                return;
            }

            for (int i = 0; i < products.Count; i++)
            {
                Add(products[i]);
            }
        }

        private static void Add(Resource resource)
        {
            if (resource == null || string.IsNullOrEmpty(resource.name) || !Seen.Add(resource.name))
            {
                return;
            }

            Products.Add(resource);
        }
    }
}
