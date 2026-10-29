using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal static class StraightExtendFeature
    {
        private static readonly FieldInfo GhostField =
            AccessTools.Field(typeof(NetworkConstructionManager), "splitGhostEdge");

        private static readonly FieldInfo PredecessorField =
            AccessTools.Field(typeof(NetworkConstructionManager), "bezierLockedEdgePredecessor");

        private static readonly FieldInfo SecondConnectableField =
            AccessTools.Field(typeof(NetworkConstructionManager), "secondConnectable");

        private static readonly FieldInfo PointerField =
            AccessTools.Field(typeof(NetworkConstructionManager), "pointerGhost");

        private static readonly FieldInfo OffsetField =
            AccessTools.Field(typeof(EdgeBezierControl), "offsetFromNode");

        private static readonly HashSet<NetEdge> PendingEdges = new HashSet<NetEdge>();

        private static NetNode lockedNode;

        private static Vector2 lockedTangent;

        internal static bool Enabled =>
            ConstructionToolsMod.Instance != null && ConstructionToolsMod.Instance.straightExtend;

        internal static void Tick()
        {
            if (!RoadSnapFeature.IsBuildingLine() || DionicGame.GetModalManager().IsGameModal())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.J) && ConstructionToolsMod.Instance != null)
            {
                ConstructionToolsMod.Instance.straightExtend = !ConstructionToolsMod.Instance.straightExtend;
                lockedNode = null;
            }
        }

        internal static void Apply(NetworkConstructionManager manager)
        {
            if (!Enabled || manager == null || !RoadSnapFeature.IsBuildingLine(manager))
            {
                lockedNode = null;
                return;
            }

            SplitGhostEdge ghost = GhostField?.GetValue(manager) as SplitGhostEdge;
            if (ghost == null)
            {
                lockedNode = null;
                return;
            }

            NetNode start = ghost.GetStartNode();
            NetNode end = ghost.GetEndNode();
            if (start == null || end == null)
            {
                return;
            }

            if (lockedNode != start)
            {
                if (!TryCapture(manager, ghost, out Vector2 captured))
                {
                    lockedNode = null;
                    return;
                }

                lockedNode = start;
                lockedTangent = captured;
            }

            Vector2 origin = Flat(start.GetPosition());
            Vector2 endFlat = Flat(end.GetPosition());
            float length = Vector2.Dot(endFlat - origin, lockedTangent);
            if (Mathf.Abs(length) < 1f)
            {
                length = length < 0f ? -1f : 1f;
            }

            Vector2 projected = origin + (lockedTangent * length);
            bool rail = ghost.GetStartEdge() != null && ghost.GetStartEdge().networkType == NetworkType.Rail;
            bool moved = (endFlat - projected).sqrMagnitude > 0.0025f;
            if (rail && end.GetEdges().Count > 1 && !RailAligned(end))
            {
                moved = true;
            }
            if (moved)
            {
                Vector3 projectedPos = new Vector3(projected.x, end.GetPosition().y, projected.y);
                ghost.SetEndNode(projectedPos, true);
                SecondConnectableField?.SetValue(manager, null);
                end = ghost.GetEndNode();
            }

            if (ghost.GetStartEdge() != null && ghost.GetStartEdge().networkType == NetworkType.Road)
            {
                StraightenGhost(ghost);
            }

            PlacePointer(manager, end);
            if (manager.distanceVisual != null && end != null)
            {
                manager.distanceVisual.gameObject.SetActive(true);
                manager.distanceVisual.Set(start.GetPosition(), end.GetPosition(), 0.8f);
            }

            if (manager.angleVisual != null)
            {
                manager.angleVisual.gameObject.SetActive(false);
            }
        }

        internal static void RememberCommitEdges(NetworkConstructionManager manager)
        {
            PendingEdges.Clear();
            if (!Enabled || manager == null || lockedNode == null)
            {
                return;
            }

            SplitGhostEdge ghost = GhostField?.GetValue(manager) as SplitGhostEdge;
            if (ghost == null)
            {
                return;
            }

            foreach (NetEdge edge in ghost.GetChildEdges())
            {
                if (edge != null)
                {
                    PendingEdges.Add(edge);
                }
            }
        }

        internal static void ForgetCommitEdges()
        {
            PendingEdges.Clear();
        }

        internal static bool KeepStraight(EdgeBezierControl control)
        {
            if (!Enabled || control == null || OffsetField == null)
            {
                return false;
            }

            NetEdge edge = control.edge;
            if (edge == null || edge.networkType != NetworkType.Road)
            {
                return false;
            }

            if (!IsConstrainedEdge(edge))
            {
                return false;
            }

            WriteStraightOffset(control, edge);
            return true;
        }

        private static void StraightenGhost(SplitGhostEdge ghost)
        {
            foreach (NetEdge edge in ghost.GetChildEdges())
            {
                if (edge == null)
                {
                    continue;
                }

                WriteStraightOffset(edge.startBezierControl, edge);
                WriteStraightOffset(edge.endBezierControl, edge);
                edge.UpdateShape(true, false);
            }
        }

        private static void WriteStraightOffset(EdgeBezierControl control, NetEdge edge)
        {
            if (control == null || edge.startNode == null || edge.endNode == null)
            {
                return;
            }

            Vector2 delta = Flat(edge.endNode.GetPosition()) - Flat(edge.startNode.GetPosition());
            Vector2 handle = delta / 3f;
            if (control.GetNetNode() != edge.startNode)
            {
                handle = -handle;
            }

            OffsetField.SetValue(control, handle);
            control.snappedStraight = true;
            control.overrideAngle = false;
            control.playerDefined = false;
        }

        private static bool IsConstrainedEdge(NetEdge edge)
        {
            if (PendingEdges.Contains(edge))
            {
                return true;
            }

            if (lockedNode == null)
            {
                return false;
            }

            NetworkConstructionManager manager = WorldScripts.Inst == null
                ? null
                : WorldScripts.Inst.networkConstructionManager;
            SplitGhostEdge ghost = manager == null ? null : GhostField?.GetValue(manager) as SplitGhostEdge;
            return ghost != null && ghost.GetStartNode() == lockedNode && ghost.ContainsEdge(edge);
        }

        private static bool TryCapture(NetworkConstructionManager manager, SplitGhostEdge ghost, out Vector2 tangent)
        {
            tangent = Vector2.zero;
            NetNode start = ghost.GetStartNode();
            NetEdge startEdge = ghost.GetStartEdge();
            if (start == null || startEdge == null)
            {
                return false;
            }

            if (startEdge.networkType == NetworkType.Rail)
            {
                if (start.railNode == null || !HasForeignEdge(ghost, start))
                {
                    return false;
                }

                Vector3 direction = start.railNode.bezierDirection;
                Vector2 flat = Flat(direction);
                if (flat.sqrMagnitude < 0.0001f)
                {
                    return false;
                }

                tangent = flat.normalized;
                return true;
            }

            if (startEdge.networkType != NetworkType.Road)
            {
                return false;
            }

            NetEdge source = ghost.GetStartBezierControl() == null
                ? null
                : ghost.GetStartBezierControl().GetPartner();
            if (source != null && (ghost.ContainsEdge(source) || !source.IsValid()))
            {
                source = null;
            }

            if (source == null)
            {
                NetEdge predecessor = PredecessorField?.GetValue(manager) as NetEdge;
                if (predecessor != null &&
                    predecessor.IsValid() &&
                    !ghost.ContainsEdge(predecessor) &&
                    (predecessor.startNode == start || predecessor.endNode == start))
                {
                    source = predecessor;
                }
            }

            if (source != null)
            {
                return ForwardFrom(source, start, out tangent);
            }

            int foreignCount = 0;
            NetEdge only = null;
            foreach (NetEdge edge in start.GetEdges())
            {
                if (edge == null || !edge.IsValid() || ghost.ContainsEdge(edge))
                {
                    continue;
                }

                foreignCount++;
                only = edge;
            }

            if (foreignCount == 1)
            {
                return ForwardFrom(only, start, out tangent);
            }

            if (foreignCount == 0)
            {
                return false;
            }

            Vector2 delta = Flat(ghost.GetEndNode().GetPosition()) - Flat(start.GetPosition());
            if (delta.sqrMagnitude < 0.5f)
            {
                return false;
            }

            float bestAngle = float.MaxValue;
            bool found = false;
            foreach (NetEdge edge in start.GetEdges())
            {
                if (edge == null || !edge.IsValid() || ghost.ContainsEdge(edge))
                {
                    continue;
                }

                if (!ForwardFrom(edge, start, out Vector2 forward))
                {
                    continue;
                }

                float angle = Vector2.Angle(forward, delta);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    tangent = forward;
                    found = true;
                }
            }

            return found;
        }

        private static bool RailAligned(NetNode node)
        {
            if (node == null || node.railNode == null)
            {
                return false;
            }

            Vector2 direction = Flat(node.railNode.bezierDirection);
            if (direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float angle = Vector2.Angle(direction.normalized, lockedTangent);
            return angle < 8f || angle > 172f;
        }

        private static bool HasForeignEdge(SplitGhostEdge ghost, NetNode node)
        {
            foreach (NetEdge edge in node.GetEdges())
            {
                if (edge != null && edge.IsValid() && !ghost.ContainsEdge(edge))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ForwardFrom(NetEdge edge, NetNode node, out Vector2 forward)
        {
            forward = Vector2.zero;
            Vector2 back = Flat(edge.GetDirectionFromNode(node));
            if (back.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward = (-back).normalized;
            return true;
        }

        private static void PlacePointer(NetworkConstructionManager manager, NetNode end)
        {
            if (end == null || PointerField == null)
            {
                return;
            }

            GameObject pointer = PointerField.GetValue(manager) as GameObject;
            if (pointer == null)
            {
                return;
            }

            pointer.transform.position = end.GetEdgePosition() + NetworkConstructionManager.pointerGhostOffset;
        }

        private static Vector2 Flat(Vector3 position)
        {
            return new Vector2(position.x, position.z);
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.CustomUpdate))]
    internal static class StraightExtendUpdatePatch
    {
        private static void Postfix(NetworkConstructionManager __instance)
        {
            StraightExtendFeature.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(NetworkConstructionManager), nameof(NetworkConstructionManager.HandleLeftMouseClick))]
    internal static class StraightExtendCommitPatch
    {
        private static void Prefix(NetworkConstructionManager __instance)
        {
            StraightExtendFeature.RememberCommitEdges(__instance);
        }

        private static void Postfix()
        {
            StraightExtendFeature.ForgetCommitEdges();
        }
    }

    [HarmonyPatch(typeof(EdgeBezierControl), nameof(EdgeBezierControl.SetAutoOffsetRoadPipe))]
    internal static class StraightExtendRoadPatch
    {
        private static bool Prefix(EdgeBezierControl __instance)
        {
            return !StraightExtendFeature.KeepStraight(__instance);
        }
    }
}
