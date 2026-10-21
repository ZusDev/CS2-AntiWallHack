using System.Numerics;

namespace AntiWallHack;

internal static class CornerPrediction
{
    public static Vector3 Lead(Vector3 velocity, float yaw, ulong buttons, float seconds, float maximum)
    {
        if (!Finite(velocity) || !float.IsFinite(yaw) || !float.IsFinite(seconds) || !float.IsFinite(maximum) || seconds <= 0 || maximum <= 0)
        {
            return Vector3.Zero;
        }

        seconds = Math.Min(seconds, 0.2f);
        maximum = Math.Min(maximum, 64);
        Vector3 lead = velocity * seconds;

        int forward = ((buttons & 8) != 0 ? 1 : 0) - ((buttons & 16) != 0 ? 1 : 0);
        int side = ((buttons & 0x400) != 0 ? 1 : 0) - ((buttons & 0x200) != 0 ? 1 : 0);

        if (forward != 0 || side != 0)
        {
            float angle = yaw * (MathF.PI / 180);
            Vector2 direction = Vector2.Normalize(new Vector2(MathF.Cos(angle) * forward + MathF.Sin(angle) * side, MathF.Sin(angle) * forward - MathF.Cos(angle) * side));
            float distance = Math.Min(maximum, Math.Max(Math.Min(16, maximum), new Vector2(lead.X, lead.Y).Length()));

            lead.X = direction.X * distance;
            lead.Y = direction.Y * distance;
        }

        float length = lead.Length();

        if (!float.IsFinite(length))
        {
            return Vector3.Zero;
        }

        if (length > maximum)
        {
            lead *= maximum / length;
        }

        return lead;
    }

    public static float ClipScale(float distance, float fraction, bool solid)
    {
        if (!float.IsFinite(distance) || distance <= 0 || solid || !float.IsFinite(fraction) || fraction < 0 || fraction > 1)
        {
            return 0;
        }
        
        return fraction == 1 ? 1 : Math.Max(0, fraction - 2 / distance);
    }

    public static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
