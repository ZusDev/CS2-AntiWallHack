namespace AntiWallHack;

public sealed class Config
{
    internal static bool CanFilterWithConvar(string? value)
    {
        // A missing convar is optional, a present, enabled value pauses filtering.
        if (value == null)
            return true;

        value = value.Trim();
        
        return value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    public bool Enabled { get; set; } = true;
    public int NearestEnemies { get; set; } = 5;
    public int VisibleGraceTicks { get; set; } = 20;
    public float BoundsPadding { get; set; } = 48;
    public float PredictionSeconds { get; set; } = 0.2f;
    public float AlwaysVisibleDistance { get; set; } = 120;
    public bool SetDontTransmitToZero { get; set; } = true;
}


