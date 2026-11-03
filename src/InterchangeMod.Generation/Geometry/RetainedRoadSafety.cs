using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Geometry
{
    public enum ConnectionNeed { None, Required, Manual }

    public readonly struct MatchedConnectionRoad
    {
        public MatchedConnectionRoad(Bezier3 curve, float halfWidth, int? flow)
        { Curve = curve; HalfWidth = halfWidth; Flow = flow; }
        public Bezier3 Curve { get; }
        public float HalfWidth { get; }
        public int? Flow { get; }
    }

    /// <summary>Safety policy for original roads kept beyond a generated leg.</summary>
    public sealed class RetainedRoadSafety
    {
        private readonly List<(Bezier3 Curve, Footprint Corridor, float HalfWidth)> m_Roads = new List<(Bezier3, Footprint, float)>();
        private readonly float m_Margin;

        public RetainedRoadSafety(IEnumerable<(Bezier3 Curve, float HalfWidth)> roads, float margin)
        {
            m_Margin = margin;
            foreach (var road in roads)
                m_Roads.Add((road.Curve, Footprint.Corridor(new[] { road }, margin), road.HalfWidth));
        }

        /// <summary>
        /// Check every new road separately: only the owner's unbranched straight tail
        /// can overlap within a small outward join capsule. Exempting the union would
        /// also hide another ramp or its pillar.
        /// All curve heights use the same fitted reference plane as the new geometry.
        /// </summary>
        public bool Collides(Bezier3 retained, float halfWidth, int owner, Vec3 end, Vec3 outward)
        {
            var tail = ContinuousTail(owner, end, outward);
            for (int i = 0; i < m_Roads.Count; i++)
            {
                var road = m_Roads[i];
                var outside = road.Corridor.OutsideCollisionIntervals(retained, halfWidth);
                float previous = 0f;
                foreach (var interval in outside)
                {
                    if (interval.Start > previous && !Allowed(previous, interval.Start)) return true;
                    previous = interval.End;
                }
                if (previous < 1f && !Allowed(previous, 1f)) return true;

                bool Allowed(float start, float stop)
                {
                    if (!tail.Contains(i)) return false;
                    float magnitude = MathF.Sqrt(outward.X * outward.X + outward.Z * outward.Z);
                    if (magnitude < 1e-5f) return false;
                    Vec3 dir = outward * (1f / magnitude);
                    float reach = halfWidth + road.HalfWidth + m_Margin + 6f;
                    Bezier3 overlap = retained.Slice(start, stop);
                    foreach (Vec3 control in new[] { overlap.P0, overlap.P1, overlap.P2, overlap.P3 })
                    {
                        Vec3 delta = control - end;
                        float along = delta.X * dir.X + delta.Z * dir.Z;
                        float radius = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                        // Width-dependent endpoint caps also overlap at legal angled
                        // joins. Bound the exemption radially instead of imposing a
                        // fixed six-metre lateral limit on a twenty-degree join.
                        if (along < -0.05f || radius > reach || MathF.Abs(delta.Y - dir.Y * along) > 3f)
                            return false;
                    }
                    // Collision is geometric: an incoming retained one-way course is
                    // parameterized towards the join, while an outgoing course is
                    // parameterized away from it. Either uniform orientation is valid.
                    // Do not independently flip each derivative: that would admit a
                    // curve that turns back through the new road.
                    bool forward = true, backward = true;
                    foreach (Vec3 tangent in new[] { overlap.P1 - overlap.P0, overlap.P2 - overlap.P1, overlap.P3 - overlap.P2 })
                    {
                        forward &= ConnectionPolicy.CanConnect(true, outward, 0, tangent, 0);
                        backward &= ConnectionPolicy.CanConnect(true, outward, 0, -tangent, 0);
                    }
                    return forward || backward;
                }
            }
            return false;
        }

        // Only an unbranched chain of exact, equal-width, collinear segments at the
        // same longitudinal grade belongs to the owner's tail. A shared prefab or a
        // shared node alone says nothing about whether another road is safe to ignore.
        private HashSet<int> ContinuousTail(int owner, Vec3 end, Vec3 outward)
        {
            var result = new HashSet<int>();
            if (owner < 0 || owner >= m_Roads.Count) return result;
            result.Add(owner);
            Bezier3 ownerCurve = m_Roads[owner].Curve;
            if (!StraightOnAxis(ownerCurve, end, outward)) return result;
            Vec3 node;
            if (ownerCurve.P0 == end) node = ownerCurve.P3;
            else if (ownerCurve.P3 == end) node = ownerCurve.P0;
            else return result;
            int current = owner;
            while (true)
            {
                int next = -1;
                for (int i = 0; i < m_Roads.Count; i++)
                {
                    if (i == current) continue;
                    Bezier3 c = m_Roads[i].Curve;
                    if (c.P0 != node && c.P3 != node) continue;
                    if (next >= 0) return result;
                    next = i;
                }
                if (next < 0 || result.Contains(next) || m_Roads[next].HalfWidth != m_Roads[owner].HalfWidth) return result;
                Bezier3 curve = m_Roads[next].Curve;
                if (!StraightOnAxis(curve, end, outward)) return result;
                Vec3 far = curve.P0 == node ? curve.P3 : curve.P0;
                Vec3 step = far - node;
                if (step.X * outward.X + step.Z * outward.Z >= 0f) return result;
                result.Add(next);
                current = next;
                node = far;
            }
        }

        private static bool StraightOnAxis(Bezier3 curve, Vec3 origin, Vec3 axis)
        {
            float length2 = axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z;
            if (length2 < 1e-10f) return false;
            foreach (Vec3 p in new[] { curve.P0, curve.P1, curve.P2, curve.P3 })
            {
                Vec3 d = p - origin;
                float projection = (d.X * axis.X + d.Y * axis.Y + d.Z * axis.Z) / length2;
                if ((d - axis * projection).Length() > 0.001f) return false;
            }
            return true;
        }

        public static ConnectionNeed ConnectionAt(Vec3 end, Vec3 outward, int flow, float halfWidth,
            IReadOnlyList<MatchedConnectionRoad> matched)
        {
            ConnectionNeed need = ConnectionNeed.None;
            foreach (var road in matched)
            {
                float t = NearestT(road.Curve, end);
                Vec3 onRoad = road.Curve.Evaluate(t);
                float dx = onRoad.X - end.X, dz = onRoad.Z - end.Z;
                float lateral = MathF.Sqrt(dx * dx + dz * dz);
                if (lateral > 6f + halfWidth + road.HalfWidth) continue;
                foreach (bool after in new[] { false, true })
                {
                    Vec3 tangent = road.Curve.Derivative(t) * (after ? 1f : -1f);
                    if (!ConnectionPolicy.CanConnect(true, outward, 0, tangent, 0)) continue;
                    // The closest point may be the road's dead end. Only a real
                    // outward continuation creates a reconnection obligation.
                    float probe = Math.Min(1f, 10f / Math.Max(1f, road.Curve.Length()));
                    Vec3 continuation = road.Curve.Evaluate(after ? Math.Min(1f, t + probe) : Math.Max(0f, t - probe)) - onRoad;
                    float horizontal = MathF.Sqrt(outward.X * outward.X + outward.Z * outward.Z);
                    if (horizontal < 1e-5f || (continuation.X * outward.X + continuation.Z * outward.Z) / horizontal < 2f)
                        continue;
                    if (lateral <= 6f && road.Flow.HasValue && flow == road.Flow.Value * (after ? 1 : -1))
                        return ConnectionNeed.Required;
                    need = ConnectionNeed.Manual;
                }
            }
            return need;
        }

        private static float NearestT(Bezier3 curve, Vec3 end)
        {
            float Distance(float t)
            { Vec3 p = curve.Evaluate(t) - end; return p.X * p.X + p.Z * p.Z; }
            float best = 0f, distance = float.MaxValue;
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f, d = Distance(t);
                if (d < distance) { best = t; distance = d; }
            }
            float lo = Math.Max(0f, best - 1f / 64f), hi = Math.Min(1f, best + 1f / 64f);
            for (int i = 0; i < 24; i++)
            {
                float a = lo + (hi - lo) / 3f, b = hi - (hi - lo) / 3f;
                if (Distance(a) < Distance(b)) hi = b; else lo = a;
            }
            return (lo + hi) * 0.5f;
        }
    }
}
