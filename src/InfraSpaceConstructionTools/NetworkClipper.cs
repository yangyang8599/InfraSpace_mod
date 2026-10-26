using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class RoadPiece
    {
        internal NetEdge Source;
        internal bool Entire;
        internal bool StartInside;
        internal bool EndInside;
        internal float StartT;
        internal float EndT;
        internal BezierDef Bezier;
    }

    internal static class NetworkClipper
    {
        private const int Samples = 40;
        private const float MinimumLength = 1.25f;

        private static readonly FieldInfo OffsetField = typeof(EdgeBezierControl).GetField(
            "offsetFromNode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDefinedField = typeof(EdgeBezierControl).GetField(
            "playerDefined",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo SnappedField = typeof(EdgeBezierControl).GetField(
            "snappedStraight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static List<RoadPiece> Clip(Rect screenRect)
        {
            List<RoadPiece> pieces = new List<RoadPiece>();
            Camera camera = Camera.main;
            if (camera == null || WorldScripts.Inst?.registry?.netEdgesList == null)
            {
                return pieces;
            }

            foreach (NetEdge edge in WorldScripts.Inst.registry.netEdgesList.ToArray())
            {
                if (!CanClip(edge))
                {
                    continue;
                }

                RoadPiece piece = ClipEdge(edge, screenRect, camera);
                if (piece != null)
                {
                    pieces.Add(piece);
                }
            }

            return pieces;
        }

        internal static void SetSourcesVisible(IReadOnlyList<RoadPiece> pieces, bool visible)
        {
            HashSet<NetEdge> updated = new HashSet<NetEdge>();
            foreach (RoadPiece piece in pieces)
            {
                if (piece.Source == null ||
                    !piece.Source.IsValid() ||
                    piece.Source.representation == null ||
                    !updated.Add(piece.Source))
                {
                    continue;
                }

                piece.Source.representation.SetVisible(visible);
            }
        }

        internal static NetEdge CreateGhost(RoadPiece piece)
        {
            return CreateGhost(piece, null, null);
        }

        internal static NetEdge CreateGhost(RoadPiece piece, NetNode startNode, NetNode endNode)
        {
            if (piece?.Source == null || piece.Bezier.a == piece.Bezier.d)
            {
                return null;
            }

            NetworkType type = piece.Source.networkType;
            string name = piece.Source.GetEdgeName();
            if (!CanAttach(startNode, type, name))
            {
                startNode = null;
            }

            if (!CanAttach(endNode, type, name) || startNode == endNode)
            {
                endNode = null;
            }

            NetEdge ghost;
            if (startNode == null && endNode == null)
            {
                ghost = new NetEdge(type, name, piece.Bezier.a, piece.Bezier.d);
            }
            else if (endNode == null)
            {
                ghost = new NetEdge(type, name, startNode, piece.Bezier.d);
            }
            else if (startNode == null)
            {
                ghost = new NetEdge(type, name, null, piece.Bezier.a, endNode, Vector3.zero);
            }
            else
            {
                ghost = new NetEdge(type, name, startNode, endNode);
            }

            ApplyBezier(ghost, piece.Bezier);
            return ghost;
        }

        private static bool CanAttach(NetNode node, NetworkType type, string name)
        {
            return node != null &&
                   node.IsValid() &&
                   node.CanAddEdge(type, name, out _, out _);
        }

        internal static void Commit(IReadOnlyList<RoadPiece> pieces, Vector3 delta)
        {
            PrepareSources(pieces);
            HashSet<NetNode> moving = new HashSet<NetNode>();
            List<NetEdge> reshape = new List<NetEdge>();
            foreach (RoadPiece piece in pieces)
            {
                if (piece.Source == null || !piece.Source.IsValid())
                {
                    continue;
                }

                if (piece.Entire)
                {
                    moving.Add(piece.Source.startNode);
                    moving.Add(piece.Source.endNode);
                    reshape.Add(piece.Source);
                    continue;
                }

                if (piece.StartInside)
                {
                    KeepInsideEnd(piece.Source, piece.Bezier.d, piece.Source.startNode.GetPosition(), moving, reshape);
                }
                else if (piece.EndInside)
                {
                    KeepInsideEnd(piece.Source, piece.Bezier.a, piece.Source.endNode.GetPosition(), moving, reshape);
                }
                else
                {
                    ExtractMiddle(piece, delta);
                }
            }

            foreach (NetNode node in moving)
            {
                if (node != null && node.IsValid())
                {
                    node.SetPosition(node.GetPosition() + delta, updateShape: false);
                }
            }

            foreach (NetNode node in moving)
            {
                if (node != null && node.IsValid())
                {
                    node.UpdateShape();
                }
            }

            HashSet<NetEdge> affectedEdges = CollectEdges(moving, reshape);
            HashSet<Road> affectedRoads = new HashSet<Road>();
            foreach (NetEdge edge in affectedEdges)
            {
                edge.SetIsBuilt(isBuilt: true, firstTime: false);
                RefreshEdge(edge);
                if (edge.road != null)
                {
                    affectedRoads.Add(edge.road);
                }
            }

            if (affectedRoads.Count > 0)
            {
                Road.UpdateSideRoads(affectedRoads);
            }
        }

        private static void PrepareSources(IReadOnlyList<RoadPiece> pieces)
        {
            HashSet<NetEdge> prepared = new HashSet<NetEdge>();
            foreach (RoadPiece piece in pieces)
            {
                NetEdge edge = piece?.Source;
                if (edge == null || !edge.IsValid() || !prepared.Add(edge))
                {
                    continue;
                }

                edge.road?.RemoveSideRoads();
                edge.SetIsBuilt(isBuilt: false, firstTime: false);
                if (edge.AffectsTerrain())
                {
                    WorldScripts.Inst.roadDecalManager.UpdateArea(edge, batch: true);
                }
            }
        }

        private static HashSet<NetEdge> CollectEdges(HashSet<NetNode> moving, List<NetEdge> reshape)
        {
            HashSet<NetEdge> edges = new HashSet<NetEdge>();
            foreach (NetEdge edge in reshape)
            {
                if (edge != null && edge.IsValid())
                {
                    edges.Add(edge);
                }
            }

            foreach (NetNode node in moving)
            {
                if (node == null || !node.IsValid())
                {
                    continue;
                }

                foreach (NetEdge edge in node.GetEdges())
                {
                    if (edge != null && edge.IsValid())
                    {
                        edges.Add(edge);
                    }
                }
            }

            return edges;
        }

        private static void RefreshEdge(NetEdge edge)
        {
            if (edge.representation != null && edge.representation.GetInstancedRendererIndex() < 0)
            {
                edge.representation.SetVisible(true);
            }

            Bounds previous = edge.oldBounds;
            edge.UpdateShape(updateDoesWork: true, fromSave: false);
            edge.ApplyShape(fromSave: false);
            if (!edge.AffectsTerrain())
            {
                return;
            }

            Bounds current = edge.GetBounds();
            if (previous != default(Bounds))
            {
                WorldScripts.Inst.world.UpdateTerraformablesDetail(previous, remove: false);
                InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(previous, immediately: true);
            }

            WorldScripts.Inst.world.UpdateTerraformablesDetail(current, remove: true);
            InfraSpaceCore.GetGrassRemovalModule().UpdateTerrainDetail(current, immediately: true);
            TerrainAdaptation.UpdateTerrainAdaptation(Union(previous, current));
        }

        private static Bounds Union(Bounds previous, Bounds current)
        {
            if (previous == default(Bounds))
            {
                current.Expand(6f);
                return current;
            }

            previous.Encapsulate(current);
            previous.Expand(6f);
            return previous;
        }

        private static void KeepInsideEnd(
            NetEdge source,
            Vector3 cut,
            Vector3 insidePoint,
            HashSet<NetNode> moving,
            List<NetEdge> reshape)
        {
            NetNode middle = source.Split(cut);
            NetEdge inside = ClosestHalf(middle, insidePoint);
            if (inside == null)
            {
                return;
            }

            NetNode keep = Other(inside, middle);
            NetEdge replacement = DetachFromCut(inside, middle, keep);
            if (replacement == null)
            {
                return;
            }

            moving.Add(keep);
            moving.Add(replacement.endNode);
            reshape.Add(replacement);
            foreach (NetEdge edge in middle.GetEdges())
            {
                if (edge != null && edge.IsValid())
                {
                    reshape.Add(edge);
                }
            }

            replacement.SetIsBuilt(isBuilt: true, firstTime: true);
        }

        private static void ExtractMiddle(RoadPiece piece, Vector3 delta)
        {
            NetworkType networkType = piece.Source.networkType;
            string edgeName = piece.Source.GetEdgeName();
            Vector3 endPosition = piece.Source.endNode.GetPosition();
            NetNode firstCut = piece.Source.Split(piece.Bezier.a);
            NetEdge far = ClosestHalf(firstCut, endPosition);
            if (far == null)
            {
                return;
            }

            NetNode secondCut = far.Split(piece.Bezier.d);
            NetEdge middle = EdgeBetween(firstCut, secondCut);
            middle?.Destroy();
            BezierDef moved = piece.Bezier;
            moved.a += delta;
            moved.b += delta;
            moved.c += delta;
            moved.d += delta;
            NetEdge ghost = new NetEdge(networkType, edgeName, moved.a, moved.d);
            ApplyBezier(ghost, moved);
            ghost.SetIsBuilt(isBuilt: true, firstTime: true);
            RefreshEdge(ghost);
            RefreshNodeEdges(firstCut);
            RefreshNodeEdges(secondCut);
        }

        private static void RefreshNodeEdges(NetNode node)
        {
            if (node == null || !node.IsValid())
            {
                return;
            }

            foreach (NetEdge edge in node.GetEdges())
            {
                if (edge != null && edge.IsValid())
                {
                    edge.SetIsBuilt(isBuilt: true, firstTime: false);
                    RefreshEdge(edge);
                }
            }
        }

        private static NetEdge DetachFromCut(NetEdge insideEdge, NetNode cutNode, NetNode keepNode)
        {
            if (insideEdge == null || cutNode == null || keepNode == null || !keepNode.IsValid())
            {
                return null;
            }

            Vector3 cutPosition = cutNode.GetPosition();
            BezierDef bezier = insideEdge.GetBezierDef();
            NetworkType networkType = insideEdge.networkType;
            string edgeName = insideEdge.GetEdgeName();
            bool keepHasCompany = keepNode.GetEdges().Count > 1;
            if (keepHasCompany)
            {
                insideEdge.Destroy();
            }

            if (!keepNode.IsValid())
            {
                return null;
            }

            NetEdge replacement = new NetEdge(networkType, edgeName, keepNode, cutPosition);
            ApplyOriented(replacement, bezier, keepNode.GetPosition(), cutPosition);
            if (!keepHasCompany)
            {
                insideEdge.Destroy();
            }

            return replacement;
        }

        private static RoadPiece ClipEdge(NetEdge edge, Rect screenRect, Camera camera)
        {
            BezierDef bezier = edge.GetBezierDef();
            bool startInside = Contains(camera, screenRect, edge.startNode.GetPosition());
            bool endInside = Contains(camera, screenRect, edge.endNode.GetPosition());
            if (startInside && endInside)
            {
                return new RoadPiece
                {
                    Source = edge,
                    Entire = true,
                    StartInside = true,
                    EndInside = true,
                    StartT = 0f,
                    EndT = 1f,
                    Bezier = bezier
                };
            }

            float bestStart = 0f;
            float bestEnd = 0f;
            float bestLength = 0f;
            bool inSpan = false;
            float spanStart = 0f;
            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                bool inside = Contains(camera, screenRect, Evaluate(bezier, t));
                if (inside && !inSpan)
                {
                    inSpan = true;
                    spanStart = i == 0
                        ? 0f
                        : Crossing(bezier, camera, screenRect, (i - 1) / (float)Samples, t);
                }
                else if (!inside && inSpan)
                {
                    inSpan = false;
                    float spanEnd = Crossing(bezier, camera, screenRect, (i - 1) / (float)Samples, t);
                    Consider(bezier, spanStart, spanEnd, ref bestStart, ref bestEnd, ref bestLength);
                }
            }

            if (inSpan)
            {
                Consider(bezier, spanStart, 1f, ref bestStart, ref bestEnd, ref bestLength);
            }

            if (bestLength <= 0f && !startInside && !endInside)
            {
                return null;
            }

            if (startInside)
            {
                bestStart = 0f;
            }

            if (endInside)
            {
                bestEnd = 1f;
            }

            bestLength = Vector3.Distance(Evaluate(bezier, bestStart), Evaluate(bezier, bestEnd));
            if (bestLength < MinimumLength || bestEnd - bestStart < 0.02f)
            {
                return null;
            }

            return new RoadPiece
            {
                Source = edge,
                Entire = false,
                StartInside = startInside,
                EndInside = endInside,
                StartT = bestStart,
                EndT = bestEnd,
                Bezier = Slice(bezier, bestStart, bestEnd)
            };
        }

        private static void Consider(
            BezierDef bezier,
            float start,
            float end,
            ref float bestStart,
            ref float bestEnd,
            ref float bestLength)
        {
            float length = Vector3.Distance(Evaluate(bezier, start), Evaluate(bezier, end));
            if (length > bestLength)
            {
                bestStart = start;
                bestEnd = end;
                bestLength = length;
            }
        }

        private static float Crossing(BezierDef bezier, Camera camera, Rect screenRect, float t0, float t1)
        {
            bool insideAtStart = Contains(camera, screenRect, Evaluate(bezier, t0));
            float lo = t0;
            float hi = t1;
            for (int i = 0; i < 8; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Contains(camera, screenRect, Evaluate(bezier, mid)) == insideAtStart)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return (lo + hi) * 0.5f;
        }

        private static BezierDef Slice(BezierDef bezier, float startT, float endT)
        {
            startT = Mathf.Clamp01(startT);
            endT = Mathf.Clamp01(endT);
            if (endT - startT < 0.0001f)
            {
                Vector3 point = Evaluate(bezier, startT);
                return new BezierDef(point, point, point, point);
            }

            if (startT <= 0.0001f && endT >= 0.9999f)
            {
                return bezier;
            }

            if (startT <= 0.0001f)
            {
                bezier.Split(endT, out BezierDef first, out _);
                return first;
            }

            bezier.Split(startT, out _, out BezierDef rest);
            float local = Mathf.Clamp((endT - startT) / (1f - startT), 0.0001f, 0.9999f);
            if (endT >= 0.9999f)
            {
                return rest;
            }

            rest.Split(local, out BezierDef inside, out _);
            return inside;
        }

        internal static void ApplyBezier(NetEdge edge, BezierDef bezier)
        {
            ApplyOriented(edge, bezier, bezier.a, bezier.d);
        }

        private static void ApplyOriented(NetEdge edge, BezierDef bezier, Vector3 start, Vector3 end)
        {
            bool forward = Vector3.Distance(bezier.a, start) <= Vector3.Distance(bezier.d, start);
            Vector3 from = forward ? bezier.a : bezier.d;
            Vector3 controlA = forward ? bezier.b : bezier.c;
            Vector3 controlB = forward ? bezier.c : bezier.b;
            Vector3 to = forward ? bezier.d : bezier.a;
            SetControl(edge.startBezierControl, Xz(controlA - from));
            SetControl(edge.endBezierControl, Xz(controlB - to));
            edge.UpdateShape(updateDoesWork: false, fromSave: false);
            if (edge.representation != null)
            {
                BezierDef visual = forward ? bezier : new BezierDef(to, controlA, controlB, from);
                if (!forward)
                {
                    visual = new BezierDef(bezier.d, bezier.c, bezier.b, bezier.a);
                }

                edge.representation.SetPositions(visual);
            }
        }

        private static void SetControl(EdgeBezierControl control, Vector2 offset)
        {
            if (offset.sqrMagnitude < 0.01f)
            {
                offset = Vector2.right;
            }

            OffsetField?.SetValue(control, offset);
            PlayerDefinedField?.SetValue(control, true);
            SnappedField?.SetValue(control, false);
        }

        private static NetEdge ClosestHalf(NetNode middle, Vector3 point)
        {
            NetEdge best = null;
            float bestDistance = float.MaxValue;
            foreach (NetEdge edge in middle.GetEdges())
            {
                if (edge == null || !edge.IsValid())
                {
                    continue;
                }

                float distance = Vector3.Distance(Other(edge, middle).GetPosition(), point);
                if (distance < bestDistance)
                {
                    best = edge;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static NetEdge EdgeBetween(NetNode a, NetNode b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            foreach (NetEdge edge in a.GetEdges())
            {
                if (edge != null && edge.IsValid() && Other(edge, a) == b)
                {
                    return edge;
                }
            }

            return null;
        }

        private static NetNode Other(NetEdge edge, NetNode node)
        {
            return edge.startNode == node ? edge.endNode : edge.startNode;
        }

        private static bool CanClip(NetEdge edge)
        {
            return edge != null &&
                   edge.IsValid() &&
                   edge.IsBuilt() &&
                   edge.IsEditable() &&
                   edge.startNode != null &&
                   edge.endNode != null &&
                   (edge.networkType == NetworkType.Road || edge.networkType == NetworkType.Pipe);
        }

        private static bool Contains(Camera camera, Rect screenRect, Vector3 world)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            return screen.z > 0f && screenRect.Contains(new Vector2(screen.x, screen.y));
        }

        private static Vector3 Evaluate(BezierDef bezier, float t)
        {
            float u = 1f - t;
            return u * u * u * bezier.a +
                   3f * u * u * t * bezier.b +
                   3f * u * t * t * bezier.c +
                   t * t * t * bezier.d;
        }

        private static Vector2 Xz(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void DestroyEdge(NetEdge edge)
        {
            if (edge != null && edge.IsValid())
            {
                edge.Destroy();
            }
        }
    }
}
