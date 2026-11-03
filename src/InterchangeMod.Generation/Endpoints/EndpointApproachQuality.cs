using System.Collections.Generic;
using System;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointApproachQuality
    {
        internal EndpointApproachQuality(double peak,double jump,double energy,double length,IReadOnlyList<SpatialSegment> curves)
        {PeakCurvature=peak;CurvatureJump=jump;BendingEnergy=energy;Length=length;Curves=curves;}
        public double PeakCurvature {get;}
        public double CurvatureJump {get;}
        public double BendingEnergy {get;}
        public double Length {get;}
        public IReadOnlyList<SpatialSegment> Curves {get;}
        public double Score=>10000*PeakCurvature+1000*CurvatureJump+100*BendingEnergy+.0001*Length;
        internal static EndpointApproachQuality Measure(EndpointStackCoreResult core,SpatialCandidateResult candidate,
            IReadOnlyList<RoadRetreatResult> cuts,IReadOnlyList<RoadApproachTrace> traces)
        {
            var internalSegments=new HashSet<SpatialSegment>(core.Segments);
            var external=candidate.Segments.Where(s=>!internalSegments.Contains(s)).ToArray();
            double peak=0,energy=0,length=0,jump=0;
            foreach(var s in external)
            {
                var c=s.WorldCurve;var previous=c.P0;double last=CurveCurvature.Signed(c,0);
                for(int i=0;i<=64;i++)
                {
                    float t=i/64f;double k=CurveCurvature.Signed(c,t);peak=Math.Max(peak,Math.Abs(k));
                    var point=c.Evaluate(t);double ds=Vec3.Distance(previous,point);
                    energy+=(last*last+k*k)*.5*ds;length+=ds;previous=point;last=k;
                }
            }
            foreach(var group in candidate.Segments.Where(s=>s.Kind==CloverEdgeKind.Main).GroupBy(s=>s.PathId))
            {
                var path=group.ToArray();
                for(int i=1;i<path.Length;i++)
                    if(!internalSegments.Contains(path[i-1])||!internalSegments.Contains(path[i]))
                        jump+=Math.Abs(CurveCurvature.Signed(path[i-1].WorldCurve,1)-CurveCurvature.Signed(path[i].WorldCurve,0));
            }
            foreach(var cut in cuts)
            {
                var port=cut.Port!;var trace=traces.Single(t=>t.Origin.NodeIndex==port.NodeIndex&&t.Origin.NodeVersion==port.NodeVersion);
                Bezier3? original=cut.Remainder;
                if(!original.HasValue&&cut.Consumed.Count<trace.Pieces.Count)original=trace.Pieces[cut.Consumed.Count].Curve;
                double expected=original.HasValue?CurveCurvature.Signed(original.Value,0):0;
                foreach(var s in external)
                {
                    if(!port.TravelOutward&&s.WorldCurve.P0==port.Position)jump+=Math.Abs(CurveCurvature.Signed(s.WorldCurve,0)+expected);
                    if(port.TravelOutward&&s.WorldCurve.P3==port.Position)jump+=Math.Abs(CurveCurvature.Signed(s.WorldCurve,1)-expected);
                }
            }
            return new EndpointApproachQuality(peak,jump,energy,length,Array.AsReadOnly(external));
        }
    }
}
