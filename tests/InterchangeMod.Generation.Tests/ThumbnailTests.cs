using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>
    /// 面板首页的立交缩略图：每种类型按典型参数（正交、公路双幅主线、匝道 8 米）生成后画成 SVG。
    /// 设置环境变量 THUMBNAIL_DIR 时把 interchange-类型.svg 写到那里（UI 工程的 src/mods/thumbs）。
    /// </summary>
    public class ThumbnailTests
    {
        public static IEnumerable<object[]> Types() => TemplateRegistry.All.Select(t => new object[] { t.Type });

        /// <summary>每种类型的典型参数：与各模板测试的默认参数一致。</summary>
        internal static InterchangeParams Params(InterchangeType type)
        {
            if (type == InterchangeType.Wye)
            {
                // Y 型分向：三条双幅公路各隔 120°，都在地面
                var wye = new InterchangeParams
                {
                    Legs = new List<LegSpec> { new LegSpec(0f, 0), new LegSpec(120f, 0), new LegSpec(240f, 0) },
                    RampWidth = 8f,
                };
                foreach (var leg in wye.Legs)
                {
                    leg.Form = RoadForm.Dual;
                    leg.RoadWidth = 16f;
                    leg.MedianWidth = 4f;
                    leg.RoadPrefab = "Highway Oneway";
                }
                wye.Levels[1].Elevation = 10f;
                return wye;
            }
            bool tee = type is InterchangeType.Trumpet or InterchangeType.ExpresswayT or InterchangeType.DirectionalT or InterchangeType.SemiDirectionalT;
            bool sameLevel = type is InterchangeType.DirectionalT or InterchangeType.SemiDirectionalT;
            var p = new InterchangeParams
            {
                Legs = tee ? InterchangeParams.TLegs(90f, 0, sameLevel ? 0 : 1) : InterchangeParams.CrossLegs(90f, 0, 1),
                RampWidth = 8f,
                LoopRadius = 0f,
                SharedRampPrefab = "Highway Twoway",
                SharedRampWidth = 16f,
                RingPrefab = "Ring Oneway",
                RingWidth = 12f,
            };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            // 被交道路 / 支路：快速路类、单点、部分苜蓿叶、环岛式接双向道路，其余双幅
            bool twoWayCross = type is InterchangeType.ExpresswayNode or InterchangeType.ExpresswayT or InterchangeType.SinglePoint
                or InterchangeType.PartialCloverleaf or InterchangeType.Roundabout or InterchangeType.Diamond;
            int crossB = tee ? 1 : 3;
            if (twoWayCross)
                p.SetRoad(1, crossB, RoadForm.TwoWay, 20f, 0f, "Medium Road");
            else
                p.SetRoad(1, crossB, RoadForm.Dual, 12f, 4f, "Cross Oneway");
            // 主线在上层的类型：快速路类、单点、苜蓿叶类、环岛式、菱形；喇叭形支路跨过主线；定向类主线在下层
            bool mainOnTop = type is InterchangeType.ExpresswayNode or InterchangeType.ExpresswayT or InterchangeType.SinglePoint
                or InterchangeType.Cloverleaf or InterchangeType.PartialCloverleaf or InterchangeType.Roundabout or InterchangeType.Diamond;
            p.Levels[0].Elevation = mainOnTop ? 10f : 0f;
            p.Levels[1].Elevation = mainOnTop ? 0f : 10f;
            return p;
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void Thumbnail_IsDrawnForEveryType(InterchangeType type)
        {
            var template = TemplateRegistry.Get(type)!;
            var p = Params(type);
            Assert.Empty(template.ValidateParams(p));
            // 匝道沿路并行很长的类型固定取景范围（米），其余按匝道离开两条路的范围自动取景
            float half = type switch { InterchangeType.Stack => 300f, InterchangeType.SinglePoint => 170f, InterchangeType.Roundabout => 170f, _ => 0f };
            string svg = SvgPreview.Thumbnail(template.Generate(p), 90f, half);
            Assert.StartsWith("<svg", svg);
            Assert.DoesNotContain("NaN", svg);
            if (System.Environment.GetEnvironmentVariable("THUMBNAIL_DIR") is string dir)
                File.WriteAllText(Path.Combine(dir, $"interchange-{type}.svg"), svg);
        }
    }
}
