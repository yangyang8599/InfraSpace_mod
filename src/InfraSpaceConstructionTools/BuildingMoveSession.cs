using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class BuildingMoveSession : IBatchPlacementSession
    {
        private sealed class Snapshot
        {
            internal Building Building;
            internal Vector3 Position;
            internal float RotationY;
            internal Vector3 TargetPosition;
        }

        private readonly List<Snapshot> snapshots;
        private readonly List<Building> movingBuildings;
        private readonly List<RoadPiece> roadPieces;
        private readonly Vector3 anchor;
        private BlueprintCopySession previewSession;
        private Vector3 lastDelta;
        private bool finished;

        public bool IsPreviewValid { get; private set; }

        public int RoadNodeCount => roadPieces.Count;

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
                    RotationY = building.GetRotationY(),
                    TargetPosition = building.GetPosition()
                })
                .ToList();
            movingBuildings = snapshots.Select(snapshot => snapshot.Building).ToList();
            roadPieces = roads.ToList();
            BeginPreview(Vector3.zero);
        }

        public void UpdatePreview(Vector2 mousePosition)
        {
            if (finished)
            {
                return;
            }

            previewSession.UpdatePreview(mousePosition);
            lastDelta = previewSession.LastDelta;

            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.TargetPosition = snapshot.Position + lastDelta;
            }

            SetSourcesVisible(false);
            Physics.SyncTransforms();
            IsPreviewValid =
                previewSession.IsPreviewValid &&
                ValidateTargets() &&
                CanPay(out _);
        }

        public BatchCommitResult TryCommit()
        {
            if (finished || !IsPreviewValid)
            {
                return BatchCommitResult.Invalid;
            }

            if (!HasMoved())
            {
                EndPreview();
                finished = true;
                return BatchCommitResult.Success;
            }

            if (!CanPay(out List<ResourceCost> costs))
            {
                IsPreviewValid = false;
                return BatchCommitResult.Invalid;
            }

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            EndPreview();
            long previewCleanupMs = timer.ElapsedMilliseconds;
            HashSet<Building> unbuilt = new HashSet<Building>();
            try
            {
                if (!CommitBuildings(unbuilt))
                {
                    BeginPreview(lastDelta);
                    return BatchCommitResult.Invalid;
                }

                long buildingsMs = timer.ElapsedMilliseconds;
                NetworkClipper.Commit(roadPieces, lastDelta);
                long networkMs = timer.ElapsedMilliseconds;
                RefreshBuildingGround();
                ReconnectBuildingNetworks();
                long refreshMs = timer.ElapsedMilliseconds;
                WorldScripts.Inst.powerManager.SetNetworksDirty();
                PathFinder.SetRoadNetworkDirty();
                BatchPlacement.Pay(costs);
                timer.Stop();
                Debug.Log(
                    $"[AreaMove] Move commit {timer.ElapsedMilliseconds} ms " +
                    $"(preview {previewCleanupMs}, buildings {buildingsMs - previewCleanupMs}, " +
                    $"network {networkMs - buildingsMs}, refresh {refreshMs - networkMs})");
                finished = true;
                return BatchCommitResult.Success;
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
                    SetSourcesVisible(true);
                }

                BeginPreview(lastDelta);
                return BatchCommitResult.Invalid;
            }
        }

        public void Cancel()
        {
            if (finished)
            {
                return;
            }

            EndPreview();
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
            bool hasBounds = false;
            bool affectsTerrain = false;
            Bounds previousArea = default(Bounds);
            Bounds currentArea = default(Bounds);
            foreach (Snapshot snapshot in snapshots)
            {
                Building building = snapshot.Building;
                if (building == null || !building.IsValidAndBuilt())
                {
                    continue;
                }

                Bounds current = building.GetBounds();
                Bounds previous = new Bounds(snapshot.Position, current.size);
                if (!hasBounds)
                {
                    previousArea = previous;
                    currentArea = current;
                    hasBounds = true;
                }
                else
                {
                    previousArea.Encapsulate(previous);
                    currentArea.Encapsulate(current);
                }

                affectsTerrain |= building.GetDescription().doesAffectTerrain;
            }

            if (!hasBounds)
            {
                return;
            }

            previousArea.Expand(4f);
            currentArea.Expand(4f);
            WorldScripts.Inst.world.UpdateTerraformablesDetail(previousArea, remove: false);
            WorldScripts.Inst.world.UpdateTerraformablesDetail(currentArea, remove: true);
            InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(previousArea, immediately: true);
            InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(currentArea, immediately: true);
            if (affectsTerrain)
            {
                TerrainAdaptation.UpdateTerrainAdaptation(previousArea);
                TerrainAdaptation.UpdateTerrainAdaptation(currentArea);
            }
        }

        private void ReconnectBuildingNetworks()
        {
            foreach (Snapshot snapshot in snapshots)
            {
                snapshot.Building?.UpdateSideRoad(doneByComputer: true, force: true);
                snapshot.Building?.UpdateSidePipes(doneByComputer: true, force: true);
            }
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
            return BatchPlacement.CanPlaceBuildings(movingBuildings);
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

            ReconnectBuildingNetworks();
            WorldScripts.Inst.powerManager.SetNetworksDirty();
        }

        private void BeginPreview(Vector3 delta)
        {
            previewSession = new BlueprintCopySession(
                movingBuildings,
                roadPieces,
                anchor,
                allowRotation: false,
                checkBuildCosts: false,
                ignoredBuildings: movingBuildings,
                validateRoads: false,
                canCommit: false,
                copyConfiguration: false,
                includePowerLines: false);
            previewSession.Place(delta);
            SetSourcesVisible(false);
            Physics.SyncTransforms();
            IsPreviewValid = previewSession.IsPreviewValid;
        }

        private void EndPreview()
        {
            previewSession?.Cancel();
            previewSession = null;
            SetSourcesVisible(true);
            Physics.SyncTransforms();
        }

        private void SetSourcesVisible(bool visible)
        {
            SetRepresentationsActive(visible);
            NetworkClipper.SetSourcesVisible(roadPieces, visible);
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
                BatchPlacement.AddCosts(
                    totals,
                    snapshot.Building.GetDescription().GetMovingCosts(distance));
            }

            costs = totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
            return BatchPlacement.CanPay(costs);
        }

    }
}
