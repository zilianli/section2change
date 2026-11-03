using System.Linq;
using InterchangeMod.Generation.Endpoints;
using Xunit;
namespace InterchangeMod.Generation.Tests
{
    public class NodeFeedbackTests
    {
        [Fact] public void RejectedSelectionsMustNotInvalidateAnExistingPreview()
        {
            Assert.True(NodeFeedback.SelectionChanged(EndpointSelectionChange.Added));
            Assert.True(NodeFeedback.SelectionChanged(EndpointSelectionChange.Removed));
            Assert.False(NodeFeedback.SelectionChanged(EndpointSelectionChange.AlreadySelected));
            Assert.False(NodeFeedback.SelectionChanged(EndpointSelectionChange.ArmFull));
        }
        [Fact] public void RequestsAutomaticPreviewOnlyForCompleteValidSelections()
        {
            var arms=EndpointFixtures.DualCross();
            Assert.True(NodeFeedback.ReadyForAutoPreview(InterchangeType.Cloverleaf,arms));
            var partial=arms.Select(a=>new EndpointArm(a.Index,a.Index==3?a.Endpoints.Take(1):a.Endpoints)).ToArray();
            Assert.False(NodeFeedback.ReadyForAutoPreview(InterchangeType.Cloverleaf,partial));
            Assert.False(NodeFeedback.ReadyForAutoPreview(InterchangeType.Trumpet,arms));
        }
        [Fact] public void SelectedStateWinsOverHoverAndKeepsUserGroupNumber()
        {
            var arms=EndpointFixtures.DualCross();
            var p=arms[2].Endpoints[0];
            var selected=NodeFeedback.Style(p.NodeIndex,p.NodeVersion,true,arms);
            Assert.Equal(NodeMarkerKind.Selected,selected.Kind); Assert.Equal(3,selected.GroupNumber);
            Assert.Equal(NodeMarkerKind.Hovered,NodeFeedback.Style(999,1,true,arms).Kind);
            Assert.Equal(NodeMarkerKind.Available,NodeFeedback.Style(999,1,false,arms).Kind);
            Assert.Equal(NodeMarkerKind.Available,NodeFeedback.Style(p.NodeIndex,p.NodeVersion+1,false,arms).Kind);
        }
    }
}
