using System;
using InterchangeMod.Generation.Geometry;
using System.Collections.Generic;
using System.Linq;
namespace InterchangeMod.Generation.Endpoints
{
    public enum NodeMarkerKind { Available, Hovered, Selected }
    public readonly struct NodeMarkerStyle
    {
        public NodeMarkerStyle(NodeMarkerKind kind,int groupNumber) { Kind=kind;GroupNumber=groupNumber; }
        public NodeMarkerKind Kind { get; }
        public int GroupNumber { get; }
    }
    public static class NodeFeedback
    {
        public static bool SelectionChanged(EndpointSelectionChange change) =>
            change==EndpointSelectionChange.Added || change==EndpointSelectionChange.Removed;
        public static bool ReadyForAutoPreview(InterchangeType type,IReadOnlyList<EndpointArm> arms) =>
            EndpointTypeRequest.Create(type,arms,true,.16f,new float[arms.Count]).InputReady;

        /// <summary>一个方向已选齐：一个双向端点，或一进一出两个单向端点。</summary>
        public static bool ArmComplete(EndpointArm arm) =>
            (arm.Endpoints.Count == 1 && !arm.Endpoints[0].OneWay)
            || (arm.Endpoints.Count == 2 && arm.Endpoints.All(e => e.OneWay) && arm.Endpoints[0].TravelOutward != arm.Endpoints[1].TravelOutward);

        /// <summary>当前方向之后（循环）第一个还没选齐的方向；都选齐时留在当前方向。</summary>
        public static int NextIncompleteArm(IReadOnlyList<EndpointArm> arms, int current)
        {
            for (int k = 1; k <= arms.Count; k++)
            {
                int i = (current + k) % arms.Count;
                if (!ArmComplete(arms[i]))
                    return i;
            }
            return current;
        }

        /// <summary>双幅另一幅的端点：外向切线与它平行（夹角 20° 以内），行驶方向相反，沿路错开不超过这个距离（米）。</summary>
        private const float kPairAlong = 20f;
        /// <summary>两幅中心线的横向距离范围（米）。</summary>
        private const float kPairMinAcross = 2f, kPairMaxAcross = 60f;

        /// <summary>
        /// 点了双幅道路一幅的单行端点后，从附近的开放端点里找对向那一幅：平行、行驶方向相反、沿路基本对齐，取横向最近的一个。
        /// 找不到时返回 null。
        /// </summary>
        public static SelectedEndpoint? OppositeCarriageway(SelectedEndpoint picked, IEnumerable<SelectedEndpoint> candidates)
        {
            if (!picked.OneWay)
                return null;
            Vec3 Flat(Vec3 v) => new Vec3(v.X, 0f, v.Z);
            Vec3 axis = Flat(picked.OutwardTangent);
            if (axis.Length() < 1e-4f)
                return null;
            axis = Vec3.Normalize(axis);
            SelectedEndpoint? best = null;
            float bestAcross = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.OneWay || c.TravelOutward == picked.TravelOutward
                    || (c.NodeIndex == picked.NodeIndex && c.NodeVersion == picked.NodeVersion))
                    continue;
                Vec3 tangent = Flat(c.OutwardTangent);
                if (tangent.Length() < 1e-4f || Directions.Dot(Vec3.Normalize(tangent), axis) < MathF.Cos(20f * Directions.DegToRad))
                    continue;
                Vec3 offset = Flat(c.Position - picked.Position);
                float along = Math.Abs(Directions.Dot(offset, axis));
                float across = MathF.Sqrt(Math.Max(0f, Directions.Dot(offset, offset) - along * along));
                if (along > kPairAlong || across < kPairMinAcross || across > kPairMaxAcross || across >= bestAcross)
                    continue;
                best = c;
                bestAcross = across;
            }
            return best;
        }
        public static NodeMarkerStyle Style(int index,int version,bool hovered,IReadOnlyList<EndpointArm> arms)
        {
            foreach(var arm in arms)
                if(arm.Endpoints.Any(p=>p.NodeIndex==index && p.NodeVersion==version))
                    return new NodeMarkerStyle(NodeMarkerKind.Selected,arm.Index+1);
            return new NodeMarkerStyle(hovered?NodeMarkerKind.Hovered:NodeMarkerKind.Available,0);
        }
    }
}
