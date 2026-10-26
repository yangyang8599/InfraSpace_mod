using System.Collections.Generic;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class FinalProductDemand
    {
        private static int cachedFrame = -1;

        private static byte cachedDistrict;

        private static Dictionary<Resource, double> cachedDemand = new Dictionary<Resource, double>();

        internal static bool TryGet(byte district, Resource resource, out double demand)
        {
            demand = 0d;
            if (district == 0 || resource == null || !FinalProductToggle.IsArmed)
            {
                return false;
            }

            if (cachedFrame != Time.frameCount || cachedDistrict != district)
            {
                ProductionSnapshots.Copy(district, out List<DemandPull.Node<Resource>> nodes, out Dictionary<Resource, double> endUse);
                cachedDemand = DemandPull.Solve(nodes, endUse);
                cachedFrame = Time.frameCount;
                cachedDistrict = district;
            }

            return cachedDemand.TryGetValue(resource, out demand);
        }
    }
}
