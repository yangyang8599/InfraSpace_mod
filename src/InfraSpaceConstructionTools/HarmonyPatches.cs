using HarmonyLib;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    [HarmonyPatch(typeof(CustomInput), nameof(CustomInput.GetButtonDown), typeof(Keybinding))]
    internal static class SuppressPlainCKeyDownPatch
    {
        private static bool Prefix(Keybinding binding, ref bool __result)
        {
            return !SuppressPlainCWhileControl.ShouldBlock(binding, ref __result);
        }
    }

    [HarmonyPatch(typeof(CustomInput), nameof(CustomInput.GetButton), typeof(Keybinding))]
    internal static class SuppressPlainCKeyPatch
    {
        private static bool Prefix(Keybinding binding, ref bool __result)
        {
            return !SuppressPlainCWhileControl.ShouldBlock(binding, ref __result);
        }
    }

    internal static class SuppressPlainCWhileControl
    {
        internal static bool ShouldBlock(Keybinding binding, ref bool result)
        {
            if (binding == Keybinding.None || !IsControlHeld())
            {
                return false;
            }

            if (!SettingsHandler.GetCurrentSettings().keybindings.TryGetValue(binding, out KeyCode key) ||
                key != KeyCode.C)
            {
                return false;
            }

            result = false;
            return true;
        }

        private static bool IsControlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), nameof(BuildingConstructionManager.HandleLeftMouseClick))]
    internal static class BlockSingleBuildingMoveClickPatch
    {
        private static bool Prefix()
        {
            return ConstructionToolsMod.Controller == null || !ConstructionToolsMod.Controller.OwnsMoveInput;
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), nameof(BuildingConstructionManager.HandleLeftMouseRelease))]
    internal static class BlockSingleBuildingMoveReleasePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (ConstructionToolsMod.Controller == null || !ConstructionToolsMod.Controller.OwnsMoveInput)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.HandleLeftMouseClick))]
    internal static class BlockNetworkMoveClickPatch
    {
        private static bool Prefix()
        {
            return ConstructionToolsMod.Controller == null || !ConstructionToolsMod.Controller.OwnsMoveInput;
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.HandleLeftMouseRelease))]
    internal static class BlockNetworkMoveReleasePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (ConstructionToolsMod.Controller == null || !ConstructionToolsMod.Controller.OwnsMoveInput)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), nameof(BuildingConstructionManager.DisplayEfficiencyLinesForBuilding))]
    internal static class ShowDependentEfficiencyLinesPatch
    {
        // 移动和放置仍走原版。普通点选不画线；按住约半秒后才补上靠它提高产率的建筑。
        private static bool Prefix(BuildingConstructionManager __instance, Building building)
        {
            if (EfficiencyDependents.ShouldShow(__instance, building))
            {
                return true;
            }

            EfficiencyDependents.HideAll(__instance);
            return false;
        }

        private static void Postfix(Building building)
        {
            EfficiencyDependents.Show(building);
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "CustomUpdate")]
    internal static class PreviewEfficiencyLinesWhileHeldPatch
    {
        // 不跳过原版。移动和放置时的连线保持原样。
        private static void Postfix(BuildingConstructionManager __instance)
        {
            EfficiencyDependents.PreviewHold(__instance);
        }
    }
}
