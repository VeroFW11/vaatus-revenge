namespace VaatusRevenge
{
    // The core (pure C#) uses System.Numerics vectors so it can run without Unity.
    // These convert at the boundary between core and Unity code.
    public static class NumericsExtensions
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v)
        {
            return new UnityEngine.Vector3(v.X, v.Y, v.Z);
        }

        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v)
        {
            return new System.Numerics.Vector3(v.x, v.y, v.z);
        }

        public static UnityEngine.Vector2 ToUnity(this System.Numerics.Vector2 v)
        {
            return new UnityEngine.Vector2(v.X, v.Y);
        }

        public static System.Numerics.Vector2 ToNumerics(this UnityEngine.Vector2 v)
        {
            return new System.Numerics.Vector2(v.x, v.y);
        }
    }
}
