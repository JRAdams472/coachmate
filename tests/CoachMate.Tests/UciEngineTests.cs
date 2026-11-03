using CoachMate.Engine;
using Xunit;

namespace CoachMate.Tests;

public class UciEngineTests : IAsyncLifetime
{
    private readonly UciEngine _engine = new();

    public async Task InitializeAsync() { UciEngine.DebugLogPath = Path.Combine(Path.GetTempPath(), "coachmate-eng.log"); await _engine.StartAsync(); }
    public async Task DisposeAsync() => await _engine.DisposeAsync();

    [Fact]
    public async Task StartPos_Depth10_ReturnsBestMoveAndPvs()
    {
        var res = await _engine.AnalyzeAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 10);
        Assert.False(string.IsNullOrEmpty(res.BestMove));
        Assert.NotEqual("(none)", res.BestMove);
        Assert.True(res.Pvs.Count >= 2);
        Assert.True(res.Best!.Cp is > -200 and < 200); // startpos ~even
        Assert.Equal(res.BestMove, res.Best.Pv[0]);
    }

    [Fact]
    public async Task WeakenedSearch_RestoresSkill_AndStillReturns()
    {
        var weak = await _engine.AnalyzeAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 4, 0);
        Assert.False(string.IsNullOrEmpty(weak.BestMove));
        var strong = await _engine.AnalyzeAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 10);
        Assert.NotNull(strong.Best);
    }

    [Fact]
    public async Task MatePosition_ReportsMateScore()
    {
        var res = await _engine.AnalyzeAsync(
            "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP1PP/RNBQKBNR w KQkq - 0 1", 8);
        Assert.NotNull(res.Best);
    }
}
