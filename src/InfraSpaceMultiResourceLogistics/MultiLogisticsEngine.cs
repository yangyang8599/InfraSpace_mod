using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    internal static class MultiResourceLogisticsEngine
    {
        private const int MaxDeparturesPerUpdate = 10;

        private static readonly object CursorGate = new object();

        private static readonly object RoadGate = new object();

        private static readonly object ReachGate = new object();

        private const int ReachChecksPerFrame = 1;

        private static readonly Dictionary<long, int> nextDistributionIndex = new Dictionary<long, int>();

        private static readonly Dictionary<long, List<ConsumerProducer>> pendingConsumers =
            new Dictionary<long, List<ConsumerProducer>>();

        private static readonly Dictionary<long, int> recheckIndex = new Dictionary<long, int>();

        private static readonly Dictionary<long, int> lastReachQueryFrame = new Dictionary<long, int>();

        private static readonly Dictionary<long, Dictionary<long, bool>> distributionReachable =
            new Dictionary<long, Dictionary<long, bool>>();

        private static readonly Dictionary<long, Dictionary<long, int>> distributionReachMisses =
            new Dictionary<long, Dictionary<long, int>>();

        private static int distributionQueriesArmedFrame = -1;

        private static readonly Dictionary<CenterLink, bool> roadBlocked = new Dictionary<CenterLink, bool>();

        internal static void UpdateCollection(CollectionProductionLogic center)
        {
            if (!center.consumerProducer.building.IsValidAndBuilt())
            {
                return;
            }

            EnsureConsumables(center.consumerProducer, CenterState.GetResources(center));
            RefreshCollectionRoadLinks(center);
            int departures = ForwardToDistributionCenters(center, MaxDeparturesPerUpdate);
            if (departures < MaxDeparturesPerUpdate)
            {
                PullFromProducers(center, MaxDeparturesPerUpdate - departures);
            }
        }

        internal static void UpdateDistribution(DistributionProductionLogic center)
        {
            if (!center.consumerProducer.building.IsValidAndBuilt())
            {
                return;
            }

            // 读档重连发生在路网重建期间。那里寻路会和路网后处理互相等待，所以改到模拟跑起来后再查。
            AdvanceDistributionReach(center);
            if ((Old.GetSimulator().GetSimulationFrame() + center.consumerProducer.building.GetID()) % 20 != 0L)
            {
                return;
            }

            EnsureConsumables(center.consumerProducer, CenterState.GetResources(center));
            SendFromDistribution(center);
        }

        [ThreadStatic]
        private static List<Pair<ConsumerProducer, float>> publicConsumers;

        internal static void UpdateProducerOutgoing(ConsumerProducer producer)
        {
            Building building = producer?.building;
            if (building == null || producer.productionLogic == null)
            {
                return;
            }

            if ((Old.GetSimulator().simulationFrame + building.GetID()) % 50 != 0)
            {
                return;
            }

            List<Resource> producables = producer.productionLogic.GetProducables();
            if (producables == null || producer.outgoingStorage == null)
            {
                return;
            }

            bool laneBlocked = building.outgoingLane != null &&
                               !building.outgoingLane.CanPushOntoLane(true);
            int index = 0;
            foreach (Resource resource in producables)
            {
                if (index >= producer.outgoingStorage.Length)
                {
                    break;
                }

                PipeComponent pipe = building.GetPipeComponentForResource(resource);
                if (pipe != null)
                {
                    int moved = pipe.PutIntoComponent(producer.outgoingStorage[index]);
                    producer.outgoingStorage[index] -= moved;
                }
                else if (!laneBlocked &&
                         StorageHasOrder(producer, index, resource, out int orderAmount) &&
                         !ReserveForCollection(producer, resource, orderAmount) &&
                         TryDeliverSurplus(producer, index, resource, orderAmount))
                {
                    break;
                }

                index++;
            }
        }

        private static bool StorageHasOrder(
            ConsumerProducer producer,
            int index,
            Resource resource,
            out int orderAmount)
        {
            orderAmount = ConsumerProducer.GetOrderAmount(resource);
            return orderAmount > 0 && producer.outgoingStorage[index] >= orderAmount;
        }

        private static bool ReserveForCollection(
            ConsumerProducer producer,
            Resource resource,
            int orderAmount)
        {
            if (!producer.connectedCollectionCenters.TryGetValue(
                    resource,
                    out List<CollectionProductionLogic> centers) ||
                centers == null ||
                centers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < centers.Count; i++)
            {
                if (CenterCanAccept(centers[i], resource, orderAmount))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CenterCanAccept(
            CollectionProductionLogic center,
            Resource resource,
            int orderAmount)
        {
            ConsumerProducer storage = center?.consumerProducer;
            if (storage == null || IndexOf(storage.incomingResources, resource) < 0)
            {
                return false;
            }

            return HasCapacity(
                storage,
                resource,
                CenterState.GetCapacity(center, resource),
                orderAmount);
        }

        private static bool TryDeliverSurplus(
            ConsumerProducer producer,
            int storageIndex,
            Resource resource,
            int orderAmount)
        {
            List<Pair<ConsumerProducer, float>> targets = CollectSurplusTargets(producer, resource);
            if (targets.Count == 0)
            {
                return false;
            }

            targets.Sort((left, right) => left.second.CompareTo(right.second));
            int pathAttempts = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                ConsumerProducer destination = targets[i].first;
                if (!CanAcceptSurplus(destination, resource, orderAmount))
                {
                    continue;
                }

                pathAttempts++;
                if (pathAttempts > 12)
                {
                    break;
                }

                lock (producer)
                {
                    if (producer.outgoingStorage[storageIndex] < orderAmount)
                    {
                        return false;
                    }

                    CarDispatch result = DispatchCar(
                        producer.building,
                        destination.building,
                        resource,
                        orderAmount);
                    if (result == CarDispatch.Sent)
                    {
                        producer.outgoingStorage[storageIndex] -= orderAmount;
                        return true;
                    }

                    if (result == CarDispatch.NotPlaced)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static List<Pair<ConsumerProducer, float>> CollectSurplusTargets(
            ConsumerProducer producer,
            Resource resource)
        {
            List<Pair<ConsumerProducer, float>> targets = new List<Pair<ConsumerProducer, float>>();
            HashSet<long> seen = new HashSet<long>();
            if (publicConsumers == null)
            {
                publicConsumers = new List<Pair<ConsumerProducer, float>>();
            }

            WorldScripts.Inst.orderManager.GetInterestedConsumers(resource, publicConsumers, producer);
            for (int i = 0; i < publicConsumers.Count; i++)
            {
                Pair<ConsumerProducer, float> candidate = publicConsumers[i];
                ConsumerProducer consumer = candidate.first;
                if (candidate.second <= 0f ||
                    consumer?.building == null ||
                    consumer.productionLogic is CollectionProductionLogic ||
                    !seen.Add(consumer.building.GetID()))
                {
                    continue;
                }

                targets.Add(new Pair<ConsumerProducer, float>(
                    consumer,
                    producer.GetConsumptionPriorityQuick(consumer, candidate.second)));
            }

            Registry registry = WorldScripts.Inst.registry;
            if (registry?.consumers == null ||
                !registry.consumers.TryGetValue(resource, out HashSet<ConsumerProducer> consumers))
            {
                return targets;
            }

            foreach (ConsumerProducer consumer in consumers)
            {
                if (!(consumer?.productionLogic is StorehouseProductionLogic) ||
                    consumer.building == null ||
                    !consumer.building.IsValidAndBuilt() ||
                    !seen.Add(consumer.building.GetID()))
                {
                    continue;
                }

                targets.Add(new Pair<ConsumerProducer, float>(
                    consumer,
                    producer.GetConsumptionPriorityQuick(consumer, 0.25f)));
            }

            return targets;
        }

        private static bool CanAcceptSurplus(
            ConsumerProducer destination,
            Resource resource,
            int orderAmount)
        {
            if (destination?.building == null || !destination.building.IsValidAndBuilt())
            {
                return false;
            }

            if (destination.productionLogic is StorehouseProductionLogic storehouse &&
                (storehouse.consumables == null || !storehouse.consumables.Contains(resource)))
            {
                return false;
            }

            int index = IndexOf(destination.incomingResources, resource);
            if (index < 0 ||
                destination.incomingStorage == null ||
                index >= destination.incomingStorage.Length)
            {
                return false;
            }

            int capacity = destination.GetConsumables(false)[index].amount;
            int pending = destination.GetIncomingCarsResourceAmount(resource);
            int available = capacity - destination.incomingStorage[index] - pending;
            return CanReceiveTruck(destination, available, orderAmount);
        }

        private struct ScoredDemand
        {
            public ConsumerProducer Consumer;
            public Resource Resource;
            public float Score;
            public int TruckAmount;
            public long BuildingId;
        }

        private enum CarDispatch
        {
            Sent,
            NoPath,
            NotPlaced,
        }

        private static void SendFromDistribution(DistributionProductionLogic center)
        {
            List<ScoredDemand> demands = CollectScoredDemands(center);
            if (demands.Count == 0)
            {
                return;
            }

            demands.Sort(CompareDemand);
            Dictionary<string, int> stock = new Dictionary<string, int>();
            int departures = 0;
            for (int i = 0; i < demands.Count; i++)
            {
                if (departures >= MaxDeparturesPerUpdate)
                {
                    break;
                }

                ScoredDemand demand = demands[i];
                if (!stock.TryGetValue(demand.Resource.name, out int remaining))
                {
                    remaining = GetStorage(center.consumerProducer, demand.Resource);
                    stock[demand.Resource.name] = remaining;
                }

                if (remaining < demand.TruckAmount)
                {
                    continue;
                }

                int index = IndexOf(demand.Consumer.incomingResources, demand.Resource);
                if (index < 0)
                {
                    continue;
                }

                int pending = demand.Consumer.GetIncomingCarsResourceAmount(demand.Resource);
                int available = demand.Consumer.GetConsumables(false)[index].amount -
                                demand.Consumer.incomingStorage[index] -
                                pending;
                if (!CanReceiveTruck(demand.Consumer, available, demand.TruckAmount))
                {
                    continue;
                }

                CarDispatch result = DispatchCar(
                    center.consumerProducer.building,
                    demand.Consumer.building,
                    demand.Resource,
                    demand.TruckAmount);
                if (result == CarDispatch.NotPlaced)
                {
                    break;
                }

                if (result != CarDispatch.Sent)
                {
                    continue;
                }

                center.consumerProducer.RemoveFromIncomingStorage(demand.Resource, demand.TruckAmount);
                stock[demand.Resource.name] = remaining - demand.TruckAmount;
                departures++;
            }
        }

        private static List<ScoredDemand> CollectScoredDemands(DistributionProductionLogic center)
        {
            List<ScoredDemand> demands = new List<ScoredDemand>();
            HashSet<string> carried = new HashSet<string>();
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (resource != null && !CenterState.IsStorageOnly(center, resource))
                {
                    carried.Add(resource.name);
                }
            }

            List<DemandScores.ReportedDemand> reported = DemandScores.Copy(center);
            for (int i = 0; i < reported.Count; i++)
            {
                DemandScores.ReportedDemand report = reported[i];
                if (report.Resource == null ||
                    report.Consumer?.building == null ||
                    !report.Consumer.building.IsValidAndBuilt() ||
                    !carried.Contains(report.Resource.name) ||
                    !center.connectedConsumers.Contains(report.Consumer))
                {
                    continue;
                }

                int truckAmount = ConsumerProducer.GetOrderAmount(report.Resource);
                if (truckAmount <= 0)
                {
                    continue;
                }

                demands.Add(new ScoredDemand
                {
                    Consumer = report.Consumer,
                    Resource = report.Resource,
                    Score = report.Score,
                    TruckAmount = truckAmount,
                    BuildingId = report.Consumer.building.GetID(),
                });
            }

            return demands;
        }

        private static int CompareDemand(ScoredDemand left, ScoredDemand right)
        {
            int score = right.Score.CompareTo(left.Score);
            if (score != 0)
            {
                return score;
            }

            int building = left.BuildingId.CompareTo(right.BuildingId);
            if (building != 0)
            {
                return building;
            }

            return string.CompareOrdinal(left.Resource.name, right.Resource.name);
        }

        internal static void ReconnectCollection(CollectionProductionLogic center, Registry registry)
        {
            foreach (ConsumerProducer producer in center.connectedProducers.ToArray())
            {
                foreach (List<CollectionProductionLogic> centers in producer.connectedCollectionCenters.Values)
                {
                    centers.Remove(center);
                }
            }

            center.connectedProducers.Clear();
            if (!center.consumerProducer.building.IsValidAndBuilt())
            {
                return;
            }

            Vector2 position = center.consumerProducer.building.GetPosition().ToVector2XZ();
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (CenterState.IsStorageOnly(center, resource) ||
                    !registry.producers.TryGetValue(resource, out HashSet<ConsumerProducer> producers))
                {
                    continue;
                }

                foreach (ConsumerProducer producer in producers)
                {
                    float radius = CenterState.GetRadius(center, resource);
                    if (!producer.building.IsValidAndBuilt() ||
                        Vector2.SqrMagnitude(producer.building.GetPosition().ToVector2XZ() - position) >
                        radius * radius)
                    {
                        continue;
                    }

                    if (!producer.connectedCollectionCenters.TryGetValue(
                            resource, out List<CollectionProductionLogic> centers))
                    {
                        centers = new List<CollectionProductionLogic>();
                        producer.connectedCollectionCenters[resource] = centers;
                    }

                    if (!centers.Contains(center))
                    {
                        centers.Add(center);
                    }

                    center.connectedProducers.Add(producer);
                }
            }
        }

        internal static void ReconnectDistribution(DistributionProductionLogic center, Registry registry)
        {
            foreach (ConsumerProducer consumer in center.connectedConsumers.ToArray())
            {
                foreach (List<DistributionProductionLogic> centers in consumer.connectedDistributionCenters.Values)
                {
                    centers.Remove(center);
                }
            }

            center.connectedConsumers.Clear();
            if (!center.consumerProducer.building.IsValidAndBuilt())
            {
                return;
            }

            Building source = center.consumerProducer.building;
            List<ConsumerProducer> candidates = CollectConsumersInRange(center, source, registry);
            lock (ReachGate)
            {
                distributionQueriesArmedFrame = Time.frameCount;
                pendingConsumers[source.GetID()] = candidates;
                recheckIndex.Remove(source.GetID());
            }
        }

        internal static void ReconnectConsumerProducer(ConsumerProducer consumerProducer, Registry registry)
        {
            foreach (List<CollectionProductionLogic> centers in consumerProducer.connectedCollectionCenters.Values)
            {
                foreach (CollectionProductionLogic center in centers)
                {
                    center.connectedProducers.Remove(consumerProducer);
                }
            }

            foreach (List<DistributionProductionLogic> centers in consumerProducer.connectedDistributionCenters.Values)
            {
                foreach (DistributionProductionLogic center in centers)
                {
                    center.connectedConsumers.Remove(consumerProducer);
                }
            }

            consumerProducer.connectedCollectionCenters.Clear();
            consumerProducer.connectedDistributionCenters.Clear();
            if (!consumerProducer.building.IsValidAndBuilt())
            {
                return;
            }

            Vector2 position = consumerProducer.building.GetPosition().ToVector2XZ();
            foreach (Resource resource in consumerProducer.outgoingResources)
            {
                foreach (CollectionProductionLogic center in registry.collectionCenters)
                {
                    if (CenterState.Moves(center, resource) &&
                        IsInRange(position, center.consumerProducer.building, CenterState.GetRadius(center, resource)))
                    {
                        AddConnection(consumerProducer, resource, center);
                    }
                }
            }

            foreach (Resource resource in consumerProducer.incomingResources)
            {
                foreach (DistributionProductionLogic center in registry.distributionCenters)
                {
                    Building source = center.consumerProducer?.building;
                    if (!CenterState.Moves(center, resource) ||
                        !IsInRange(position, source, CenterState.GetRadius(center, resource)))
                    {
                        continue;
                    }

                    // 先排队，等模拟帧再确认货车能否到达。读档时不寻路。
                    EnqueueDistributionCandidate(source, consumerProducer);
                }
            }
        }

        internal static void RefreshDistributionRoadLinks(DistributionProductionLogic center)
        {
            Building destination = center?.consumerProducer?.building;
            if (destination == null || !destination.IsValidAndBuilt())
            {
                return;
            }

            foreach (Reference<Building> reference in center.connectedCollectionCenters.ToArray())
            {
                Building source = reference.Get();
                CollectionProductionLogic collection =
                    source?.consumerProducer?.productionLogic as CollectionProductionLogic;
                if (collection == null || !CenterState.ShareResource(collection, center))
                {
                    continue;
                }

                SetRoadBlocked(source.GetID(), destination.GetID(), !HasRoadPath(source, destination));
            }
        }

        internal static bool IsResourceRoadBlocked(CollectionProductionLogic center, Resource resource)
        {
            Building source = center?.consumerProducer?.building;
            if (source == null)
            {
                return false;
            }

            foreach (Reference<Building> reference in center.connectedDistributionCenters.ToArray())
            {
                Building destination = reference.Get();
                DistributionProductionLogic distribution =
                    destination?.consumerProducer?.productionLogic as DistributionProductionLogic;
                if (distribution != null &&
                    CenterState.Moves(distribution, resource) &&
                    IsRoadBlocked(source.GetID(), destination.GetID()))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsResourceRoadBlocked(DistributionProductionLogic center, Resource resource)
        {
            Building destination = center?.consumerProducer?.building;
            if (destination == null)
            {
                return false;
            }

            foreach (Reference<Building> reference in center.connectedCollectionCenters.ToArray())
            {
                Building source = reference.Get();
                CollectionProductionLogic collection =
                    source?.consumerProducer?.productionLogic as CollectionProductionLogic;
                if (collection != null &&
                    CenterState.Moves(collection, resource) &&
                    IsRoadBlocked(source.GetID(), destination.GetID()))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ForwardToDistributionCenters(CollectionProductionLogic center, int limit)
        {
            Reference<Building>[] links = center.connectedDistributionCenters.ToArray();
            if (links.Length == 0)
            {
                return 0;
            }

            long buildingId = center.consumerProducer.building.GetID();
            int index;
            lock (CursorGate)
            {
                if (!nextDistributionIndex.TryGetValue(buildingId, out index))
                {
                    index = -1;
                }
            }

            int departures = 0;
            int idle = 0;
            while (departures < limit && idle < links.Length)
            {
                index = (index + 1) % links.Length;
                if (TrySendToDistribution(center, links[index].Get()))
                {
                    departures++;
                    idle = 0;
                    lock (CursorGate)
                    {
                        nextDistributionIndex[buildingId] = index;
                    }
                }
                else
                {
                    idle++;
                }
            }

            return departures;
        }

        private static bool TrySendToDistribution(CollectionProductionLogic center, Building building)
        {
            DistributionProductionLogic distribution =
                building?.consumerProducer?.productionLogic as DistributionProductionLogic;
            if (distribution == null || !CenterState.ShareResource(center, distribution))
            {
                return false;
            }

            if (IsRoadBlocked(center.consumerProducer.building.GetID(), building.GetID()))
            {
                return false;
            }

            EnsureConsumables(distribution.consumerProducer, CenterState.GetResources(distribution));
            Resource resource = SelectResourceToSend(center, distribution);
            if (resource == null)
            {
                return false;
            }

            int orderAmount = ConsumerProducer.GetOrderAmount(resource);
            if (!TryStartCar(center.consumerProducer.building, building, resource, orderAmount))
            {
                return false;
            }

            center.consumerProducer.RemoveFromIncomingStorage(resource, orderAmount);
            return true;
        }

        private static Resource SelectResourceToSend(
            CollectionProductionLogic center,
            DistributionProductionLogic distribution)
        {
            Resource selected = null;
            float bestFill = float.MaxValue;
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (CenterState.IsStorageOnly(center, resource) ||
                    CenterState.IsStorageOnly(distribution, resource) ||
                    !CenterState.Contains(distribution, resource) ||
                    IndexOf(distribution.consumerProducer.incomingResources, resource) < 0)
                {
                    continue;
                }

                int orderAmount = ConsumerProducer.GetOrderAmount(resource);
                if (orderAmount <= 0 ||
                    GetStorage(center.consumerProducer, resource) < orderAmount)
                {
                    continue;
                }

                int capacity = CenterState.GetCapacity(distribution, resource);
                if (!HasCapacity(distribution.consumerProducer, resource, capacity, orderAmount))
                {
                    continue;
                }

                int stored = GetStorage(distribution.consumerProducer, resource);
                int incoming = distribution.consumerProducer.GetIncomingCarsResourceAmount(resource);
                float fill = capacity <= 0 ? 1f : (float)(stored + incoming) / capacity;
                if (fill < bestFill)
                {
                    bestFill = fill;
                    selected = resource;
                }
            }

            return selected;
        }

        private static void RefreshCollectionRoadLinks(CollectionProductionLogic center)
        {
            Building source = center.consumerProducer.building;
            List<CenterLink> stale = new List<CenterLink>();
            lock (RoadGate)
            {
                foreach (CenterLink link in roadBlocked.Keys)
                {
                    if (link.SourceId == source.GetID())
                    {
                        stale.Add(link);
                    }
                }

                foreach (CenterLink link in stale)
                {
                    roadBlocked.Remove(link);
                }
            }

            foreach (Reference<Building> reference in center.connectedDistributionCenters.ToArray())
            {
                Building destination = reference.Get();
                DistributionProductionLogic distribution =
                    destination?.consumerProducer?.productionLogic as DistributionProductionLogic;
                if (distribution == null || !CenterState.ShareResource(center, distribution))
                {
                    continue;
                }

                SetRoadBlocked(source.GetID(), destination.GetID(), !HasRoadPath(source, destination));
            }
        }

        private static void SetRoadBlocked(long sourceId, long destinationId, bool blocked)
        {
            lock (RoadGate)
            {
                roadBlocked[new CenterLink(sourceId, destinationId)] = blocked;
            }
        }

        private static bool IsRoadBlocked(long sourceId, long destinationId)
        {
            lock (RoadGate)
            {
                return roadBlocked.TryGetValue(new CenterLink(sourceId, destinationId), out bool blocked) &&
                       blocked;
            }
        }

        private static bool HasRoadPath(Building source, Building destination)
        {
            if (source == null || destination == null || !source.IsValid() || !destination.IsValid())
            {
                return false;
            }

            if (QueryTruckPath(source, destination))
            {
                return true;
            }

            source.UpdateSideRoad(false, false);
            destination.UpdateSideRoad(false, false);
            return QueryTruckPath(source, destination);
        }

        private static bool QueryTruckPath(Building source, Building destination)
        {
            try
            {
                return PathFinderFacadeRoads.GetPath(source, destination) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EnsureConsumables(ConsumerProducer consumerProducer, IReadOnlyList<Resource> resources)
        {
            if (consumerProducer == null || resources == null || resources.Count == 0)
            {
                return;
            }

            Resource[] current = consumerProducer.incomingResources;
            if (current != null)
            {
                bool complete = true;
                for (int i = 0; i < resources.Count; i++)
                {
                    if (IndexOf(current, resources[i]) < 0)
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete)
                {
                    return;
                }
            }

            consumerProducer.ResetConsumablesProducables();
        }

        private readonly struct CenterLink : IEquatable<CenterLink>
        {
            public CenterLink(long sourceId, long destinationId)
            {
                SourceId = sourceId;
                DestinationId = destinationId;
            }

            public long SourceId { get; }

            public long DestinationId { get; }

            public bool Equals(CenterLink other)
            {
                return SourceId == other.SourceId && DestinationId == other.DestinationId;
            }

            public override bool Equals(object obj)
            {
                return obj is CenterLink other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (SourceId.GetHashCode() * 397) ^ DestinationId.GetHashCode();
                }
            }
        }

        private static void PullFromProducers(CollectionProductionLogic center, int limit)
        {
            int departures = 0;
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (CenterState.IsStorageOnly(center, resource))
                {
                    continue;
                }

                int orderAmount = ConsumerProducer.GetOrderAmount(resource);
                foreach (ConsumerProducer producer in center.connectedProducers.ToArray())
                {
                    if (!CenterCanAccept(center, resource, orderAmount))
                    {
                        break;
                    }

                    int resourceIndex = IndexOf(producer.outgoingResources, resource);
                    if (resourceIndex < 0)
                    {
                        continue;
                    }

                    lock (producer)
                    {
                        if (producer.outgoingStorage[resourceIndex] < orderAmount ||
                            !TryStartCar(producer.building, center.consumerProducer.building, resource, orderAmount))
                        {
                            continue;
                        }

                        producer.outgoingStorage[resourceIndex] -= orderAmount;
                    }

                    departures++;
                    if (departures >= limit)
                    {
                        return;
                    }
                }
            }
        }

        private static bool CanReceiveTruck(ConsumerProducer consumer, int available, int truckAmount)
        {
            if (available <= 0)
            {
                return false;
            }

            if (available >= truckAmount)
            {
                return true;
            }

            return consumer.productionLogic is FactoryProductionLogic factory &&
                   factory.HasSpaceshipLogicOverride();
        }

        private static bool HasCapacity(
            ConsumerProducer consumerProducer,
            Resource resource,
            int capacity,
            int amount)
        {
            return capacity - GetStorage(consumerProducer, resource) -
                   consumerProducer.GetIncomingCarsResourceAmount(resource) >= amount;
        }

        private static int GetStorage(ConsumerProducer consumerProducer, Resource resource)
        {
            int index = IndexOf(consumerProducer.incomingResources, resource);
            return index < 0 ? 0 : consumerProducer.incomingStorage[index];
        }

        private static bool TryStartCar(
            Building source,
            Building destination,
            Resource resource,
            int amount)
        {
            return DispatchCar(source, destination, resource, amount) == CarDispatch.Sent;
        }

        private static CarDispatch DispatchCar(
            Building source,
            Building destination,
            Resource resource,
            int amount)
        {
            if (source == null || destination == null || resource == null || amount <= 0)
            {
                return CarDispatch.NoPath;
            }

            List<PathPart> path = PathFinderFacadeRoads.GetPath(source, destination);
            if (path == null)
            {
                return CarDispatch.NoPath;
            }

            Car car = new Car(resource, amount, source, destination, path);
            if (source.TryPutCarOnPath(car, true))
            {
                return CarDispatch.Sent;
            }

            car.Destroy();
            return CarDispatch.NotPlaced;
        }

        private static int IndexOf(Resource[] resources, Resource resource)
        {
            return resources == null ? -1 : Array.IndexOf(resources, resource);
        }

        private static void AdvanceDistributionReach(DistributionProductionLogic center)
        {
            Building source = center.consumerProducer.building;
            if (!DistributionQueriesAllowed(source.GetID()))
            {
                return;
            }

            long centerId = source.GetID();
            RefillPendingIfDue(center, source);
            for (int i = 0; i < ReachChecksPerFrame; i++)
            {
                ConsumerProducer pending = TakePending(centerId);
                if (pending == null)
                {
                    RecheckOneDistributionLink(center, source);
                    return;
                }

                ApplyDistributionReach(center, source, pending);
            }
        }

        private static bool DistributionQueriesAllowed(long centerId)
        {
            if (distributionQueriesArmedFrame < 0 ||
                Time.frameCount <= distributionQueriesArmedFrame ||
                Time.timeScale <= 0f)
            {
                return false;
            }

            lock (ReachGate)
            {
                if (lastReachQueryFrame.TryGetValue(centerId, out int frame) && frame == Time.frameCount)
                {
                    return false;
                }

                lastReachQueryFrame[centerId] = Time.frameCount;
            }

            return true;
        }

        private static void ApplyDistributionReach(
            DistributionProductionLogic center,
            Building source,
            ConsumerProducer consumer)
        {
            Building destination = consumer?.building;
            long centerId = source.GetID();
            long buildingId = destination == null ? 0L : destination.GetID();
            bool inRange = destination != null &&
                           destination.IsValidAndBuilt() &&
                           CoveredByMovingResource(center, source, destination);
            // 离开半径立刻解开。连续两次寻路失败只保留仍在半径内、原先能到的连接。
            if (!inRange)
            {
                ClearDistributionReach(centerId, buildingId);
                UnlinkDistribution(center, consumer);
                return;
            }

            bool reachable = CanDeliverTo(source, destination);
            bool stable = RememberDistributionReach(centerId, buildingId, reachable);
            if (stable)
            {
                LinkDistribution(center, consumer);
                return;
            }

            UnlinkDistribution(center, consumer);
        }

        private static void RecheckOneDistributionLink(DistributionProductionLogic center, Building source)
        {
            ConsumerProducer[] linked = center.connectedConsumers.ToArray();
            if (linked.Length == 0)
            {
                return;
            }

            int index;
            lock (ReachGate)
            {
                recheckIndex.TryGetValue(source.GetID(), out index);
                if (index < 0 || index >= linked.Length)
                {
                    index = 0;
                }

                recheckIndex[source.GetID()] = index + 1;
            }

            ApplyDistributionReach(center, source, linked[index]);
        }

        private static void RefillPendingIfDue(DistributionProductionLogic center, Building source)
        {
            long frame = Old.GetSimulator().GetSimulationFrame();
            if ((frame + source.GetID()) % 300 != 0L)
            {
                return;
            }

            Registry registry = WorldScripts.Inst?.registry;
            if (registry == null)
            {
                return;
            }

            List<ConsumerProducer> candidates = CollectConsumersInRange(center, source, registry);
            lock (ReachGate)
            {
                if (pendingConsumers.TryGetValue(source.GetID(), out List<ConsumerProducer> pending) &&
                    pending.Count > 0)
                {
                    return;
                }

                pendingConsumers[source.GetID()] = candidates;
            }
        }

        private static List<ConsumerProducer> CollectConsumersInRange(
            DistributionProductionLogic center,
            Building source,
            Registry registry)
        {
            List<ConsumerProducer> candidates = new List<ConsumerProducer>();
            HashSet<ConsumerProducer> seen = new HashSet<ConsumerProducer>();
            Vector2 position = source.GetPosition().ToVector2XZ();
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (resource == null ||
                    CenterState.IsStorageOnly(center, resource) ||
                    !registry.consumers.TryGetValue(resource, out HashSet<ConsumerProducer> consumers))
                {
                    continue;
                }

                foreach (ConsumerProducer consumer in consumers)
                {
                    Building destination = consumer?.building;
                    if (destination != null &&
                        destination.IsValidAndBuilt() &&
                        IsInRange(position, destination, CenterState.GetRadius(center, resource)) &&
                        seen.Add(consumer))
                    {
                        candidates.Add(consumer);
                    }
                }
            }

            return candidates;
        }

        private static void EnqueueDistributionCandidate(Building source, ConsumerProducer consumer)
        {
            if (source == null || consumer == null)
            {
                return;
            }

            lock (ReachGate)
            {
                distributionQueriesArmedFrame = Time.frameCount;
                long centerId = source.GetID();
                if (!pendingConsumers.TryGetValue(centerId, out List<ConsumerProducer> pending))
                {
                    pending = new List<ConsumerProducer>();
                    pendingConsumers[centerId] = pending;
                }

                if (!pending.Contains(consumer))
                {
                    pending.Add(consumer);
                }
            }
        }

        private static ConsumerProducer TakePending(long centerId)
        {
            lock (ReachGate)
            {
                if (!pendingConsumers.TryGetValue(centerId, out List<ConsumerProducer> pending) ||
                    pending.Count == 0)
                {
                    return null;
                }

                int last = pending.Count - 1;
                ConsumerProducer consumer = pending[last];
                pending.RemoveAt(last);
                return consumer;
            }
        }

        private static void LinkDistribution(DistributionProductionLogic center, ConsumerProducer consumer)
        {
            Building source = center.consumerProducer?.building;
            Building destination = consumer?.building;
            Vector2 origin = source == null ? Vector2.zero : source.GetPosition().ToVector2XZ();
            bool linked = false;
            foreach (Resource resource in CenterState.GetResources(center))
            {
                bool serves = source != null &&
                              destination != null &&
                              !CenterState.IsStorageOnly(center, resource) &&
                              IndexOf(consumer.incomingResources, resource) >= 0 &&
                              IsInRange(origin, destination, CenterState.GetRadius(center, resource));
                if (!serves)
                {
                    RemoveDistributionResource(consumer, resource, center);
                    continue;
                }

                linked = true;
                if (consumer.connectedDistributionCenters != null &&
                    consumer.connectedDistributionCenters.TryGetValue(
                        resource,
                        out List<DistributionProductionLogic> centers) &&
                    centers != null &&
                    centers.Contains(center))
                {
                    if (!center.connectedConsumers.Contains(consumer))
                    {
                        center.connectedConsumers.Add(consumer);
                    }

                    continue;
                }

                AddConnection(consumer, resource, center);
            }

            if (!linked)
            {
                while (center.connectedConsumers.Remove(consumer))
                {
                }
            }
        }

        private static bool CoveredByMovingResource(
            DistributionProductionLogic center,
            Building source,
            Building destination)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            Vector2 origin = source.GetPosition().ToVector2XZ();
            Resource[] incoming = destination.consumerProducer?.incomingResources;
            foreach (Resource resource in CenterState.GetResources(center))
            {
                if (resource == null ||
                    CenterState.IsStorageOnly(center, resource) ||
                    IndexOf(incoming, resource) < 0)
                {
                    continue;
                }

                if (IsInRange(origin, destination, CenterState.GetRadius(center, resource)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveDistributionResource(
            ConsumerProducer consumer,
            Resource resource,
            DistributionProductionLogic center)
        {
            if (resource == null ||
                consumer?.connectedDistributionCenters == null ||
                !consumer.connectedDistributionCenters.TryGetValue(
                    resource,
                    out List<DistributionProductionLogic> centers))
            {
                return;
            }

            centers?.Remove(center);
        }

        private static bool RememberDistributionReach(long centerId, long buildingId, bool reachable)
        {
            if (buildingId == 0L)
            {
                return false;
            }

            lock (ReachGate)
            {
                if (!distributionReachable.TryGetValue(centerId, out Dictionary<long, bool> results))
                {
                    results = new Dictionary<long, bool>();
                    distributionReachable[centerId] = results;
                }

                if (!distributionReachMisses.TryGetValue(centerId, out Dictionary<long, int> misses))
                {
                    misses = new Dictionary<long, int>();
                    distributionReachMisses[centerId] = misses;
                }

                if (!results.TryGetValue(buildingId, out bool previous))
                {
                    results[buildingId] = reachable;
                    misses[buildingId] = reachable ? 0 : 1;
                    return reachable;
                }

                if (reachable)
                {
                    results[buildingId] = true;
                    misses[buildingId] = 0;
                    return true;
                }

                int streak = misses.TryGetValue(buildingId, out int count) ? count + 1 : 1;
                misses[buildingId] = streak;
                if (!previous && streak >= 1)
                {
                    results[buildingId] = false;
                    return false;
                }

                if (streak >= 2)
                {
                    results[buildingId] = false;
                    return false;
                }

                return previous;
            }
        }

        private static void ClearDistributionReach(long centerId, long buildingId)
        {
            if (buildingId == 0L)
            {
                return;
            }

            lock (ReachGate)
            {
                if (distributionReachable.TryGetValue(centerId, out Dictionary<long, bool> results))
                {
                    results.Remove(buildingId);
                }

                if (distributionReachMisses.TryGetValue(centerId, out Dictionary<long, int> misses))
                {
                    misses.Remove(buildingId);
                }
            }
        }

        private static void UnlinkDistribution(DistributionProductionLogic center, ConsumerProducer consumer)
        {
            while (center.connectedConsumers.Remove(consumer))
            {
            }

            if (consumer?.connectedDistributionCenters == null)
            {
                return;
            }

            foreach (List<DistributionProductionLogic> centers in consumer.connectedDistributionCenters.Values)
            {
                centers?.Remove(center);
            }
        }

        // 配送货车从配送中心开往消费建筑。没有车道时寻路可能误判为可达，所以先看进出口。
        private static bool CanDeliverTo(Building source, Building destination)
        {
            if (source == null || destination == null || !source.IsValid() || !destination.IsValid())
            {
                return false;
            }

            if (!source.HasAnyOutgoingLane() || !destination.HasAnyIncomingLane())
            {
                return false;
            }

            try
            {
                List<PathPart> path = PathFinderFacadeRoads.GetPath(source, destination);
                return path != null && path.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsInRange(Vector2 origin, Building building, float radius)
        {
            return building != null &&
                   building.IsValidAndBuilt() &&
                   Vector2.SqrMagnitude(building.GetPosition().ToVector2XZ() - origin) <= radius * radius;
        }

        private static void AddConnection(
            ConsumerProducer producer,
            Resource resource,
            CollectionProductionLogic center)
        {
            if (!producer.connectedCollectionCenters.TryGetValue(
                    resource, out List<CollectionProductionLogic> centers))
            {
                centers = new List<CollectionProductionLogic>();
                producer.connectedCollectionCenters[resource] = centers;
            }

            centers.Add(center);
            center.connectedProducers.Add(producer);
        }

        private static void AddConnection(
            ConsumerProducer consumer,
            Resource resource,
            DistributionProductionLogic center)
        {
            if (!consumer.connectedDistributionCenters.TryGetValue(
                    resource, out List<DistributionProductionLogic> centers))
            {
                centers = new List<DistributionProductionLogic>();
                consumer.connectedDistributionCenters[resource] = centers;
            }

            centers.Add(center);
            center.connectedConsumers.Add(consumer);
        }
    }
}
