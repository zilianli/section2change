using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public enum VerticalProfileFailure { None, InvalidInput, InvalidGeometry, GradeExceeded, Uncertain, InsufficientLength }

    public sealed class VerticalProfileResult
    {
        internal VerticalProfileResult(VerticalProfileFailure failure, IEnumerable<Bezier3> curves)
        {
            Failure = failure;
            Curves = Array.AsReadOnly(curves.ToArray());
        }
        public VerticalProfileFailure Failure { get; }
        public IReadOnlyList<Bezier3> Curves { get; }
    }

    /// <summary>Builds a smooth 3D profile; does not check terrain, obstacles or game validity.</summary>
    public static class VerticalProfile
    {
        private const int MaxSubdivisionDepth = 14;
        private const double SpeedEpsilon = 1e-9;
        private const double GradeTolerance = 1e-6;
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Finite(Vec3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        private static double Horizontal(Vec3 v) => Math.Sqrt((double)v.X * v.X + (double)v.Z * v.Z);

        /// <summary>Certifies an already generated world-space curve without rebuilding its height profile.</summary>
        public static VerticalProfileFailure CheckGrade(Bezier3 curve,float maxGrade)
        {
            if(!Finite(maxGrade)||maxGrade<=0||maxGrade>.30f)return VerticalProfileFailure.InvalidInput;
            if(!Finite(curve.P0)||!Finite(curve.P1)||!Finite(curve.P2)||!Finite(curve.P3))return VerticalProfileFailure.InvalidGeometry;
            return Certify(curve,maxGrade);
        }

        /// <summary>
        /// Keeps the specified start/end arc lengths level and raises or lowers
        /// only the middle. Lengths are sampled, not road-clearance certificates.
        /// </summary>
        public static VerticalProfileResult ApplyWithFlatEnds(IReadOnlyList<Bezier3> planar, float startHeight, float endHeight,
            float flatStart, float flatEnd, float maxGrade) =>
            ApplyWithBoundaryGrades(planar, startHeight, endHeight, flatStart, flatEnd, 0, 0, maxGrade);

        /// <summary>
        /// Keeps explicit end intervals level while preserving prescribed grades
        /// at unflattened ends. A level interval requires a zero boundary grade.
        /// </summary>
        public static VerticalProfileResult ApplyWithBoundaryGrades(IReadOnlyList<Bezier3> planar, float startHeight, float endHeight,
            float flatStart, float flatEnd, float startGrade, float endGrade, float maxGrade)
        {
            if (planar == null) throw new ArgumentNullException(nameof(planar));
            VerticalProfileResult Fail(VerticalProfileFailure code) => new VerticalProfileResult(code, Array.Empty<Bezier3>());
            if (!Finite(flatStart) || !Finite(flatEnd) || flatStart < 0 || flatEnd < 0 || !Finite(startHeight) || !Finite(endHeight)
                || !Finite(startGrade) || !Finite(endGrade)
                || (flatStart > 0 && startGrade != 0) || (flatEnd > 0 && endGrade != 0))
                return Fail(VerticalProfileFailure.InvalidInput);
            // Validate the whole path first, including joins between the groups.
            var flatCheck = Apply(planar, 0, 0, 0, 0, maxGrade);
            if (flatCheck.Failure != VerticalProfileFailure.None) return Fail(flatCheck.Failure);

            var lengths = planar.Select(c => (double)c.Length(64)).ToArray();
            double total = lengths.Sum(), start = flatStart, end = total - flatEnd;
            if (start > end + 1e-4) return Fail(VerticalProfileFailure.InsufficientLength);
            if (end - start <= 1e-4)
                return Math.Abs(endHeight - startHeight) > 1e-5f || startGrade != 0 || endGrade != 0
                    ? Fail(VerticalProfileFailure.InsufficientLength) : Apply(planar, startHeight, endHeight, 0, 0, maxGrade);

            var before = new List<Bezier3>(); var middle = new List<Bezier3>(); var after = new List<Bezier3>();
            double offset = 0;
            for (int i = 0; i < planar.Count; i++)
            {
                var curve = planar[i]; double length = lengths[i];
                var cuts = new List<float> { 0f, 1f };
                foreach (double boundary in new[] { start, end })
                {
                    double local = boundary - offset;
                    // Snap almost coincident cut points to the existing node.
                    if (local <= 1e-3 || local >= length - 1e-3) continue;
                    float lo = 0, hi = 1;
                    for (int n = 0; n < 25; n++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (curve.Slice(0f, mid).Length(64) < local) lo = mid; else hi = mid;
                    }
                    cuts.Add((lo + hi) * 0.5f);
                }
                cuts.Sort();
                for (int j = 0; j + 1 < cuts.Count; j++)
                {
                    if (cuts[j + 1] - cuts[j] < 1e-6f) continue;
                    var slice = curve.Slice(cuts[j], cuts[j + 1]);
                    float mid = (cuts[j] + cuts[j + 1]) * 0.5f;
                    double at = offset + curve.Slice(0, mid).Length(64);
                    if (at < start) before.Add(slice); else if (at < end) middle.Add(slice); else after.Add(slice);
                }
                offset += length;
            }
            if (middle.Count == 0) return Fail(VerticalProfileFailure.InsufficientLength);
            var output = new List<Bezier3>();
            VerticalProfileFailure Add(IReadOnlyList<Bezier3> group, float h0, float h1, float g0 = 0, float g1 = 0)
            {
                if (group.Count == 0) return VerticalProfileFailure.None;
                var part = Apply(group, h0, h1, g0, g1, maxGrade);
                if (part.Failure == VerticalProfileFailure.None) output.AddRange(part.Curves);
                return part.Failure;
            }
            var status = Add(before, startHeight, startHeight);
            if (status == VerticalProfileFailure.None)
                status = Add(middle, startHeight, endHeight, flatStart > 0 ? 0 : startGrade, flatEnd > 0 ? 0 : endGrade);
            if (status == VerticalProfileFailure.None) status = Add(after, endHeight, endHeight);
            return status == VerticalProfileFailure.None ? new VerticalProfileResult(status, output) : Fail(status);
        }

        public static VerticalProfileResult Apply(IReadOnlyList<Bezier3> planar, float startHeight, float endHeight,
            float startGrade, float endGrade, float maxGrade)
        {
            if (planar == null) throw new ArgumentNullException(nameof(planar));
            VerticalProfileResult Fail(VerticalProfileFailure code) => new VerticalProfileResult(code, Array.Empty<Bezier3>());
            if (planar.Count == 0 || !Finite(startHeight) || !Finite(endHeight) || !Finite(startGrade) || !Finite(endGrade)
                || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > 0.30f)
                return Fail(VerticalProfileFailure.InvalidInput);

            var lengths = new double[planar.Count];
            double total = 0;
            for (int i = 0; i < planar.Count; i++)
            {
                var c = planar[i];
                if (!Finite(c.P0) || !Finite(c.P1) || !Finite(c.P2) || !Finite(c.P3)
                    || c.P0.Y != 0 || c.P1.Y != 0 || c.P2.Y != 0 || c.P3.Y != 0)
                    return Fail(VerticalProfileFailure.InvalidGeometry);
                var startTangent = c.Derivative(0); var endTangent = c.Derivative(1);
                double startSpeed = Horizontal(startTangent), endSpeed = Horizontal(endTangent);
                if (startSpeed < SpeedEpsilon || endSpeed < SpeedEpsilon)
                    return Fail(VerticalProfileFailure.InvalidGeometry);
                if (i > 0)
                {
                    var previous = planar[i - 1];
                    var before = previous.Derivative(1); double beforeSpeed = Horizontal(before);
                    double dot = ((double)before.X * startTangent.X + (double)before.Z * startTangent.Z) / (beforeSpeed * startSpeed);
                    if (!previous.P3.Equals(c.P0) || dot < 0.9999)
                        return Fail(VerticalProfileFailure.InvalidGeometry);
                }
                double length = c.Length(64);
                if (double.IsNaN(length) || double.IsInfinity(length) || length < 1e-4)
                    return Fail(VerticalProfileFailure.InvalidGeometry);
                lengths[i] = length;
                total += length;
            }

            double Height(double u)
            {
                double u2 = u * u, u3 = u2 * u;
                return (2 * u3 - 3 * u2 + 1) * startHeight + (u3 - 2 * u2 + u) * startGrade * total
                    + (-2 * u3 + 3 * u2) * endHeight + (u3 - u2) * endGrade * total;
            }
            double Slope(double u)
            {
                double u2 = u * u;
                return ((6 * u2 - 6 * u) * startHeight + (3 * u2 - 4 * u + 1) * startGrade * total
                    + (-6 * u2 + 6 * u) * endHeight + (3 * u2 - 2 * u) * endGrade * total) / total;
            }

            var curves = new List<Bezier3>();
            double distance = 0;
            for (int i = 0; i < planar.Count; i++)
            {
                var c = planar[i];
                double u0 = distance / total; distance += lengths[i]; double u1 = distance / total;
                Vec3 a = c.P0, b = c.P1, d = c.P3, control2 = c.P2;
                a.Y = (float)Height(u0); d.Y = (float)Height(u1);
                b.Y = (float)(Height(u0) + Slope(u0) * Horizontal(c.Derivative(0)) / 3);
                control2.Y = (float)(Height(u1) - Slope(u1) * Horizontal(c.Derivative(1)) / 3);
                if (!Finite(a) || !Finite(b) || !Finite(control2) || !Finite(d)) return Fail(VerticalProfileFailure.InvalidInput);
                var curve = new Bezier3(a, b, control2, d);
                var grade = Certify(curve, maxGrade);
                if (grade != VerticalProfileFailure.None) return Fail(grade);
                curves.Add(curve);
            }
            return new VerticalProfileResult(VerticalProfileFailure.None, curves);
        }

        private readonly struct D3
        {
            public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public readonly double X, Y, Z;
            public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static D3 operator *(D3 a, double k) => new D3(a.X * k, a.Y * k, a.Z * k);
            public static D3 Between(Vec3 a, Vec3 b) => new D3(3.0 * ((double)b.X - a.X), 3.0 * ((double)b.Y - a.Y), 3.0 * ((double)b.Z - a.Z));
        }

        private static VerticalProfileFailure Certify(Bezier3 curve, float maxGrade) =>
            Certify(D3.Between(curve.P0, curve.P1), D3.Between(curve.P1, curve.P2), D3.Between(curve.P2, curve.P3), maxGrade, 0);

        private static VerticalProfileFailure Certify(D3 a, D3 b, D3 c, double limit, int depth)
        {
            var middle = (a + b * 2 + c) * 0.25;
            foreach (var sample in new[] { a, middle, c })
            {
                double horizontal = Math.Sqrt(sample.X * sample.X + sample.Z * sample.Z);
                if (horizontal < SpeedEpsilon) return VerticalProfileFailure.InvalidGeometry;
                if (Math.Abs(sample.Y) / horizontal > limit + GradeTolerance) return VerticalProfileFailure.GradeExceeded;
            }

            // The quadratic derivative lies within the convex hull of its three
            // controls. The hull's distance from zero bounds horizontal speed.
            double lower = DistanceToHull(a, b, c);
            double verticalUpper = Math.Max(Math.Abs(a.Y), Math.Max(Math.Abs(b.Y), Math.Abs(c.Y)));
            if (lower > SpeedEpsilon && verticalUpper / lower <= limit + GradeTolerance) return VerticalProfileFailure.None;
            if (depth >= MaxSubdivisionDepth) return VerticalProfileFailure.Uncertain;
            var ab = (a + b) * 0.5; var bc = (b + c) * 0.5;
            var first = Certify(a, ab, middle, limit, depth + 1);
            return first != VerticalProfileFailure.None ? first : Certify(middle, bc, c, limit, depth + 1);
        }

        private static double DistanceToHull(D3 a, D3 b, D3 c)
        {
            double Cross(D3 p, D3 q, D3 r) => (q.X - p.X) * (r.Z - p.Z) - (q.Z - p.Z) * (r.X - p.X);
            var zero = new D3(0, 0, 0);
            double area = Cross(a, b, c), ab = Cross(a, b, zero), bc = Cross(b, c, zero), ca = Cross(c, a, zero);
            if (Math.Abs(area) > 1e-20 && ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0)))
                return 0;
            double Segment(D3 p, D3 q)
            {
                double x = q.X - p.X, z = q.Z - p.Z, lengthSquared = x * x + z * z;
                double t = lengthSquared <= 1e-30 ? 0 : Math.Max(0, Math.Min(1, -(p.X * x + p.Z * z) / lengthSquared));
                double closestX = p.X + t * x, closestZ = p.Z + t * z;
                return Math.Sqrt(closestX * closestX + closestZ * closestZ);
            }
            return Math.Min(Segment(a, b), Math.Min(Segment(b, c), Segment(c, a)));
        }
    }
}
