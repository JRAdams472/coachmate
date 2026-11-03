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
