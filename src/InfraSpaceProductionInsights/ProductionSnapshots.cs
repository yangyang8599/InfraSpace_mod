using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class ProductionSnapshots
    {
        private const long FreshFrames = 15;

        private static readonly object Gate = new object();

        private static readonly Dictionary<long, FactorySnapshot> Factories = new Dictionary<long, FactorySnapshot>();

        private static readonly Dictionary<long, HabitatSnapshot> Habitats = new Dictionary<long, HabitatSnapshot>();

        private static readonly MethodInfo ConsumptionPerWorker =
            AccessTools.Method(typeof(Habitat), "GetConsumptionPerTickAndWorker");

        internal static void RecordFactory(FactoryProductionLogic factory)
        {
            if (factory?.productionDefinition == null || factory.consumerProducer == null || factory.consumerProducer.building == null)
            {
                return;
            }

            Building building = factory.consumerProducer.building;
            int timeSteps = factory.productionDefinition.timeSteps;
            int speed = Mathf.RoundToInt(100f * factory.GetEfficiency()) * 10;
            var snapshot = new FactorySnapshot
            {
                Building = building,
                District = DistrictOf(building),
                Frame = Frame(),
            };
            if (timeSteps > 0)
            {
                foreach (ResourceCost cost in factory.productionDefinition.consumables)
                {
                    Add(snapshot.Inputs, cost.resource, Rate(cost.amount, timeSteps, speed));
                }

                foreach (ResourceCost cost in factory.productionDefinition.producables)
                {
                    Add(snapshot.Outputs, cost.resource, Rate(cost.amount, timeSteps, speed));
                }
            }

            lock (Gate)
            {
                Factories[building.GetID()] = snapshot;
            }
        }

        internal static void RecordHabitat(Habitat habitat)
        {
            if (habitat?.consumerProducer == null || habitat.consumerProducer.building == null || ConsumptionPerWorker == null)
            {
                return;
            }

            Building building = habitat.consumerProducer.building;
            var snapshot = new HabitatSnapshot
            {
                Building = building,
                District = DistrictOf(building),
                Frame = Frame(),
            };
            AddHabitatList(habitat, habitat.currentRequiredResources, snapshot);
            AddHabitatList(habitat, habitat.nextRequiredResources, snapshot);
            lock (Gate)
            {
                Habitats[building.GetID()] = snapshot;
            }
        }

        internal static void Copy(
            byte district,
            out List<DemandPull.Node<Resource>> nodes,
            out Dictionary<Resource, double> endUse)
        {
            long frame = Frame();
            nodes = new List<DemandPull.Node<Resource>>();
            endUse = new Dictionary<Resource, double>();
            lock (Gate)
            {
                List<long> staleFactories = null;
                foreach (KeyValuePair<long, FactorySnapshot> pair in Factories)
                {
                    FactorySnapshot snapshot = pair.Value;
                    if (!IsFresh(snapshot.Building, snapshot.Frame, frame))
                    {
                        staleFactories = staleFactories ?? new List<long>();
                        staleFactories.Add(pair.Key);
                        continue;
                    }

                    if (snapshot.District != district)
                    {
                        continue;
                    }

                    nodes.Add(new DemandPull.Node<Resource>
                    {
                        Inputs = snapshot.Inputs,
                        Outputs = snapshot.Outputs,
                    });
                }

                List<long> staleHabitats = null;
                foreach (KeyValuePair<long, HabitatSnapshot> pair in Habitats)
                {
                    HabitatSnapshot snapshot = pair.Value;
                    if (!IsFresh(snapshot.Building, snapshot.Frame, frame))
                    {
                        staleHabitats = staleHabitats ?? new List<long>();
                        staleHabitats.Add(pair.Key);
                        continue;
                    }

                    if (snapshot.District != district)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<Resource, double> consumption in snapshot.Consumption)
                    {
                        endUse.TryGetValue(consumption.Key, out double current);
                        endUse[consumption.Key] = current + consumption.Value;
                    }
                }

                if (staleFactories != null)
                {
                    foreach (long id in staleFactories)
                    {
                        Factories.Remove(id);
                    }
                }

                if (staleHabitats != null)
                {
                    foreach (long id in staleHabitats)
                    {
                        Habitats.Remove(id);
                    }
                }
            }
        }

        private static void AddHabitatList(Habitat habitat, IEnumerable<HabitatResourceInfo> resources, HabitatSnapshot snapshot)
        {
            if (resources == null || habitat.storage == null || habitat.workers == null)
            {
                return;
            }

            foreach (HabitatResourceInfo info in resources)
            {
                if (info == null || info.resource == null || !habitat.storage.ContainsKey(info.resource))
                {
                    continue;
                }

                double perTick = (double)ConsumptionPerWorker.Invoke(habitat, new object[] { info });
                Add(snapshot.Consumption, info.resource, habitat.workers.Count * perTick * 50d * 60d);
            }
        }

        private static bool IsFresh(Building building, long frame, long now)
        {
            return building != null && now - frame <= FreshFrames;
        }

        private static byte DistrictOf(Building building)
        {
            return WorldScripts.Inst.districtsManager.GetDistrict(building.GetPosition());
        }

        private static long Frame()
        {
            return Old.GetSimulator().simulationFrame;
        }

        private static double Rate(int amount, int timeSteps, int speed)
        {
            return (double)(amount * 50 * 60 * speed) / timeSteps / 10d;
        }

        private static void Add(Dictionary<Resource, double> values, Resource resource, double amount)
        {
            if (resource == null || amount == 0d)
            {
                return;
            }

            values.TryGetValue(resource, out double current);
            values[resource] = current + amount;
        }

        private sealed class FactorySnapshot
        {
            public Building Building;

            public byte District;

            public long Frame;

            public Dictionary<Resource, double> Inputs = new Dictionary<Resource, double>();

            public Dictionary<Resource, double> Outputs = new Dictionary<Resource, double>();
        }

        private sealed class HabitatSnapshot
        {
            public Building Building;

            public byte District;

            public long Frame;

            public Dictionary<Resource, double> Consumption = new Dictionary<Resource, double>();
        }
    }
}
