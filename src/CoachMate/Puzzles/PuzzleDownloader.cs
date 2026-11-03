namespace CoachMate.Puzzles;

/// <summary>Downloads the Lichess puzzle DB (.csv.zst, ~700MB compressed / ~4GB raw),
/// streams-decompresses, and stores a compact filtered text file.</summary>
public sealed class PuzzleDownloader
{
    public const string Url = "https://database.lichess.org/lichess_db_puzzle.csv.zst";

    public static async Task<long> DownloadAsync(IProgress<(long Got, long Total)>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PuzzleStore.FullPath)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var res = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        var tmp = PuzzleStore.FullPath + ".tmp";
        long written = 0, got = 0;
        await using (var zs = new ZstdSharp.DecompressionStream(await res.Content.ReadAsStreamAsync()))
        using (var r = new StreamReader(zs))
        await using (var w = new StreamWriter(tmp))
        {
            await r.ReadLineAsync(); // header
            while (await r.ReadLineAsync() is { } line)
            {
                got += line.Length + 1;
                var f = line.Split(',');
                if (f.Length < 8) continue;
                // compact: id,fen,moves,rating,,,,themes
                await w.WriteLineAsync($"{f[0]},{f[1]},{f[2]},{f[3]},,,,{f[7]}");
                written += line.Length;
                if (got % (64 << 20) < 1000) progress?.Report((got, total));
            }
        }
        File.Move(tmp, PuzzleStore.FullPath, true);
        return written;
    }
}
