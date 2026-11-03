namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointPreviewMessages
    {
        public static string StackFailure(RetreatSearchResult result,int retreatLimit)
        {
            string reason;
            if(result.Failure==RetreatSearchFailure.InvalidInput)
                return "道路或参数数据已变化，请重新选点后生成预览。";
            if(result.Failure==RetreatSearchFailure.BudgetExceeded)
                reason="本轮搜索次数已用完，尚未找到满足约束的接路方案。";
            else if(result.CoreMatched==false||(result.CoreMatched==null&&result.Attempts==0))
                reason="内部布局暂未满足道路断面、坡度、净空或桥墩要求，请核对两幅道路是否同款同宽，或调整层差和坡度。";
            else if(result.ClearanceDetail.Contains("near-orthogonal"))
                return $"井字要求两条路接近垂直（交角 {EndpointCompactGridCore.kMinGridCrossing:F0}°–{180-EndpointCompactGridCore.kMinGridCrossing:F0}°，相对两臂偏差不超过 20°），这个路口超出范围：请改用定向（标准），或调整端点。";
            else if(result.Attempts==0)
                reason="已找到安全内部布局，但尚未找到匹配全部端点曲线和方向的接路组合。";
            else
                reason=result.LastSpatialFailure switch
                {
                    SpatialCandidateFailure.GradeExceeded=>"本轮候选接路尚未满足所选最大坡度。",
                    SpatialCandidateFailure.PillarConflict=>"接路或自动桥墩与其他道路冲突。",
                    SpatialCandidateFailure.InvalidGeometry=>"当前候选接路尚未匹配内部接口的曲线和方向。",
                    SpatialCandidateFailure.Uncertain=>"当前接路方案无法确认满足安全约束。",
                    _=>"当前接路方案存在道路净空冲突。",
                };
            string suggestion=retreatLimit<1000
                ?$"本次按每臂{retreatLimit}米上限搜索；可调整端点或参数，也可自行选择1000米上限重试，遇路口或尽头仍会停止。"
                :"当前退接上限为每臂1000米；请检查原路可延伸长度，或调整端点、层高和坡度后重试。";
            return reason+suggestion;
        }
    }
}
