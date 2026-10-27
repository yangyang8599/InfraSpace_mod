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

        internal static bool IsStorageOnly(CollectionProductionLogic center, Resource resource)
        {
            return IsStorageOnly(center?.consumerProducer, resource);
        }

        internal static bool IsStorageOnly(DistributionProductionLogic center, Resource resource)
        {
            return IsStorageOnly(center?.consumerProducer, resource);
        }

        internal static bool Moves(CollectionProductionLogic center, Resource resource)
        {
            return Listed(center?.consumerProducer, resource, center?.resource) &&
                   !IsStorageOnly(center, resource);
        }

        internal static bool Moves(DistributionProductionLogic center, Resource resource)
        {
            return Listed(center?.consumerProducer, resource, center?.resource) &&
                   !IsStorageOnly(center, resource);
        }

        internal static void SetStorageOnly(CollectionProductionLogic center, Resource resource, bool storageOnly)
        {
            if (!SetStorageOnly(center?.consumerProducer, resource, storageOnly))
            {
                return;
            }

            CollectionDistributionHookup.UpdateCollectionCenter(center, WorldScripts.Inst.registry);
        }

        internal static void SetStorageOnly(DistributionProductionLogic center, Resource resource, bool storageOnly)
        {
            if (!SetStorageOnly(center?.consumerProducer, resource, storageOnly))
            {
                return;
            }

            CollectionDistributionHookup.UpdateDistributionCenter(center, WorldScripts.Inst.registry);
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

        internal static float GetRadius(CollectionProductionLogic center, Resource resource)
        {
            return GetRadius(center?.consumerProducer, resource, center == null ? 0f : center.collectionRadius);
        }

        internal static float GetRadius(DistributionProductionLogic center, Resource resource)
        {
            return GetRadius(center?.consumerProducer, resource, center == null ? 0f : center.distributionRadius);
        }

        // 拖动滑块时记住当前物资的半径。第一次分开调节前，其他物资先记下中心原来的半径。
        internal static void RememberRadius(CollectionProductionLogic center, Resource resource, float radius)
        {
            if (center == null)
            {
                return;
            }

            RememberRadius(
                center.consumerProducer,
                resource,
                radius,
                center.collectionRadius,
                GetResources(center));
        }

        internal static void RememberRadius(DistributionProductionLogic center, Resource resource, float radius)
        {
            if (center == null)
            {
                return;
            }

            RememberRadius(
                center.consumerProducer,
                resource,
                radius,
                center.distributionRadius,
                GetResources(center));
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
            HashSet<Resource> collectionResources = new HashSet<Resource>();
            foreach (Resource resource in GetResources(collection))
            {
                if (!IsStorageOnly(collection, resource))
                {
                    collectionResources.Add(resource);
                }
            }

            foreach (Resource resource in GetResources(distribution))
            {
                if (!IsStorageOnly(distribution, resource) && collectionResources.Contains(resource))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Listed(ConsumerProducer consumerProducer, Resource resource, Resource fallback)
        {
            if (resource == null)
            {
                return false;
            }

            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null || consumerProducer?.building == null ||
                !mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) ||
                settings.resourceNames == null || settings.resourceNames.Count == 0)
            {
                return resource == fallback;
            }

            return settings.resourceNames.Contains(resource.name);
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
            if (settings.storageOnly != null)
            {
                settings.storageOnly.RemoveAll(name => !settings.resourceNames.Contains(name));
            }

            if (settings.radii != null)
            {
                List<string> removedRadii = settings.radii.Keys
                    .Where(name => !settings.resourceNames.Contains(name))
                    .ToList();
                foreach (string name in removedRadii)
                {
                    settings.radii.Remove(name);
                }
            }

            if (settings.capacities == null)
            {
                settings.capacities = new Dictionary<string, int>();
            }

            setPrimary(selected[0]);
            consumerProducer.ResetConsumablesProducables();
        }

        private static float GetRadius(ConsumerProducer consumerProducer, Resource resource, float fallback)
        {
            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (resource == null || consumerProducer?.building == null || mod == null ||
                !mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) ||
                settings?.radii == null ||
                !settings.radii.TryGetValue(resource.name, out float radius))
            {
                return fallback;
            }

            return radius;
        }

        private static void RememberRadius(
            ConsumerProducer consumerProducer,
            Resource resource,
            float radius,
            float sharedRadius,
            IReadOnlyList<Resource> resources)
        {
            if (resource == null || consumerProducer?.building == null)
            {
                return;
            }

            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null)
            {
                return;
            }

            long buildingId = consumerProducer.building.GetID();
            if (!mod.centers.TryGetValue(buildingId, out CenterSettings settings))
            {
                settings = new CenterSettings();
                mod.centers[buildingId] = settings;
            }

            if (settings.radii == null)
            {
                settings.radii = new Dictionary<string, float>();
            }

            if (settings.radii.Count == 0)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    Resource other = resources[i];
                    if (other == null || other.name == resource.name || settings.radii.ContainsKey(other.name))
                    {
                        continue;
                    }

                    settings.radii[other.name] = sharedRadius;
                }
            }

            settings.radii[resource.name] = radius;
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

        private static bool IsStorageOnly(ConsumerProducer consumerProducer, Resource resource)
        {
            if (resource == null || consumerProducer?.building == null)
            {
                return false;
            }

            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            return mod != null &&
                   mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) &&
                   settings?.storageOnly != null &&
                   settings.storageOnly.Contains(resource.name);
        }

        private static bool SetStorageOnly(ConsumerProducer consumerProducer, Resource resource, bool storageOnly)
        {
            if (resource == null || consumerProducer?.building == null)
            {
                return false;
            }

            MultiResourceLogisticsMod mod = MultiResourceLogisticsMod.Instance;
            if (mod == null ||
                !mod.centers.TryGetValue(consumerProducer.building.GetID(), out CenterSettings settings) ||
                settings?.resourceNames == null ||
                !settings.resourceNames.Contains(resource.name))
            {
                return false;
            }

            if (settings.storageOnly == null)
            {
                settings.storageOnly = new List<string>();
            }

            bool listed = settings.storageOnly.Contains(resource.name);
            if (storageOnly && !listed)
            {
                settings.storageOnly.Add(resource.name);
            }
            else if (!storageOnly && listed)
            {
                settings.storageOnly.Remove(resource.name);
            }

            return true;
        }
    }
}
