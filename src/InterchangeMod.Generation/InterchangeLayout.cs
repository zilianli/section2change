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

        /// <summary>弯道上坡度超限时收紧最大坡度重新求解的次数上限。</summary>
        private const int kCurveRetries = 3;
        /// <summary>收紧后的坡度按这个档位向下取整：沿弯道移动鼠标时相邻几帧收紧到同一档，带缓存的模板才能复用求解结果。</summary>
        private const float kGradeStep = 0.0025f;

        /// <summary>
        /// 弯道上的立交（docs/04）：按直线生成后整体弯曲。弯道内侧的匝道被压短、坡度变陡，弯曲后检查坡度：超限时把求解用的
        /// 最大坡度按超出比例收紧（再留 3%，按 kGradeStep 向下取整），重新配匝道长度、生成、弯曲，最多 kCurveRetries 次——
        /// 这就是弯道求解较慢的原因。used 是最后实际求解用的参数；仍超限的路段交给调用方用原来的上限 ValidateGeometry 报出来。
        /// </summary>
        public static List<RoadSegmentSpec> Bend(IInterchangeTemplate template, InterchangeParams p, CurveWarp warp, out InterchangeParams used)
        {
            used = p;
            var segments = warp.Apply(template.Generate(p));
            for (int attempt = 0; attempt < kCurveRetries && !warp.IsIdentity; attempt++)
            {
                float worst = segments.Max(s => s.Curve.MaxGrade());
                if (worst <= p.MaxGrade + kGradeTolerance)
                    break;
                var tighter = used.Clone();
                // 按档位向下取整（至少比上次低一档）：收紧值随弯道连续变化时也落到同一档
                float grade = (float)System.Math.Floor(used.MaxGrade * p.MaxGrade / worst * 0.97f / kGradeStep) * kGradeStep;
                tighter.MaxGrade = System.Math.Min(grade, used.MaxGrade - kGradeStep);
                if (tighter.MaxGrade < kGradeStep)
                    break;
                tighter = FitRampLength(template, tighter);
                if (template.ValidateParams(tighter).Count > 0)
                    break;
                used = tighter;
                segments = warp.Apply(template.Generate(used));
            }
            return segments;
        }

        /// <summary>坡度检查留的余量（采样误差）。</summary>
        private const float kGradeTolerance = 0.005f;

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
        /// <summary>臂端坡道每段的最大长度（米），短于游戏切分高架路段的 80 米。</summary>
        private const float kMaxSlopePiece = 70f;

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
                // 分段（每段不超过 kMaxSlopePiece）：高架路段超过 80 米游戏会从中间切开，切点的立柱位置不受控制
                int pieces = System.Math.Max(1, (int)System.Math.Ceiling(length / kMaxSlopePiece));
                for (int k = 0; k < pieces; k++)
                {
                    var piece = curve.Slice((float)k / pieces, (float)(k + 1) / pieces);
                    // 最外面那段（接原有道路）叫 *-slope，里面的叫 *-slope-1、-2…
                    string name = k == pieces - 1 ? $"{owner.Name}-slope" : $"{owner.Name}-slope-{k + 1}";
                    segments.Add(new RoadSegmentSpec(name, owner.Role, outward ? piece : piece.Reverse(), owner.Prefab));
                }
            }
        }

        /// <summary>
        /// 内部双向、外部双幅的臂（NeedsTwoWayToDualTransition）：双向道路的臂端节点分出两条单行道路，S 形平行接到外部双幅的位置。
        /// 驶离立交的一幅在外指方向的行驶一侧（右侧通行在右、左侧通行在左）。分出处两幅外缘对齐双向道路的两侧边缘（曲线端点横移、节点仍是双向道路的端点）。
        /// 要在 AddApproachSlopes 之前调用（坡道接在分出后的两幅端点上）。
        /// </summary>
        public static void AddDualTails(List<RoadSegmentSpec> segments, InterchangeParams p)
        {
            for (int index = 0; index < p.Legs.Count; index++)
            {
                LegSpec leg = p.Legs[index];
                if (!leg.NeedsTwoWayToDualTransition)
                    continue;
                RoadRole role = index % 2 == 0 ? RoadRole.MainRoad : RoadRole.CrossRoad;
                Vec3 dir = leg.Direction;
                // 这条臂的臂端：该道路未接其他路段、沿臂方向最远的端点
                var ends = LegEnds(segments.Where(s => s.Role == role)).Where(e => Directions.Dot(e, dir) > 0f).ToList();
                if (ends.Count == 0)
                    continue;
                Vec3 end = ends.OrderByDescending(e => Directions.Dot(e, dir)).First();
                RoadSegmentSpec owner = segments.First(s => s.StartNode.Equals(end) || s.EndNode.Equals(end));
                // 驶离那一幅所在的一侧
                Vec3 right = Directions.RightOf(dir) * (p.RightHandTraffic ? 1f : -1f);
                float width = leg.ExternalWidth > 0f ? leg.ExternalWidth : leg.RoadWidth / 2f;
                float edge = System.Math.Max(0f, (leg.RoadWidth - width) / 2f);
                float offset = (leg.ExternalMedian + width) / 2f;
                float length = System.Math.Min(leg.StubLength, kMaxSlopePiece);
                Vec3 outFar = end + dir * length + right * offset, inFar = end + dir * length - right * offset;
                segments.Add(new RoadSegmentSpec($"{owner.Name}-out", role,
                    Bezier3.FromTangents(end + right * edge, dir, outFar, dir, length / 3f), leg.ExternalPrefab, end, outFar));
                segments.Add(new RoadSegmentSpec($"{owner.Name}-in", role,
                    Bezier3.FromTangents(inFar, -dir, end - right * edge, -dir, length / 3f), leg.ExternalPrefab, inFar, end));
            }
        }

        public static List<string> ValidateGeometry(IEnumerable<RoadSegmentSpec> segments, InterchangeParams p)
        {
            var errors = new List<string>();
            foreach (var s in segments)
            {
                float grade = s.Curve.MaxGrade();
                // 留 0.5% 余量，避免采样误差误报
                if (grade > p.MaxGrade + kGradeTolerance)
                    errors.Add($"{s.Name} 坡度 {grade:P1} 超过上限 {p.MaxGrade:P1}");
            }
            return errors;
        }
    }
}
