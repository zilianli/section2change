namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Only a checked, current preview revision can be consumed, and only once.</summary>
    public sealed class NodePlacementGate
    {
        public int Revision { get; private set; }
        public bool HasPreview { get; private set; }
        public bool NativeChecked { get; private set; }
        public bool NativeAllowed { get; private set; }
        private string? m_LastBatch;
        private int m_StableBatchFrames;
        public void Invalidate()
        {
            Revision++;
            HasPreview = NativeChecked = NativeAllowed = false;
            m_LastBatch=null;m_StableBatchFrames=0;
        }
        public void Stage() { Invalidate(); HasPreview = true; }
        /// <summary>A late first Temp batch or changed native geometry must wait a full subsequent observation.</summary>
        public bool ObserveBatch(string? fingerprint)
        {
            if(!HasPreview||string.IsNullOrEmpty(fingerprint))
            {
                m_LastBatch=null;m_StableBatchFrames=0;ObserveNative(false,false);return false;
            }
            m_StableBatchFrames=fingerprint==m_LastBatch?m_StableBatchFrames+1:1;
            m_LastBatch=fingerprint;
            bool stable=m_StableBatchFrames>=2;
            if(!stable)ObserveNative(false,false);
            return stable;
        }
        public void ObserveNative(bool ready, bool allowed)
        {
            NativeChecked = HasPreview && ready;
            NativeAllowed = NativeChecked && allowed;
        }
        public bool CanConfirm(bool currentContext) => HasPreview && NativeChecked && NativeAllowed && currentContext;
        /// <summary>The UI must send the revision that was actually displayed, never infer a newer backend revision.</summary>
        public bool CanQueue(int displayedRevision,bool currentContext) => displayedRevision==Revision&&CanConfirm(currentContext);
        public bool TryConsume(int requestedRevision, bool currentContext)
        {
            if (!CanQueue(requestedRevision,currentContext)) return false;
            Invalidate();
            return true;
        }
        public static bool Supports(InterchangeType type) => type == InterchangeType.Cloverleaf
            || type == InterchangeType.Stack || type == InterchangeType.Turbine || type == InterchangeType.Trumpet
            || type == InterchangeType.DirectionalT || type == InterchangeType.SemiDirectionalT || type == InterchangeType.Wye
            || type == InterchangeType.CompactGrid || type == InterchangeType.Roundabout;
    }
}
