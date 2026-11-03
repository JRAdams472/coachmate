using Rudzoft.ChessLib.Types;

namespace CoachMate.Chess;

public enum Phase { Opening, Middlegame, Endgame }

/// <summary>
/// Heuristic phase classification — the standard material/move-count approximation.
/// Phases blur in real games; this is the same rough rule engines and trainers use.
/// </summary>
public static class GamePhase
{
    public static Phase Classify(GameState g)
    {
        var queens = 0;
        var nonPawn = 0; // N/B=3, R=5, Q=9 — kings and pawns excluded
        foreach (var (_, t, _) in g.BoardSquares())
        {
            switch (t)
            {
                case PieceTypes.Queen: queens++; nonPawn += 9; break;
                case PieceTypes.Rook: nonPawn += 5; break;
                case PieceTypes.Knight:
                case PieceTypes.Bishop: nonPawn += 3; break;
            }
        }
        // traded down to few pieces: queenless with ≤26 pts, or ≤20 total (Q alone + scraps)
        if (nonPawn <= 20 || (queens == 0 && nonPawn <= 26)) return Phase.Endgame;
        // armies mostly intact and it's early — still in the opening (~first 13 full moves)
        if (g.MoveCount <= 26 && nonPawn >= 50) return Phase.Opening;
        return Phase.Middlegame;
    }

    public static string Title(Phase p) => p switch
    {
        Phase.Opening => "Opening",
        Phase.Endgame => "Endgame",
        _ => "Middlegame"
    };

    public static string Goal(Phase p) => p switch
    {
        Phase.Opening => "Goal: develop knights and bishops toward the center, castle your king to safety, and fight for the middle squares. Avoid moving the same piece twice or bringing your queen out early.",
        Phase.Endgame => "Goal: bring your king into the fight — he is a strong piece now. Push passed pawns toward promotion and keep the enemy king boxed out.",
        _ => "Goal: before every move ask \"what is my opponent threatening?\" Then look for checks, captures, and attacks on undefended pieces — and keep your own pieces protected."
    };
}
