using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    [Serializable]
    public sealed class ProductionInsightsMod : Mod
    {
        [NonSerialized]
        private static bool patched;

        public override void Load()
        {
            if (!patched)
            {
                new Harmony("yangy.infraspace.production-insights").PatchAll(Assembly.GetExecutingAssembly());
                patched = true;
            }

            Debug.Log("[InfraSpaceProductionInsights] Loaded");
        }

        public override void Start()
        {
        }

        public override void FrameUpdate()
        {
            PowerGridColors.RequestRefreshIfNeeded();
            ProducerList.Tick();
            ProducerFocus.Tick();
        }

        public override void SimulationUdpate()
        {
        }
    }
}
