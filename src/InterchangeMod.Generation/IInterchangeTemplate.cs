using System.Collections.Generic;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 一种立交类型的生成器。每种立交写一个实现，放在 Templates 目录。
    /// 输出使用局部坐标：原点为立交中心，水平方向由汇入臂角度决定，Y = 离地高度。
    /// 平移、旋转到鼠标位置由工具层完成（见 InterchangeLayout.Place）。
    /// </summary>
    public interface IInterchangeTemplate
    {
        InterchangeType Type { get; }

        string DisplayName { get; }

        /// <summary>汇入方向数：3（三向）或 4（四向）。</summary>
        int LegCount { get; }

        /// <summary>检查参数本身是否合理（不涉及地形和已有建筑）。返回空列表表示通过。</summary>
        IReadOnlyList<string> ValidateParams(InterchangeParams p);

        IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p);
    }
}
