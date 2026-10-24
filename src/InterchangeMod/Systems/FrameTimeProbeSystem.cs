using Game;

namespace InterchangeMod.Systems
{
    /// <summary>
    /// 诊断用：某个操作之后观察接下来若干帧的帧时间，把最慢的一帧写进 mod 日志。
    /// 用来判断卡顿是我们的代码造成的，还是游戏切换工具本身的开销。
    /// </summary>
    public partial class FrameTimeProbeSystem : GameSystemBase
    {
        private const int kFramesToWatch = 10;

        private string m_Label;
        private int m_FramesLeft;
        private int m_Frame;
        private int m_SlowestFrame;
        private float m_SlowestMs;
        private float m_TotalMs;

        public void Watch(string label)
        {
            m_Label = label;
            m_FramesLeft = kFramesToWatch;
            m_Frame = 0;
            m_SlowestFrame = 0;
            m_SlowestMs = 0f;
            m_TotalMs = 0f;
        }

        protected override void OnUpdate()
        {
            if (m_FramesLeft <= 0)
                return;

            // 系统内的 Time 指 ECS 的属性，这里要的是 Unity 的真实帧时间
            float ms = UnityEngine.Time.unscaledDeltaTime * 1000f;
            m_Frame++;
            m_TotalMs += ms;
            if (ms > m_SlowestMs)
            {
                m_SlowestMs = ms;
                m_SlowestFrame = m_Frame;
            }

            if (--m_FramesLeft == 0)
                Mod.log.Info($"[perf] {m_Label}: slowest frame {m_SlowestMs:F0} ms (frame +{m_SlowestFrame}), average {m_TotalMs / m_Frame:F0} ms over {m_Frame} frames");
        }
    }
}
