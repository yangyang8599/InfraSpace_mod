using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    [HarmonyPatch(typeof(CollectionProductionLogic), nameof(CollectionProductionLogic.AddConsumablesToList))]
    internal static class CollectionConsumablesPatch
    {
        private static bool Prefix(CollectionProductionLogic __instance, List<ResourceCost> consumables)
        {
            IReadOnlyList<Resource> resources = CenterState.GetResources(__instance);
            foreach (Resource resource in resources)
            {
                consumables.Add(new ResourceCost(resource, CenterState.GetCapacity(__instance, resource)));
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(DistributionProductionLogic), nameof(DistributionProductionLogic.AddConsumablesToList))]
    internal static class DistributionConsumablesPatch
    {
        private static bool Prefix(DistributionProductionLogic __instance, List<ResourceCost> consumables)
        {
            IReadOnlyList<Resource> resources = CenterState.GetResources(__instance);
            foreach (Resource resource in resources)
            {
                consumables.Add(new ResourceCost(resource, CenterState.GetCapacity(__instance, resource)));
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(CollectionProductionLogic), nameof(CollectionProductionLogic.UpdateSimulationThread))]
    internal static class CollectionSimulationPatch
    {
        private static bool Prefix(CollectionProductionLogic __instance)
        {
            MultiResourceLogisticsEngine.UpdateCollection(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(DistributionProductionLogic), nameof(DistributionProductionLogic.UpdateSimulationThread))]
    internal static class DistributionSimulationPatch
    {
        // 一种或多种物资都由 Mod 配送。返回 false，跳过原版只认主物资的模拟。
        private static bool Prefix(DistributionProductionLogic __instance)
        {
            MultiResourceLogisticsEngine.UpdateDistribution(__instance);
            return false;
        }

        private static void Postfix(DistributionProductionLogic __instance)
        {
            MultiResourceLogisticsEngine.RefreshDistributionRoadLinks(__instance);
        }
    }

    [HarmonyPatch(
        typeof(CollectionProductionLogic),
        nameof(CollectionProductionLogic.TryStartDeliveryToCollectionCenter))]
    internal static class CollectionPassiveDeliveryPatch
    {
        // 搜集中心一律自己拉取。返回 false，避免和生产建筑的原版推送各发一车。
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CollectionProductionLogic), nameof(CollectionProductionLogic.SetResource))]
    internal static class CollectionSetResourcePatch
    {
        private static bool Prefix(CollectionProductionLogic __instance, Resource resource)
        {
            CenterState.SetResources(__instance, new[] { resource });
            return false;
        }
    }

    [HarmonyPatch(typeof(DistributionProductionLogic), nameof(DistributionProductionLogic.SetResource))]
    internal static class DistributionSetResourcePatch
    {
        private static bool Prefix(DistributionProductionLogic __instance, Resource resource)
        {
            CenterState.SetResources(__instance, new[] { resource });
            return false;
        }
    }

    [HarmonyPatch(
        typeof(CollectionDistributionHookup),
        nameof(CollectionDistributionHookup.UpdateCollectionCenter))]
    internal static class CollectionReconnectPatch
    {
        // 按当前物资列表重建连接。返回 false，避免多种减成一种时原版只解开第一种物资。
        private static bool Prefix(CollectionProductionLogic collectionCenter, Registry registry)
        {
            MultiResourceLogisticsEngine.ReconnectCollection(collectionCenter, registry);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(CollectionDistributionHookup),
        nameof(CollectionDistributionHookup.UpdateDistributionCenter))]
    internal static class DistributionReconnectPatch
    {
        // 按当前物资列表重建连接。返回 false，避免多种减成一种时残留旧物资连接。
        private static bool Prefix(DistributionProductionLogic distributionCenter, Registry registry)
        {
            MultiResourceLogisticsEngine.ReconnectDistribution(distributionCenter, registry);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(CollectionDistributionHookup),
        nameof(CollectionDistributionHookup.UpdateConsumerProducer))]
    internal static class ConsumerProducerReconnectPatch
    {
        private static bool Prefix(ConsumerProducer consumerProducer, Registry registry)
        {
            MultiResourceLogisticsEngine.ReconnectConsumerProducer(consumerProducer, registry);
            return false;
        }
    }

    [HarmonyPatch(typeof(CollectionProductionLogic), nameof(CollectionProductionLogic.GetMaxStorage))]
    internal static class CenterStorageCapPatch
    {
        internal const int MaxCapacityPerResource = 2000;

        // 搜集中心和配送中心共用这个上限，覆盖原版的 1000 加研究加成。
        private static void Postfix(ref int __result)
        {
            __result = MaxCapacityPerResource;
        }
    }

    [HarmonyPatch(typeof(CollectionProductionLogic), nameof(CollectionProductionLogic.SetPlayerStorageLimit))]
    internal static class CollectionCapacityPatch
    {
        private static bool Prefix(CollectionProductionLogic __instance)
        {
            return CenterState.GetResources(__instance).Count <= 1;
        }
    }

    [HarmonyPatch(typeof(DistributionProductionLogic), nameof(DistributionProductionLogic.SetPlayerStorageLimit))]
    internal static class DistributionCapacityPatch
    {
        private static bool Prefix(DistributionProductionLogic __instance)
        {
            return CenterState.GetResources(__instance).Count <= 1;
        }
    }

    [HarmonyPatch(typeof(CollectionInfoPanel), nameof(CollectionInfoPanel.IsVisualizationSelectable))]
    internal static class CollectionSelectablePatch
    {
        private static void Postfix(
            CollectionInfoPanel __instance,
            Building building,
            ref bool __result,
            CollectionProductionLogic ___collectionCenter)
        {
            if (__result || ___collectionCenter == null)
            {
                return;
            }

            DistributionProductionLogic distribution =
                building.consumerProducer?.productionLogic as DistributionProductionLogic;
            if (distribution != null && CenterState.ShareResource(___collectionCenter, distribution))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(DistributionInfoPanel), nameof(DistributionInfoPanel.IsVisualizationSelectable))]
    internal static class DistributionSelectablePatch
    {
        private static void Postfix(
            Building building,
            ref bool __result,
            DistributionProductionLogic ___distributionCenter)
        {
            if (__result || ___distributionCenter == null)
            {
                return;
            }

            CollectionProductionLogic collection =
                building.consumerProducer?.productionLogic as CollectionProductionLogic;
            if (collection != null && CenterState.ShareResource(collection, ___distributionCenter))
            {
                __result = true;
            }
        }
    }

    internal static class RangeReachability
    {
        private const float CacheSeconds = 4f;

        private static readonly Dictionary<long, Reachability> Cache = new Dictionary<long, Reachability>();

        private static bool loggedFailure;

        private static Material unreachableMaterial;

        private struct Reachability
        {
            public bool CanReach;
            public int Misses;
            public float Time;
            public bool Known;
        }

        // 只给当前查看的物资上色。不读连接列表，避免多种物资的扫描进度把颜色带跑。
        internal static void Paint(
            Building center,
            Building covered,
            Resource resource,
            bool storageOnly,
            bool relevant,
            bool towardCenter,
            float radius,
            ref Material result)
        {
            VisualizationManager visualization = WorldScripts.Inst?.visualizationManager;
            if (visualization == null || center == null || covered == null)
            {
                return;
            }

            if (covered == center)
            {
                result = visualization.selectedDefaultMat;
                return;
            }

            if (resource == null || storageOnly || !relevant)
            {
                result = null;
                return;
            }

            float distance = Vector2.SqrMagnitude(
                covered.GetPosition().ToVector2XZ() - center.GetPosition().ToVector2XZ());
            if (distance > radius * radius)
            {
                result = visualization.scaleMatYellow;
                return;
            }

            Building source = towardCenter ? covered : center;
            Building destination = towardCenter ? center : covered;
            if (!TryGet(source, destination, out bool canReach))
            {
                result = null;
                return;
            }

            if (canReach)
            {
                result = visualization.scaleMatGreen;
                return;
            }

            Material unreachable = UnreachableMaterial(visualization);
            if (unreachable != null)
            {
                result = unreachable;
            }
        }

        internal static bool InRegistry(Building building, Resource resource, bool consumer)
        {
            ConsumerProducer owner = building?.consumerProducer;
            Registry registry = WorldScripts.Inst?.registry;
            if (owner == null || resource == null || registry == null)
            {
                return false;
            }

            Dictionary<Resource, HashSet<ConsumerProducer>> groups =
                consumer ? registry.consumers : registry.producers;
            return groups != null &&
                   groups.TryGetValue(resource, out HashSet<ConsumerProducer> owners) &&
                   owners != null &&
                   owners.Contains(owner);
        }

        internal static Resource ViewedResource(MonoBehaviour panel, Resource fallback)
        {
            NativeCenterPanelController controller = panel == null
                ? null
                : panel.GetComponent<NativeCenterPanelController>();
            if (controller != null && controller.TryGetCoverageResource(out Resource selected, out _))
            {
                return selected;
            }

            return fallback;
        }

        internal static bool TryGet(Building source, Building destination, out bool canReach)
        {
            canReach = false;
            if (!Cache.TryGetValue(Key(source, destination), out Reachability cached) || !cached.Known)
            {
                return false;
            }

            canReach = cached.CanReach;
            return true;
        }

        internal static bool NeedsQuery(Building source, Building destination)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            if (!Cache.TryGetValue(Key(source, destination), out Reachability cached) || !cached.Known)
            {
                return true;
            }

            return Time.unscaledTime - cached.Time >= CacheSeconds;
        }

        // 返回值表示已经显示的颜色需要刷新。单次失败不把原来能到的结果改成没路。
        internal static bool Query(Building source, Building destination)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            long key = Key(source, destination);
            Cache.TryGetValue(key, out Reachability cached);
            bool canReach = QueryTruckPath(source, destination);
            bool published = canReach;
            int misses = 0;
            if (cached.Known && cached.CanReach && !canReach && cached.Misses < 1)
            {
                published = true;
                misses = cached.Misses + 1;
            }

            bool changed = !cached.Known || cached.CanReach != published;
            Cache[key] = new Reachability
            {
                CanReach = published,
                Misses = misses,
                Time = Time.unscaledTime,
                Known = true,
            };
            return changed;
        }

        private static long Key(Building source, Building destination)
        {
            long sourceId = source == null ? 0L : source.GetID();
            long destinationId = destination == null ? 0L : destination.GetID();
            return (sourceId * 397) ^ destinationId;
        }

        private static Material UnreachableMaterial(VisualizationManager visualization)
        {
            if (unreachableMaterial != null)
            {
                return unreachableMaterial;
            }

            if (visualization.scaleMatGreen == null)
            {
                return null;
            }

            unreachableMaterial = new Material(visualization.scaleMatGreen)
            {
                color = new Color(1f, 0.08f, 0.06f, 1f),
            };
            return unreachableMaterial;
        }

        private static bool QueryTruckPath(Building source, Building destination)
        {
            if (!source.IsValid() || !destination.IsValid())
            {
                return false;
            }

            // 没有出口或入口车道时，寻路会把缺省方向当成查询并可能误判为可达。
            if (!source.HasAnyOutgoingLane() || !destination.HasAnyIncomingLane())
            {
                return false;
            }

            try
            {
                List<PathPart> path = PathFinderFacadeRoads.GetPath(source, destination);
                return path != null && path.Count > 0;
            }
            catch (Exception exception)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Debug.LogWarning(
                        "[InfraSpaceMultiResourceLogistics] 物流范围可达性检查失败，该建筑会标成红色。 " +
                        exception.Message);
                }

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CollectionInfoPanel), nameof(CollectionInfoPanel.GetCustomVisualizationMat))]
    internal static class CollectionRangeReachabilityPatch
    {
        // 搜集货车从生产建筑开往搜集中心。地图只显示当前查看的那种产出。
        private static void Postfix(
            CollectionInfoPanel __instance,
            Building building,
            ref Material __result,
            CollectionProductionLogic ___collectionCenter)
        {
            if (__instance == null || !__instance.isActiveAndEnabled || ___collectionCenter == null || building == null)
            {
                return;
            }

            Resource viewed = ___collectionCenter.resource;
            bool storageOnly = CenterState.IsStorageOnly(___collectionCenter, viewed);
            NativeCenterPanelController controller = __instance.GetComponent<NativeCenterPanelController>();
            if (controller != null &&
                controller.TryGetCoverageResource(out Resource selected, out bool selectedStorageOnly))
            {
                viewed = selected;
                storageOnly = selectedStorageOnly;
            }

            RangeReachability.Paint(
                ___collectionCenter.consumerProducer?.building,
                building,
                viewed,
                storageOnly,
                RangeReachability.InRegistry(building, viewed, false),
                true,
                CenterState.GetRadius(___collectionCenter, viewed),
                ref __result);
        }
    }

    [HarmonyPatch(typeof(DistributionInfoPanel), nameof(DistributionInfoPanel.GetCustomVisualizationMat))]
    internal static class DistributionRangeReachabilityPatch
    {
        // 配送货车从配送中心开往消费建筑。地图只显示当前查看的那种消耗。
        private static void Postfix(
            DistributionInfoPanel __instance,
            Building building,
            ref Material __result,
            DistributionProductionLogic ___distributionCenter)
        {
            if (__instance == null || !__instance.isActiveAndEnabled || ___distributionCenter == null || building == null)
            {
                return;
            }

            Resource viewed = ___distributionCenter.resource;
            bool storageOnly = CenterState.IsStorageOnly(___distributionCenter, viewed);
            NativeCenterPanelController controller = __instance.GetComponent<NativeCenterPanelController>();
            if (controller != null &&
                controller.TryGetCoverageResource(out Resource selected, out bool selectedStorageOnly))
            {
                viewed = selected;
                storageOnly = selectedStorageOnly;
            }

            RangeReachability.Paint(
                ___distributionCenter.consumerProducer?.building,
                building,
                viewed,
                storageOnly,
                RangeReachability.InRegistry(building, viewed, true),
                false,
                CenterState.GetRadius(___distributionCenter, viewed),
                ref __result);
        }
    }

    [HarmonyPatch(typeof(CollectionInfoPanel), "CollectionRadiusSliderChanged")]
    internal static class CollectionRadiusPerResourcePatch
    {
        // 滑块只写入当前查看的物资。原方法接着用这个数值重算覆盖并移动范围圈。
        private static void Prefix(
            CollectionInfoPanel __instance,
            float value,
            CollectionProductionLogic ___collectionCenter)
        {
            if (___collectionCenter == null)
            {
                return;
            }

            CenterState.RememberRadius(
                ___collectionCenter,
                RangeReachability.ViewedResource(__instance, ___collectionCenter.resource),
                value);
        }
    }

    [HarmonyPatch(typeof(DistributionInfoPanel), "CollectionRadiusSliderChanged")]
    internal static class DistributionRadiusPerResourcePatch
    {
        private static void Prefix(
            DistributionInfoPanel __instance,
            float value,
            DistributionProductionLogic ___distributionCenter)
        {
            if (___distributionCenter == null)
            {
                return;
            }

            CenterState.RememberRadius(
                ___distributionCenter,
                RangeReachability.ViewedResource(__instance, ___distributionCenter.resource),
                value);
        }
    }

    [HarmonyPatch(typeof(ConsumerProducer), "UpdateOutgoingTraffic")]
    internal static class ProducerOutgoingTrafficPatch
    {
        // 覆盖物资在搜集中心都满员时转公共供需。返回 false，跳过原版互斥分支。
        private static bool Prefix(ConsumerProducer __instance)
        {
            MultiResourceLogisticsEngine.UpdateProducerOutgoing(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(ConsumerProducer), "HandleIncomingCar")]
    internal static class IncomingStorageOverflowPatch
    {
        private static void Prefix(ConsumerProducer __instance, Car car, out int __state)
        {
            __state = int.MinValue;
            if (car == null || !AllowsOverflow(__instance))
            {
                return;
            }

            int index = __instance.GetConsumptionListIndex(car.resource);
            if (index < 0 || __instance.incomingStorage == null || index >= __instance.incomingStorage.Length)
            {
                return;
            }

            __state = __instance.incomingStorage[index];
        }

        private static void Postfix(ConsumerProducer __instance, Car car, int __state)
        {
            if (__state == int.MinValue || car == null || __instance?.incomingStorage == null)
            {
                return;
            }

            int index = __instance.GetConsumptionListIndex(car.resource);
            if (index < 0 || index >= __instance.incomingStorage.Length)
            {
                return;
            }

            int stored = __state + car.amount;
            __instance.incomingStorage[index] = stored < 0 ? 0 : stored;
        }

        private static bool AllowsOverflow(ConsumerProducer consumerProducer)
        {
            ProductionLogic logic = consumerProducer?.productionLogic;
            return logic is CollectionProductionLogic || logic is DistributionProductionLogic;
        }
    }
}
