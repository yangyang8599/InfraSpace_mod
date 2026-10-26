using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    internal static class CenterState
    {
        internal static IReadOnlyList<Resource> GetResources(CollectionProductionLogic center)
        {
            return GetResources(center.consumerProducer, center.resource);
        }

        internal static IReadOnlyList<Resource> GetResources(DistributionProductionLogic center)
        {
            return GetResources(center.consumerProducer, center.resource);
        }

        internal static bool Contains(CollectionProductionLogic center, Resource resource)
        {
            return GetResources(center).Contains(resource);
        }

        internal static bool Contains(DistributionProductionLogic center, Resource resource)
        {
            return GetResources(center).Contains(resource);
        }

        internal static int GetCapacity(CollectionProductionLogic center, Resource resource)
        {
            return GetCapacity(center.consumerProducer, resource, center.playerStorageLimit);
        }

        internal static int GetCapacity(DistributionProductionLogic center, Resource resource)
        {
            return GetCapacity(center.consumerProducer, resource, center.playerStorageLimit);
        }

        internal static void SetCapacity(CollectionProductionLogic center, Resource resource, int capacity)
        {
            SetCapacity(center.consumerProducer, resource, capacity);
            if (GetResources(center).Count == 1)
            {
                center.playerStorageLimit = GetCapacity(center, resource);
            }
        }

        internal static void SetCapacity(DistributionProductionLogic center, Resource resource, int capacity)
        {
            SetCapacity(center.consumerProducer, resource, capacity);
            if (GetResources(center).Count == 1)
            {
                center.playerStorageLimit = GetCapacity(center, resource);
            }
        }

        internal static void SetResources(CollectionProductionLogic center, IEnumerable<Resource> resources)
        {
            List<Resource> selected = resources.ToList();
            Apply(center.consumerProducer, selected, resource => center.resource = resource);
            if (selected.Count == 1)
            {
                center.playerStorageLimit = GetCapacity(center, selected[0]);
                center.consumerProducer.ResetConsumablesProducables();
            }
            CollectionDistributionHookup.UpdateCollectionCenter(center, WorldScripts.Inst.registry);
        }

        internal static void SetResources(DistributionProductionLogic center, IEnumerable<Resource> resources)
        {
            List<Resource> selected = resources.ToList();
            Apply(center.consumerProducer, selected, resource => center.resource = resource);
            if (selected.Count == 1)
            {
                center.playerStorageLimit = GetCapacity(center, selected[0]);
                center.consumerProducer.ResetConsumablesProducables();
            }
            CollectionDistributionHookup.UpdateDistributionCenter(center, WorldScripts.Inst.registry);
        }

        internal static bool ShareResource(
            CollectionProductionLogic collection,
            DistributionProductionLogic distribution)
        {
            HashSet<Resource> collectionResources = new HashSet<Resource>(GetResources(collection));
            return GetResources(distribution).Any(collectionResources.Contains);
        }

        private static IReadOnlyList<Resource> GetResources(ConsumerProducer consumerProducer, Resource fallback)
        {
            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || consumerProducer?.building == null ||
                !mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) ||
                settings.resourceNames == null || settings.resourceNames.Count == 0)
            {
                return new[] { fallback };
            }

            List<Resource> resources = new List<Resource>();
            foreach (string resourceName in settings.resourceNames.Distinct())
            {
                Resource resource = Resource.Get(resourceName);
                if (resource != null)
                {
                    resources.Add(resource);
                }
            }

            if (resources.Count == 0)
            {
                resources.Add(fallback);
            }

            return resources;
        }

        private static void Apply(
            ConsumerProducer consumerProducer,
            IEnumerable<Resource> resources,
            Action<Resource> setPrimary)
        {
            List<Resource> selected = resources
                .Where(resource => resource != null)
                .Distinct()
                .ToList();
            if (selected.Count == 0)
            {
                return;
            }

            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || consumerProducer?.building == null)
            {
                return;
            }

            long buildingId = consumerProducer.building.GetID();
            if (!mod.centers.TryGetValue(buildingId, out CenterSettings settings))
            {
                settings = new CenterSettings();
                mod.centers[buildingId] = settings;
            }

            settings.resourceNames = selected.Select(resource => resource.name).ToList();
            if (settings.capacities == null)
            {
                settings.capacities = new Dictionary<string, int>();
            }

            setPrimary(selected[0]);
            consumerProducer.ResetConsumablesProducables();
        }

        private static int GetCapacity(
            ConsumerProducer consumerProducer,
            Resource resource,
            int fallback)
        {
            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || consumerProducer?.building == null ||
                !mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) ||
                settings.capacities == null ||
                !settings.capacities.TryGetValue(resource.name, out int capacity))
            {
                return fallback;
            }

            return capacity;
        }

        private static void SetCapacity(
            ConsumerProducer consumerProducer,
            Resource resource,
            int capacity)
        {
            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || consumerProducer?.building == null)
            {
                return;
            }

            long buildingId = consumerProducer.building.GetID();
            if (!mod.centers.TryGetValue(buildingId, out CenterSettings settings))
            {
                settings = new CenterSettings
                {
                    resourceNames = new List<string> { resource.name }
                };
                mod.centers[buildingId] = settings;
            }

            if (settings.capacities == null)
            {
                settings.capacities = new Dictionary<string, int>();
            }

            settings.capacities[resource.name] = Mathf.Clamp(
                capacity,
                CollectionProductionLogic.minStorageLimit,
                CollectionProductionLogic.GetMaxStorage());
            consumerProducer.ResetConsumablesProducables();
        }
    }
}
