using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 把一条曲线写成游戏能理解的“定义实体”（CreationDefinition + NetCourse）。
    /// 游戏随后会自己生成临时道路（预览）、桥墩和错误检查；工具把 applyMode 设为 Apply 时变成正式道路。
    /// 字段填法参照游戏 NetToolSystem.CreateDefinitionsJob.CreateStraightLine。
    /// </summary>
    public static class NetCourseBuilder
    {
        /// <param name="curve">世界坐标曲线，Y 已经包含高度（地形高度 + elevation）。</param>
        /// <param name="startElevation">起点离地高度，必须与 curve.a.y 减去地形高度一致。</param>
        /// <param name="endElevation">终点离地高度。</param>
        /// <param name="startNode">起点接到的已有节点；Entity.Null 表示新建节点。</param>
        /// <param name="endNode">终点接到的已有节点。</param>
        /// <param name="startNodePosition">起点节点坐标；null 表示曲线起点。与曲线起点不同时路面从曲线起点开始、仍接到这个节点（边缘对齐，与游戏道路工具相同）。</param>
        /// <param name="endNodePosition">终点节点坐标；null 表示曲线终点。</param>
        public static Entity CreateSegment(EntityCommandBuffer commandBuffer, Entity prefab, Bezier4x3 curve,
            float startElevation, float endElevation, int randomSeed, Entity startNode = default, Entity endNode = default,
            float3? startNodePosition = null, float3? endNodePosition = null)
        {
            var definition = new CreationDefinition
            {
                m_Prefab = prefab,
                m_RandomSeed = randomSeed,
                m_Flags = CreationFlags.SubElevation,
            };

            var course = new NetCourse
            {
                m_Curve = curve,
                m_StartPosition = GetCoursePos(curve, 0f, startElevation, CoursePosFlags.IsFirst, startNode, startNodePosition ?? curve.a),
                m_EndPosition = GetCoursePos(curve, 1f, endElevation, CoursePosFlags.IsLast, endNode, endNodePosition ?? curve.d),
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            };

            Entity entity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(entity, definition);
            commandBuffer.AddComponent(entity, default(Updated));
            commandBuffer.AddComponent(entity, course);
            return entity;
        }

        private static CoursePos GetCoursePos(Bezier4x3 curve, float t, float elevation, CoursePosFlags flags, Entity node, float3 position)
        {
            return new CoursePos
            {
                // 填已有节点时新路段接到这个节点上（拆除原有道路后保留的部分接回原来的路网）
                m_Entity = node,
                // 节点按这个坐标合并；路面用曲线自己的端点（道路不是 StrictNodes，见 GenerateEdgesSystem）
                m_Position = position,
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)),
                m_Elevation = new float2(elevation),
                m_CourseDelta = t,
                // 没有平行道路时，原版两侧都标记为最外侧
                m_Flags = flags | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                m_ParentMesh = -1,
            };
        }
    }
}
