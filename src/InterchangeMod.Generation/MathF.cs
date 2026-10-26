using System;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// netstandard2.0 没有 System.MathF，这里补一个只含用到的方法的同名版本，
    /// 让几何代码保持和 netstandard2.1 写法一致。
    /// </summary>
    internal static class MathF
    {
        public const float PI = (float)Math.PI;

        public static float Abs(float x) => Math.Abs(x);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Ceiling(float x) => (float)Math.Ceiling(x);
    }
}
