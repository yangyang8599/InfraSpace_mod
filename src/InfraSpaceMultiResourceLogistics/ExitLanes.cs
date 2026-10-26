using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceMultiResourceLogistics
{
    [HarmonyPatch(typeof(SideRoadLane), "UpdateSimulationThread")]
    internal static class ExitLanes
    {
        private const float NearestMeters = 15f;

        private const float FarthestMeters = 40f;

        private static bool logged;

        internal static Lane SelectMergeLane(Lane naturalLane, Car car)
        {
            Lane needed = naturalLane.CheckIfLaneChangeNecessary(car);
            if (needed == null)
            {
                return null;
            }

            SideRoadLane driveway = car.currentHost as SideRoadLane;
            if (!IsLogisticsCenter(driveway?.sideRoad?.building.Get()))
            {
                return needed;
            }

            int changeAt = ChangeDistance(car);
            int joinAt = JoinPosition(driveway.sideRoad, naturalLane);
            if (joinAt <= changeAt)
            {
                return needed;
            }

            car.SetTargetLane(needed, changeAt);
            return null;
        }

        private static bool IsLogisticsCenter(Building building)
        {
            ProductionLogic logic = building?.consumerProducer?.productionLogic;
            return logic is CollectionProductionLogic || logic is DistributionProductionLogic;
        }

        private static int ChangeDistance(Car car)
        {
            float spread = (float)(car.GetID() % 256) / 256f;
            return Mathf.RoundToInt(Helper.Project(
                spread,
                0f,
                1f,
                NearestMeters * 1000f,
                FarthestMeters * 1000f));
        }

        private static int JoinPosition(SideRoad sideRoad, Lane naturalLane)
        {
            int position = sideRoad.GetRightTurnMainRoadLane() == naturalLane
                ? sideRoad.positionOnMainRoadLane
                : sideRoad.positionOnMainRoadLaneLeftTurn;
            return Mathf.Max(position, Mathf.RoundToInt(480.00003f));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo check = AccessTools.Method(typeof(Lane), nameof(Lane.CheckIfLaneChangeNecessary));
            MethodInfo select = AccessTools.Method(typeof(ExitLanes), nameof(SelectMergeLane));
            if (check == null || select == null)
            {
                LogOnce("出口变道挂钩失败：找不到原版方法。");
                return codes;
            }

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(check))
                {
                    continue;
                }

                codes[i].opcode = OpCodes.Call;
                codes[i].operand = select;
                return codes;
            }

            LogOnce("出口变道挂钩失败：找不到汇入选道。");
            return codes;
        }

        private static void LogOnce(string message)
        {
            if (logged)
            {
                return;
            }

            logged = true;
            Debug.LogWarning(message);
        }
    }
}
