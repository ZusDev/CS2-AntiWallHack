using System.Text.Json;

namespace AntiWallHack;

public sealed class Config
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; } = false;
    public bool SmokeOcclusion { get; set; } = true;
    public int VisibleGraceTicks { get; set; } = 20;
    public float BoundsPadding { get; set; } = 28;
    public bool CornerPrediction { get; set; } = true;
    public float CornerPredictionSeconds { get; set; } = 0.1f;
    public float CornerMaxDistance { get; set; } = 32;

    internal static bool CanFilterWithConvar(string? value)
    {
        if (value == null)
        {
            return true;
        }

        value = value.Trim();

        return value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    internal static Config Parse(string text)
    {
        var value = JsonSerializer.Deserialize<Config>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException("config.jsonc cannot be null.");

        if (value.VisibleGraceTicks is < 0 or > 640 || !float.IsFinite(value.BoundsPadding) || value.BoundsPadding is < 0 or > 256 || !float.IsFinite(value.CornerPredictionSeconds) || value.CornerPredictionSeconds is < 0 or > 0.2f || !float.IsFinite(value.CornerMaxDistance) || value.CornerMaxDistance is < 0 or > 64)
        {
            throw new InvalidDataException("Config bounds: VisibleGraceTicks 0..640; BoundsPadding 0..256; CornerPredictionSeconds 0..0.2; CornerMaxDistance 0..64.");
        }
        
        return value;
    }

    internal const string DefaultText = """
    {
      "Enabled": true,
      "Debug": false,
      "SmokeOcclusion": true,
      "VisibleGraceTicks": 20,
      "BoundsPadding": 28,
      "CornerPrediction": true,
      "CornerPredictionSeconds": 0.1,
      "CornerMaxDistance": 32
    }
    """;
}

