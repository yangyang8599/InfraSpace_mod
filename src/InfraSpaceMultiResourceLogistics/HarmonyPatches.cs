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

    [HarmonyPatch(typeof(CollectionInfoPanel), nameof(CollectionInfoPanel.GetCustomVisualizationMat))]
    internal static class CollectionRangeReachabilityPatch
    {
        private const float CacheSeconds = 1f;

        private static readonly Dictionary<long, Reachability> ReachabilityByProducer =
            new Dictionary<long, Reachability>();

        private static long cachedCenterId = long.MinValue;

        private static bool loggedFailure;

        private static Material unreachableMaterial;

        private struct Reachability
        {
            public bool CanReach;
            public float Time;
        }

        // 范围内已连接的生产建筑原版涂成绿色。没有货车路径时改成红色；黄色仍表示未覆盖。
        private static void Postfix(
            Building building,
            ref Material __result,
            CollectionProductionLogic ___collectionCenter)
        {
            VisualizationManager visualization = WorldScripts.Inst?.visualizationManager;
            Building center = ___collectionCenter?.consumerProducer?.building;
            if (visualization == null ||
                center == null ||
                building == null ||
                __result != visualization.scaleMatGreen)
            {
                return;
            }

            Material unreachable = UnreachableMaterial(visualization);
            if (unreachable != null && !CanTruckReach(building, center))
            {
                __result = unreachable;
            }
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

        private static bool CanTruckReach(Building producer, Building center)
        {
            long centerId = center.GetID();
            if (centerId != cachedCenterId)
            {
                ReachabilityByProducer.Clear();
                cachedCenterId = centerId;
            }

            long producerId = producer.GetID();
            float now = Time.unscaledTime;
            if (ReachabilityByProducer.TryGetValue(producerId, out Reachability cached) &&
                now - cached.Time < CacheSeconds)
            {
                return cached.CanReach;
            }

            bool canReach = QueryTruckPath(producer, center);
            ReachabilityByProducer[producerId] = new Reachability
            {
                CanReach = canReach,
                Time = now,
            };
            return canReach;
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
                        "[InfraSpaceMultiResourceLogistics] 搜集范围可达性检查失败，该建筑会标成红色。 " +
                        exception.Message);
                }

                return false;
            }
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
