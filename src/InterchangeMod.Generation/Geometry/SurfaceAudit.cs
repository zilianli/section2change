using System;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>Water clearance and terrain elevation are independent quantities.
    /// This is a sampled diagnostic, not a continuous clearance guarantee.</summary>
    public sealed class SurfaceAudit
    {
        public int WetSamples { get; private set; }
        public int DrySamples { get; private set; }
        public int UnderWaterSamples { get; private set; }
        public float MinWaterClearance { get; private set; } = float.PositiveInfinity;
        public float MinTerrainElevation { get; private set; } = float.PositiveInfinity;
        public float MaxTerrainElevation { get; private set; } = float.NegativeInfinity;
        public bool Mixed => WetSamples > 0 && DrySamples > 0;

        public void Add(float roadHeight, float terrainHeight, float waterDepth, float minimumWaterElevation)
        {
            float elevation = roadHeight - terrainHeight;
            MinTerrainElevation = Math.Min(MinTerrainElevation, elevation);
            MaxTerrainElevation = Math.Max(MaxTerrainElevation, elevation);
            if (waterDepth <= 0.05f) { DrySamples++; return; }
            WetSamples++;
            float clearance = elevation - waterDepth;
            MinWaterClearance = Math.Min(MinWaterClearance, clearance);
            if (clearance < minimumWaterElevation - 0.01f) UnderWaterSamples++;
        }
    }
}
