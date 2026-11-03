using System;
using System.Collections.Generic;
using System.Linq;
namespace InterchangeMod.Generation.Endpoints
{
    public enum EndpointSelectionChange { Added, Removed, AlreadySelected, ArmFull }
    public sealed class EndpointSelection
    {
        private readonly List<SelectedEndpoint>[] arms;
        public EndpointSelection(int armCount)
        {
            if (armCount != 3 && armCount != 4) throw new ArgumentOutOfRangeException(nameof(armCount));
            arms = Enumerable.Range(0,armCount).Select(_=>new List<SelectedEndpoint>()).ToArray();
        }
        private void Check(int arm) { if (arm < 0 || arm >= arms.Length) throw new ArgumentOutOfRangeException(nameof(arm)); }
        public EndpointSelectionChange Toggle(int arm, SelectedEndpoint port)
        {
            Check(arm); if (port == null) throw new ArgumentNullException(nameof(port));
            int local = arms[arm].FindIndex(p=>p.NodeIndex == port.NodeIndex && p.NodeVersion == port.NodeVersion);
            if (local >= 0) { arms[arm].RemoveAt(local); return EndpointSelectionChange.Removed; }
            if (arms.Any(a=>a.Any(p=>p.NodeIndex == port.NodeIndex))) return EndpointSelectionChange.AlreadySelected;
            if (arms[arm].Count == 2) return EndpointSelectionChange.ArmFull;
            arms[arm].Add(port); return EndpointSelectionChange.Added;
        }
        public void ClearArm(int arm) { Check(arm); arms[arm].Clear(); }
        public IReadOnlyList<EndpointArm> Snapshot() => Array.AsReadOnly(arms.Select((a,i)=>new EndpointArm(i,a)).ToArray());
    }
}
