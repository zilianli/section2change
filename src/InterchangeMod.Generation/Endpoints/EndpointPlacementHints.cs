using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Advisory messages only; these checks do not change collision assistance or geometry validation.</summary>
    public static class EndpointPlacementHints
    {
        public const string CompactGridNoCollisionHint="斜交道路的中央空间较小，桥墩可能与道路冲突；出现叠放提示时，请手动开启无碰撞放置。";

        /// <summary>Uses the two opposite midpoint secants in XZ, independent of world rotation and endpoint tangents.</summary>
        public static bool ShouldSuggestNoCollisionForCompactGrid(IReadOnlyList<EndpointArm>? arms)
        {
            if(arms==null||arms.Count!=4||EndpointConstraints.Validate(arms,InterchangeType.CompactGrid).Count!=0)return false;
            var ordered=arms.OrderBy(a=>a.Index).ToArray();
            double X(int i)=>ordered[i].Endpoints.Average(p=>(double)p.Position.X);
            double Z(int i)=>ordered[i].Endpoints.Average(p=>(double)p.Position.Z);
            double ax=X(0)-X(2),az=Z(0)-Z(2),bx=X(1)-X(3),bz=Z(1)-Z(3);
            double aLength=Math.Sqrt(ax*ax+az*az),bLength=Math.Sqrt(bx*bx+bz*bz);
            if(aLength<1e-5||bLength<1e-5)return false;
            double dot=Math.Abs((ax*bx+az*bz)/(aLength*bLength));
            return !double.IsNaN(dot)&&!double.IsInfinity(dot)&&dot>Math.Sin(Math.PI/180);
        }
    }
}
