using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointLevels
    {
        private EndpointLevels(float main,float cross,float lift) { Main=main;Cross=cross;TerrainLift=lift; }
        public float Main { get; }
        public float Cross { get; }
        public float TerrainLift { get; }
        public static EndpointLevels Resolve(IReadOnlyList<EndpointArm> arms,bool crossOnTop,float offset,float gap,float terrainHeight)
        {
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            // Three-arm callers use the Tee convention: main 0/2, branch 1.
            var type=arms?.Count==3?InterchangeType.Trumpet:InterchangeType.Cloverleaf;
            if(arms==null||EndpointConstraints.Validate(arms,type).Count!=0||!Finite(offset)||!Finite(gap)||gap<0||!Finite(terrainHeight))
                throw new ArgumentException("Invalid endpoint level input.");
            float mean=arms.SelectMany(a=>a.Endpoints).Average(p=>p.Position.Y)+offset;
            float separation=gap>0?gap:10;
            float main=crossOnTop?mean:mean+separation,cross=crossOnTop?mean+separation:mean;
            float oldMain=arms.Where(a=>a.Index%2==0).SelectMany(a=>a.Endpoints).Average(p=>p.Position.Y);
            float oldCross=arms.Where(a=>a.Index%2==1).SelectMany(a=>a.Endpoints).Average(p=>p.Position.Y);
            if(gap==0&&Math.Abs(oldMain-oldCross)>=HeightRange.MeasuredMinimumGap)
            {main=oldMain+offset;cross=oldCross+offset;}
            float above=Math.Max(main,cross)-terrainHeight;
            float lift=gap==0&&offset==0&&above>HeightRange.RaisedThreshold&&above<HeightRange.BridgeThreshold
                ?HeightRange.BridgeThreshold-above:0;
            return new EndpointLevels(main+lift,cross+lift,lift);
        }
    }
}
