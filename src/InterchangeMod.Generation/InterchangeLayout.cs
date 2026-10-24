using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>对生成结果的通用操作：摆放到世界坐标、通用几何校验。</summary>
    public static class InterchangeLayout
    {
        /// <summary>把局部坐标的结果平移、绕 Y 轴旋转到世界坐标。</summary>
        public static List<RoadSegmentSpec> Place(IEnumerable<RoadSegmentSpec> segments, Vec3 position, float yawRadians)
        {
            return segments.Select(s => s.Transform(v => v.RotateY(yawRadians) + position)).ToList();
        }

        /// <summary>
        /// 匝道太短、坡度超限时自动加长：从 p.RampLength 起每次加 step 米，最多加 maxExtra 米，
        /// 返回第一个参数检查和几何检查都通过的参数。
        /// 加长解决不了的问题（如净空不足）返回原参数，由调用方照常报错。
        /// </summary>
        public static InterchangeParams FitRampLength(IInterchangeTemplate template, InterchangeParams p, float step = 10f, float maxExtra = 300f)
        {
            var fitted = p.Clone();
            for (float extra = 0f; extra <= maxExtra; extra += step)
            {
                fitted.RampLength = p.RampLength + extra;
                if (template.ValidateParams(fitted).Count == 0 && ValidateGeometry(template.Generate(fitted), fitted).Count == 0)
                    return fitted;
            }
            return p;
        }

        /// <summary>所有模板共用的校验：每条路段的最大坡度不能超过限制。</summary>
        /// <summary>
        /// 臂端：主线和被交道路上只属于一条路段的端点（匝道两端都接在别的路段上，不算）。
        /// 原路口改造时，保留下来的原有道路从这里接回立交。
        /// </summary>
        public static List<Vec3> LegEnds(IEnumerable<RoadSegmentSpec> segments)
        {
            var list = segments.ToList();
            var count = new Dictionary<Vec3, int>();
            foreach (var s in list)
            {
                count[s.StartNode] = count.TryGetValue(s.StartNode, out int a) ? a + 1 : 1;
                count[s.EndNode] = count.TryGetValue(s.EndNode, out int b) ? b + 1 : 1;
            }
            return list.Where(s => s.Role != RoadRole.Ramp)
                .SelectMany(s => new[] { s.StartNode, s.EndNode })
                .Where(v => count[v] == 1)
                .Distinct()
                .ToList();
        }

        /// <summary>臂端高度和外部道路相差不超过这个值（米）时不加坡道。</summary>
        public const float ApproachTolerance = 0.5f;

        /// <summary>
        /// 臂端坡道：臂端高度和这条臂的外部道路高度（LegSpec.ExternalElevation）不同时，沿臂方向再延伸一段，
        /// 升降到外部高度。两端都是水平的竖曲线，最大坡度约为 MaxGrade（长度 ≈ 1.5 × 高差 ÷ MaxGrade）。
        /// 坡道沿用臂端那段路的道路和行驶方向；双幅时两幅各一段。
        /// </summary>
        public static void AddApproachSlopes(List<RoadSegmentSpec> segments, InterchangeParams p)
        {
            foreach (Vec3 end in LegEnds(segments))
            {
                // 臂端属于沿方向伸得最远的那条臂
                LegSpec leg = p.Legs.OrderByDescending(l => Directions.Dot(end, l.Direction)).First();
                if (!(leg.ExternalElevation is float target) || System.Math.Abs(end.Y - target) <= ApproachTolerance)
                    continue;
                float length = 1.55f * System.Math.Abs(target - end.Y) / p.MaxGrade;   // 略长一点，避免数值误差超出坡度上限
                Vec3 dir = leg.Direction;
                var outer = new Vec3(end.X, target, end.Z) + dir * length;
                var p1 = end + dir * (length / 3f);
                var p2 = outer - dir * (length / 3f);
                var curve = new Bezier3(end, p1, p2, outer);

                RoadSegmentSpec owner = segments.First(s => s.StartNode.Equals(end) || s.EndNode.Equals(end));
                bool outward = owner.EndNode.Equals(end);
                segments.Add(new RoadSegmentSpec($"{owner.Name}-slope", owner.Role, outward ? curve : curve.Reverse(), owner.Prefab));
            }
        }

        public static List<string> ValidateGeometry(IEnumerable<RoadSegmentSpec> segments, InterchangeParams p)
        {
            var errors = new List<string>();
            foreach (var s in segments)
            {
                float grade = s.Curve.MaxGrade();
                // 留 0.5% 余量，避免采样误差误报
                if (grade > p.MaxGrade + 0.005f)
                    errors.Add($"{s.Name} 坡度 {grade:P1} 超过上限 {p.MaxGrade:P1}");
            }
            return errors;
        }
    }
}
