namespace AntiWallHack;

internal static unsafe class TransmitInfo
{
    internal const int PlayerSlotOffset = 0x240;
    internal const int FullUpdateOffset = 0x244;

    public static bool TryRead(nint info, out int slot, out nint primary, out nint suppressed)
    {
        slot = -1;
        primary = suppressed = 0;
        if (info == 0 || *((byte*)info + FullUpdateOffset) != 0)
            return false;

        slot = *(int*)((byte*)info + PlayerSlotOffset);
        if (slot is < 0 or >= 64)
            return false;

        primary = *(nint*)info;
        suppressed = *(nint*)((byte*)info + 8);
        return primary != 0 && suppressed != 0 && primary != suppressed;
    }
}
