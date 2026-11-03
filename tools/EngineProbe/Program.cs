using CoachMate.Engine;

var engine = new UciEngine();
engine.Line += l => Console.WriteLine($"<< {l}");
await engine.StartAsync();
Console.WriteLine("READY");
var res = await engine.AnalyzeAsync(
    "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 8);
Console.WriteLine($"bestmove={res.BestMove} pvs={res.Pvs.Count} cp={res.Best?.Cp}");
await engine.DisposeAsync();
