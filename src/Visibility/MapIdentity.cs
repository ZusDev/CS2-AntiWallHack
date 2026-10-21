namespace AntiWallHack;

internal static class MapIdentity
{
    public static bool Token(string value) => value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static string Resolve(string map, string addon = "")
    {
        if (map.StartsWith("workshop/", StringComparison.Ordinal))
        {
            string[] parts = map.Split('/');

            if (parts.Length != 3 || (addon.Length != 0 && addon != parts[1]))
            {
                return "";
            }

            addon = parts[1];
            map = parts[2];
        }

        if (!Token(map) || (addon.Length != 0 && !Token(addon)))
        {
            return "";
        }

        return addon.Length == 0 ? map : $"workshop/{addon}/{map}";
    }

    public static string Archive(string root, string key, CancellationToken cancellation = default)
    {
        if (key.Length == 0 || Resolve(key) != key)
        {
            throw new InvalidDataException("Invalid map identity");
        }

        root = Path.GetFullPath(root);

        if (Token(key))
        {
            return Path.Combine(root, "maps", key + ".vpk");
        }

        string addon = key.Split('/')[1];
        var folders = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(root, "maps", "workshop", addon) };
        string? current = root;

        for (int depth = 0; depth < 5 && current != null; depth++)
        {
            cancellation.ThrowIfCancellationRequested();
            folders.Add(Path.Combine(current, "csgo_addons", addon));
            folders.Add(Path.Combine(current, "csgo_community_addons", addon));
            folders.Add(Path.Combine(current, "steamapps", "workshop", "content", "730", addon));

            if (Path.GetFileName(current) == "steamapps")
            {
                folders.Add(Path.Combine(current, "workshop", "content", "730", addon));
            }

            string bin = Path.Combine(current, "bin");

            if (Directory.Exists(bin))
            {
                foreach (string platform in Directory.EnumerateDirectories(bin))
                {
                    folders.Add(Path.Combine(platform, "steamapps", "workshop", "content", "730", addon));
                }
            }

            current = Path.GetDirectoryName(current);
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (string folder in folders)
        {
            cancellation.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(folder, "*.vpk", SearchOption.AllDirectories))
            {
                cancellation.ThrowIfCancellationRequested();
                string stem = Path.GetFileNameWithoutExtension(file);

                if (stem.Length >= 4 && stem[^4] == '_' && stem[^3..].All(char.IsAsciiDigit))
                {
                    continue;
                }

                candidates.Add(Path.GetFullPath(file));
            }
        }
        
        return candidates.Count == 1 ? candidates.Single() : throw new InvalidDataException("Workshop archive missing or ambiguous for " + key);
    }
}
