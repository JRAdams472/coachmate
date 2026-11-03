namespace CoachMate.Net;

/// <summary>One candidate move from chessdb.cn's cloud database.</summary>
public sealed record BookMove(string Uci, double Winrate, string Note);

/// <summary>chessdb.cn queryall — top moves + winrates from millions of games.</summary>
public sealed class CloudDbClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Top 6 moves for a FEN, or null when unavailable.</summary>
    public async Task<List<BookMove>?> QueryAsync(string fen)
    {
        try
        {
            var text = await _http.GetStringAsync(
                $"https://www.chessdb.cn/cdb.php?action=queryall&board={Uri.EscapeDataString(fen)}");
            if (!text.Contains("move:")) return new List<BookMove>();
            var rows = new List<BookMove>();
            foreach (var entry in text.Split('|'))
            {
                var f = new Dictionary<string, string>();
                foreach (var kv in entry.Split(','))
                {
                    var i = kv.IndexOf(':');
                    if (i > 0) f[kv[..i]] = kv[(i + 1)..];
                }
                if (!f.TryGetValue("move", out var uci)) continue;
                rows.Add(new BookMove(
                    uci,
                    double.TryParse(f.GetValueOrDefault("winrate", ""), out var w) ? w : 0,
                    (f.GetValueOrDefault("note", "") ?? "").Split(' ')[0]));
                if (rows.Count == 6) break;
            }
            return rows;
        }
        catch { return null; }
    }
}
