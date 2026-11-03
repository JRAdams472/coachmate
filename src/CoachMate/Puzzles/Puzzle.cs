namespace CoachMate.Puzzles;

/// <summary>One puzzle: lichess format (id, fen, uci moves, rating, themes).</summary>
public sealed record Puzzle(string Id, string Fen, string[] Moves, int Rating, string[] Themes, bool EngineStarts)
{
    /// <summary>The move the user must find next (alternating user/engine replies).</summary>
    public string SolutionAt(int ply) => Moves[ply];
    public bool IsUserToMove(int ply) => EngineStarts ? ply % 2 == 1 : ply % 2 == 0;

    public static Puzzle Parse(string line)
    {
        var f = line.Split(',');
        return new Puzzle(f[0], f[1], f[2].Split(' '), int.Parse(f[3]), f[7].Split(' '),
            f.Length > 8 && f[8] == "e");
    }
}
