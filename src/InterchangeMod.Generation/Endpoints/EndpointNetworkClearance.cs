using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>
    /// Checks independent endpoint-generated road decks in three dimensions. Each nonempty
    /// PathId must identify one ordered, independently proven non-self-intersecting path.
    /// This does not certify terrain, retained roads or game-generated bridge supports.
    /// </summary>
    public static class EndpointNetworkClearance
    {
        public static CurveClearanceResult Check(SpatialCandidateResult candidate, float verticalClearance, out string detail)
        {
            detail = "";
            if (candidate == null || candidate.Failure != SpatialCandidateFailure.None
                || candidate.Segments == null || candidate.Segments.Count == 0 || !Positive(verticalClearance))
            {
                detail = "Invalid candidate or vertical clearance";
                return CurveClearanceResult.InvalidInput;
            }

            var segments = candidate.Segments;
            var paths = new Dictionary<string, PathEnds>(StringComparer.Ordinal);
            // Validate the entire candidate before an early conflict or a path/merge exemption
            // can conceal malformed geometry or incomplete width metadata.
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment == null || string.IsNullOrWhiteSpace(segment.PathId)
                    || !segment.RoadWidth.HasValue || !Positive(segment.RoadWidth.Value)
                    || !Finite(segment.WorldCurve) || !Finite(segment.StartNode) || !Finite(segment.EndNode))
                {
                    detail = $"Invalid segment {i + 1}: finite geometry, positive width and a path identity are required";
                    return CurveClearanceResult.InvalidInput;
                }
                if (paths.TryGetValue(segment.PathId, out var ends)) ends.Last = i;
                else paths.Add(segment.PathId, new PathEnds(i));
            }

            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i]; var b = segments[j];
                    // Analytic core paths and axis-monotone approaches are proved by their
                    // producer. Source arm and edge kind alone never identify a whole path.
                    if (string.Equals(a.PathId, b.PathId, StringComparison.Ordinal)) continue;
                    if (LegalMerge(i, j)) continue;

                    var result = CurveClearance.Check(a.WorldCurve, HalfWidth(a), b.WorldCurve,
                        HalfWidth(b), verticalClearance);
                    if (result != CurveClearanceResult.Clear)
                    {
                        detail = $"{a.PathId}/{a.Kind} {a.WorldCurve} vs {b.PathId}/{b.Kind} {b.WorldCurve}: {result}";
                        return result;
                    }
                }
            return CurveClearanceResult.Clear;

            bool Terminal(int index, bool start)
            {
                var ends = paths[segments[index].PathId];
                return index == (start ? ends.First : ends.Last);
            }

            bool Incident(SpatialSegment main, Vec3 node) =>
                main.StartNode.Equals(node) || main.EndNode.Equals(node);

            bool MainMerge(int mainIndex, int rampIndex)
            {
                var main = segments[mainIndex]; var ramp = segments[rampIndex];
                return (Terminal(rampIndex, true) && Incident(main, ramp.StartNode))
                    || (Terminal(rampIndex, false) && Incident(main, ramp.EndNode));
            }

            bool LegalMerge(int first, int second)
            {
                var a = segments[first]; var b = segments[second];
                if (a.Kind == CloverEdgeKind.Main && b.Kind == CloverEdgeKind.Main) return false;
                if (a.Kind == CloverEdgeKind.Main) return MainMerge(first, second);
                if (b.Kind == CloverEdgeKind.Main) return MainMerge(second, first);

                // Only the actual terminal pieces at one exact 3D logical node may join.
                // The rest of both connected paths still undergoes the full deck check.
                return (Terminal(first, true) && Terminal(second, true) && a.StartNode.Equals(b.StartNode))
                    || (Terminal(first, true) && Terminal(second, false) && a.StartNode.Equals(b.EndNode))
                    || (Terminal(first, false) && Terminal(second, true) && a.EndNode.Equals(b.StartNode))
                    || (Terminal(first, false) && Terminal(second, false) && a.EndNode.Equals(b.EndNode));
            }
        }

        private static float HalfWidth(SpatialSegment segment) => Math.Max(float.Epsilon, segment.RoadWidth!.Value / 2);
        private static bool Positive(float value) => Finite(value) && value > 0;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vec3 point) => Finite(point.X) && Finite(point.Y) && Finite(point.Z);
        private static bool Finite(Bezier3 curve) => Finite(curve.P0) && Finite(curve.P1) && Finite(curve.P2) && Finite(curve.P3);

        private sealed class PathEnds
        {
            public PathEnds(int first) { First = first; Last = first; }
            public int First { get; }
            public int Last { get; set; }
        }
    }
}
