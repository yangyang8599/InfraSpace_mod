using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class ProducerCounts
    {
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly List<Resource> Products = new List<Resource>();
        private static readonly HashSet<string> Seen = new HashSet<string>();

        private static int censusFrame = -1;

        internal static int Count(Resource resource)
        {
            EnsureFresh();
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

        private static void EnsureFresh()
        {
            if (censusFrame >= 0 && Time.frameCount - censusFrame < 20)
            {
                return;
            }

            Counts.Clear();
            IEnumerable<Building> buildings = WorldScripts.Inst?.registry?.builtBuildings;
            if (buildings != null)
            {
                foreach (Building building in buildings)
                {
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
