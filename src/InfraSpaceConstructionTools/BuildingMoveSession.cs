using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class BuildingMoveSession
    {
        internal enum CommitResult
        {
            Success,
            Invalid
        }

        private sealed class Snapshot
        {
            internal Building Building;
            internal Vector3 Position;
            internal Quaternion VisualRotation;
            internal float RotationY;
            internal Vector3 TargetPosition;
        }

        private readonly List<Snapshot> snapshots;
        private readonly List<RoadPiece> roadPieces;
        private readonly List<NetworkClipper.Preview> roadPreviews;
        private readonly Vector3 anchor;
        private Vector3 lastDelta;
        private bool finished;

        internal event Action Committed;

        internal bool IsPreviewValid { get; private set; }

        internal int RoadNodeCount => roadPieces.Count;

        internal BuildingMoveSession(
            IEnumerable<Building> buildings,
            IReadOnlyList<RoadPiece> roads,
            Vector3 anchor)
        {
            this.anchor = anchor;
            snapshots = buildings
                .Distinct()
                .Select(building => new Snapshot
                {
                    Building = building,
                    Position = building.GetPosition(),
                    VisualRotation = building.representation.transform.rotation,
                    RotationY = building.GetRotationY(),
                    TargetPosition = building.GetPosition()
                })
                .ToList();
            roadPieces = roads.ToList();
            roadPreviews = NetworkClipper.CreatePreviews(roadPieces);
            IsPreviewValid = true;
        }

        internal void UpdatePreview(Vector2 mousePosition)
        {
            if (finished ||
                !AreaMoveController.TryGetTerrainPosition(mousePosition, out Vector3 current))
            {
                return;
            }

            lastDelta = GetDelta(current);
            foreach (NetworkClipper.Preview preview in roadPreviews)
            {
                preview.Place(lastDelta);
            }

            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.TargetPosition = snapshot.Position + lastDelta;
                snapshot.Building?.SetPosition(snapshot.TargetPosition, doneByComputer: true);
            }

            Physics.SyncTransforms();
            IsPreviewValid = ValidateTargets();
        }

        internal CommitResult TryCommit()
        {
            if (finished || !IsPreviewValid)
            {
                return CommitResult.Invalid;
            }

            if (!HasMoved())
            {
                RestorePreviewVisuals();
                finished = true;
                Committed?.Invoke();
                return CommitResult.Success;
            }

            RestorePreviewVisuals();
            if (!CanPay(out List<ResourceCost> costs))
            {
                RestoreRoadPreviews();
                return CommitResult.Invalid;
            }

            HashSet<Building> unbuilt = new HashSet<Building>();
            try
            {
                if (!CommitBuildings(unbuilt))
                {
                    RestoreRoadPreviews();
                    return CommitResult.Invalid;
                }

                NetworkClipper.Commit(roadPieces, lastDelta);
                RefreshBuildingGround();
                ReconnectSideRoads();
                WorldScripts.Inst.powerManager.SetNetworksDirty();
                PathFinder.SetRoadNetworkDirty();
                Pay(costs);
                finished = true;
                Committed?.Invoke();
                return CommitResult.Success;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AreaMove] Commit failed: {exception}");
                if (unbuilt.Count > 0)
                {
                    RollbackUnbuilt(unbuilt);
                }
                else
                {
                    RestoreBuildingVisuals();
                }

                RestoreRoadPreviews();
                return CommitResult.Invalid;
            }
        }

        internal void Cancel()
        {
            if (finished)
            {
                return;
            }

            RestorePreviewVisuals();
            finished = true;
        }

        private bool CommitBuildings(HashSet<Building> unbuilt)
        {
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building.sideRoad?.mainRoad.RemoveBuilding(snapshot.Building);
                snapshot.Building.SetIsBuilt(false);
                unbuilt.Add(snapshot.Building);
            }

            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building.SetPosition(snapshot.TargetPosition, doneByComputer: true);
                snapshot.Building.SetRotation(snapshot.RotationY);
            }

            if (!ValidateWithNativePlacement())
            {
                RollbackUnbuilt(unbuilt);
                return false;
            }

            SetRepresentationsActive(true);
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building.SetIsBuilt(true);
                snapshot.Building.UpdateDoesWork();
            }

            return true;
        }

        private void RefreshBuildingGround()
        {
            foreach (Snapshot snapshot in snapshots)
            {
                Building building = snapshot.Building;
                if (building == null || !building.IsValidAndBuilt())
                {
                    continue;
                }

                Bounds current = building.GetBounds();
                Bounds previous = new Bounds(snapshot.Position, current.size);
                previous.Expand(4f);
                current.Expand(4f);
                if (building.GetDescription().doesAffectTerrain)
                {
                    TerrainAdaptation.UpdateTerrainAdaptation(previous);
                    building.UpdateTerrainAdaption(updateTerrain: true);
                }

                WorldScripts.Inst.world.UpdateTerraformablesDetail(previous, remove: false);
                WorldScripts.Inst.world.UpdateTerraformablesDetail(current, remove: true);
                InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(previous, immediately: true);
                InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(current, immediately: true);
            }
        }

        private void ReconnectSideRoads()
        {
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building?.UpdateSideRoad(doneByComputer: true, force: true);
            }
        }

        private Vector3 GetDelta(Vector3 current)
        {
            Vector3 raw = current - anchor;
            bool fineTune =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            if (fineTune || snapshots.Count == 0)
            {
                if (fineTune || WorldScripts.Inst.gridManager.GetGridState() == GridStates.Off)
                {
                    return raw;
                }

                return WorldScripts.Inst.gridManager.SnapToGridCell(anchor + raw) -
                       WorldScripts.Inst.gridManager.SnapToGridCell(anchor);
            }

            Snapshot reference = snapshots[0];
            Vector3 desired = reference.Position + raw;
            Vector3 snapped = WorldScripts.Inst.gridManager.SnapToGridBuilding(
                reference.Building.GetDescription().bounds.size,
                desired,
                reference.RotationY);
            return snapped - reference.Position;
        }

        private bool HasMoved()
        {
            return lastDelta.sqrMagnitude > 0.000001f;
        }

        private bool ValidateTargets()
        {
            foreach (Snapshot snapshot in snapshots)
            {
                if (!WorldScripts.Inst.world.IsPointInPlayableArea(snapshot.TargetPosition))
                {
                    return false;
                }
            }

            Vector3 end = anchor + lastDelta;
            return WorldScripts.Inst.world.IsPointInPlayableArea(end);
        }

        private bool ValidateWithNativePlacement()
        {
            SetRepresentationsActive(false);
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building.SetRepresentationActive(true);
                Physics.SyncTransforms();
                bool canPlace = snapshot.Building.CanPlace(checkCollision: true);
                snapshot.Building.SetRepresentationActive(false);
                if (!canPlace)
                {
                    SetRepresentationsActive(true);
                    return false;
                }
            }

            SetRepresentationsActive(true);
            return true;
        }

        private void RollbackUnbuilt(HashSet<Building> unbuilt)
        {
            SetRepresentationsActive(true);
            foreach (Snapshot snapshot in snapshots)
            {
                if (!unbuilt.Contains(snapshot.Building))
                {
                    continue;
                }

                snapshot.Building.SetPosition(snapshot.Position, doneByComputer: true);
                snapshot.Building.SetRotation(snapshot.RotationY);
            }

            foreach (Snapshot snapshot in snapshots)
            {
                if (!unbuilt.Contains(snapshot.Building))
                {
                    continue;
                }

                snapshot.Building.SetIsBuilt(true);
                snapshot.Building.UpdateDoesWork();
            }

            ReconnectSideRoads();
            WorldScripts.Inst.powerManager.SetNetworksDirty();
        }

        private void RestorePreviewVisuals()
        {
            foreach (NetworkClipper.Preview preview in roadPreviews)
            {
                preview.Restore();
            }

            roadPreviews.Clear();
            RestoreBuildingVisuals();
            Physics.SyncTransforms();
        }

        private void RestoreRoadPreviews()
        {
            roadPreviews.Clear();
            roadPreviews.AddRange(NetworkClipper.CreatePreviews(roadPieces));
            foreach (NetworkClipper.Preview preview in roadPreviews)
            {
                preview.Place(lastDelta);
            }
        }

        private void RestoreBuildingVisuals()
        {
            foreach (Snapshot snapshot in snapshots)
            {
                if (snapshot.Building == null)
                {
                    continue;
                }

                snapshot.Building.SetPosition(snapshot.Position, doneByComputer: true);
                snapshot.Building.SetRotation(snapshot.RotationY);
                if (snapshot.Building.representation != null)
                {
                    snapshot.Building.representation.transform.rotation = snapshot.VisualRotation;
                }
            }
        }

        private void SetRepresentationsActive(bool active)
        {
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building.SetRepresentationActive(active);
            }
        }

        private bool CanPay(out List<ResourceCost> costs)
        {
            Dictionary<Resource, int> totals = new Dictionary<Resource, int>();
            foreach (Snapshot snapshot in snapshots)
            {
                float distance = Vector3.Distance(snapshot.Position, snapshot.TargetPosition);
                foreach (ResourceCost cost in snapshot.Building.GetDescription().GetMovingCosts(distance))
                {
                    totals[cost.resource] = totals.TryGetValue(cost.resource, out int amount)
                        ? amount + cost.amount
                        : cost.amount;
                }
            }

            costs = totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
            return CanSkipPay() || WorldScripts.Inst.resourceManager.CanPay(costs);
        }

        private static bool CanSkipPay()
        {
            return WorldScripts.Inst.worldSettings.noBuildingCosts ||
                   !WorldScripts.Inst.debugPanel.buildCostsButton.isOn;
        }

        private static void Pay(IEnumerable<ResourceCost> costs)
        {
            if (CanSkipPay())
            {
                return;
            }

            foreach (ResourceCost cost in costs)
            {
                WorldScripts.Inst.resourceManager.AddResource(cost.resource, -cost.amount);
            }
        }

    }
}
