using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal static class EfficiencyDependents
    {
        private static readonly Color Blue = new Color(0.3f, 0.62f, 1f, 1f);
        private static readonly List<EfficiencyLineVisual> Lines = new List<EfficiencyLineVisual>();

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, Building> HoveredOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, Building>("hoveredBuilding");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, Building> CurrentOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, Building>("currentBuilding");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> MovingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isMoving");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> BuildingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isBuilding");

        private static readonly AccessTools.FieldRef<BuildingConstructionManager, bool> DestroyingOf =
            AccessTools.FieldRefAccess<BuildingConstructionManager, bool>("isDestroying");

        internal static void PreviewHold(BuildingConstructionManager manager)
        {
            if (manager == null || !Input.GetButton("Fire1"))
            {
                return;
            }

            if (WorldScripts.Inst?.canvasHandler != null && WorldScripts.Inst.canvasHandler.IsMouseOverUI())
            {
                return;
            }

            Building current = CurrentOf(manager);
            if (current != null && (MovingOf(manager) || BuildingOf(manager)))
            {
                return;
            }

            if (DestroyingOf(manager))
            {
                return;
            }

            Building hovered = HoveredOf(manager);
            if (hovered == null || !hovered.IsValidAndBuilt())
            {
                return;
            }

            manager.DisplayEfficiencyLinesForBuilding(hovered);
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
