using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    internal static class LegacyStationStateImporter
    {
        private static bool logged;

        internal static bool TryImport(StationToolsMod target)
        {
            System.Type legacyType = AccessTools.TypeByName(
                "InfraSpaceMultiResourceLogistics.MultiResourceLogisticsMod");
            if (legacyType == null)
            {
                return false;
            }

            FieldInfo instanceField = AccessTools.Field(legacyType, "Instance");
            object legacy = instanceField?.GetValue(null);
            if (legacy == null)
            {
                return false;
            }

            CopyDictionary(legacyType, legacy, "mirroredTrainStations", target.mirroredTrainStations);
            CopyDictionary(legacyType, legacy, "trainStationRailPorts", target.trainStationRailPorts);
            CopyDictionary(legacyType, legacy, "trainStationRoadPorts", target.trainStationRoadPorts);

            if (!logged)
            {
                logged = true;
                Debug.Log(
                    "[StationTools] Imported legacy station state from InfraSpaceMultiResourceLogistics.");
            }

            return true;
        }

        private static void CopyDictionary<TValue>(
            System.Type sourceType,
            object source,
            string fieldName,
            Dictionary<long, TValue> target)
        {
            FieldInfo field = AccessTools.Field(sourceType, fieldName);
            Dictionary<long, TValue> values = field?.GetValue(source) as Dictionary<long, TValue>;
            if (values == null)
            {
                return;
            }

            foreach (KeyValuePair<long, TValue> pair in values)
            {
                if (!target.ContainsKey(pair.Key))
                {
                    target[pair.Key] = Clone(pair.Value);
                }
            }
        }

        private static TValue Clone<TValue>(TValue value)
        {
            if (value is List<int> list)
            {
                return (TValue)(object)new List<int>(list);
            }

            return value;
        }
    }
}
