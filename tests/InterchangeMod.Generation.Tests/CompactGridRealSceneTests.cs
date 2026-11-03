using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;
using Xunit.Abstractions;
namespace InterchangeMod.Generation.Tests
{
    /// <summary>游戏内井字连接：78° 交叉，方向 1/3 双幅公路、方向 2/4 双向 Large Road Divided（混合道路）。原先因 80°–100° 门槛和核心朝向只对齐一组路而算不出。</summary>
    public class CompactGridRealSceneTests
    {
        private readonly ITestOutputHelper o; public CompactGridRealSceneTests(ITestOutputHelper o){this.o=o;}
        // 能否接上随坡度跳变（10%、16% 放不下），所以工具按坡度阶梯依次尝试（InterchangeToolSystem.GradeLadder）
        [Theory][InlineData(.12f)][InlineData(.15f)][InlineData(.18f)]
        public void SkewedMixedRoadSceneHasPreview(float grade) => Assert.True(Solve(grade).Failure==RetreatSearchFailure.None);

        /// <summary>连接·自动的坡度阶梯 10% ~ 16% 里能找到预览。</summary>
        [Fact]
        public void AutoGradeLadderFindsAPreview() =>
            Assert.Contains(Enumerable.Range(10,7), p => Solve(p/100f).Failure==RetreatSearchFailure.None);

        private RetreatSearchResult Solve(float grade)
        {
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("InterchangeMod.Generation.Tests.Fixtures.grid-mixed-20261103.json")!;
            using var doc=JsonDocument.Parse(stream);
            Vec3 V(JsonElement v)=>new(v.GetProperty("x").GetSingle(),v.GetProperty("y").GetSingle(),v.GetProperty("z").GetSingle());
            var traces=doc.RootElement.EnumerateArray().Select(e=> {
                var q=e.GetProperty("origin");var p=new SelectedEndpoint(q.GetProperty("nodeIndex").GetInt32(),q.GetProperty("nodeVersion").GetInt32(),
                    V(q.GetProperty("position")),V(q.GetProperty("outwardTangent")),q.GetProperty("oneWay").GetBoolean(),q.GetProperty("travelOutward").GetBoolean(),
                    q.GetProperty("width").GetSingle(),q.GetProperty("lanes").GetInt32(),q.GetProperty("prefab").GetString()!);
                return new RoadApproachTrace(p,e.GetProperty("pieces").EnumerateArray().Select(x=> {
                    var c=x.GetProperty("curve");return new RoadTracePiece(x.GetProperty("edgeIndex").GetInt32(),x.GetProperty("edgeVersion").GetInt32(),
                        new Bezier3(V(c.GetProperty("p0")),V(c.GetProperty("p1")),V(c.GetProperty("p2")),V(c.GetProperty("p3"))),x.GetProperty("reversed").GetBoolean(),
                        x.GetProperty("outerNodeIndex").GetInt32(),x.GetProperty("outerNodeVersion").GetInt32());
                }));
            }).ToArray();
            int[][] groups={new[]{211696,211691},new[]{214263},new[]{211506,211480},new[]{214253}};
            var arms=groups.Select((g,i)=>new EndpointArm(i,g.Select(id=>traces.Single(t=>t.Origin.NodeIndex==id).Origin))).ToArray();
            var setup=EndpointMixedRoadSetup.Create(arms,StackRealSceneRegressionTests.Catalog(),InterchangeType.CompactGrid,true,0,0,478.6323f,"Highway Oneway - 1 lane");
            Assert.True(setup.Success,setup.Diagnostic);
            var watch=Stopwatch.StartNew();
            var r=EndpointMixedRoadSearch.SearchCore(arms,traces,setup,grade,1000,128,d=>o.WriteLine(d));
            o.WriteLine($"grade {grade}: {r.Failure} attempts {r.Attempts} {watch.Elapsed.TotalSeconds:F2}s retreat {string.Join("/",r.Distances.Select(d=>d.ToString("F0")))}");
            if(System.Environment.GetEnvironmentVariable("GRID_SVG") is string svgDir&&r.Spatial!=null)
                System.IO.File.WriteAllText(System.IO.Path.Combine(svgDir,$"grid-{grade*100:F0}.svg"),Svg(r,traces));
            return r;
        }

        /// <summary>调试：把求解结果（青色）和原路（灰色）画成 SVG，看汇入段的形状。</summary>
        private static string Svg(RetreatSearchResult r,RoadApproachTrace[] traces)
        {
            var curves=r.Spatial!.Segments.Select(s=>(s.WorldCurve,"#3cc","3")).Concat(traces.SelectMany(t=>t.Pieces).Select(p=>(p.Curve,"#888","2"))).ToArray();
            var pts=curves.SelectMany(c=>Enumerable.Range(0,9).Select(i=>c.Item1.Evaluate(i/8f))).ToArray();
            float cx=pts.Average(p=>p.X),cz=pts.Average(p=>p.Z);
            var sb=new System.Text.StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-800 -800 1600 1600\" style=\"background:#123\">");
            foreach(var (c,col,w) in curves)
                sb.Append($"<path d=\"M{string.Join("L",Enumerable.Range(0,17).Select(i=>c.Evaluate(i/16f)).Select(p=>$"{p.X-cx:F1},{-(p.Z-cz):F1}"))}\" stroke=\"{col}\" stroke-width=\"{w}\" fill=\"none\"/>");
            return sb.Append("</svg>").ToString();
        }
    }
}
