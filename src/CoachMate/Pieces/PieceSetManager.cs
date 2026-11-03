namespace CoachMate.Pieces;

/// <summary>
/// Discovers and loads piece sets. A set = a folder with 12 PNGs named
/// wp/wn/wb/wr/wq/wk/bp/bn/bb/br/bq/bk.png.
/// Bundled sets live in Assets/pieces; imported sets in %LOCALAPPDATA%/CoachMate/pieces.
/// </summary>
public sealed class PieceSetManager
{
    public static readonly string[] PieceKeys =
        { "wp", "wn", "wb", "wr", "wq", "wk", "bp", "bn", "bb", "br", "bq", "bk" };

    public string BundledDir { get; }
    public string UserDir { get; }

    public PieceSetManager(string? bundledDir = null, string? userDir = null)
    {
        BundledDir = bundledDir ?? Path.Combine(AppContext.BaseDirectory, "Assets", "pieces");
        UserDir = userDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoachMate", "pieces");
    }

    private static bool IsValidSet(string dir) =>
        PieceKeys.All(k => File.Exists(Path.Combine(dir, k + ".png")));

    /// <summary>Names of all valid sets (bundled + imported).</summary>
    public List<string> ListSets()
    {
        var sets = new List<string>();
        foreach (var root in new[] { BundledDir, UserDir })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var d in Directory.GetDirectories(root))
                if (IsValidSet(d)) sets.Add(Path.GetFileName(d));
        }
        return sets;
    }

    /// <summary>key -> png path for a named set (bundled first, then user).</summary>
    public Dictionary<string, string> SetPaths(string name)
    {
        foreach (var root in new[] { BundledDir, UserDir })
        {
            var d = Path.Combine(root, name);
            if (IsValidSet(d))
                return PieceKeys.ToDictionary(k => k, k => Path.Combine(d, k + ".png"));
        }
        throw new DirectoryNotFoundException($"piece set '{name}' not found or incomplete");
    }

    /// <summary>Copies a folder of 12 named PNGs into the user sets dir. Returns set name.</summary>
    public string ImportSet(string sourceFolder)
    {
        var name = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("invalid folder name");
        var dest = Path.Combine(UserDir, name);
        Directory.CreateDirectory(dest);
        foreach (var k in PieceKeys)
        {
            var src = Path.Combine(sourceFolder, k + ".png");
            if (!File.Exists(src))
                throw new FileNotFoundException($"missing {k}.png — a set needs all 12 named PNGs");
            File.Copy(src, Path.Combine(dest, k + ".png"), true);
        }
        return name;
    }
}
