using System;
using System.Globalization;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>
    /// 最小三维向量。netstandard2.0 不含 System.Numerics.Vector3，
    /// 引入 NuGet 包又可能与游戏自带的 System.Numerics.dll 冲突，所以自己写一个。
    /// 坐标约定同 Unity：Y 向上。落地层把它转换成 Unity.Mathematics.float3。
    /// </summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public static Vec3 Zero => new Vec3(0, 0, 0);
        public static Vec3 UnitX => new Vec3(1, 0, 0);
        public static Vec3 UnitZ => new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;

        public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length();

        public static Vec3 Normalize(Vec3 v) => v * (1f / v.Length());

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        /// <summary>
        /// 绕 Y 轴旋转 yaw 弧度，方向与 Unity 的 Quaternion.Euler(0, yaw°, 0) 一致：
        /// yaw = 90° 时 +Z 转到 +X。
        /// </summary>
        public Vec3 RotateY(float yaw)
        {
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            return new Vec3(X * c + Z * s, Y, -X * s + Z * c);
        }

        public bool Equals(Vec3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Vec3 o && Equals(o);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
    }
}
