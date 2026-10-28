using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    [HarmonyPatch(typeof(StationInfoPanel), "Update")]
    internal static class StationCargoTip
    {
        private static readonly FieldInfo StationField =
            AccessTools.Field(typeof(StationInfoPanel), "station");

        private static readonly FieldInfo PanelsField =
            AccessTools.Field(typeof(StationInfoPanel), "resourceInfoPanels");

        private static bool logged;

        private static void Postfix(StationInfoPanel __instance)
        {
            if (__instance == null || !__instance.isActiveAndEnabled)
            {
                return;
            }

            if (StationField == null || PanelsField == null || !CargoRoutes.FieldsReady())
            {
                LogOnce("车站货物提示未挂钩：找不到面板字段。");
                return;
            }

            Building station = StationField.GetValue(__instance) as Building;
            List<ResourceInfoPanel> panels = PanelsField.GetValue(__instance) as List<ResourceInfoPanel>;
            if (station == null || !station.IsValid() || station.stationModule == null || panels == null)
            {
                return;
            }

            Dictionary<string, List<CargoRoutes.Route>> routes = CargoRoutes.Collect(station.stationModule.carsWaitingForRide);
            CargoRoutes.Apply(panels, routes);
        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogWarning("[StationTools] " + message);
        }
    }

    [HarmonyPatch(typeof(CarCarrierInfoPanel), "Update")]
    internal static class TrainCargoTip
    {
        private static readonly FieldInfo CarrierField =
            AccessTools.Field(typeof(CarCarrierInfoPanel), "carCarrier");

        private static readonly FieldInfo PanelsField =
            AccessTools.Field(typeof(CarCarrierInfoPanel), "resourceInfoPanels");

        private static bool logged;

        private static void Postfix(CarCarrierInfoPanel __instance)
        {
            if (__instance == null || !__instance.isActiveAndEnabled)
            {
                return;
            }

            if (CarrierField == null || PanelsField == null || !CargoRoutes.FieldsReady())
            {
                LogOnce("列车货物提示未挂钩：找不到面板字段。");
                return;
            }

            Train train = CarrierField.GetValue(__instance) as Train;
            List<ResourceInfoPanel> panels = PanelsField.GetValue(__instance) as List<ResourceInfoPanel>;
            if (train == null || !train.IsValid() || panels == null)
            {
                return;
            }

            Dictionary<string, List<CargoRoutes.Route>> routes = CargoRoutes.Collect(train.cars);
            CargoRoutes.Apply(panels, routes);
        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogWarning("[StationTools] " + message);
        }
    }

    internal static class CargoRoutes
    {
        private const int MaxLines = 12;

        private static readonly HashSet<TooltipHoverable> marked = new HashSet<TooltipHoverable>();

        private static readonly FieldInfo ResourceField =
            AccessTools.Field(typeof(ResourceInfoPanel), "resource");

        internal struct Route
        {
            public string Source;
            public string Target;
            public string NextStation;
            public int Amount;
        }

        internal static bool FieldsReady()
        {
            return ResourceField != null;
        }

        internal static void Apply(List<ResourceInfoPanel> panels, Dictionary<string, List<Route>> routes)
        {
            if (panels == null || routes == null)
            {
                return;
            }

            for (int i = 0; i < panels.Count; i++)
            {
                Apply(panels[i], routes);
            }
        }

        internal static Dictionary<string, List<Route>> Collect(List<Pair<Reference<Car>, long>> waiting)
        {
            if (waiting == null)
            {
                return new Dictionary<string, List<Route>>();
            }

            Pair<Reference<Car>, long>[] snapshot;
            try
            {
                snapshot = waiting.ToArray();
            }
            catch (Exception)
            {
                return null;
            }

            Dictionary<string, List<Route>> routes = new Dictionary<string, List<Route>>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Car car = snapshot[i].first == null ? null : snapshot[i].first.Get();
                AddCar(routes, car);
            }

            return routes;
        }

        internal static Dictionary<string, List<Route>> Collect(List<Reference<Car>> cars)
        {
            if (cars == null)
            {
                return new Dictionary<string, List<Route>>();
            }

            Reference<Car>[] snapshot;
            try
            {
                snapshot = cars.ToArray();
            }
            catch (Exception)
            {
                return null;
            }

            Dictionary<string, List<Route>> routes = new Dictionary<string, List<Route>>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Car car = snapshot[i] == null ? null : snapshot[i].Get();
                AddCar(routes, car);
            }

            return routes;
        }

        private static void Apply(ResourceInfoPanel panel, Dictionary<string, List<Route>> routes)
        {
            if (panel == null || !panel.gameObject.activeSelf || panel.tooltip == null)
            {
                return;
            }

            Resource resource = ResourceField.GetValue(panel) as Resource;
            if (resource == null || string.IsNullOrEmpty(resource.name))
            {
                return;
            }

            List<Route> matches;
            if (!routes.TryGetValue(resource.name, out matches) || matches.Count == 0)
            {
                return;
            }

            panel.tooltip.Init(resource.GetNameInGame(), Format(matches));
            Mark(panel.tooltip);
        }

        internal static void Mark(TooltipHoverable tip)
        {
            if (tip != null)
            {
                marked.Add(tip);
            }
        }

        internal static bool IsMarked(TooltipHoverable tip)
        {
            return tip != null && marked.Contains(tip);
        }

        private static void AddCar(Dictionary<string, List<Route>> routes, Car car)
        {
            if (car == null || !car.IsValid() || car.resource == null || car.amount <= 0 ||
                string.IsNullOrEmpty(car.resource.name))
            {
                return;
            }

            Building source = car.source == null ? null : car.source.Get();
            Building target = car.target == null ? null : car.target.Get();
            Add(routes, car.resource.name, BuildingLabel(source), BuildingLabel(target), NextStation(car), car.amount);
        }

        private static void Add(
            Dictionary<string, List<Route>> routes,
            string resourceName,
            string source,
            string target,
            string nextStation,
            int amount)
        {
            List<Route> matches;
            if (!routes.TryGetValue(resourceName, out matches))
            {
                matches = new List<Route>();
                routes[resourceName] = matches;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Source == source &&
                    matches[i].Target == target &&
                    matches[i].NextStation == nextStation)
                {
                    Route existing = matches[i];
                    existing.Amount += amount;
                    matches[i] = existing;
                    return;
                }
            }

            matches.Add(new Route
            {
                Source = source,
                Target = target,
                NextStation = nextStation,
                Amount = amount,
            });
        }

        private static string NextStation(Car car)
        {
            if (car.path == null)
            {
                return null;
            }

            if (car.currentHost is Train train)
            {
                return TrainNextStation(train, car);
            }

            return RailDestination(car.path, car.currentPathIndex);
        }

        private static string TrainNextStation(Train train, Car car)
        {
            int index = car.currentPathIndex;
            if (train.stationWaitingTime > 0)
            {
                string upcoming = RailDestination(car.path, index);
                if (!string.IsNullOrEmpty(upcoming))
                {
                    return upcoming;
                }

                return RailDestination(car.path, index - 1);
            }

            string riding = RailDestination(car.path, index - 1);
            if (!string.IsNullOrEmpty(riding))
            {
                return riding;
            }

            return RailDestination(car.path, index);
        }

        private static string RailDestination(List<PathPart> path, int index)
        {
            if (path == null || index < 0 || index >= path.Count)
            {
                return null;
            }

            if (!(path[index] is RailPathPart rail))
            {
                return null;
            }

            return BuildingLabel(rail.toStation);
        }

        private static string Format(List<Route> matches)
        {
            matches.Sort((left, right) => right.Amount.CompareTo(left.Amount));
            int shown = matches.Count < MaxLines ? matches.Count : MaxLines;
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    text.Append('\n');
                }

                text.Append(matches[i].Source);
                text.Append(" → ");
                text.Append(matches[i].Target);
                if (!string.IsNullOrEmpty(matches[i].NextStation))
                {
                    text.Append("  下一站 ");
                    text.Append(matches[i].NextStation);
                }

                text.Append("  ");
                text.Append(matches[i].Amount);
            }

            if (matches.Count > shown)
            {
                text.Append("\n还有 ");
                text.Append(matches.Count - shown);
                text.Append(" 条");
            }

            return text.ToString();
        }

        private static string BuildingLabel(Building building)
        {
            if (building == null)
            {
                return "未知地点";
            }

            if (!string.IsNullOrEmpty(building.customName))
            {
                return building.customName;
            }

            BuildingDescription description = building.GetDescription();
            string localized = description == null ? null : description.GetNameInGame();
            return string.IsNullOrEmpty(localized) ? "未知地点" : localized;
        }
    }
}
