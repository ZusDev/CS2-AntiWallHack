namespace AntiWallHack;

internal enum DirectVisibility : byte
{
    Unchecked,
    Hidden,
    Visible,
    Unknown,
    Held
}

internal sealed class PairVisibility
{
    private readonly DirectVisibility[,] values = new DirectVisibility[64, 64];
    private readonly long[,] deadlines = new long[64, 64];

    public void BeginTick() => Array.Clear(values);

    public void Reset()
    {
        Array.Clear(values);
        Array.Clear(deadlines);
    }

    public void Invalidate(int slot, int tick, int grace)
    {
        for (int other = 0; other < 64; other++)
        {
            values[slot, other] = values[other, slot] = DirectVisibility.Unchecked;
            deadlines[slot, other] = deadlines[other, slot] = (long)tick + grace;
        }
    }

    public void Evaluate(int viewer, int target, int tick, int grace, Func<DirectVisibility> query)
    {
        if (NeedsQuery(viewer, target, tick))
        {
            CompleteQuery(viewer, target, tick, grace, query());
        }
    }

    public bool NeedsQuery(int viewer, int target, int tick)
    {
        if (values[viewer, target] != DirectVisibility.Unchecked)
        {
            return false;
        }

        if (tick <= deadlines[viewer, target])
        {
            values[viewer, target] = DirectVisibility.Held;
            return false;
        }

        return true;
    }

    public void CompleteQuery(int viewer, int target, int tick, int grace, DirectVisibility value)
    {
        values[viewer, target] = value;

        if (value == DirectVisibility.Visible || value == DirectVisibility.Unknown)
        {
            deadlines[viewer, target] = Math.Max(deadlines[viewer, target], (long)tick + grace);
        }

        if (value == DirectVisibility.Visible)
        {
            deadlines[target, viewer] = Math.Max(deadlines[target, viewer], (long)tick + grace);
        }
    }

    public bool Hidden(int viewer, int target, int tick) => values[viewer, target] == DirectVisibility.Hidden && tick > deadlines[viewer, target];
}
