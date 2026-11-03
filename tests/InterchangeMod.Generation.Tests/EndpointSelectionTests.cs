using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using Xunit;
namespace InterchangeMod.Generation.Tests
{
    public class EndpointSelectionTests
    {
        [Fact] public void GroupsTogglesAndFreezesSnapshots()
        {
            var s = new EndpointSelection(4);
            var p = EndpointFixtures.DualCross()[0].Endpoints;
            Assert.Equal(EndpointSelectionChange.Added,s.Toggle(0,p[0]));
            var saved = s.Snapshot();
            Assert.Equal(EndpointSelectionChange.Added,s.Toggle(0,p[1]));
            Assert.Single(saved[0].Endpoints);
            Assert.Equal(EndpointSelectionChange.Removed,s.Toggle(0,p[0]));
            Assert.Single(s.Snapshot()[0].Endpoints);
            s.ClearArm(0); Assert.Empty(s.Snapshot()[0].Endpoints);
        }
        [Fact] public void RejectsDuplicateEntityAcrossArmsAndCapacity()
        {
            var s = new EndpointSelection(3);
            var p = EndpointFixtures.DualCross();
            s.Toggle(0,p[0].Endpoints[0]); s.Toggle(0,p[0].Endpoints[1]);
            Assert.Equal(EndpointSelectionChange.ArmFull,s.Toggle(0,p[1].Endpoints[0]));
            Assert.Equal(EndpointSelectionChange.AlreadySelected,s.Toggle(1,p[0].Endpoints[0]));
            Assert.Equal(2,s.Snapshot().Sum(a=>a.Endpoints.Count));
            Assert.Throws<ArgumentOutOfRangeException>(()=>s.Toggle(3,p[1].Endpoints[0]));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new EndpointSelection(2));
        }
    }
}
