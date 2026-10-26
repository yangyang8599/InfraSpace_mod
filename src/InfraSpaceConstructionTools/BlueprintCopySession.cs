using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class BlueprintCopySession
    {
        internal enum CommitResult
        {
            Success,
            Invalid
        }

        private sealed class BuildingCopy
        {
            internal Building Source;
            internal Building Ghost;
            internal Vector3 RelativePosition;
        }

        private sealed class NodeCopy
        {
            internal NetNode Ghost;
            internal Vector3 RelativePosition;
        }

        private sealed class EdgeCopy
        {
            internal NetEdge Ghost;
            internal BezierDef Bezier;
        }

        private static readonly HashSet<string> ConfigurationFields =
            new HashSet<string>
            {
                "resource",
                "productionDefinition",
                "playerStorageLimit",
                "collectionRadius",
                "distributionRadius",
                "radius",
                "targetResource"
            };

        private readonly List<BuildingCopy> buildings = new List<BuildingCopy>();
        private readonly List<NodeCopy> nodes = new List<NodeCopy>();
        private readonly List<NetEdge> ghostEdges = new List<NetEdge>();
        private readonly List<EdgeCopy> edgeCopies = new List<EdgeCopy>();
        private readonly List<BuildingConnector> ghostPowerLines = new List<BuildingConnector>();
        private readonly Dictionary<BuildingGroup, List<Building>> copiedGroups =
            new Dictionary<BuildingGroup, List<Building>>();
        private readonly Vector3 anchor;
        private readonly Vector3 pivot;
        private int quarterTurns;
        private bool finished;

        internal event Action Committed;

        internal bool IsPreviewValid { get; private set; }

        internal int RoadNodeCount => ghostEdges.Count;

        internal BlueprintCopySession(
            IEnumerable<Building> sourceBuildings,
            IReadOnlyList<RoadPiece> roads,
            Vector3 anchor)
        {
            this.anchor = anchor;
            CreateBuildingGhosts(sourceBuildings);
            CreatePowerLines();
            CreateRoadGhosts(roads);
            pivot = ComputePivot();
            IsPreviewValid = ValidatePreview();
        }

        internal void UpdatePreview(Vector2 mousePosition)
        {
            if (finished ||
                !AreaMoveController.TryGetTerrainPosition(mousePosition, out Vector3 current))
            {
                return;
            }

            ReadRotation();
            Vector3 delta = GetDelta(current);
            foreach (BuildingCopy copy in buildings)
            {
                copy.Ghost.SetRotation(copy.Source.GetRotationY() + quarterTurns * 90f);
                copy.Ghost.SetPosition(TransformPoint(copy.Source.GetPosition(), delta), doneByComputer: true);
            }

            foreach (NodeCopy copy in nodes)
            {
                copy.Ghost.SetPosition(TransformPoint(anchor + copy.RelativePosition, delta));
            }

            foreach (EdgeCopy copy in edgeCopies)
            {
                if (copy.Ghost != null && copy.Ghost.IsValid())
                {
                    NetworkClipper.ApplyBezier(copy.Ghost, RotateBezier(copy.Bezier, delta));
                }
            }

            RefreshPowerLines();
            Physics.SyncTransforms();
            IsPreviewValid = ValidatePreview() && CanPay(GetTotalCosts());
        }

        private Vector3 GetDelta(Vector3 current)
        {
            Vector3 raw = current - anchor;
            bool fineTune =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            if (fineTune || WorldScripts.Inst.gridManager.GetGridState() == GridStates.Off)
            {
                return raw;
            }

            if (buildings.Count == 0)
            {
                Vector3 movedPivot = TransformPoint(pivot, raw);
                Vector3 snappedPivot = WorldScripts.Inst.gridManager.SnapToGridCell(movedPivot);
                return raw + (snappedPivot - movedPivot);
            }

            BuildingCopy reference = buildings[0];
            Vector3 unsnapped = TransformPoint(reference.Source.GetPosition(), raw);
            float yaw = reference.Source.GetRotationY() + quarterTurns * 90f - WorldScripts.Inst.gridManager.angle;
            Vector3 snapped = WorldScripts.Inst.gridManager.SnapToGridBuilding(
                reference.Ghost.GetDescription().bounds.size,
                unsnapped,
                yaw);
            return raw + (snapped - unsnapped);
        }

        private void ReadRotation()
        {
            if (CustomInput.GetButtonDown(Keybinding.CameraRotateRight))
            {
                quarterTurns = (quarterTurns + 1) & 3;
            }
            else if (CustomInput.GetButtonDown(Keybinding.CameraRotateLeft))
            {
                quarterTurns = (quarterTurns + 3) & 3;
            }
        }

        private Vector3 ComputePivot()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (BuildingCopy copy in buildings)
            {
                sum += copy.Source.GetPosition();
                count++;
            }

            if (count == 0)
            {
                foreach (NodeCopy copy in nodes)
                {
                    sum += anchor + copy.RelativePosition;
                    count++;
                }
            }

            return count == 0 ? anchor : sum / count;
        }

        private Vector3 TransformPoint(Vector3 originalWorld, Vector3 delta)
        {
            Vector3 offset = originalWorld - pivot;
            if (quarterTurns != 0)
            {
                offset = Quaternion.Euler(0f, quarterTurns * 90f, 0f) * offset;
            }

            return pivot + offset + delta;
        }

        private BezierDef RotateBezier(BezierDef bezier, Vector3 delta)
        {
            return new BezierDef(
                TransformPoint(bezier.a, delta),
                TransformPoint(bezier.b, delta),
                TransformPoint(bezier.c, delta),
                TransformPoint(bezier.d, delta));
        }

        internal CommitResult TryCommit()
        {
            if (finished)
            {
                return CommitResult.Invalid;
            }

            List<ResourceCost> costs = GetTotalCosts();
            if (!ValidatePreview() || !CanPay(costs))
            {
                IsPreviewValid = false;
                return CommitResult.Invalid;
            }

            try
            {
                CreateBuildingGroups();
                foreach (NetEdge edge in ghostEdges)
                {
                    edge.SetIsBuilt(isBuilt: true, firstTime: true);
                }

                foreach (NodeCopy copy in nodes)
                {
                    if (copy.Ghost != null && copy.Ghost.IsValid())
                    {
                        copy.Ghost.UpdateShape();
                    }
                }

                foreach (NetEdge edge in ghostEdges)
                {
                    edge.UpdateShape(updateDoesWork: true, fromSave: false);
                }

                foreach (BuildingCopy copy in buildings)
                {
                    copy.Ghost.SetRepresentationActive(isActive: true);
                    copy.Ghost.SetIsBuilt(isBuilt: true);
                    copy.Ghost.UpdateDoesWork();
                }

                RefreshPowerLines();

                Pay(costs);
                WorldScripts.Inst.powerManager.SetNetworksDirty();
                PathFinder.SetRoadNetworkDirty();
                finished = true;
                Committed?.Invoke();
                return CommitResult.Success;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AreaMove] Blueprint placement failed: {exception}");
                IsPreviewValid = false;
                return CommitResult.Invalid;
            }
        }

        internal void Cancel()
        {
            if (finished)
            {
                return;
            }

            foreach (BuildingConnector connector in ghostPowerLines.ToArray())
            {
                if (connector != null && connector.IsValid())
                {
                    connector.Destroy();
                }
            }

            foreach (NetEdge edge in ghostEdges.ToArray())
            {
                if (edge != null && edge.IsValid())
                {
                    edge.Destroy();
                }
            }

            foreach (BuildingCopy copy in buildings)
            {
                if (copy.Ghost != null && copy.Ghost.IsValid())
                {
                    copy.Ghost.Destroy(destroyGroup: false);
                }
            }

            ghostPowerLines.Clear();
            ghostEdges.Clear();
            buildings.Clear();
            nodes.Clear();
            finished = true;
            PathFinder.SetRoadNetworkDirty();
        }

        private void CreateBuildingGhosts(IEnumerable<Building> sourceBuildings)
        {
            foreach (Building source in sourceBuildings.Distinct())
            {
                Building ghost = new Building(
                    source.buildingName,
                    source.GetPosition(),
                    source.GetRotationY());
                ghost.SetIsBuilt(isBuilt: false);
                CopyBuildingConfiguration(source, ghost);
                buildings.Add(new BuildingCopy
                {
                    Source = source,
                    Ghost = ghost,
                    RelativePosition = source.GetPosition() - anchor
                });

                if (source.group != null)
                {
                    if (!copiedGroups.TryGetValue(source.group, out List<Building> group))
                    {
                        group = new List<Building>();
                        copiedGroups[source.group] = group;
                    }

                    group.Add(ghost);
                }
            }
        }

        private void CreatePowerLines()
        {
            Dictionary<Building, Building> ghosts = new Dictionary<Building, Building>();
            foreach (BuildingCopy copy in buildings)
            {
                ghosts[copy.Source] = copy.Ghost;
            }

            HashSet<BuildingConnector> seen = new HashSet<BuildingConnector>();
            foreach (BuildingCopy copy in buildings)
            {
                foreach (BuildingConnector connector in copy.Source.buildingConnectors)
                {
                    if (connector == null ||
                        !connector.IsValid() ||
                        connector.type != "powerLine" ||
                        !seen.Add(connector))
                    {
                        continue;
                    }

                    Building other = connector.GetOther(copy.Source);
                    if (other == null || !ghosts.TryGetValue(other, out Building otherGhost))
                    {
                        continue;
                    }

                    ghostPowerLines.Add(
                        BuildingConnectorFactory.CreateBuildingConnector("powerLine", copy.Ghost, otherGhost));
                }
            }
        }

        private void RefreshPowerLines()
        {
            foreach (BuildingConnector connector in ghostPowerLines)
            {
                if (connector != null && connector.IsValid())
                {
                    connector.UpdateShape();
                }
            }
        }

        private void CreateRoadGhosts(IReadOnlyList<RoadPiece> roads)
        {
            Dictionary<NetNode, NetNode> shared = new Dictionary<NetNode, NetNode>();
            HashSet<NetNode> tracked = new HashSet<NetNode>();
            foreach (RoadPiece piece in roads)
            {
                if (piece?.Source == null || !piece.Source.IsValid())
                {
                    continue;
                }

                NetNode startGhost = null;
                NetNode endGhost = null;
                if (piece.StartInside)
                {
                    shared.TryGetValue(piece.Source.startNode, out startGhost);
                }

                if (piece.EndInside)
                {
                    shared.TryGetValue(piece.Source.endNode, out endGhost);
                }

                NetEdge ghost = NetworkClipper.CreateGhost(piece, startGhost, endGhost);
                if (ghost == null)
                {
                    continue;
                }

                ghostEdges.Add(ghost);
                edgeCopies.Add(new EdgeCopy
                {
                    Ghost = ghost,
                    Bezier = piece.Bezier
                });
                if (piece.StartInside && startGhost == null)
                {
                    shared[piece.Source.startNode] = ghost.startNode;
                }

                if (piece.EndInside && endGhost == null)
                {
                    shared[piece.Source.endNode] = ghost.endNode;
                }

                TrackNode(
                    ghost.startNode,
                    piece.StartInside ? piece.Source.startNode.GetPosition() : piece.Bezier.a,
                    tracked);
                TrackNode(
                    ghost.endNode,
                    piece.EndInside ? piece.Source.endNode.GetPosition() : piece.Bezier.d,
                    tracked);
            }
        }

        private void TrackNode(NetNode ghost, Vector3 worldPosition, HashSet<NetNode> tracked)
        {
            if (ghost == null || !tracked.Add(ghost))
            {
                return;
            }

            nodes.Add(new NodeCopy
            {
                Ghost = ghost,
                RelativePosition = worldPosition - anchor
            });
        }

        private bool ValidatePreview()
        {
            foreach (BuildingCopy copy in buildings)
            {
                if (!WorldScripts.Inst.world.IsPointInPlayableArea(copy.Ghost.GetPosition()))
                {
                    return false;
                }
            }

            SetBuildingRepresentationsActive(false);
            foreach (BuildingCopy copy in buildings)
            {
                copy.Ghost.SetRepresentationActive(isActive: true);
                Physics.SyncTransforms();
                bool canPlace = copy.Ghost.CanPlace(checkCollision: true);
                copy.Ghost.SetRepresentationActive(isActive: false);
                if (!canPlace)
                {
                    SetBuildingRepresentationsActive(true);
                    return false;
                }
            }

            SetBuildingRepresentationsActive(true);
            return nodes.All(copy =>
                       WorldScripts.Inst.world.IsPointInPlayableArea(copy.Ghost.GetPosition())) &&
                   ghostEdges.All(edge => edge.DoesWork());
        }

        private void SetBuildingRepresentationsActive(bool active)
        {
            foreach (BuildingCopy copy in buildings)
            {
                copy.Ghost.SetRepresentationActive(active);
            }
        }

        private void CreateBuildingGroups()
        {
            foreach (List<Building> group in copiedGroups.Values)
            {
                if (group.Count > 0)
                {
                    new BuildingGroup(group);
                }
            }
        }

        private static void CopyBuildingConfiguration(Building source, Building target)
        {
            target.customName = source.customName;
            HashSet<string> sourceUpgrades =
                new HashSet<string>(source.upgrades.Select(upgrade => upgrade.upgradeType));
            foreach (BuildingUpgrade upgrade in target.upgrades.ToList())
            {
                if (!sourceUpgrades.Contains(upgrade.upgradeType))
                {
                    target.RemoveUpgrade(upgrade.upgradeType);
                }
            }

            foreach (string upgrade in sourceUpgrades)
            {
                if (target.upgrades.All(existing => existing.upgradeType != upgrade))
                {
                    target.AddUpgrade(upgrade);
                }
            }

            object sourceLogic = source.consumerProducer?.productionLogic;
            object targetLogic = target.consumerProducer?.productionLogic;
            if (sourceLogic == null ||
                targetLogic == null ||
                sourceLogic.GetType() != targetLogic.GetType())
            {
                return;
            }

            Type logicType = sourceLogic.GetType();
            foreach (string fieldName in ConfigurationFields)
            {
                FieldInfo field = logicType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public);
                if (field != null && !field.IsInitOnly)
                {
                    field.SetValue(targetLogic, field.GetValue(sourceLogic));
                }
            }

            target.consumerProducer.ResetConsumablesProducables();
        }

        private List<ResourceCost> GetTotalCosts()
        {
            Dictionary<Resource, int> totals = new Dictionary<Resource, int>();
            foreach (BuildingCopy copy in buildings)
            {
                AddCosts(totals, copy.Ghost.GetDescription().GetMultipliedCosts(1));
            }

            foreach (NetEdge edge in ghostEdges)
            {
                AddCosts(totals, edge.GetPrice());
            }

            return totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
        }

        private static void AddCosts(
            Dictionary<Resource, int> totals,
            IEnumerable<ResourceCost> costs)
        {
            foreach (ResourceCost cost in costs)
            {
                totals[cost.resource] = totals.TryGetValue(cost.resource, out int amount)
                    ? amount + cost.amount
                    : cost.amount;
            }
        }

        private static bool CanPay(IReadOnlyCollection<ResourceCost> costs)
        {
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
