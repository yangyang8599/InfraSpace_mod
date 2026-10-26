using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class BlueprintCopySession : IBatchPlacementSession
    {
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
        private readonly List<Building> ghostBuildings = new List<Building>();
        private readonly List<NodeCopy> nodes = new List<NodeCopy>();
        private readonly List<NetEdge> ghostEdges = new List<NetEdge>();
        private readonly List<EdgeCopy> edgeCopies = new List<EdgeCopy>();
        private readonly List<BuildingConnector> ghostPowerLines = new List<BuildingConnector>();
        private readonly Dictionary<BuildingGroup, List<Building>> copiedGroups =
            new Dictionary<BuildingGroup, List<Building>>();
        private readonly IReadOnlyList<Building> ignoredBuildings;
        private readonly Vector3 anchor;
        private readonly Vector3 pivot;
        private readonly bool allowRotation;
        private readonly bool checkBuildCosts;
        private readonly bool validateRoads;
        private readonly bool canCommit;
        private readonly bool copyConfiguration;
        private readonly bool includePowerLines;
        private bool configurationCopied;
        private bool powerLinesCreated;
        private int quarterTurns;
        private bool finished;

        internal Vector3 LastDelta { get; private set; }

        public bool IsPreviewValid { get; private set; }

        public int RoadNodeCount => ghostEdges.Count;

        internal BlueprintCopySession(
            IEnumerable<Building> sourceBuildings,
            IReadOnlyList<RoadPiece> roads,
            Vector3 anchor,
            bool allowRotation = true,
            bool checkBuildCosts = true,
            IReadOnlyList<Building> ignoredBuildings = null,
            bool validateRoads = true,
            bool canCommit = true,
            bool copyConfiguration = true,
            bool includePowerLines = true)
        {
            this.anchor = anchor;
            this.allowRotation = allowRotation;
            this.checkBuildCosts = checkBuildCosts;
            this.ignoredBuildings = ignoredBuildings;
            this.validateRoads = validateRoads;
            this.canCommit = canCommit;
            this.copyConfiguration = copyConfiguration;
            this.includePowerLines = includePowerLines;
            CreateBuildingGhosts(sourceBuildings);
            if (includePowerLines)
            {
                EnsurePowerLines();
            }
            CreateRoadGhosts(roads);
            pivot = ComputePivot();
            IsPreviewValid = ValidatePreview();
        }

        public void UpdatePreview(Vector2 mousePosition)
        {
            if (finished ||
                !AreaMoveController.TryGetTerrainPosition(mousePosition, out Vector3 current))
            {
                return;
            }

            if (allowRotation)
            {
                ReadRotation();
            }

            Place(GetDelta(current));
        }

        internal void Place(Vector3 delta)
        {
            if (finished)
            {
                return;
            }

            LastDelta = delta;
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
            IsPreviewValid =
                ValidatePreview() &&
                (!checkBuildCosts || BatchPlacement.CanPay(GetTotalCosts()));
        }

        private Vector3 GetDelta(Vector3 current)
        {
            Vector3 raw = BatchPlacement.GetRawDelta(anchor, current);
            if (BatchPlacement.ShouldUseRawTranslation())
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

        public BatchCommitResult TryCommit()
        {
            if (finished || !canCommit)
            {
                return BatchCommitResult.Invalid;
            }

            List<ResourceCost> costs = checkBuildCosts
                ? GetTotalCosts()
                : new List<ResourceCost>();
            if (!ValidatePreview() ||
                (checkBuildCosts && !BatchPlacement.CanPay(costs)))
            {
                IsPreviewValid = false;
                return BatchCommitResult.Invalid;
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

                if (checkBuildCosts)
                {
                    BatchPlacement.Pay(costs);
                }
                WorldScripts.Inst.powerManager.SetNetworksDirty();
                PathFinder.SetRoadNetworkDirty();
                finished = true;
                return BatchCommitResult.Success;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AreaMove] Blueprint placement failed: {exception}");
                IsPreviewValid = false;
                return BatchCommitResult.Invalid;
            }
        }

        public void Cancel()
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
            ghostBuildings.Clear();
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
                ghostBuildings.Add(ghost);
                if (copyConfiguration)
                {
                    CopyBuildingConfiguration(source, ghost);
                }
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

            configurationCopied = copyConfiguration;
        }

        internal void PrepareConfiguration()
        {
            if (configurationCopied)
            {
                return;
            }

            foreach (BuildingCopy copy in buildings)
            {
                CopyBuildingConfiguration(copy.Source, copy.Ghost);
            }

            configurationCopied = true;
        }

        internal void EnsurePowerLines()
        {
            if (powerLinesCreated)
            {
                return;
            }

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

            powerLinesCreated = true;
        }

        internal void TransferRuntimeState()
        {
            foreach (BuildingCopy copy in buildings)
            {
                ConsumerProducer source = copy.Source.consumerProducer;
                ConsumerProducer target = copy.Ghost.consumerProducer;
                if (source == null || target == null)
                {
                    continue;
                }

                CopyStorage(source.incomingStorage, target.incomingStorage);
                CopyStorage(source.outgoingStorage, target.outgoingStorage);
                source.ReplaceIncomingCars(target);
            }
        }

        private static void CopyStorage(int[] source, int[] target)
        {
            if (source == null || target == null)
            {
                return;
            }

            Array.Copy(source, target, Math.Min(source.Length, target.Length));
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

            if (!BatchPlacement.CanPlaceBuildings(ghostBuildings, ignoredBuildings))
            {
                return false;
            }

            return nodes.All(copy =>
                       WorldScripts.Inst.world.IsPointInPlayableArea(copy.Ghost.GetPosition())) &&
                   (!validateRoads || ghostEdges.All(edge => edge.DoesWork()));
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
                BatchPlacement.AddCosts(totals, copy.Ghost.GetDescription().GetMultipliedCosts(1));
            }

            foreach (NetEdge edge in ghostEdges)
            {
                BatchPlacement.AddCosts(totals, edge.GetPrice());
            }

            return totals.Select(pair => new ResourceCost(pair.Key, pair.Value)).ToList();
        }

    }
}
