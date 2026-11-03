namespace CoachMate.Engine;

/// <summary>One engine line: depth + score (cp, mate normalized to ±1e5 range) + PV in UCI.</summary>
public sealed record PvInfo(int Depth, int Cp, int? Mate, string[] Pv);

/// <summary>Result of one engine search: the bestmove plus every MultiPV line.</summary>
public sealed class SearchResult
{
    public string? BestMove { get; set; }
    public Dictionary<int, PvInfo> Pvs { get; } = new();
    public PvInfo? Best => Pvs.TryGetValue(1, out var p) ? p : null;
}
