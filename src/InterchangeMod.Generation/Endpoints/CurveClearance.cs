using System;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public enum CurveClearanceResult { Clear, Conflict, Uncertain, InvalidInput }

    /// <summary>Conservative centerline clearance test; shared-node joins require separate treatment.</summary>
    public static class CurveClearance
    {
        public static CurveClearanceResult Check(Bezier3 a, float halfWidthA, Bezier3 b,
            float halfWidthB, float verticalClearance, int maxNodes = 50000)
        {
            if (!Finite(a) || !Finite(b) || !Positive(halfWidthA) || !Positive(halfWidthB)
                || !Positive(verticalClearance) || maxNodes <= 0)
                return CurveClearanceResult.InvalidInput;
            var ca = new Curve(a); var cb = new Curve(b);
            double scale = Math.Max(ca.Scale, cb.Scale);
            double margin = 1e-7 + scale * 1e-12;
            int remaining = maxNodes;
            return Visit(ca, cb, (double)halfWidthA + halfWidthB, verticalClearance, margin, 0, ref remaining);
        }

        private static CurveClearanceResult Visit(Curve a, Curve b, double width, double height,
            double margin, int depth, ref int remaining)
        {
            if (remaining-- <= 0) return CurveClearanceResult.Uncertain;
            var aa = new Bounds(a); var bb = new Bounds(b);
            double dx = Gap(aa.MinX, aa.MaxX, bb.MinX, bb.MaxX);
            double dz = Gap(aa.MinZ, aa.MaxZ, bb.MinZ, bb.MaxZ);
            if (dx * dx + dz * dz > (width + margin) * (width + margin)
                || Gap(aa.MinY, aa.MaxY, bb.MinY, bb.MaxY) > height + margin)
                return CurveClearanceResult.Clear;

            if (width > margin && height > margin)
                foreach (var pa in a.Samples())
                    foreach (var pb in b.Samples())
                    {
                        double x = pa.X - pb.X, z = pa.Z - pb.Z;
                        if (x * x + z * z < (width - margin) * (width - margin)
                            && Math.Abs(pa.Y - pb.Y) < height - margin)
                            return CurveClearanceResult.Conflict;
                    }
            if (depth >= 32 || remaining <= 0) return CurveClearanceResult.Uncertain;
            bool splitA = aa.Span >= bb.Span;
            Curve left, right;
            if (splitA) a.Split(out left, out right); else b.Split(out left, out right);
            var first = splitA ? Visit(left, b, width, height, margin, depth + 1, ref remaining)
                               : Visit(a, left, width, height, margin, depth + 1, ref remaining);
            if (first == CurveClearanceResult.Conflict) return first;
            var second = splitA ? Visit(right, b, width, height, margin, depth + 1, ref remaining)
                                : Visit(a, right, width, height, margin, depth + 1, ref remaining);
            if (second == CurveClearanceResult.Conflict) return second;
            return first == CurveClearanceResult.Clear && second == CurveClearanceResult.Clear
                ? CurveClearanceResult.Clear : CurveClearanceResult.Uncertain;
        }

        private static double Gap(double amin, double amax, double bmin, double bmax) =>
            Math.Max(0, Math.Max(bmin - amax, amin - bmax));
        private static bool Positive(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0;
        private static bool Finite(Vec3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Bezier3 c) => Finite(c.P0) && Finite(c.P1) && Finite(c.P2) && Finite(c.P3);

        private readonly struct Point
        {
            public readonly double X, Y, Z;
            public Point(Vec3 v) { X = v.X; Y = v.Y; Z = v.Z; }
            private Point(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Point Mid(Point a, Point b) => new Point((a.X + b.X) * .5, (a.Y + b.Y) * .5, (a.Z + b.Z) * .5);
            public double Scale => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
        }
        private readonly struct Curve
        {
            public readonly Point A, B, C, D;
            public Curve(Bezier3 c) { A = new Point(c.P0); B = new Point(c.P1); C = new Point(c.P2); D = new Point(c.P3); }
            private Curve(Point a, Point b, Point c, Point d) { A = a; B = b; C = c; D = d; }
            public double Scale => Math.Max(Math.Max(A.Scale, B.Scale), Math.Max(C.Scale, D.Scale));
            public void Split(out Curve left, out Curve right)
            {
                var ab = Point.Mid(A, B); var bc = Point.Mid(B, C); var cd = Point.Mid(C, D);
                var abc = Point.Mid(ab, bc); var bcd = Point.Mid(bc, cd); var mid = Point.Mid(abc, bcd);
                left = new Curve(A, ab, abc, mid); right = new Curve(mid, bcd, cd, D);
            }
            public Point[] Samples() { Split(out var left, out _); return new[] { A, left.D, D }; }
        }
        private readonly struct Bounds
        {
            public readonly double MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
            public Bounds(Curve c)
            {
                MinX = Math.Min(Math.Min(c.A.X, c.B.X), Math.Min(c.C.X, c.D.X));
                MaxX = Math.Max(Math.Max(c.A.X, c.B.X), Math.Max(c.C.X, c.D.X));
                MinY = Math.Min(Math.Min(c.A.Y, c.B.Y), Math.Min(c.C.Y, c.D.Y));
                MaxY = Math.Max(Math.Max(c.A.Y, c.B.Y), Math.Max(c.C.Y, c.D.Y));
                MinZ = Math.Min(Math.Min(c.A.Z, c.B.Z), Math.Min(c.C.Z, c.D.Z));
                MaxZ = Math.Max(Math.Max(c.A.Z, c.B.Z), Math.Max(c.C.Z, c.D.Z));
            }
            public double Span => Math.Max(MaxX - MinX, MaxZ - MinZ);
        }
    }
}
