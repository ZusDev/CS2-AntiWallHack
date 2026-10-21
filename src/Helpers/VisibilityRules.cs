namespace AntiWallHack;

internal static class VisibilityRules
{
    // Move one sample to the front without repeating or dropping any others.
    public static int OrderedSample(int order, int preferred) => order == 0 ? preferred : order <= preferred ? order - 1 : order;

    public static bool IsOccluded(float fraction, bool startSolid) => !startSolid && float.IsFinite(fraction) && fraction > 0 && fraction < 0.99f;
}
