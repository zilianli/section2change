using System.Collections.Generic;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointRoundaboutSearch
    {
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,RoadInfo ring,float maxGrade,
            float maxRetreat,CloverleafRoadDesign design,int maxCandidates=128,float verticalClearance=8.5f,float? terrainHeight=null,bool racetrack=false)
            =>EndpointRoundaboutFitter.Search(arms,traces,rightHandTraffic,mainHeight,crossHeight,ramp,ring,maxGrade,
                maxRetreat,design,maxCandidates,verticalClearance,terrainHeight,racetrack);
    }
}
