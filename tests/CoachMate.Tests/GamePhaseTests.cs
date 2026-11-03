using CoachMate.Chess;
using Xunit;

namespace CoachMate.Tests;

public class GamePhaseTests
{
    [Fact]
    public void StartPosition_IsOpening()
    {
        var g = new GameState();
        Assert.Equal(Phase.Opening, GamePhase.Classify(g));
    }

    [Fact]
    public void EarlyMoves_StillOpening()
    {
        var g = new GameState();
        g.TryUci("e2e4"); g.TryUci("e7e5");
        g.TryUci("g1f3"); g.TryUci("b8c6");
        Assert.Equal(Phase.Opening, GamePhase.Classify(g));
    }

    [Fact]
    public void QueenlessButArmiesIntact_IsMiddlegame()
    {
        // queens traded but rooks/minors/pawns remain: a queenless middlegame
        var g = new GameState("r1b1kb1r/pppp1ppp/2n2n2/8/3P4/2N2N2/PPP1PPPP/R1B1KB1R w KQkq - 0 6");
        Assert.Equal(Phase.Middlegame, GamePhase.Classify(g));
    }

    [Fact]
    public void SparsePosition_IsEndgame()
    {
        var g = new GameState("8/8/4k3/8/8/4K3/8/6R1 w - - 0 1");
        Assert.Equal(Phase.Endgame, GamePhase.Classify(g));
    }

    [Fact]
    public void LoneQueen_IsEndgame()
    {
        var g = new GameState("8/8/3k4/8/8/3K4/8/2q5 w - - 0 1");
        Assert.Equal(Phase.Endgame, GamePhase.Classify(g));
    }
}
