namespace AntiWallHack;

internal static class VisibilityRules
{
    // Insertion into a small sorted buffer avoids sorting the entire server roster.
    public static void InsertNearest(Span<int> slots, Span<float> distances, ref int count, int slot, float distance)
    {
        if (!float.IsFinite(distance) || distance < 0)
            return;

        int position = 0;

        while (position < count && (distances[position] < distance || (distances[position] == distance && slots[position] < slot)))
            position++;

        if (position >= slots.Length)
            return;

        for (int i = Math.Min(count, slots.Length - 1); i > position; i--)
        {
            slots[i] = slots[i - 1];
            distances[i] = distances[i - 1];
        }

        slots[position] = slot;
        distances[position] = distance;
        count = Math.Min(count + 1, slots.Length);
    }

    public static bool CanHide(int tick, int visibleUntil) => tick > visibleUntil;
    public static bool IsOccluded(float fraction, bool startSolid) => !startSolid && float.IsFinite(fraction) && fraction > 0 && fraction < 0.99f;
}
