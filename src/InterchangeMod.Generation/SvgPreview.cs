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

        private static string HeightColor(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            int r = (int)(80 + 175 * t), g = (int)(110 + 40 * t), b = (int)(200 - 160 * t);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
