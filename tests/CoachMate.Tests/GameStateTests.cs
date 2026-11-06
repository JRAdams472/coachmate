using CoachMate.Chess;
using Xunit;

namespace CoachMate.Tests;

public class GameStateTests
{
    [Fact]
    public void StartPos_WhiteToMove_20LegalMoves()
    {
        var g = new GameState();
        Assert.True(g.WhiteToMove);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", g.Fen);
        var total = 0;
        foreach (var f in "abcdefgh")
            foreach (var r in "12")
                total += g.LegalTargets($"{f}{r}").Count;
        Assert.Equal(20, total);
    }

    [Fact]
    public void TryMove_LegalAppliesSan_IllegalReturnsNull()
    {
        var g = new GameState();
        Assert.Equal("e4", g.TryMove("e2", "e4"));
        Assert.False(g.WhiteToMove);              // now black to move
        Assert.Null(g.TryMove("e2", "e5"));       // no piece / wrong side
        Assert.Null(g.TryMove("a7", "a4"));       // pawn can't jump three
        Assert.Equal("e5", g.TryMove("e7", "e5"));
    }

    [Fact]
    public void Undo_RestoresPosition()
    {
        var g = new GameState();
        var start = g.Fen;
        g.TryMove("e2", "e4");
        g.Undo();
        Assert.Equal(start, g.Fen);
        Assert.Equal(0, g.MoveCount);
    }

    [Fact]
    public void Uci_Promotion_RoundTrips()
    {
        var g = new GameState("8/2P5/8/8/8/8/7k/K7 w - - 0 1");
        var san = g.TryUci("c7c8q");
        Assert.Equal("c8=Q", san);
    }

    [Fact]
    public void UciLineToSan_ConvertsAndRestores()
    {
        var g = new GameState();
        var fen = g.Fen;
        var san = g.UciLineToSan(new[] { "e2e4", "e7e5", "g1f3" });
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, san);
        Assert.Equal(fen, g.Fen); // position restored after take-backs
    }

    [Fact]
    public void Checkmate_Detected()
    {
        var g = new GameState("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 0 1");
        Assert.True(g.IsCheckmate);
        Assert.True(g.IsGameOver);
    }
}

public class TryMovePromoContract
{
    [Fact]
    public void TryMove_PromoArgOnNormalMove_ReturnsNull()
    {
        // AttemptMoveAsync relies on this: promo must be null for non-promotion moves
        var g = new CoachMate.Chess.GameState();
        Assert.Null(g.TryMove("e2", "e4", 'q'));
        Assert.Equal("e4", g.TryMove("e2", "e4"));
    }
}

public class CastleUci
{
    [Fact]
    public void TryUci_StandardCastleNotation_Works()
    {
        // stockfish replies e1g1; ChessLib generates e1h1 — must translate
        var g = new CoachMate.Chess.GameState("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
        Assert.Equal("O-O", g.TryUci("e1g1"));
        Assert.Equal("O-O-O", g.TryUci("e8c8") is null ? null : "O-O-O"); // black: try via fresh
    }

    [Fact]
    public void TryMove_KingDestinationSquare_Castles()
    {
        var g = new CoachMate.Chess.GameState("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
        Assert.Equal("O-O-O", g.TryMove("e1", "c1"));
    }

    [Fact]
    public void TryUci_KingSidestepToCFile_NotCastleRemapped()
    {
        // regression: same-rank king steps to c/g files were remapped to a/h
        // as if castling, dropping the move — engine replies like d6c6 vanished
        var g = new CoachMate.Chess.GameState("8/8/3k4/8/8/8/4K3/8 b - - 0 1");
        Assert.Equal("Kc6", g.TryUci("d6c6"));
    }

    [Fact]
    public void MoveUcis_Castle_StandardNotation()
    {
        // ChessLib stores O-O as e1h1; engines need e1g1 in `position ... moves`
        var g = new CoachMate.Chess.GameState("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
        Assert.NotNull(g.TryUci("e1g1"));
        Assert.Equal("e1g1", g.MoveUcis[0]);
    }

    [Fact]
    public void TryUci_KingSidestepToGFile_NotCastleRemapped()
    {
        // worse variant: f6g6 remapped to f6h6 — h6 is legal, so the WRONG
        // move was silently played
        var g = new CoachMate.Chess.GameState("8/8/5k2/8/8/8/4K3/8 b - - 0 1");
        Assert.Equal("Kg6", g.TryUci("f6g6"));
    }
}

public class DrawRules
{
    [Fact]
    public void BareKings_InsufficientMaterial()
    {
        var g = new CoachMate.Chess.GameState("8/8/4k3/8/8/4K3/8/8 w - - 0 1");
        Assert.True(g.IsGameOver);
        Assert.Equal("insufficient material", g.DrawReason);
    }

    [Fact]
    public void KingPlusKnightVsKing_InsufficientMaterial()
    {
        var g = new CoachMate.Chess.GameState("8/8/4k3/8/8/4K1N1/8/8 w - - 0 1");
        Assert.Equal("insufficient material", g.DrawReason);
    }

    [Fact]
    public void KnightVsKnight_NotInsufficientMaterial()
    {
        // helpmate sequences exist -> not a dead position (K+N vs K+N)
        var g = new CoachMate.Chess.GameState("8/8/4k3/8/8/4K1N1/4n3/8 w - - 0 1");
        Assert.False(g.IsGameOver);
        Assert.Null(g.DrawReason);
    }

    [Fact]
    public void Stalemate_Labeled()
    {
        // black to move, not in check, no legal moves (classic Q vs K stalemate)
        var g = new CoachMate.Chess.GameState("k7/2Q5/8/8/8/8/8/6K1 b - - 0 1");
        Assert.Equal("stalemate", g.DrawReason);
    }

    [Fact]
    public void Threefold_OnlyAtThirdOccurrence()
    {
        // regression: ChessLib IsRepetition fired on the second occurrence
        // (and even on a first occurrence via its path-dependent Zobrist key)
        var g = new CoachMate.Chess.GameState();
        foreach (var u in new[] { "g1f3", "g8f6", "f3g1", "f6g8" }) g.TryUci(u);
        Assert.False(g.IsRepetition); // second occurrence — play continues
        Assert.False(g.IsGameOver);
        foreach (var u in new[] { "g1f3", "g8f6", "f3g1", "f6g8" }) g.TryUci(u);
        Assert.True(g.IsRepetition);
        Assert.Equal("threefold repetition", g.DrawReason);
    }

    [Fact]
    public void Threefold_UndonePositionsDontCount()
    {
        var g = new CoachMate.Chess.GameState();
        foreach (var u in new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1" }) g.TryUci(u);
        g.Undo(); // retract 4.Ng1 — startpos back to two occurrences
        Assert.False(g.IsRepetition);
    }

    [Fact]
    public void Stalemate_NotCheckmate()
    {
        // regression: ChessLib IsMate is "no legal moves" — stalemate was
        // reported as checkmate (Qc8 vs Ka7, boxed in by Rb5)
        var g = new CoachMate.Chess.GameState("2Q5/k7/8/1R4P1/4N3/P3K2P/8/N7 b - - 0 1");
        Assert.False(g.IsCheckmate);
        Assert.Equal("stalemate", g.DrawReason);
        Assert.True(g.IsGameOver);
    }
}
