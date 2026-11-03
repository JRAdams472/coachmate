using CoachMate.Chess;
using CoachMate.Puzzles;
using Xunit;

namespace CoachMate.Tests;

public class PuzzleTests
{
    [Fact]
    public void BundledPuzzles_Load()
    {
        var s = new PuzzleStore();
        s.Load();
        Assert.True(s.Puzzles.Count >= 7);
    }

    [Fact]
    public void BundledPuzzles_SolutionActuallyMates()
    {
        var s = new PuzzleStore();
        s.Load();
        foreach (var p in s.Puzzles)
        {
            var g = new GameState(p.Fen);
            foreach (var u in p.Moves)
                Assert.True(g.TryUci(u) is not null, $"{p.Id} move {u}");
            Assert.True(g.IsCheckmate, $"{p.Id}: solution should end in mate");
        }
    }

    [Fact]
    public void Parse_LichessRow()
    {
        var p = Puzzle.Parse("00sHx,q3k1nr/1pp1nQpp/3p4/1P2p3/4P3/B1PP1b2/B5PP/5K2 b k - 0 17,e8d7 a2e9 d7d8 f7f8,1760,80,83,72,mate mateIn2 middlegame,https://lichess.org/x#black");
        Assert.Equal("00sHx", p.Id);
        Assert.Equal(new[] { "e8d7", "a2e9", "d7d8", "f7f8" }, p.Moves);
        Assert.Equal(1760, p.Rating);
        Assert.True(p.IsUserToMove(0));
    }
}
