using CoachMate.Rating;
using Xunit;

namespace CoachMate.Tests;

public class RatingTests
{
    [Fact]
    public void WinVsEqual_RatingRises()
    {
        var t = new RatingTracker();
        var r0 = t.R;
        t.Apply(1500, 60, 1.0);
        Assert.True(t.R > r0);
    }

    [Fact]
    public void LossVsEqual_RatingDrops()
    {
        var t = new RatingTracker();
        var r0 = t.R;
        t.Apply(1500, 60, 0.0);
        Assert.True(t.R < r0);
    }

    [Fact]
    public void DrawVsEqual_RoughlyStable()
    {
        var t = new RatingTracker();
        var r0 = t.R;
        t.Apply(1500, 60, 0.5);
        Assert.True(System.Math.Abs(t.R - r0) < 20);
    }

    [Fact]
    public void Rd_ShrinksWithGames()
    {
        var t = new RatingTracker();
        for (var i = 0; i < 10; i++) t.Apply(1400, 60, i % 2 == 0 ? 1 : 0.5);
        Assert.True(t.Rd < 350 && t.Rd >= 30);
        Assert.Equal(10, t.Games);
    }

    [Fact]
    public void Rating_StaysInBounds()
    {
        var t = new RatingTracker();
        for (var i = 0; i < 50; i++) t.Apply(3000, 60, 0);
        Assert.True(t.R > 400 && t.Rd >= 30 && t.Rd <= 350);
    }
}
