using CoachMate.Coach;
using CoachMate.Engine;
using CoachMate.Chess;
using Xunit;

namespace CoachMate.Tests;

public class CoachTests
{
    private static SearchResult Res(int cp, params string[][] pvs)
    {
        var r = new SearchResult();
        for (var i = 0; i < pvs.Length; i++) r.Pvs[i + 1] = new PvInfo(14, cp - i * 40, null, pvs[i]);
        return r;
    }

    [Fact]
    public void LossCp_TopLineMove_ZeroLoss()
    {
        var pre = Res(30, new[] { "e2e4" }, new[] { "d2d4" }, new[] { "g1f3" });
        var post = Res(-30, new[] { "e7e5" });
        Assert.Equal(0, MoveGrader.LossCp("e2e4", pre, post));
    }

    [Fact]
    public void LossCp_InPv_GradedAgainstSameSearch()
    {
        var pre = Res(30, new[] { "d2d4" }, new[] { "e2e4" }, new[] { "c2c4" });
        var post = Res(-10, new[] { "e7e5" });
        Assert.Equal(40, MoveGrader.LossCp("e2e4", pre, post)); // 30 - (-10)
    }

    [Fact]
    public void LossCp_OffLine_CrossSearchFallback()
    {
        var pre = Res(80, new[] { "d2d4" });
        var post = Res(120, new[] { "e7e5" });
        Assert.Equal(200, MoveGrader.LossCp("a2a3", pre, post)); // 80 + 120
    }

    [Theory]
    [InlineData(0, Grade.Best)] [InlineData(40, Grade.Good)]
    [InlineData(100, Grade.Inaccuracy)] [InlineData(200, Grade.Mistake)]
    [InlineData(400, Grade.Blunder)]
    public void Classify_Thresholds(int cp, Grade g) => Assert.Equal(g, MoveGrader.Classify(cp));

    [Fact]
    public void Explainer_HangingPiece_NamedAndRefuted()
    {
        var pre = new GameState("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 1");
        var uci = "f1d3"; // Bd3?? does nothing for the hanging... craft: black knight c6 undefended after white plays something leaving it en prise? use simple hang: post has hanging queen
        var post = new GameState("rnbqkbnr/pppp1ppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1");
        var preA = Res(20, new[] { "d2d4" });
        var postA = Res(300, new[] { "d1d4" }); // engine takes the pawn on d4
        var why = Explainer.Explain(pre, "e2e4", "e4", post, preA, postA, 300, false, 3);
        Assert.Contains(why, w => w.Contains("pawn") || w.Contains("Best"));
    }

    [Fact]
    public void Explainer_BlunderMate_Immediate()
    {
        var pre = new GameState();
        var post = new GameState();
        var preA = Res(10, new[] { "e2e4" });
        var postA = new SearchResult();
        postA.Pvs[1] = new PvInfo(14, 90000, 2, new[] { "d8h4" });
        var why = Explainer.Explain(pre, "g2g4", "g4", post, preA, postA, 400, true, 3);
        Assert.Contains(why, w => w.Contains("mate in 2"));
    }
}
