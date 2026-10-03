using System.Text.Json;

namespace AntiWallHack;

public sealed partial class Plugin
{
    private void LoadConfig()
    {
        string path = Core.Configuration.GetConfigPath("config.jsonc");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        if (File.Exists(path))
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("config.jsonc cannot be null.");
        }
        else
        {
            File.WriteAllText(path, JsonSerializer.Serialize(config, options));
        }

    }

}


