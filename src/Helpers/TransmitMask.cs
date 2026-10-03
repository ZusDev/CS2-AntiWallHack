namespace AntiWallHack;

internal static unsafe class TransmitMask
{
    public const int EntityLimit = 16384;

    public static bool Withhold(uint* primary, uint* dontTransmit, int index)
    {
        if (primary == null || dontTransmit == null || primary == dontTransmit || index <= 0 || index >= EntityLimit)
            return false;

        int word = index >> 5;
        uint bit = 1u << (index & 31);

        if ((primary[word] & bit) == 0)
            return false;

        // Ordering is intentional: register suppression before removing transmission.
        dontTransmit[word] |= bit;
        primary[word] &= ~bit;
        return true;
    }
}
