using System;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>路段在立交中的角色，用于调试和预览配色。</summary>
    public enum RoadRole
    {
        MainRoad,
        CrossRoad,
        Ramp,
        /// <summary>地面辅路（中式快速路节点）。</summary>
        FrontageRoad,
    }

    /// <summary>
    /// 生成层的输出：一条路段的完整描述。
    /// 落地层会把它转换成游戏里的 CreationDefinition + NetCourse 实体。
    /// 曲线 Y 是离地高度；单行道路的通行方向就是曲线方向（P0 → P3）。
    /// 两条路段的节点坐标（StartNode / EndNode）完全相同时，游戏会把它们接到同一个节点上。
    /// 节点坐标默认就是曲线端点；也可以让曲线端点相对节点横向偏移（游戏道路工具的“边缘对齐”就是这样）：
    /// 节点仍是共用的那个点，路面从偏移后的曲线端点开始，用于宽度变化时按不变车道一侧对齐。
    /// </summary>
    public sealed class RoadSegmentSpec
    {
        public RoadSegmentSpec(string name, RoadRole role, Bezier3 curve, string? prefab, Vec3? startNode = null, Vec3? endNode = null)
        {
            Name = name;
            Role = role;
            Curve = curve;
            Prefab = prefab;
            StartNode = startNode ?? curve.P0;
            EndNode = endNode ?? curve.P3;
        }

        /// <summary>调试用名称，例如 "NB-off"。</summary>
        public string Name { get; }

        public RoadRole Role { get; }

        public Bezier3 Curve { get; }

        /// <summary>道路预制体名。null 表示使用玩家在道路菜单里选的道路。</summary>
        public string? Prefab { get; }

        /// <summary>起点所接节点的坐标；通常等于 Curve.P0，边缘对齐时与它横向偏移。</summary>
        public Vec3 StartNode { get; }

        /// <summary>终点所接节点的坐标；通常等于 Curve.P3。</summary>
        public Vec3 EndNode { get; }

        /// <summary>曲线端点相对节点有偏移（边缘对齐）。</summary>
        public bool HasNodeOffset => !StartNode.Equals(Curve.P0) || !EndNode.Equals(Curve.P3);

        /// <summary>曲线和节点一起做同一个变换（平移、旋转）。</summary>
        public RoadSegmentSpec Transform(Func<Vec3, Vec3> point) =>
            new RoadSegmentSpec(Name, Role, new Bezier3(point(Curve.P0), point(Curve.P1), point(Curve.P2), point(Curve.P3)), Prefab, point(StartNode), point(EndNode));

        /// <summary>反向（单行道路换行驶方向），节点随之交换。</summary>
        public RoadSegmentSpec Reversed() => new RoadSegmentSpec(Name, Role, Curve.Reverse(), Prefab, EndNode, StartNode);

        public override string ToString() => $"{Name} [{Role}] {Curve}";
    }
}
