using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InterchangeMod.Generation
{
    /// <summary>把生成结果画成俯视 SVG，方便不开游戏也能检查几何形状。颜色越暖表示越高。</summary>
    public static class SvgPreview
    {
        public static string Render(IReadOnlyList<RoadSegmentSpec> segments, float margin = 20f)
        {
            var points = segments.SelectMany(s => new[] { s.Curve.P0, s.Curve.P1, s.Curve.P2, s.Curve.P3 }).ToList();
            float minX = points.Min(v => v.X) - margin, maxX = points.Max(v => v.X) + margin;
            float minZ = points.Min(v => v.Z) - margin, maxZ = points.Max(v => v.Z) + margin;
            float maxY = Math.Max(1f, points.Max(v => v.Y));

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(minX)} {F(-maxZ)} {F(maxX - minX)} {F(maxZ - minZ)}\">\n");
            sb.Append($"<rect x=\"{F(minX)}\" y=\"{F(-maxZ)}\" width=\"{F(maxX - minX)}\" height=\"{F(maxZ - minZ)}\" fill=\"#f4f4f0\"/>\n");
            foreach (var s in segments)
            {
                var c = s.Curve;
                float avgY = (c.P0.Y + c.P3.Y) / 2f;
                float width = s.Role == RoadRole.Ramp ? 4f : 8f;
                // SVG 的 y 轴向下，这里用 -Z 让北朝上
                sb.Append($"<path d=\"M{F(c.P0.X)},{F(-c.P0.Z)} C{F(c.P1.X)},{F(-c.P1.Z)} {F(c.P2.X)},{F(-c.P2.Z)} {F(c.P3.X)},{F(-c.P3.Z)}\" ");
                sb.Append($"stroke=\"{HeightColor(avgY / maxY)}\" stroke-width=\"{F(width)}\" fill=\"none\" stroke-linecap=\"round\"><title>{s.Name}</title></path>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>缩略图取景：匝道离开两条路（中心线两侧这么宽，米）之外的部分，沿路并行的长段不算。</summary>
        private const float kThumbnailCorridor = 25f;

        /// <summary>
        /// 面板用的缩略图：透明底、正方形，裁到匝道离开两条路走廊的范围（沿路并行的长段和两端道路伸出画面）。
        /// 按高度从低到高画，每段先画深色描边再画颜色，交叉处上层压住下层；主线、被交道路、辅路浅灰（越高越亮），
        /// 匝道按高度从蓝到橙。只用折线，游戏 UI 的 SVG 支持有限。crossingAngle 是被交道路方向（度，相对主线 +Z）；
        /// halfSize > 0 时改为以立交中心为中心、半边长 halfSize 米取景（匝道沿路并行很长的类型自动取景不好看）。
        /// </summary>
        public static string Thumbnail(IReadOnlyList<RoadSegmentSpec> segments, float crossingAngle = 90f, float halfSize = 0f)
        {
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => Sample(s.Curve)).ToList();
            var cross = Geometry.Directions.FromAngle(crossingAngle);
            var focus = ramps.Where(v => Math.Abs(v.X) > kThumbnailCorridor && Math.Abs(v.X * cross.Z - v.Z * cross.X) > kThumbnailCorridor).ToList();
            if (focus.Count == 0)
                focus = ramps.Count > 0 ? ramps : segments.SelectMany(s => Sample(s.Curve)).ToList();
            float minX = focus.Min(v => v.X), maxX = focus.Max(v => v.X), minZ = focus.Min(v => v.Z), maxZ = focus.Max(v => v.Z);
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;
            float half = Math.Max(maxX - minX, maxZ - minZ) / 2f * 1.15f + 20f;
            if (halfSize > 0f)
            {
                cx = cz = 0f;
                half = halfSize;
            }
            var all = segments.SelectMany(s => new[] { s.Curve.P0.Y, s.Curve.P3.Y }).ToList();
            float low = all.Min(), high = Math.Max(low + 1f, all.Max());

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(cx - half)} {F(-cz - half)} {F(2f * half)} {F(2f * half)}\">");
            foreach (var s in segments.OrderBy(s => (s.Curve.P0.Y + s.Curve.P3.Y) / 2f).ThenBy(s => s.Role == RoadRole.Ramp ? 1 : 0))
            {
                float t = ((s.Curve.P0.Y + s.Curve.P3.Y) / 2f - low) / (high - low);
                float width = s.Role switch { RoadRole.MainRoad => 14f, RoadRole.CrossRoad => 12f, RoadRole.FrontageRoad => 10f, _ => 7f };
                string color = s.Role == RoadRole.Ramp ? Lerp(0x3F, 0xA9, 0xF5, 0xF5, 0xA6, 0x23, t) : Lerp(0x8C, 0x97, 0xA3, 0xDD, 0xE3, 0xE8, t);
                string d = "M" + string.Join("L", Sample(s.Curve).Select(v => $"{F(v.X)},{F(-v.Z)}"));
                sb.Append($"<path d=\"{d}\" stroke=\"#10161d\" stroke-width=\"{F(width + 6f)}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                sb.Append($"<path d=\"{d}\" stroke=\"{color}\" stroke-width=\"{F(width)}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static IEnumerable<Geometry.Vec3> Sample(Geometry.Bezier3 curve) => Enumerable.Range(0, 7).Select(i => curve.Evaluate(i / 6f));

        private static string Lerp(int r0, int g0, int b0, int r1, int g1, int b1, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return $"#{(int)(r0 + (r1 - r0) * t):X2}{(int)(g0 + (g1 - g0) * t):X2}{(int)(b0 + (b1 - b0) * t):X2}";
        }

        private static string HeightColor(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            int r = (int)(80 + 175 * t), g = (int)(110 + 40 * t), b = (int)(200 - 160 * t);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
