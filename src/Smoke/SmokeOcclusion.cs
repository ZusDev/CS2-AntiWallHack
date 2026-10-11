using System.Numerics;

namespace AntiWallHack;

internal sealed class SmokeVolume
{
    public Vector3 Center;
    public float Age;
    public float StartTime;
    public readonly byte[] Opaque = new byte[4096];
    public readonly float[] Density = new float[32768];
}

internal static class SmokeOcclusion
{
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static int Morton(int x, int y, int z)
    {
        int result = 0;

        for (int bit = 0; bit < 5; bit++)
            result |= ((x >> bit) & 1) << (bit * 3) | ((y >> bit) & 1) << (bit * 3 + 1) | ((z >> bit) & 1) << (bit * 3 + 2);

        return result;
    }

    private static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    public static float AgeScale(float age) => Smooth((age - 0.6f) / 1.4f) * Smooth((21.5f - age) / 5);

    public static bool ClearedByExplosion(SmokeVolume volume, Vector3 center, float time, float now) => time >= volume.StartTime && now >= time && now - time <= 3 && Vector3.DistanceSquared(center, Vector3.Clamp(center, volume.Center - new Vector3(320), volume.Center + new Vector3(320))) <= 10000;

    public static bool Clip(Vector3 center, Vector3 origin, Vector3 direction, out float entry, out float exit)
    {
        entry = 0; exit = 1;

        for (int axis = 0; axis < 3; axis++)
        {
            float lo = center[axis] - 320, hi = center[axis] + 320;
            float d = direction[axis], p = origin[axis];

            if (MathF.Abs(d) < 1e-7f) { if (p < lo || p > hi) return false; continue; }
            float a = (lo - p) / d, b = (hi - p) / d;

            entry = MathF.Max(entry, MathF.Min(a, b));
            exit = MathF.Min(exit, MathF.Max(a, b));

            if (entry >= exit) 
                return false;
        }

        return true;
    }

    // Returns an approximate density contribution, not a renderer opacity value.
    public static float Contribution(SmokeVolume volume, Vector3 origin, Vector3 target, float advance)
    {
        var direction = target - origin;

        if (!Finite(origin) || !Finite(target) || !Finite(direction) || !float.IsFinite(advance)) return 0;

        float scale = AgeScale(volume.Age + MathF.Max(advance, 0));

        if (scale <= 0 || direction.LengthSquared() < 1e-8f || !Clip(volume.Center, origin, direction, out float entry, out float exit)) return 0;

        var minimum = volume.Center - new Vector3(320);

        float t = entry;

        if (entry > 0) t += MathF.Min(1 / direction.Length(), exit - entry);

        var start = origin + direction * t;

        Span<int> cell = stackalloc int[3];
        Span<int> step = stackalloc int[3];
        Span<float> next = stackalloc float[3];
        Span<float> delta = stackalloc float[3];

        for (int axis = 0; axis < 3; axis++)
        {
            cell[axis] = Math.Clamp((int)MathF.Floor((start[axis] - minimum[axis]) / 20), 0, 31);

            float d = direction[axis];

            step[axis] = Math.Sign(d);
            next[axis] = d == 0 ? float.PositiveInfinity : t + (minimum[axis] + (cell[axis] + (d > 0 ? 1 : 0)) * 20 - start[axis]) / d;
            delta[axis] = d == 0 ? float.PositiveInfinity : 20 / MathF.Abs(d);
        }

        float accumulated = 0;

        for (int visited = 0; visited < 128; visited++)
        {
            if ((uint)cell[0] >= 32 || (uint)cell[1] >= 32 || (uint)cell[2] >= 32) break;

            int index = Morton(cell[0], cell[1], cell[2]);

            if ((volume.Opaque[index >> 3] & (1 << (index & 7))) != 0) 
                return scale;

            float density = Math.Clamp(volume.Density[index] / 50, 0, 1);

            if (density >= 0.8f) 
                return scale;

            if (density > 0.1f)
            {
                accumulated += density;

                if (accumulated >= 0.2f) 
                    return scale;
            }

            float crossing = MathF.Min(next[0], MathF.Min(next[1], next[2]));

            if (crossing >= exit) 
                break;
                
            // Advance tied axes together, avoiding cells touched only at an edge.
            for (int axis = 0; axis < 3; axis++)
                if (next[axis] == crossing) { cell[axis] += step[axis]; next[axis] += delta[axis]; }
        }
        
        return accumulated * scale;
    }
}
