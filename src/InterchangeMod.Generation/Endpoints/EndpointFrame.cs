using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public enum EndpointFrameFailure { None, InvalidSelection, ParallelAxes }

    public sealed class EndpointFrameResult
    {
        internal EndpointFrameResult(EndpointFrameFailure failure, Vec3? center, float residualRms, float[] alongByArm)
        {
            Failure = failure;
            Center = center;
            ResidualRms = residualRms;
            AlongByArm = Array.AsReadOnly(alongByArm);
        }

        public EndpointFrameFailure Failure { get; }
        /// <summary>Horizontal reference center; Y=0 is not a road or terrain height.</summary>
        public Vec3? Center { get; }
        public float ResidualRms { get; }
        /// <summary>Signed horizontal distance from center to each arm, ordered by arm index.</summary>
        public IReadOnlyList<float> AlongByArm { get; }
    }

    /// <summary>Fits a horizontal search reference to independently selected road arms.</summary>
    public static class EndpointFrame
    {
        private static bool Representable(double value) => !double.IsNaN(value) && !double.IsInfinity(value)
            && Math.Abs(value) <= float.MaxValue;
        public static EndpointFrameResult Estimate(IReadOnlyList<EndpointArm> arms)
            => Estimate(arms, InterchangeType.Cloverleaf);

        public static EndpointFrameResult Estimate(IReadOnlyList<EndpointArm> arms, InterchangeType type)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            EndpointFrameResult Fail(EndpointFrameFailure code) => new EndpointFrameResult(code, null, 0f, Array.Empty<float>());
            if (EndpointConstraints.Validate(arms, type).Count != 0)
                return Fail(EndpointFrameFailure.InvalidSelection);

            int count = arms.Count;
            var px = new double[count];
            var pz = new double[count];
            var dx = new double[count];
            var dz = new double[count];
            foreach (var arm in arms)
            {
                int i = arm.Index;
                foreach (var port in arm.Endpoints)
                {
                    px[i] += port.Position.X;
                    pz[i] += port.Position.Z;
                    double x = port.OutwardTangent.X, z = port.OutwardTangent.Z;
                    double length = Math.Sqrt(x * x + z * z);
                    dx[i] += x / length;
                    dz[i] += z / length;
                }
                px[i] /= arm.Endpoints.Count;
                pz[i] /= arm.Endpoints.Count;
                double d = Math.Sqrt(dx[i] * dx[i] + dz[i] * dz[i]);
                if (d < 1e-6) return Fail(EndpointFrameFailure.InvalidSelection);
                dx[i] /= d;
                dz[i] /= d;
            }

            double xx = 0, xz = 0, zz = 0, bx = 0, bz = 0;
            for (int i = 0; i < count; i++)
            {
                double nx = -dz[i], nz = dx[i], target = nx * px[i] + nz * pz[i];
                xx += nx * nx; xz += nx * nz; zz += nz * nz;
                bx += nx * target; bz += nz * target;
            }
            double det = xx * zz - xz * xz, trace = xx + zz;
            if (det <= 1e-8 * trace * trace) return Fail(EndpointFrameFailure.ParallelAxes);
            double cx = (bx * zz - bz * xz) / det, cz = (bz * xx - bx * xz) / det;
            if (!Representable(cx) || !Representable(cz))
                return Fail(EndpointFrameFailure.InvalidSelection);

            var along = new float[count];
            double squared = 0;
            for (int i = 0; i < count; i++)
            {
                double x = px[i] - cx, z = pz[i] - cz;
                double distance = x * dx[i] + z * dz[i];
                if (!Representable(distance)) return Fail(EndpointFrameFailure.InvalidSelection);
                along[i] = (float)distance;
                double perpendicular = -dz[i] * x + dx[i] * z;
                squared += perpendicular * perpendicular;
            }
            double rms = Math.Sqrt(squared / count);
            if (!Representable(rms)) return Fail(EndpointFrameFailure.InvalidSelection);
            return new EndpointFrameResult(EndpointFrameFailure.None, new Vec3((float)cx, 0f, (float)cz), (float)rms, along);
        }
    }
}
