using UnityEngine;

namespace Aelid.Environment
{
    /// <summary>
    /// Global ring buffer of water ripples (position, start time, strength),
    /// published to all water shaders as _WaterRipples / _WaterTime.
    /// Anything can emit: wading characters now, rain drops and spells later.
    /// </summary>
    public static class WaterRipples
    {
        public const int Capacity = 16;

        static readonly Vector4[] s_Ripples = new Vector4[Capacity];
        static int s_Next;

        static readonly int k_Ripples = Shader.PropertyToID("_WaterRipples");
        static readonly int k_Time = Shader.PropertyToID("_WaterTime");

        /// <summary>Freezes water time (captures, tests). Null = live time.</summary>
        public static float? TimeOverride;

        /// <summary>Clock shared by ripples and wave animation.</summary>
        public static float Now => TimeOverride ?? (Application.isPlaying ? Time.time : Time.realtimeSinceStartup);

        /// <param name="position">World position on the water surface.</param>
        /// <param name="strength">1 = a footstep.</param>
        /// <param name="age">Seconds the ripple has already been spreading.</param>
        public static void Emit(Vector3 position, float strength, float age = 0f)
        {
            s_Ripples[s_Next] = new Vector4(position.x, position.z, Now - age, Mathf.Max(0f, strength));
            s_Next = (s_Next + 1) % Capacity;
        }

        public static void Clear()
        {
            for (int i = 0; i < Capacity; i++)
                s_Ripples[i] = Vector4.zero;
            s_Next = 0;
        }

        /// <summary>Number of ripples still spreading after <paramref name="lifetime"/> seconds.</summary>
        public static int CountActive(float lifetime)
        {
            int count = 0;
            float now = Now;
            foreach (var r in s_Ripples)
                if (r.w > 0f && now - r.z >= 0f && now - r.z <= lifetime)
                    count++;
            return count;
        }

        public static void Publish()
        {
            Shader.SetGlobalVectorArray(k_Ripples, s_Ripples);
            Shader.SetGlobalFloat(k_Time, Now);
        }
    }
}
