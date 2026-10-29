using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    [Serializable]
    public sealed class MultiResourceLogisticsMod : Mod
    {
        public Dictionary<long, CenterSettings> centers = new Dictionary<long, CenterSettings>();

        // Migration state imported by InfraSpaceStationTools.
        public Dictionary<long, bool> mirroredTrainStations = new Dictionary<long, bool>();

        public Dictionary<long, List<int>> trainStationRailPorts = new Dictionary<long, List<int>>();

        public Dictionary<long, List<int>> trainStationRoadPorts = new Dictionary<long, List<int>>();

        [NonSerialized]
        internal static MultiResourceLogisticsMod Instance;

        [NonSerialized]
        private static bool patched;

        public override void Load()
        {
            Instance = this;
            if (centers == null)
            {
                centers = new Dictionary<long, CenterSettings>();
            }

            if (mirroredTrainStations == null)
            {
                mirroredTrainStations = new Dictionary<long, bool>();
            }

            if (trainStationRailPorts == null)
            {
                trainStationRailPorts = new Dictionary<long, List<int>>();
            }

            if (trainStationRoadPorts == null)
            {
                trainStationRoadPorts = new Dictionary<long, List<int>>();
            }

            if (!patched)
            {
                Harmony harmony = new Harmony("yangy.infraspace.multi-resource-logistics");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                BlueprintLogistics.TryPatch(harmony);
                patched = true;
            }

            Debug.Log("[InfraSpaceMultiResourceLogistics] Loaded");
        }

        public override void Start()
        {
            Instance = this;
        }

        public override void FrameUpdate()
        {
            NativeCenterPanelController.ProcessPendingReopen();
            CenterHoverTip.Ensure();
        }

        public override void SimulationUdpate()
        {
        }
    }

    [Serializable]
    public sealed class CenterSettings
    {
        public List<string> resourceNames = new List<string>();

        public Dictionary<string, int> capacities = new Dictionary<string, int>();

        // 每种物资自己的覆盖半径。缺省或没有对应项时，沿用中心原来的单一半径。
        public Dictionary<string, float> radii = new Dictionary<string, float>();

        // 仅存储的物资名称。缺省或为空表示全部正常搜集和配送。
        public List<string> storageOnly = new List<string>();
    }
}
