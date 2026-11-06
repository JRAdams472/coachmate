using Rudzoft.ChessLib;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;

namespace CoachMate.Chess;

/// <summary>
/// Workarounds for confirmed bugs in Rudzoft.ChessLib 0.0.3 — the package is
/// unmaintained upstream (alpha release, PRs unmerged since 2024), so these are
/// managed here rather than waiting for a fix. Do NOT call the members below
/// directly anywhere else in the codebase:
///
/// BUG 1 — IPosition.IsMate is just "side to move has no legal moves"; it never
///         checks whether the king is attacked, so stalemate reports as
///         checkmate. Use <see cref="IsCheckmate(IPosition)"/> instead.
///
/// BUG 2 — State.Repetition is a Stockfish-style ply-distance marker
///         (+i = 2nd occurrence i plies back, -i = 3rd+), but
///         IPosition.IsRepetition tests "Repetition >= 3": it fires on the
///         SECOND occurrence (twofold — not a draw) and goes quiet on a real
///         threefold. Worse, State.Key is path-dependent — the incremental
///         update in MakeMove diverges from the key computed on a fresh FEN
///         load, so unrelated positions can "repeat" while identical ones
///         reached differently don't match. Repetition detection from this
///         library is unusable; <see cref="GameState"/> counts normalized
///         position keys from its own move history instead.
///
/// BUG 3 — Castling moves are encoded king→rook-square (e1h1, Chess960 style).
///         That is neither standard UCI (e1g1, required by `position ... moves`)
///         nor SAN. <see cref="StandardUci(Move, string)"/> converts for
///         anything sent to an engine.
///
/// Also known upstream: en-passant edge cases and FEN fullmove numbering have
/// open issues (rudzen/ChessLib #67, #71); verify before relying on them.
/// </summary>
public static class ChessLibCompat
{
    /// <summary>True checkmate: in check AND no legal moves.</summary>
    public static bool IsCheckmate(this IPosition pos)
        => pos.InCheck && pos.GenerateMoves().Length == 0;

    /// <summary>
    /// Standard UCI for <paramref name="m"/>: converts a ChessLib castle
    /// encoding (e1h1/e1a1, king→rook square) to king-destination form
    /// (e1g1/e1c1). Pass the already-computed encoding as <paramref name="uci"/>
    /// to avoid recomputing.
    /// </summary>
    public static string StandardUci(Move m, string uci)
    {
        if (m.IsType(MoveTypes.Castling) && uci.Length == 4)
            uci = uci[..2] + (uci[2] == 'h' ? 'g' : 'c') + uci[3];
        return uci;
    }
}
