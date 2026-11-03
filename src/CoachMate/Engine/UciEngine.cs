using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoachMate.Engine;

/// <summary>
/// Drives a stockfish.exe child process over UCI. Serializes searches via a
/// semaphore; parses MultiPV info lines into per-index PV maps; bestmove
/// resolves the pending search. Weakened searches set Skill Level and restore
/// 20 before the next queued search runs.
/// </summary>
public sealed partial class UciEngine : IAsyncDisposable
{
    private static readonly Regex InfoRe = new(
        @"depth (\d+)(?:.*multipv (\d+))?.*score (cp|mate) (-?\d+).*pv (.*)$",
        RegexOptions.Compiled);

    private Process? _proc;
    private StreamWriter? _stdin;
    private readonly SemaphoreSlim _searchLock = new(1, 1);
    private TaskCompletionSource<SearchResult>? _pending;
    private readonly Dictionary<int, PvInfo> _pvs = new();
    private readonly TaskCompletionSource _ready = new();
    private bool _engineUp;

    public bool IsReady => _engineUp;

    /// <summary>Extracts embedded stockfish.exe and completes the UCI handshake.</summary>
    public static string? DebugLogPath;
    private static void D(string m) { try { if (DebugLogPath is { } p) File.AppendAllLines(p, new[] { $"{DateTime.Now:HH:mm:ss.fff} {m}" }); } catch { } }

    public async Task StartAsync()
    {
        D("StartAsync: extracting");
        var path = await ExtractEngineAsync();
        D($"StartAsync: extracted {path}");
        _proc = new Process
        {
            StartInfo =
            {
                FileName = path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        _proc.Start();
        D($"StartAsync: spawned pid={_proc.Id}");
        _stdin = _proc.StandardInput;
        _ = Task.Run(ReadLoopAsync);
        Send("uci");
        Send("setoption name MultiPV value 3");
        Send("isready");
        await _ready.Task;
        _engineUp = true;
    }

    private async Task ReadLoopAsync()
    {
        if (_proc is null) return;
        while (await _proc.StandardOutput.ReadLineAsync() is { } line)
            HandleLine(line);
    }

    /// <summary>Raw engine output lines (diagnostics).</summary>
    public event Action<string>? Line;

    private void HandleLine(string line)
    {
        Line?.Invoke(line);
        if (line is "uciok" or "readyok")
        {
            _ready.TrySetResult();
            return;
        }
        var pending = _pending;
        if (pending is null) return;
        if (line.StartsWith("info") && line.Contains(" pv ") && !line.Contains("bound"))
        {
            var m = InfoRe.Match(line);
            if (m.Success)
            {
                var depth = int.Parse(m.Groups[1].Value);
                var idx = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                var kind = m.Groups[3].Value;
                var val = int.Parse(m.Groups[4].Value);
                var pv = m.Groups[5].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!_pvs.TryGetValue(idx, out var old) || depth >= old.Depth)
                {
                    var cp = kind == "mate" ? (val > 0 ? 100_000 - val * 10 : -100_000 - val * 10) : val;
                    _pvs[idx] = new PvInfo(depth, cp, kind == "mate" ? val : null, pv);
                }
            }
        }
        else if (line.StartsWith("bestmove"))
        {
            _pending = null;
            var res = new SearchResult { BestMove = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) };
            foreach (var kv in _pvs) res.Pvs[kv.Key] = kv.Value;
            pending.TrySetResult(res);
        }
    }

    /// <summary>Runs one search; serialized so only one is in flight at a time.</summary>
    public async Task<SearchResult> AnalyzeAsync(string fen, int depth = 14, int? skill = null)
    {
        await _searchLock.WaitAsync();
        try
        {
            _pvs.Clear();
            var tcs = _pending = new TaskCompletionSource<SearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (skill is { } s) Send($"setoption name Skill Level value {s}");
            Send("stop");
            Send($"position fen {fen}");
            Send($"go depth {depth}");
            var res = await tcs.Task;
            if (skill is not null) Send("setoption name Skill Level value 20");
            return res;
        }
        finally { _searchLock.Release(); }
    }

    /// <summary>Aborts the current search (result still resolves with whatever depth it reached).</summary>
    public void Stop() => Send("stop");

    private void Send(string cmd) => _stdin?.WriteLine(cmd + "\n");

    private static async Task<string> ExtractEngineAsync()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoachMate", "engine");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "stockfish.exe");
        D($"Extract: target {path}");
        var asm = Assembly.GetExecutingAssembly();
        await using var res = asm.GetManifestResourceStream("CoachMate.engine.stockfish.exe")
            ?? throw new InvalidOperationException("embedded stockfish.exe not found");
        D($"Extract: stream len={res.Length}");
        if (File.Exists(path) && new FileInfo(path).Length == res.Length) { D("Extract: cached"); return path; }
        D("Extract: copying");
        await using var fs = File.Create(path);
        await res.CopyToAsync(fs);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        try { Send("quit"); } catch { }
        try { _proc?.Kill(); } catch { }
        _proc?.Dispose();
        await Task.CompletedTask;
    }
}
