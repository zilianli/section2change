using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>
    /// 按相对高度缓存布置的模板（首轮核查第 6 条）：同一个实例在同一高度段里换几种基层高度连续生成，复用布置时
    /// 高架段（游戏超过 80 米会从中间切开、立柱位置不受控制）都要短于 80 米。
    /// </summary>
    public class CacheReuseTests
    {
        [Theory]
        [InlineData(InterchangeType.DirectionalT)]
        [InlineData(InterchangeType.SemiDirectionalT)]
        [InlineData(InterchangeType.Wye)]
        [InlineData(InterchangeType.Stack)]
        [InlineData(InterchangeType.Turbine)]
        public void ShiftedBase_KeepsElevatedPiecesShort(InterchangeType type)
        {
            var template = TemplateRegistry.Get(type)!;
            foreach (float shift in new[] { 0f, 3.5f, 1.5f, -1f, 2.5f, 0f })
            {
                var p = ThumbnailTests.Params(type);
                foreach (var level in p.Levels)
                    level.Elevation += shift;
                Assert.Empty(template.ValidateParams(p));
                var segments = template.Generate(p);
                foreach (var sg in segments.Where(sg => System.Math.Max(sg.Curve.P0.Y, sg.Curve.P3.Y) >= HeightRange.BridgeThreshold))
                    Assert.True(sg.Curve.Length() < 80f, $"base {shift}: {sg.Name} is {sg.Curve.Length():F0} m");
            }
        }
    }
}
