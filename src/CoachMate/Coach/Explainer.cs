using CoachMate.Chess;
using CoachMate.Engine;
using PV = CoachMate.Chess.PieceValues;
using Rudzoft.ChessLib.Types;

namespace CoachMate.Coach;

/// <summary>
/// Rules-based "why" for a graded move. Detectors run in priority order over
/// (pre-position, played move, post-position, pre-analysis, post-analysis).
/// Returns explanation sentences; the strongest applicable reason wins, plus
/// an annotated refutation line when available.
/// </summary>
public static class Explainer
{
    public static List<string> Explain(
        GameState pre, string uciMove, string san,
        GameState post, SearchResult? preA, SearchResult? postA,
        int loss, bool moverWhite, int moveNumber)
    {
        var best = BestMoveInfo(pre, preA);
        var bestTxt = best is not null ? $"Best was {best}." : "";
        var enemy = moverWhite ? "Black" : "White";

        if (postA?.Best?.Mate is > 0)
            return L($"{bestTxt} Your move allows mate in {postA.Best.Mate} — the engine punishes it immediately.");
        if (preA?.Best?.Mate is > 0)
            return L($"{bestTxt} You had mate in {preA.Best.Mate} and let it slip.");

        // hanging piece the engine actually takes
        var pv0 = postA?.Best?.Pv is { Length: > 0 } p ? p[0] : null;
        foreach (var (sq, type) in post.Hanging(moverWhite).OrderByDescending(h => PV.Of(h.Type)))
        {
            if (pv0 is { Length: >= 4 } && pv0[2..4] == sq)
            {
                var reply = post.UciLineToSan(new[] { pv0 }).FirstOrDefault() ?? "the capture";
                return L($"{bestTxt} Your {PV.Name(type)} on {sq} is attacked and undefended — {enemy} simply plays {reply} and wins it.");
            }
        }

        // missed tactic: engine's best was a capture or forcing check
        var bestUci = preA?.Best?.Pv is { Length: > 0 } b ? b[0] : null;
        if (bestUci is not null && loss >= 120)
        {
            var cap = pre.Occupant(bestUci[2..4]);
            if (cap is { } c && c.IsWhite != moverWhite && PV.Of(c.Type) >= 1)
                return L($"{bestTxt} It wins the {PV.Name(c.Type)} on {bestUci[2..4]} — a tactic you missed.");
            var fork = DetectFork(pre, bestUci, moverWhite);
            if (fork is not null) return L($"{bestTxt} It {fork} — a tactic you missed.");
            var pin = DetectPin(pre, bestUci, moverWhite);
            if (pin is not null) return L($"{bestTxt} It {pin}.");
            if (best?.Contains('+') == true)
                return L($"{bestTxt} It keeps the initiative with a forcing check.");
        }

        // bad trade: user captured and nets material loss across the refutation
        var captured = pre.Occupant(uciMove[2..4]);
        if (captured is { } cp2 && loss >= 100)
        {
            var sim = post.Clone();
            var ownMat0 = sim.Material(moverWhite);
            foreach (var u in (postA?.Best?.Pv ?? Array.Empty<string>()).Take(3)) sim.TryUci(u);
            if (ownMat0 - sim.Material(moverWhite) >= 2)
                return L($"{bestTxt} The exchange loses material — {enemy}'s recapture sequence comes out ahead.");
        }

        // king exposure
        var kz = post.KingZonePressure(moverWhite) - pre.KingZonePressure(moverWhite);
        if (kz >= 2 && loss >= 100)
            return L($"{bestTxt} It exposes your king — {enemy} now attacks {post.KingZonePressure(moverWhite)} squares around it.");

        // trapped piece
        foreach (var (sq, type) in post.AttackedEnemyPieces(!moverWhite).Concat(post.Hanging(moverWhite)))
            if (!post.HasSafeEscape(sq, moverWhite) && PV.Of(type) >= 3)
                return L($"{bestTxt} Your {PV.Name(type)} on {sq} has no safe squares — it's trapped.");

        // opening heuristic
        if (moveNumber <= 10 && loss >= 80 && pre.Occupant(uciMove[..2]) is { Type: PieceTypes.Queen })
            return L($"{bestTxt} Bringing the queen out early lets {enemy} develop with tempo by attacking it.");

        var line = postA is not null ? post.UciLineToSan(postA.Best?.Pv ?? Array.Empty<string>()).Take(4).ToList() : new List<string>();
        return L($"{bestTxt}{(line.Count > 0 ? $" Best line continues {string.Join(' ', line)}." : "")}");
    }

    /// <summary>Annotates the first moves of the refutation PV in plain terms.</summary>
    public static string? Refutation(GameState post, SearchResult? postA, bool moverWhite)
    {
        var pv = postA?.Best?.Pv;
        if (pv is not { Length: > 0 }) return null;
        var parts = new List<string>();
        var sim = post.Clone();
        foreach (var u in pv.Take(3))
        {
            var cap = sim.Occupant(u[2..4]);
            var san = sim.TryUci(u);
            if (san is null) break;
            parts.Add(cap is { } c ? $"{san} — takes the {PV.Name(c.Type)}" : san.Contains('+') ? $"{san} — check" : san);
        }
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static string? DetectFork(GameState pre, string bestUci, bool moverWhite)
    {
        var sim = pre.Clone();
        sim.TryUci(bestUci);
        var targets = sim.AttackedEnemyPieces(moverWhite).Where(t => PV.Of(t.Type) >= 3).ToList();
        return targets.Count >= 2
            ? $"forks your {PV.Name(targets[0].Type)} on {targets[0].Sq} and {PV.Name(targets[1].Type)} on {targets[1].Sq}"
            : null;
    }

    private static string? DetectPin(GameState pre, string bestUci, bool moverWhite)
    {
        // after the best move, look along the moved piece's attack ray: enemy piece
        // backed by a higher-value enemy piece = pin
        var sim = pre.Clone();
        sim.TryUci(bestUci);
        var to = bestUci[2..4];
        var (tf, tr) = (to[0] - 97, to[1] - 49);
        foreach (var (sq, type) in sim.AttackedEnemyPieces(moverWhite))
        {
            var f = sq[0] - 97; var r = sq[1] - 49;
            var df = Math.Sign(f - tf); var dr = Math.Sign(r - tr);
            if (df == 0 && dr == 0) continue;
            // must be aligned horizontally, vertically, or diagonally
            if (!(df == 0 || dr == 0 || Math.Abs(f - tf) == Math.Abs(r - tr))) continue;
            // walk past the enemy piece along the ray
            for (var (x, y) = (f + df, r + dr); x is >= 0 and <= 7 && y is >= 0 and <= 7; x += df, y += dr)
            {
                var o = sim.Occupant($"{(char)(97 + x)}{(char)(49 + y)}");
                if (o is null) continue;
                if (o.Value.IsWhite != moverWhite && PV.Of(o.Value.Type) > PV.Of(type))
                    return $"pins your {PV.Name(type)} on {sq} to your {PV.Name(o.Value.Type)}";
                break;
            }
        }
        return null;
    }

    private static string? BestMoveInfo(GameState pre, SearchResult? preA)
        => preA?.Best?.Pv is { Length: > 0 } pv ? pre.UciLineToSan(new[] { pv[0] }).FirstOrDefault() : null;

    private static List<string> L(params string[] s) => new(s);
}
