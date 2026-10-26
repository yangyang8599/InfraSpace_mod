using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    [Serializable]
    public sealed class StationToolsMod : Mod
    {
        public Dictionary<long, bool> mirroredTrainStations = new Dictionary<long, bool>();

        public Dictionary<long, List<int>> trainStationRailPorts = new Dictionary<long, List<int>>();

        public Dictionary<long, List<int>> trainStationRoadPorts = new Dictionary<long, List<int>>();

        [NonSerialized]
        internal static StationToolsMod Instance;

        [NonSerialized]
        private static bool patched;

        [NonSerialized]
        private int legacyImportAttempts;

        [NonSerialized]
        private bool legacyImportComplete;

        public override void Load()
        {
            Instance = this;
            EnsureState();
            StationMirror.EnsureHint();

            if (!patched)
            {
                Harmony harmony = new Harmony("yangy.infraspace.station-tools");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                BlueprintStationState.TryPatch(harmony);
                patched = true;
            }

            Debug.Log("[StationTools] Loaded");
        }

        public override void Start()
        {
            Instance = this;
        }

        public override void FrameUpdate()
        {
            if (!legacyImportComplete && legacyImportAttempts++ < 600)
            {
                legacyImportComplete = LegacyStationStateImporter.TryImport(this);
            }

            StationMirror.Tick();
        }

        public override void SimulationUdpate()
        {
        }

        private void EnsureState()
        {
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
        }
    }
}
