namespace InterchangeMod.Generation.Geometry
{
    /// <summary>PlaceableNetData limits are tool offsets, not maximum bridge-pillar depth.
    /// Mirrors CourseSplitSystem.LimitElevation for an already positioned world course:
    /// only nonnegative lower bounds and negative upper bounds constrain actual elevation.</summary>
    public static class PhysicalElevation
    {
        public static bool Allowed(float min, float max, float lower, float upper) =>
            (lower < 0f || min >= lower - 0.01f) && (upper >= 0f || max <= upper + 0.01f);
    }
}
