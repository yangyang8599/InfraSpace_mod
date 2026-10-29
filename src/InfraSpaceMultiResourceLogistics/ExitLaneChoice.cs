using System.Collections.Generic;
using HarmonyLib;

namespace InfraSpaceMultiResourceLogistics
{
    // 物流中心自带多车道出口放车时，优先放到接到下一条路后、再下一个路口不用变道的车道。
    [HarmonyPatch(typeof(Building), nameof(Building.TryPutCarOnPath))]
    internal static class ExitLaneChoice
    {
        private static bool Prefix(Building __instance, Car car, bool ensureSpace, ref bool __result)
        {
            if (!IsLogisticsCenter(__instance) || car?.path == null || car.path.Count == 0 || DrivewayMatches(__instance, car))
            {
                return true;
            }

            bool matched = false;
            foreach (PathPart item in __instance.GetIntegratedPathPartsOutgoing())
            {
                if (!(item is RoadDirection direction) || direction.road?.forwardLanes == null || !car.path[0].Equals(item))
                {
                    continue;
                }

                matched = true;
                if (TryPlace(car, direction.road.forwardLanes, ensureSpace))
                {
                    __result = true;
                    return false;
                }
            }

            if (!matched)
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool TryPlace(Car car, List<Lane> lanes, bool ensureSpace)
        {
            int count = lanes.Count;
            if (count == 0)
            {
                return false;
            }

            int start = HelperInfraSpaceCore.GetRandomWS(count);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    Lane lane = lanes[(i + start) % count];
                    if ((pass == 0) != KeepsLane(lane, car))
                    {
                        continue;
                    }

                    if (lane.TryPushOntoLane(car, ensureSpace))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool KeepsLane(Lane lane, Car car)
        {
            if (lane == null || car.path.Count < 2)
            {
                return false;
            }

            Lane arrival = FirstContinuation(lane, car.path[1]);
            if (arrival == null)
            {
                return false;
            }

            return car.path.Count < 3 || Reaches(arrival, car.path[2]);
        }

        private static Lane FirstContinuation(Lane lane, PathPart next)
        {
            Intersection end = lane.GetEnd();
            if (end?.connections == null)
            {
                return null;
            }

            foreach (IntersectionConnection connection in end.connections)
            {
                if (connection?.incoming == null || connection.outgoing == null || connection.incoming.Get() != lane)
                {
                    continue;
                }

                Lane outgoing = connection.outgoing.Get();
                if (outgoing != null && outgoing.GetRoadDirection().Equals(next))
                {
                    return outgoing;
                }
            }

            return null;
        }

        private static bool Reaches(Lane lane, PathPart next)
        {
            Intersection end = lane.GetEnd();
            if (end == null)
            {
                return false;
            }

            List<Lane> nextLanes = new List<Lane>(4);
            end.GetNextLanes(lane, nextLanes);
            for (int i = 0; i < nextLanes.Count; i++)
            {
                Lane nextLane = nextLanes[i];
                if (nextLane != null && nextLane.GetRoadDirection().Equals(next))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DrivewayMatches(Building building, Car car)
        {
            SideRoadLane outgoing = building.outgoingLane;
            if (outgoing == null)
            {
                return false;
            }

            PathPart direction = outgoing.GetRoadDirection();
            return car.path[0].Equals(direction) || car.path[0].Equals(direction.GetReverse());
        }

        private static bool IsLogisticsCenter(Building building)
        {
            ProductionLogic logic = building?.consumerProducer?.productionLogic;
            return logic is CollectionProductionLogic || logic is DistributionProductionLogic;
        }
    }
}
