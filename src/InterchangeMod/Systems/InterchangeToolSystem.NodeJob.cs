using System;
using System.Diagnostics;
using System.Threading.Tasks;
using InterchangeMod.Generation.Endpoints;

namespace InterchangeMod.Systems
{
    /// <summary>
    /// 连接模式的后台求解：主线程读好原路、准备好输入后，搜索（最慢的一步，几百毫秒到几秒）在后台线程跑，
    /// 面板显示阶段和已用时间；算完回到主线程生成预览。同一时间只算一个（与混合道路定向共用 s_MixedNodeSolveGate，
    /// 几个模板有静态缓存）；选点或参数变了旧结果作废。
    /// </summary>
    public partial class InterchangeToolSystem
    {
        private sealed class NodeJob
        {
            public Task<RetreatSearchResult> Task;
            public Action<RetreatSearchResult> Complete;
            public int Version;
        }

        private NodeJob m_NodeJob;
        private volatile int m_NodeJobVersion;
        private readonly Stopwatch m_NodeSolveClock = new Stopwatch();
        private string m_NodeSolveStage = "";
        /// <summary>后台线程报告的阶段说明和进度（0 = 没有可数的步数，面板按时间显示）。</summary>
        private volatile string m_NodeSolveDetail;
        private volatile float m_NodeSolveProgress;

        /// <summary>正在后台求解时的阶段说明；空串表示没有在求解。</summary>
        public string nodeSolveStage => m_NodeSolveStage.Length > 0 ? m_NodeSolveDetail ?? m_NodeSolveStage : "";

        /// <summary>后台求解的进度（0 ~ 1）；0 表示没有可数的步数。</summary>
        public float nodeSolveProgress => m_NodeSolveStage.Length > 0 ? m_NodeSolveProgress : 0f;

        /// <summary>这次求解已用的秒数。</summary>
        public float nodeSolveSeconds => m_NodeSolveStage.Length > 0 ? (float)m_NodeSolveClock.Elapsed.TotalSeconds : 0f;

        private bool nodeSolving => m_NodeSolveStage.Length > 0;

        /// <summary>后台线程上还有求解在跑（包括已作废、正在收尾的）：替换模式的模板这时不能同时使用。</summary>
        private static bool NodeWorkerBusy => s_MixedNodeSolveGate.CurrentCount == 0;

        /// <summary>
        /// 在后台线程求解 solve，完成后在主线程调用 complete。之前的求解作废。solve 收到一个报告函数（阶段说明、0 ~ 1 的进度），
        /// 只对这次求解有效（作废后的报告被忽略）。
        /// </summary>
        private void StartNodeJob(string stage, Func<Action<string, float>, RetreatSearchResult> solve, Action<RetreatSearchResult> complete)
        {
            int version = ++m_NodeJobVersion;
            var job = new NodeJob { Version = version, Complete = complete };
            void Report(string detail, float progress)
            {
                if (version != m_NodeJobVersion) return;
                m_NodeSolveDetail = detail;
                m_NodeSolveProgress = progress;
            }
            job.Task = Task.Run(() =>
            {
                s_MixedNodeSolveGate.Wait();
                try { return solve(Report); }
                finally { s_MixedNodeSolveGate.Release(); }
            });
            m_NodeJob = job;
            BeginNodeSolveProgress(stage);
            nodeMessage = "正在后台求解，可继续操作面板；更改端点或参数会使这次结果作废。";
        }

        private void BeginNodeSolveProgress(string stage)
        {
            m_NodeSolveStage = stage;
            m_NodeSolveDetail = null;
            m_NodeSolveProgress = 0f;
            m_NodeSolveClock.Restart();
        }

        private void EndNodeSolveProgress()
        {
            m_NodeSolveStage = "";
            m_NodeSolveDetail = null;
            m_NodeSolveProgress = 0f;
            m_NodeSolveClock.Stop();
        }

        /// <summary>
        /// 按坡度阶梯依次求解（ladder 里第一个能放下的坡度胜出），每次之前报告进度；都放不下时返回第一次的结果（按想要的坡度报原因）。
        /// used 收到实际用的坡度。
        /// </summary>
        private static RetreatSearchResult SolveOnGradeLadder(float[] ladder, Func<float, RetreatSearchResult> solveAt,
            Action<string, float> report, string stage, Action<float> used)
        {
            RetreatSearchResult first = null;
            for (int i = 0; i < ladder.Length; i++)
            {
                if (ladder.Length > 1)
                    report($"{stage}（坡度 {ladder[i] * 100f:F0}%，第 {i + 1}/{ladder.Length} 次）", i / (float)ladder.Length);
                var result = solveAt(ladder[i]);
                if (result.Failure == RetreatSearchFailure.None)
                {
                    used(ladder[i]);
                    return result;
                }
                first ??= result;
            }
            used(ladder[0]);
            return first;
        }

        /// <summary>作废正在进行的后台求解（结果到达后丢弃）。</summary>
        private void InvalidateNodeJob()
        {
            m_NodeJobVersion++;
            m_NodeJob = null;
            EndNodeSolveProgress();
        }

        /// <summary>后台求解完成时在主线程生成预览。返回 true 表示这一帧处理了结果。</summary>
        private bool FinishNodeJob()
        {
            var job = m_NodeJob;
            if (job == null || !job.Task.IsCompleted)
                return false;
            m_NodeJob = null;
            float seconds = nodeSolveSeconds;
            EndNodeSolveProgress();
            if (job.Version != m_NodeJobVersion)
                return false;
            if (job.Task.IsFaulted)
            {
                m_HasPreview = false;
                nodePreviewFailed = true;
                nodeMessage = "后台求解异常，详情已记录到日志；请重新生成预览。";
                Mod.log.Error(job.Task.Exception);
                return true;
            }
            Mod.log.Info($"[autochange] background solve finished in {seconds:F2} s");
            try
            {
                job.Complete(job.Task.Result);
                nodePreviewFailed = !m_HasPreview && !m_MixedNodeSolvePending && !nodeSolving;
            }
            catch (Exception ex)
            {
                m_HasPreview = false;
                nodePreviewFailed = true;
                nodeMessage = "节点预览异常，详情已记录到日志。";
                Mod.log.Error(ex);
            }
            return true;
        }
    }
}
