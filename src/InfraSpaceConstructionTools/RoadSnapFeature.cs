using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal static class RoadSnapFeature
    {
        internal static readonly FieldInfo BuildingField =
            AccessTools.Field(typeof(NetworkConstructionManager), "isBuildingEdge");

        internal static readonly FieldInfo NetworkTypeField =
            AccessTools.Field(typeof(NetworkConstructionManager), "currentNetworkType");

        internal static readonly FieldInfo FullNodeField =
            AccessTools.Field(typeof(NetworkConstructionManager), "isPlacingOnTopOfFullNode");

        private static RoadSnapHint hint;

        internal static bool MagnetSuppressed =>
            ConstructionToolsMod.Instance != null && ConstructionToolsMod.Instance.suppressRoadMagnet;

        internal static void Tick()
        {
            if (WorldScripts.Inst == null)
            {
                return;
            }

            EnsureHint();
            if (!IsBuildingRoad() || DionicGame.GetModalManager().IsGameModal())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.H) && ConstructionToolsMod.Instance != null)
            {
                ConstructionToolsMod.Instance.suppressRoadMagnet =
                    !ConstructionToolsMod.Instance.suppressRoadMagnet;
            }
        }

        internal static bool IsBuildingRoad()
        {
            WorldScripts scripts = WorldScripts.Inst;
            return scripts != null &&
                   scripts.networkConstructionManager != null &&
                   IsBuildingRoad(scripts.networkConstructionManager);
        }

        internal static bool IsBuildingRoad(NetworkConstructionManager manager)
        {
            if (manager == null || BuildingField == null || NetworkTypeField == null)
            {
                return false;
            }

            object building = BuildingField.GetValue(manager);
            object networkType = NetworkTypeField.GetValue(manager);
            return building is bool isBuilding &&
                   isBuilding &&
                   networkType is NetworkType type &&
                   type == NetworkType.Road;
        }

        internal static bool IsBuildingLine()
        {
            WorldScripts scripts = WorldScripts.Inst;
            return scripts != null &&
                   scripts.networkConstructionManager != null &&
                   IsBuildingLine(scripts.networkConstructionManager);
        }

        internal static bool IsBuildingLine(NetworkConstructionManager manager)
        {
            if (manager == null || BuildingField == null || NetworkTypeField == null)
            {
                return false;
            }

            object building = BuildingField.GetValue(manager);
            object networkType = NetworkTypeField.GetValue(manager);
            return building is bool isBuilding &&
                   isBuilding &&
                   networkType is NetworkType type &&
                   (type == NetworkType.Road || type == NetworkType.Rail);
        }

        private static void EnsureHint()
        {
            if (hint != null)
            {
                return;
            }

            GameObject host = new GameObject("RoadSnapHint");
            hint = host.AddComponent<RoadSnapHint>();
        }
    }
}
