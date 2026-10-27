using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal static class EfficiencyDependents
    {
        private const float HoldSeconds = 0.45f;

        private static readonly Color Blue = new Color(0.3f, 0.62f, 1f, 1f);
        private static readonly List<EfficiencyLineVisual> Lines = new List<EfficiencyLineVisual>();

        private static Building holdBuilding;
        private static float holdStart;
        private static bool holdReady;

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, Building> HoveredOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, Building>("hoveredBuilding");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> MovingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isMoving");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> BuildingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isBuilding");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> DestroyingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isDestroying");

        internal static bool ShouldShow(BuildingConstructionManager manager, Building building)
        {
            if (manager == null)
            {
                return false;
            }

            if (MovingOf(manager) || BuildingOf(manager))
            {
                return true;
            }

            return holdReady &&
                   building != null &&
                   building == holdBuilding &&
                   Input.GetButton("Fire1");
        }

        internal static void PreviewHold(BuildingConstructionManager manager)
        {
            if (manager == null)
            {
                return;
            }

            if (MovingOf(manager) || BuildingOf(manager))
            {
                ResetHold();
                return;
            }

            Building hovered = HoveredOf(manager);
            bool overUi = WorldScripts.Inst?.canvasHandler != null &&
                          WorldScripts.Inst.canvasHandler.IsMouseOverUI();
            if (DestroyingOf(manager) || overUi || !Input.GetButton("Fire1") ||
                hovered == null || !hovered.IsValidAndBuilt())
            {
                bool wasHolding = holdBuilding != null || holdReady;
                ResetHold();
                if (wasHolding)
                {
                    HideAll(manager);
                }

                return;
            }

            if (holdBuilding != hovered)
            {
                bool wasReady = holdReady;
                holdBuilding = hovered;
                holdStart = Time.unscaledTime;
                holdReady = false;
                if (wasReady)
                {
                    HideAll(manager);
                }
            }

            if (!holdReady && Time.unscaledTime - holdStart >= HoldSeconds)
            {
                holdReady = true;
            }

            if (holdReady)
            {
                manager.DisplayEfficiencyLinesForBuilding(hovered);
            }
        }

        internal static void HideAll(BuildingConstructionManager manager)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i] != null)
                {
                    Lines[i].gameObject.SetActive(false);
                }
            }

            List<EfficiencyLineVisual> vanilla = manager?.efficiencyLines;
            if (vanilla == null)
            {
                return;
            }

            for (int i = 0; i < vanilla.Count; i++)
            {
                if (vanilla[i] != null)
                {
                    vanilla[i].gameObject.SetActive(false);
                }
            }
        }

        private static void ResetHold()
        {
            holdBuilding = null;
            holdReady = false;
        }

        internal static void Show(Building building)
        {
            int used = 0;
            List<Building> dependents = building != null ? building.efficiencyBoostingTo : null;
            if (dependents != null)
            {
                string label = "+" + (Building.GetEfficiencyBoost() * 100f) + "%";
                for (int i = 0; i < dependents.Count; i++)
                {
                    Building other = dependents[i];
                    if (other == null || !other.IsValid() || !other.DoesWork())
                    {
                        continue;
                    }

                    EfficiencyLineVisual visual = LineAt(used);
                    if (visual == null)
                    {
                        return;
                    }

                    Vector3 start = building.GetPosition();
                    Vector3 end = other.GetPosition();
                    if (building.efficiencyBoostedBy != null && building.efficiencyBoostedBy.Contains(other))
                    {
                        Vector3 side = Vector3.Cross(end - start, Vector3.up);
                        if (side.sqrMagnitude > 0.001f)
                        {
                            side = side.normalized * 0.45f;
                            start += side;
                            end += side;
                        }
                    }

                    visual.Set(start, end, label);
                    Paint(visual);
                    visual.gameObject.SetActive(true);
                    used++;
                }
            }

            for (int i = used; i < Lines.Count; i++)
            {
                if (Lines[i] != null)
                {
                    Lines[i].gameObject.SetActive(false);
                }
            }
        }

        private static EfficiencyLineVisual LineAt(int index)
        {
            if (index < Lines.Count && Lines[index] != null)
            {
                return Lines[index];
            }

            VisualItemModule visuals = Old.GetVisualItemModule();
            TMP_Text template = WorldScripts.Inst?.networkConstructionManager?.edgeLengthText;
            if (visuals == null || template == null)
            {
                return null;
            }

            EfficiencyLineVisual created = visuals.CreateEfficiencyLineVisual(template);
            if (created == null)
            {
                return null;
            }

            Paint(created);
            Lines.Add(created);
            return created;
        }

        private static void Paint(EfficiencyLineVisual visual)
        {
            LineVisual line = visual.GetComponentInChildren<LineVisual>(true);
            if (line != null)
            {
                line.SetFillColor(Blue);
                line.SetEmissive(1f);
                line.SetOutlineWidthPercentage(0f);
            }

            TMP_Text text = visual.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.color = Blue;
            }
        }
    }
}
