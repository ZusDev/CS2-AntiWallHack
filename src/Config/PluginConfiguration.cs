namespace AntiWallHack;

public sealed partial class Plugin
{
    private void LoadConfig()
    {
        string path = Core.Configuration.GetConfigPath("config.jsonc");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Config.DefaultText + Environment.NewLine);
        }

        config = Config.Parse(File.ReadAllText(path));
    }
}
