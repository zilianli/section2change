using System;
using System.Runtime.CompilerServices;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;

[assembly: InternalsVisibleTo("InterchangeMod.Generation.Tests")]

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Planar split geometry for one endpoint-solved loop and its opposing shared outer movement.</summary>
    internal sealed class EndpointHugGeometry
    {
        private const float MinimumSharedAngle = 10f * Directions.DegToRad;

        internal RampPath LoopIn { get; private set; }
        internal RampPath LoopOut { get; private set; }
        internal RampPath Shared { get; private set; }
        internal RampPath ConnA { get; private set; }
        internal RampPath ConnB { get; private set; }
        internal Vec3 SplitA { get; private set; }
        internal Vec3 SplitB { get; private set; }
        internal float OnA { get; private set; }
        internal float OnB { get; private set; }
        internal float SplitAlongLoopA { get; private set; }
        internal float SplitAlongLoopB { get; private set; }
        internal float LoopLength { get; private set; }
        internal float AngleSign { get; private set; }

        internal static bool TryCreate(RampArc loop, Vec3 originA, Vec3 dirA, Vec3 originB, Vec3 dirB,
            float connectorRadius, float sharedWidth, out EndpointHugGeometry? result)
        {
            result = null;
            if (!Finite(loop.From) || !Finite(loop.Heading) || !Finite(loop.Radius) || !Finite(loop.Angle)
                || !Finite(originA) || !Finite(originB) || !Finite(dirA) || !Finite(dirB)
                || !Finite(connectorRadius) || !Finite(sharedWidth)
                || loop.Radius <= 0f || connectorRadius <= 0f || sharedWidth <= 0f
                || !TryDirection(loop.Heading, out Vec3 heading)
                || !TryDirection(dirA, out dirA) || !TryDirection(dirB, out dirB))
                return false;

            float total = Math.Abs(loop.Angle), sign = Math.Sign(loop.Angle);
            if (total <= 0f || total >= 2f * MathF.PI - 1e-4f)
                return false;
            // The input arc and both road origins are world coordinates. Only height
            // is removed here; no estimated interchange centre is substituted.
            loop = new RampArc(Planar.Flat0(loop.From), heading, loop.Radius, loop.Angle);
            Vec3 c = loop.Center;
            float r = loop.Radius, rc = r + sharedWidth / 4f, ro = r + sharedWidth / 2f;
            float tangentDistance = ro + connectorRadius;
            if (!Finite(c) || !Finite(rc) || !Finite(ro) || !Finite(tangentDistance) || !Finite(loop.Length))
                return false;
            float tolerance = Math.Max(.05f, tangentDistance * 1e-5f);

            // The connector centre lies on the line offset to the loop's turning
            // side, and on the exterior tangent circle around the loop centre.
            bool Touch(Vec3 origin, Vec3 direction, bool before, out float along,
                out Vec3 foot, out Vec3 center, out Vec3 touch, out Vec3 radial)
            {
                along = 0f;
                foot = center = touch = radial = Vec3.Zero;
                Vec3 offset = Planar.Perp(direction) * (sign * connectorRadius);
                double dx = (double)origin.X + offset.X - c.X, dz = (double)origin.Z + offset.Z - c.Z;
                double norm = Math.Sqrt((double)direction.X * direction.X + (double)direction.Z * direction.Z);
                double parallel = (dx * direction.X + dz * direction.Z) / norm;
                double perpendicular = (-dx * direction.Z + dz * direction.X) / norm;
                // Using perpendicular distance avoids subtracting two huge squares
                // when a selected road's actual origin is far along its axis.
                double discriminant = (double)tangentDistance * tangentDistance - perpendicular * perpendicular;
                if (double.IsNaN(discriminant) || double.IsInfinity(discriminant) || discriminant < 0d)
                    return false;
                double at = (-parallel + (before ? -1d : 1d) * Math.Sqrt(discriminant)) / norm;
                if (double.IsNaN(at) || double.IsInfinity(at) || Math.Abs(at) > float.MaxValue)
                    return false;
                along = (float)at;
                foot = Planar.Flat0(origin) + direction * along;
                center = foot + offset;
                Vec3 delta = center - c;
                double distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Z * delta.Z);
                // Reject origins whose float along-distance loses the tangent root.
                if (!Finite(foot) || !Finite(center) || Math.Abs(distance - tangentDistance) > tolerance
                    || !TryDirection(delta, out radial))
                    return false;
                touch = c + radial * ro;
                return Finite(touch);
            }

            if (!Touch(originA, dirA, true, out float onA, out Vec3 footA, out Vec3 centerA, out Vec3 touchA, out Vec3 uA)
                || !Touch(originB, dirB, false, out float onB, out Vec3 footB, out Vec3 centerB, out Vec3 touchB, out Vec3 uB))
                return false;

            float angleA = Planar.RotAngle(footA - centerA, touchA - centerA, sign);
            float angleB = Planar.RotAngle(touchB - centerB, footB - centerB, sign);
            if (!Finite(angleA) || !Finite(angleB) || angleA <= 0f || angleB <= 0f
                || angleA > MathF.PI || angleB > MathF.PI)
                return false;
            var connA = new RampArc(footA, dirA, connectorRadius, sign * angleA);
            var connB = new RampArc(touchB, Planar.Perp(uB) * -sign, connectorRadius, sign * angleB);

            if (!TryDirection(loop.From - c, out Vec3 u0))
                return false;
            float alphaB = Planar.RotAngle(u0, uB, sign), alphaA = Planar.RotAngle(u0, uA, sign);
            float sB = r * alphaB, sA = r * alphaA;
            if (!Finite(alphaA) || !Finite(alphaB) || !Finite(sA) || !Finite(sB)
                || !(alphaB < alphaA && alphaA < total)
                || alphaA - alphaB < MinimumSharedAngle
                || sB < RampPath.MinSegment || loop.Length - sA < RampPath.MinSegment)
                return false;

            var loopIn = new RampArc(loop.From, loop.Heading, r, sign * alphaB);
            var loopOut = new RampArc(loop.PointAt(sA), loop.HeadingAt(sA), r, sign * (total - alphaA));
            Vec3 splitA = c + uA * rc, splitB = c + uB * rc;
            // Shared is emitted in the outer movement's direction, A -> B. The
            // loop movement traverses its inner half in the opposite direction.
            var shared = new RampArc(splitA, Planar.Perp(uA) * -sign, rc, -sign * (alphaA - alphaB));
            if (!Usable(loopIn) || !Usable(loopOut) || !Usable(shared) || !Usable(connA) || !Usable(connB)
                || !Finite(splitA) || !Finite(splitB)
                || Vec3.Distance(connA.End, touchA) > tolerance || Vec3.Distance(connB.End, footB) > tolerance
                || Vec3.Distance(shared.End, splitB) > tolerance)
                return false;

            result = new EndpointHugGeometry
            {
                LoopIn = RampPath.FromArcs(loopIn),
                LoopOut = RampPath.FromArcs(loopOut),
                Shared = RampPath.FromArcs(shared),
                ConnA = RampPath.FromArcs(connA),
                ConnB = RampPath.FromArcs(connB),
                SplitA = splitA,
                SplitB = splitB,
                OnA = onA,
                OnB = onB,
                SplitAlongLoopA = sA,
                SplitAlongLoopB = sB,
                LoopLength = loop.Length,
                AngleSign = sign,
            };
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vec3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

        private static bool TryDirection(Vec3 value, out Vec3 direction)
        {
            double length = Math.Sqrt((double)value.X * value.X + (double)value.Z * value.Z);
            direction = Vec3.Zero;
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 1e-6d)
                return false;
            direction = new Vec3((float)(value.X / length), 0f, (float)(value.Z / length));
            return Finite(direction);
        }

        private static bool Usable(RampArc arc) => Finite(arc.From) && Finite(arc.Heading) && Finite(arc.Center)
            && Finite(arc.End) && Finite(arc.EndHeading) && Finite(arc.Length) && arc.Length >= RampPath.MinSegment
            && Vec3.Distance(arc.From, arc.End) > 1e-3f;
    }
}
