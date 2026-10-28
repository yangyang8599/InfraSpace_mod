using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    [HarmonyPatch(typeof(TrainLine), nameof(TrainLine.AddTrain))]
    internal static class TrainRouteChoice
    {
        private const float DirectionTie = 0.05f;

        private static readonly FieldInfo AvailableRailsField =
            AccessTools.Field(typeof(TrainLine), "availableRailsByStation");

        private static bool Prefix(TrainLine __instance)
        {
            if (__instance == null || !__instance.CanAddTrain())
            {
                return false;
            }

            Rail rail;
            try
            {
                rail = ChooseSpawnRail(__instance);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[StationTools] 新增列车选线失败，改用游戏原逻辑：" + exception.Message);
                return true;
            }

            if (rail == null)
            {
                return true;
            }

            Train train = new Train(__instance);
            train.SetPosition(rail, 1000);
            __instance.trains.Add(train);
            if (__instance.trains.Count == 1)
            {
                WorldScripts.Inst.railPathfinder.SetNetworkDirty();
            }

            return false;
        }

        private static Rail ChooseSpawnRail(TrainLine line)
        {
            if (AvailableRailsField == null || line.trainStations.Count < 2)
            {
                return null;
            }

            List<List<Rail>> railsByStation = AvailableRailsField.GetValue(line) as List<List<Rail>>;
            if (railsByStation == null || railsByStation.Count == 0)
            {
                return null;
            }

            List<Rail> candidates = railsByStation[0];
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (railsByStation.Count > 1)
            {
                Rail byRoute = ChooseByNextStation(candidates, railsByStation[1]);
                if (byRoute != null)
                {
                    return byRoute;
                }
            }

            Building first = line.trainStations[0];
            Building second = line.trainStations[1];
            if (first == null || second == null)
            {
                return null;
            }

            Vector3 desired = second.GetPosition() - first.GetPosition();
            Dictionary<Rail, List<Rail>> groups = new Dictionary<Rail, List<Rail>>();
            Dictionary<Rail, float> alignment = new Dictionary<Rail, float>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Rail spawn = candidates[i];
                if (spawn == null)
                {
                    continue;
                }

                Rail departure = DepartureRail(spawn, first);
                float align = Alignment(Heading(departure), desired);
                List<Rail> group;
                if (!groups.TryGetValue(departure, out group))
                {
                    group = new List<Rail>();
                    groups[departure] = group;
                    alignment[departure] = align;
                }
                else if (align > alignment[departure])
                {
                    alignment[departure] = align;
                }

                group.Add(spawn);
            }

            if (groups.Count != 2)
            {
                return Helper.GetRandom(candidates);
            }

            Rail forward = null;
            Rail other = null;
            foreach (Rail key in groups.Keys)
            {
                if (forward == null || alignment[key] > alignment[forward])
                {
                    other = forward;
                    forward = key;
                }
                else
                {
                    other = key;
                }
            }

            if (forward == null || other == null || alignment[forward] - alignment[other] <= DirectionTie)
            {
                return Helper.GetRandom(candidates);
            }

            return Helper.GetRandom(groups[forward]);
        }

        private static Rail ChooseByNextStation(List<Rail> candidates, List<Rail> nextStationRails)
        {
            if (nextStationRails == null || nextStationRails.Count == 0 || WorldScripts.Inst?.railPathfinder == null)
            {
                return null;
            }

            int best = int.MaxValue;
            int[] distances = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                distances[i] = DistanceTo(candidates[i], nextStationRails);
                if (distances[i] < best)
                {
                    best = distances[i];
                }
            }

            if (best == int.MaxValue)
            {
                return null;
            }

            int limit = best + Math.Max(8, best / 10);
            List<Rail> matched = new List<Rail>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null && distances[i] <= limit)
                {
                    matched.Add(candidates[i]);
                }
            }

            if (matched.Count == 0)
            {
                return null;
            }

            return Helper.GetRandom(matched);
        }

        private static int DistanceTo(Rail start, List<Rail> goals)
        {
            if (start == null)
            {
                return int.MaxValue;
            }

            int best = int.MaxValue;
            for (int i = 0; i < goals.Count; i++)
            {
                Rail goal = goals[i];
                if (goal == null || goal == start)
                {
                    continue;
                }

                List<Rail> path = WorldScripts.Inst.railPathfinder.GetPath(start, goal);
                if (path == null)
                {
                    continue;
                }

                int distance = RailPathfinder.GetPathDist(path);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private static Rail DepartureRail(Rail spawn, Building station)
        {
            Rail current = spawn;
            for (int step = 0; step < 8; step++)
            {
                if (current == null)
                {
                    return spawn;
                }

                List<Rail> next = new List<Rail>();
                current.GetNextRails(next);
                if (next.Count != 1)
                {
                    return current;
                }

                Rail following = next[0];
                if (following == null || following.netEdge == null || following.netEdge.parentBuilding != station)
                {
                    return following ?? current;
                }

                current = following;
            }

            return current ?? spawn;
        }

        private static Vector3 Heading(Rail rail)
        {
            if (rail == null || rail.netEdge == null || rail.netEdge.startNode == null || rail.netEdge.endNode == null)
            {
                return Vector3.zero;
            }

            return rail.netEdge.endNode.GetPosition() - rail.netEdge.startNode.GetPosition();
        }

        private static float Alignment(Vector3 heading, Vector3 desired)
        {
            Vector2 headingFlat = new Vector2(heading.x, heading.z);
            Vector2 desiredFlat = new Vector2(desired.x, desired.z);
            if (headingFlat.sqrMagnitude < 0.25f || desiredFlat.sqrMagnitude < 1f)
            {
                return 0f;
            }

            return Vector2.Dot(headingFlat.normalized, desiredFlat.normalized);
        }
    }
}
