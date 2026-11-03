using System.Reflection;

namespace CoachMate.Puzzles;

/// <summary>Bundled curated puzzles; full lichess set loads from %LOCALAPPDATA% after download.</summary>
public sealed class PuzzleStore
{
    public List<Puzzle> Puzzles { get; } = new();
    private readonly Random _rng = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoachMate", "puzzles");
    public static string FullPath => Path.Combine(Dir, "puzzles-full.txt");
    public static bool FullDbAvailable => File.Exists(FullPath);

    public void Load()
    {
        Puzzles.Clear();
        if (FullDbAvailable)
            foreach (var l in File.ReadLines(FullPath)) Puzzles.Add(Puzzle.Parse(l));
        else
        {
            var asm = Assembly.GetExecutingAssembly();
            using var res = asm.GetManifestResourceStream("CoachMate.puzzles.csv")
                ?? throw new InvalidOperationException("embedded puzzles.csv not found");
            using var r = new StreamReader(res);
            while (r.ReadLine() is { } l)
                if (l.Length > 0 && !l.StartsWith("PuzzleId")) Puzzles.Add(Puzzle.Parse(l));
        }
    }

    public Puzzle Random(string? themeFilter = null)
    {
        var pool = themeFilter is null ? Puzzles
            : Puzzles.Where(p => p.Themes.Contains(themeFilter)).ToList();
        return pool[_rng.Next(pool.Count)];
    }
}
