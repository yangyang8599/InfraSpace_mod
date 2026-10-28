using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class FinalProductDemand
    {
        private static int cachedFrame = -1;

        private static byte cachedDistrict;

        private static bool cachedStop;

        private static bool cachedHighTech;

        private static double cachedPower;

        private static Dictionary<Resource, double> cachedDemand = new Dictionary<Resource, double>();

        private static Dictionary<Resource, double> cachedProduction = new Dictionary<Resource, double>();

        internal static double PlannedPower(byte district)
        {
            Ensure(district);
            return cachedPower;
        }

        internal static bool TryPlan(byte district, Resource resource, out double production, out double demand)
        {
            production = 0d;
            demand = 0d;
            if (district == 0 || resource == null || !FinalProductToggle.IsArmed)
            {
                return false;
            }

            Ensure(district);
            if (resource.name == "power")
            {
                production = ProductionSnapshots.LastPowerOutput;
                demand = cachedPower;
                return true;
            }
            cachedProduction.TryGetValue(resource, out production);
            cachedDemand.TryGetValue(resource, out demand);
            return true;
        }

        private static void Ensure(byte district)
        {
            bool stop = FinalProductToggle.StopAtNoProduction;
            bool highTech = FinalProductToggle.IncludeHighTech;
            if (cachedFrame == Time.frameCount && cachedDistrict == district &&
                cachedStop == stop && cachedHighTech == highTech)
            {
                return;
            }

            List<DemandPull.Node<Resource>> nodes = ProductionSnapshots.NominalNodes(district, highTech, out cachedPower);
            cachedProduction = new Dictionary<Resource, double>();
            for (int i = 0; i < nodes.Count; i++)
            {
                foreach (KeyValuePair<Resource, double> output in nodes[i].Outputs)
                {
                    cachedProduction.TryGetValue(output.Key, out double current);
                    cachedProduction[output.Key] = current + output.Value;
                }
            }

            byte scope = district;
            cachedDemand = DemandPull.Solve(
                nodes,
                ProductionSnapshots.MissingRecipeScope(),
                resource => ProductionSnapshots.IsPlanningRaw(scope, resource, stop));
            cachedFrame = Time.frameCount;
            cachedDistrict = district;
            cachedStop = stop;
            cachedHighTech = highTech;
        }
    }
}
