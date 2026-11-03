using CoachMate.Engine;

namespace CoachMate.Coach;

public enum Grade { Best, Good, Inaccuracy, Mistake, Blunder }

public static class MoveGrader
{
    /// <summary>cp-loss thresholds (lichess-style).</summary>
    public static Grade Classify(int lossCp) => lossCp switch
    {
        <= 0 => Grade.Best,
        < 50 => Grade.Good,
        < 120 => Grade.Inaccuracy,
        < 300 => Grade.Mistake,
        _ => Grade.Blunder
    };

    public static string Symbol(Grade g) => g switch
    {
        Grade.Best => "!", Grade.Good => "✓", Grade.Inaccuracy => "?!",
        Grade.Mistake => "?", _ => "??"
    };

    /// <summary>
    /// Loss of the played move: if it appears in the pre-move top-N lines, grade it
    /// against that same search (no cross-search noise); else compare to post-move eval.
    /// </summary>
    public static int LossCp(string uciMove, SearchResult? pre, SearchResult? post)
    {
        if (pre?.Best is null || post?.Best is null) return 0;
        var hit = pre.Pvs.Values.FirstOrDefault(v => v.Pv.Length > 0 && v.Pv[0] == uciMove);
        return hit is not null ? pre.Best.Cp - hit.Cp : pre.Best.Cp + post.Best.Cp;
    }
}
