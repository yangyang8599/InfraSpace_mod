using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    [Serializable]
    public sealed class ConstructionToolsMod : Mod
    {
        [NonSerialized]
        internal static ConstructionToolsMod Instance;

        [NonSerialized]
        internal static AreaMoveController Controller;

        [NonSerialized]
        private static bool patched;

        public bool suppressRoadMagnet;

        public bool straightExtend;

        public override void Load()
        {
            Instance = this;
            if (!patched)
            {
                new Harmony("yangy.infraspace.construction-tools").PatchAll(Assembly.GetExecutingAssembly());
                patched = true;
            }

            Debug.Log("[InfraSpaceConstructionTools] Loaded");
        }

        public override void Start()
        {
            Instance = this;
        }

        public override void FrameUpdate()
        {
            RoadSnapFeature.Tick();
            StraightExtendFeature.Tick();
            if (WorldScripts.Inst == null || WorldScripts.Inst.clickHandler == null)
            {
                return;
            }

            if (Controller == null)
            {
                GameObject host = new GameObject("AreaMoveController");
                Controller = host.AddComponent<AreaMoveController>();
                Controller.Initialize();
            }
        }

        public override void SimulationUdpate()
        {
        }
    }
}
