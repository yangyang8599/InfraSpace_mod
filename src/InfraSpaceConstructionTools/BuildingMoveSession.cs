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
            internal Building Source;
            internal Vector3 Position;
            internal Vector3 TargetPosition;
        }

        private readonly List<Snapshot> snapshots;
        private readonly List<Building> sourceBuildings;
        private readonly List<RoadPiece> roadPieces;
        private readonly BlueprintCopySession previewSession;
        private Vector3 lastDelta;
        private bool finished;

        public bool IsPreviewValid { get; private set; }

        public int RoadNodeCount => previewSession.RoadNodeCount;

        internal BuildingMoveSession(
            IEnumerable<Building> buildings,
            IReadOnlyList<RoadPiece> roads,
            Vector3 anchor)
        {
            snapshots = buildings
                .Distinct()
                .Select(building => new Snapshot
                {
                    Source = building,
                    Position = building.GetPosition(),
                    TargetPosition = building.GetPosition()
                })
                .ToList();
            sourceBuildings = snapshots.Select(snapshot => snapshot.Source).ToList();
            roadPieces = roads.ToList();
            previewSession = new BlueprintCopySession(
                sourceBuildings,
                roadPieces,
                anchor,
                allowRotation: false,
                checkBuildCosts: false,
                ignoredBuildings: sourceBuildings,
                validateRoads: true,
                canCommit: true,
                copyConfiguration: false,
                includePowerLines: false);
            SetSourcesVisible(false);
            previewSession.Place(Vector3.zero);
            SetSourcesVisible(false);
            IsPreviewValid = previewSession.IsPreviewValid;
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
            IsPreviewValid =
                previewSession.IsPreviewValid &&
                BatchPlacement.CanPay(GetMovingCosts());
        }

        public BatchCommitResult TryCommit()
        {
            if (finished || !IsPreviewValid)
            {
                return BatchCommitResult.Invalid;
            }

            if (lastDelta.sqrMagnitude <= 0.000001f)
            {
                Cancel();
                return BatchCommitResult.Success;
            }

            List<ResourceCost> costs = GetMovingCosts();
            if (!BatchPlacement.CanPay(costs))
            {
                IsPreviewValid = false;
                return BatchCommitResult.Invalid;
            }

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            previewSession.PrepareConfiguration();
            previewSession.EnsurePowerLines();
            previewSession.Place(lastDelta);
            SetSourcesVisible(false);
            long prepareMs = timer.ElapsedMilliseconds;
            if (previewSession.TryCommit() != BatchCommitResult.Success)
            {
                IsPreviewValid = false;
                return BatchCommitResult.Invalid;
            }

            long placeMs = timer.ElapsedMilliseconds;
            previewSession.TransferRuntimeState();
            DestroySources();
            long removeMs = timer.ElapsedMilliseconds;
            BatchPlacement.Pay(costs);
            WorldScripts.Inst.powerManager.SetNetworksDirty();
            PathFinder.SetRoadNetworkDirty();
            timer.Stop();
            Debug.Log(
                $"[AreaMove] Cut move commit {timer.ElapsedMilliseconds} ms " +
                $"(prepare {prepareMs}, place {placeMs - prepareMs}, remove {removeMs - placeMs})");
            finished = true;
            return BatchCommitResult.Success;
        }

        public void Cancel()
        {
            if (finished)
            {
                return;
            }

            previewSession.Cancel();
            SetSourcesVisible(true);
            Physics.SyncTransforms();
            finished = true;
        }

        private void DestroySources()
        {
            foreach (Building building in sourceBuildings)
            {
                if (building != null && building.IsValid())
                {
                    building.Destroy();
                }
            }

            NetworkClipper.DeleteSelection(roadPieces);
        }

        private List<ResourceCost> GetMovingCosts()
        {
            Dictionary<Resource, int> totals = new Dictionary<Resource, int>();
            foreach (Snapshot snapshot in snapshots)
            {
                float distance = Vector3.Distance(snapshot.Position, snapshot.TargetPosition);
                BatchPlacement.AddCosts(
                    totals,
                    snapshot.Source.GetDescription().GetMovingCosts(distance));
            }

            return totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
        }

        private void SetSourcesVisible(bool visible)
        {
            HashSet<BuildingConnector> connectors = new HashSet<BuildingConnector>();
            foreach (Building building in sourceBuildings)
            {
                building.SetRepresentationActive(visible);
                foreach (IntegratedNetEdge integrated in building.integratedNetEdges)
                {
                    NetEdge edge = integrated.edge?.Get();
                    if (edge?.representation != null)
                    {
                        edge.representation.gameObject.SetActive(visible);
                    }
                }

                foreach (BuildingConnector connector in building.buildingConnectors)
                {
                    if (connector != null && connector.IsValid() && connectors.Add(connector))
                    {
                        connector.SetRepresentationActive(visible);
                    }
                }
            }

            NetworkClipper.SetSourcesVisible(roadPieces, visible);
        }
    }
}
