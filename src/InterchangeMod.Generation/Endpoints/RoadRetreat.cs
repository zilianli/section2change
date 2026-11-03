using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Curve follows the road outwards, independently of its stored/traffic direction.</summary>
    public sealed class RoadTracePiece
    {
        public RoadTracePiece(int edgeIndex,int edgeVersion,Bezier3 outwardCurve,bool reversed,int outerNodeIndex,int outerNodeVersion)
        { EdgeIndex=edgeIndex;EdgeVersion=edgeVersion;Curve=outwardCurve;Reversed=reversed;OuterNodeIndex=outerNodeIndex;OuterNodeVersion=outerNodeVersion; }
        public int EdgeIndex { get; } public int EdgeVersion { get; }
        public Bezier3 Curve { get; } public bool Reversed { get; }
        public int OuterNodeIndex { get; } public int OuterNodeVersion { get; }
    }
    public sealed class RoadApproachTrace
    {
        public RoadApproachTrace(SelectedEndpoint origin,IEnumerable<RoadTracePiece> pieces)
        { Origin=origin;Pieces=Array.AsReadOnly(pieces.ToArray()); }
        public SelectedEndpoint Origin { get; }
        public IReadOnlyList<RoadTracePiece> Pieces { get; }
        public float Length => Pieces.Any(p=>p==null)?float.NaN:(float)Pieces.Sum(p=>RoadRetreat.HorizontalLength(p.Curve));
    }
    public enum RoadRetreatFailure { None,InvalidInput,InvalidTrace,OutsideTrace,ShortRemainder }
    public sealed class RoadRetreatResult
    {
        internal RoadRetreatResult(RoadRetreatFailure failure,SelectedEndpoint? port,float distance,
            IEnumerable<RoadTracePiece> consumed,IEnumerable<Bezier3> modified,Bezier3? remainder,int bindingIndex,int bindingVersion)
        { Failure=failure;Port=port;Distance=distance;Consumed=Array.AsReadOnly(consumed.ToArray());ModifiedCurves=Array.AsReadOnly(modified.ToArray());
          Remainder=remainder;BindingNodeIndex=bindingIndex;BindingNodeVersion=bindingVersion; }
        public RoadRetreatFailure Failure { get; }
        /// <summary>Synthetic solver endpoint retaining the source identity; use BindingNode for game courses.</summary>
        public SelectedEndpoint? Port { get; }
        public float Distance { get; }
        public IReadOnlyList<RoadTracePiece> Consumed { get; }
        public IReadOnlyList<Bezier3> ModifiedCurves { get; }
        public Bezier3? Remainder { get; }
        public int BindingNodeIndex { get; } public int BindingNodeVersion { get; }
    }
    public static class RoadRetreat
    {
        private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        private static bool Finite(Vec3 v)=>Finite(v.X)&&Finite(v.Y)&&Finite(v.Z);
        private static bool ContinuousJoin(Vec3 a,Vec3 b)
        {
            // Native incident edge curves can have a nominal 5cm terminal gap.
            // Their independently rounded float coordinates need a scale-aware
            // representation allowance, not snapped/rewritten source geometry.
            const double floatEpsilon=1.1920928955078125e-7;
            double scale=Math.Max(Math.Max(Math.Abs((double)a.X),Math.Abs((double)b.X)),
                Math.Max(Math.Max(Math.Abs((double)a.Y),Math.Abs((double)b.Y)),
                    Math.Max(Math.Abs((double)a.Z),Math.Abs((double)b.Z))));
            double tolerance=.05+2*Math.Sqrt(3)*floatEpsilon*scale;
            double dx=(double)a.X-b.X,dy=(double)a.Y-b.Y,dz=(double)a.Z-b.Z;
            return dx*dx+dy*dy+dz*dz<=tolerance*tolerance;
        }
        public static double HorizontalLength(Bezier3 curve)
        {
            double length=0;var last=curve.P0;
            for(int i=1;i<=64;i++) { var p=curve.Evaluate(i/64f);double x=(double)p.X-last.X,z=(double)p.Z-last.Z;length+=Math.Sqrt(x*x+z*z);last=p; }
            return length;
        }
        public static RoadRetreatResult Cut(RoadApproachTrace trace,float distance)
        {
            RoadRetreatResult Fail(RoadRetreatFailure f)=>new RoadRetreatResult(f,null,0,Array.Empty<RoadTracePiece>(),Array.Empty<Bezier3>(),null,0,0);
            if(trace==null||trace.Origin==null||!Finite(distance)||distance<0) return Fail(RoadRetreatFailure.InvalidInput);
            var origin=trace.Origin;
            if(!Finite(origin.Position)||!Finite(origin.OutwardTangent)||!Finite(origin.Width)||origin.Width<=0||origin.Lanes<=0
                ||string.IsNullOrWhiteSpace(origin.Prefab)||origin.NodeIndex<=0||origin.NodeVersion<=0
                ||(double)origin.OutwardTangent.X*origin.OutwardTangent.X+(double)origin.OutwardTangent.Z*origin.OutwardTangent.Z<1e-12)
                return Fail(RoadRetreatFailure.InvalidInput);
            var last=origin.Position;var seen=new HashSet<(int,int)>();
            foreach(var piece in trace.Pieces)
            {
                if(piece==null)return Fail(RoadRetreatFailure.InvalidTrace);
                var c=piece.Curve;
                double length=HorizontalLength(c);
                if(!seen.Add((piece.EdgeIndex,piece.EdgeVersion))||!Finite(c.P0)||!Finite(c.P1)||!Finite(c.P2)||!Finite(c.P3)
                    ||!ContinuousJoin(last,c.P0)||double.IsNaN(length)||double.IsInfinity(length)||length<.01) return Fail(RoadRetreatFailure.InvalidTrace);
                last=c.P3;
            }
            if(distance==0) return new RoadRetreatResult(RoadRetreatFailure.None,origin,0,Array.Empty<RoadTracePiece>(),Array.Empty<Bezier3>(),null,origin.NodeIndex,origin.NodeVersion);
            double remaining=distance;
            var consumed=new List<RoadTracePiece>();var modified=new List<Bezier3>();
            foreach(var piece in trace.Pieces)
            {
                var c=piece.Curve;double length=HorizontalLength(c);
                consumed.Add(piece);
                if(remaining>length+.001) { modified.Add(c);remaining-=length;continue; }
                float t=1;
                if(remaining<length-.001)
                {
                    float lo=0,hi=1;
                    for(int n=0;n<28;n++) { float mid=(lo+hi)*.5f;if(HorizontalLength(c.Slice(0,mid))<remaining)lo=mid;else hi=mid; }
                    t=(lo+hi)*.5f;
                    if(HorizontalLength(c.Slice(t,1))<10-.001) return Fail(RoadRetreatFailure.ShortRemainder);
                }
                var pos=t==1?c.P3:c.Evaluate(t);var tangent=c.Derivative(t);
                double speed=Math.Sqrt((double)tangent.X*tangent.X+(double)tangent.Z*tangent.Z);
                if(!Finite(pos)||!Finite(tangent)||speed<1e-6) return Fail(RoadRetreatFailure.InvalidTrace);
                var port=new SelectedEndpoint(origin.NodeIndex,origin.NodeVersion,pos,tangent,origin.OneWay,origin.TravelOutward,origin.Width,origin.Lanes,origin.Prefab);
                modified.Add(t==1?c:c.Slice(0,t));
                return new RoadRetreatResult(RoadRetreatFailure.None,port,distance,consumed,modified,t==1?null:(Bezier3?)c.Slice(t,1),
                    t==1?piece.OuterNodeIndex:0,t==1?piece.OuterNodeVersion:0);
            }
            return Fail(RoadRetreatFailure.OutsideTrace);
        }
    }
}
