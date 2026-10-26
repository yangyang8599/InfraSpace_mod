using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceProductionInsights
{
    internal static class PowerGridColors
    {
        private static readonly Color VanillaFill = new Color(212f / 255f, 187f / 255f, 0f, 0.35f);
        private static readonly Color VanillaContour = new Color(191f / 255f, 169f / 255f, 0f, 1f);

        private static readonly MethodInfo VisualTypeMethod =
            AccessTools.Method(typeof(VisualizationManager), "GetVisualTypeToDisplay");

        private static readonly AccessTools.FieldRef<VisualizationManager, bool> VisualizationDirty =
            AccessTools.FieldRefAccess<VisualizationManager, bool>("visualizationDirty");

        private static readonly AccessTools.FieldRef<VisualizationManager, VisualType> CachedVisualType =
            AccessTools.FieldRefAccess<VisualizationManager, VisualType>("cachedType");

        private static readonly Dictionary<int, int> ColorByNetworkId = new Dictionary<int, int>();
        private static MaterialPropertyBlock block;
        private static int signature;
        private static bool refreshRequested;
        private static bool loggedFailure;

        internal static void RequestRefreshIfNeeded()
        {
            try
            {
                WorldScripts world = WorldScripts.Inst;
                if (world?.visualizationManager == null || world.powerManager == null)
                {
                    return;
                }

                if (world.visualizationManager.GetVisualTypeToDisplayCached() != VisualType.Power)
                {
                    return;
                }

                if (Time.frameCount % 20 != 0)
                {
                    return;
                }

                if (TryComputeSignature(world.powerManager, out int next) && next != signature)
                {
                    refreshRequested = true;
                }
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
        }

        internal static void Prepare(VisualizationManager manager, bool force)
        {
            try
            {
                if (manager == null || VisualTypeMethod == null)
                {
                    return;
                }

                if (!(VisualTypeMethod.Invoke(manager, null) is VisualType next) || next != VisualType.Power)
                {
                    return;
                }

                bool vanillaRebuild = force || VisualizationDirty(manager) || CachedVisualType(manager) != next;
                if (!vanillaRebuild && !refreshRequested)
                {
                    return;
                }

                refreshRequested = false;
                PowerManager power = WorldScripts.Inst?.powerManager;
                if (power == null || !TryRebuild(power))
                {
                    return;
                }

                if (!vanillaRebuild)
                {
                    VisualizationDirty(manager) = true;
                }
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
        }

        internal static void HideUnifiedRange()
        {
            try
            {
                WorldScripts world = WorldScripts.Inst;
                if (world?.visualizationManager == null || world.powerDecalManager?.meshRenderer == null)
                {
                    return;
                }

                if (world.visualizationManager.GetVisualTypeToDisplayCached() != VisualType.Power)
                {
                    return;
                }

                world.powerDecalManager.meshRenderer.enabled = false;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
        }

        internal static void ShowRange(Building building)
        {
            try
            {
                if (building?.powerDecal == null || !building.IsValid())
                {
                    return;
                }

                VisualizationManager visualization = WorldScripts.Inst?.visualizationManager;
                if (visualization == null || visualization.GetVisualTypeToDisplayCached() != VisualType.Power)
                {
                    return;
                }

                building.SetPowerDecalVisible(true);
                MeshRenderer renderer = building.powerDecal.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    return;
                }

                if (block == null)
                {
                    block = new MaterialPropertyBlock();
                }

                ResolveColors(building.powerNetwork, out Color fill, out Color contour);
                renderer.GetPropertyBlock(block);
                block.SetColor("_FillColor", fill);
                block.SetColor("_ContourColor", contour);
                renderer.SetPropertyBlock(block);
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
        }

        private static bool TryRebuild(PowerManager manager)
        {
            try
            {
                if (!TryComputeSignature(manager, out int nextSignature))
                {
                    return false;
                }

                var next = new Dictionary<int, int>();
                if (manager.networks != null)
                {
                    var networks = new List<PowerNetwork>();
                    foreach (PowerNetwork network in manager.networks)
                    {
                        if (network?.buildings != null && network.buildings.Count > 0)
                        {
                            networks.Add(network);
                        }
                    }

                    networks.Sort(delegate(PowerNetwork left, PowerNetwork right)
                    {
                        int bySize = right.buildings.Count.CompareTo(left.buildings.Count);
                        if (bySize != 0)
                        {
                            return bySize;
                        }

                        return left.powerNetworkID.CompareTo(right.powerNetworkID);
                    });

                    for (int i = 0; i < networks.Count; i++)
                    {
                        next[networks[i].powerNetworkID] = i;
                    }
                }

                ColorByNetworkId.Clear();
                foreach (KeyValuePair<int, int> pair in next)
                {
                    ColorByNetworkId[pair.Key] = pair.Value;
                }

                signature = nextSignature;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
                return false;
            }
        }

        private static void ResolveColors(PowerNetwork network, out Color fill, out Color contour)
        {
            if (network == null || !ColorByNetworkId.TryGetValue(network.powerNetworkID, out int index) || index == 0)
            {
                fill = VanillaFill;
                contour = VanillaContour;
                return;
            }

            float hue = Mathf.Repeat(0.13f + (index * 0.618034f), 1f);
            Color color = Color.HSVToRGB(hue, 0.78f, 0.98f);
            fill = new Color(color.r, color.g, color.b, 0.35f);
            contour = new Color(color.r * 0.82f, color.g * 0.82f, color.b * 0.82f, 1f);
        }

        private static bool TryComputeSignature(PowerManager manager, out int next)
        {
            next = 17;
            try
            {
                if (manager?.networks == null)
                {
                    return true;
                }

                unchecked
                {
                    int hash = 17;
                    foreach (PowerNetwork network in manager.networks)
                    {
                        if (network?.buildings == null)
                        {
                            continue;
                        }

                        hash = (hash * 397) ^ network.powerNetworkID;
                        hash = (hash * 397) ^ network.buildings.Count;
                        foreach (Building building in network.buildings)
                        {
                            if (building == null)
                            {
                                continue;
                            }

                            Vector3 position = building.GetPosition();
                            hash = (hash * 397) ^ Mathf.RoundToInt(position.x);
                            hash = (hash * 397) ^ Mathf.RoundToInt(position.z);
                        }
                    }

                    next = hash;
                }

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
                return false;
            }
        }

        private static void LogFailure(Exception exception)
        {
            if (loggedFailure)
            {
                return;
            }

            loggedFailure = true;
            Debug.LogWarning("[InfraSpaceProductionInsights] Power range colors skipped: " + exception.Message);
        }
    }
}
