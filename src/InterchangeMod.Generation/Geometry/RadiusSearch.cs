using System;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>
    /// 自动半径：在 [min, max] 里按 step 一档档找能用的最小半径（如喇叭形、苜蓿叶的环道）。
    /// 能不能用由调用方判断（放得下、坡度不超限等），随半径增大不一定单调，所以逐档试而不是二分。
    /// </summary>
    public static class RadiusSearch
    {
        /// <returns>最小的可用半径；整个范围都不行时返回 null。</returns>
        public static float? Smallest(Func<float, bool> fits, float min, float max, float step = 5f)
        {
            for (float radius = min; radius <= max + 1e-3f; radius += step)
            {
                if (fits(radius))
                    return radius;
            }
            return null;
        }
    }
}
