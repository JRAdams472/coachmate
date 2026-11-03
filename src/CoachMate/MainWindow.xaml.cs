using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CoachMate.Chess;
using CoachMate.Coach;
using CoachMate.Engine;
using CoachMate.Models;
using CoachMate.Net;
using CoachMate.Ai;
using CoachMate.Puzzles;
using CoachMate.Rating;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
using WinRT.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Rudzoft.ChessLib.Types;

namespace CoachMate;

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<PieceTypes, char> Outline =
        new Dictionary<PieceTypes, char>
        {
            [PieceTypes.King] = '♔', [PieceTypes.Queen] = '♕',
            [PieceTypes.Rook] = '♖', [PieceTypes.Bishop] = '♗',
            [PieceTypes.Knight] = '♘', [PieceTypes.Pawn] = '♙',
        };
    private static readonly IReadOnlyDictionary<PieceTypes, char> Filled =
        new Dictionary<PieceTypes, char>
        {
            [PieceTypes.King] = '♚', [PieceTypes.Queen] = '♛',
            [PieceTypes.Rook] = '♜', [PieceTypes.Bishop] = '♝',
            [PieceTypes.Knight] = '♞', [PieceTypes.Pawn] = '♟',
        };
    private readonly Pieces.PieceSetManager _setMgr = new();
    private static string KeyOf(PieceTypes t, bool white) =>
        (white ? "w" : "b") + t switch
        {
            PieceTypes.Pawn => "p", PieceTypes.Knight => "n", PieceTypes.Bishop => "b",
            PieceTypes.Rook => "r", PieceTypes.Queen => "q", _ => "k"
        };
    // glyph+font+key; raster sets use the key, wireframe uses the glyph
    private (char Glyph, string? Font, string Key) PieceVisual(PieceTypes t, bool white) =>
        (Outline[t], "Segoe UI Symbol", KeyOf(t, white));

    // level -> approx. Elo for UCI_LimitStrength; 0 = Master (unlimited)
    private static readonly int[] LevelElos = { 0, 1900, 1400, 900 };
    private static readonly int[] LevelRatings = { 3000, 1900, 1400, 900 };

    private GameState _game = new();
    private readonly UciEngine _engine = new();
    private readonly ObservableCollection<CoachNote> _notes = new();
    private string? _selected;
    private (string From, string To)? _lastMove;
    private bool _busy;
    private SearchResult? _cur;
    private bool _userIsWhite = true;
    private readonly CloudDbClient _cloud = new();
    private readonly OllamaClient _ollama = new();
    private readonly PuzzleStore _puzzleStore = new();
    private Puzzle? _puzzle;
    private int _puzzlePly;
    private bool _practice;
    private bool _playAsBlack;
    private int _levelIndex = 2; // Casual ~1400
    private string _selectedPiece = "Classic";
    private string _ollamaEndpoint = "http://localhost:11434";
    private string _ollamaModel = "qwen2.5:7b-instruct";
    private bool PracticeMode => _practice;
    private readonly RatingTracker _rating = RatingTracker.Load();
    private readonly ObservableCollection<BookRow> _book = new();

    public MainWindow()
    {
        InitializeComponent();
        CoachFeed.ItemsSource = _notes;
        BookList.ItemsSource = _book;
        RatingText.Text = $"You: {_rating.Display}";
        _ollamaEndpoint = LoadSetting("ollamaEndpoint") ?? _ollamaEndpoint;
        _ollamaModel = LoadSetting("ollamaModel") ?? _ollamaModel;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (System.IO.File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                System.IO.File.WriteAllText(LogPath, "");
        }
        catch { }
        UciEngine.DebugLogPath = LogPath;
        if (int.TryParse(LoadSetting("level"), out var li) && li is >= 0 and <= 3)
        {
            _levelIndex = li;
            new[] { LvlMaster, LvlClub, LvlCasual, LvlBeginner }[_levelIndex].IsChecked = true;
        }
        if (LoadSetting("playAs") == "black") { _playAsBlack = true; PlayBlack.IsChecked = true; }
        if (LoadSetting("mode") == "practice") { _practice = true; ModePractice.IsChecked = true; }
        BuildPiecesMenu();
        ApplyPieceStyle();
        Board.SquareClicked += sq => _ = HandleSquareAsync(sq);
        Board.MoveAttempted += (f, t) => _ = AttemptMoveAsync(f, t);
        Board.PositionRendered += Refresh;
        BackBtn.Click += async (_, _) => await BackAsync();
        NewBtn.Click += async (_, _) => await NewGameAsync();
        FlipBtn.Click += (_, _) => Board.Flip();
        HintBtn.Click += (_, _) => ShowHint();
        AskBtn.Click += async (_, _) => await AskCoachChatAsync();
        FileNew.Click += async (_, _) => await NewGameAsync();
        FileSave.Click += async (_, _) => await SaveGameAsync();
        FileLog.Click += (_, _) => System.Diagnostics.Process.Start(
            "explorer.exe", Path.GetDirectoryName(LogPath)!);
        FileExit.Click += (_, _) => Close();
        ModePlay.Click += async (_, _) =>
        {
            CheckRadio(ModePlay, ModePlay, ModePractice);
            SaveSetting("mode", "play");
            if (_practice) { _practice = false; await NewGameAsync(); }
        };
        ModePractice.Click += async (_, _) =>
        {
            CheckRadio(ModePractice, ModePlay, ModePractice);
            SaveSetting("mode", "practice");
            if (!_practice) { _practice = true; _puzzleStore.Load(); await NextPuzzleAsync(); }
        };
        PlayWhite.Click += async (_, _) =>
        {
            CheckRadio(PlayWhite, PlayWhite, PlayBlack);
            SaveSetting("playAs", "white");
            if (_playAsBlack) { _playAsBlack = false; await NewGameAsync(); }
        };
        PlayBlack.Click += async (_, _) =>
        {
            CheckRadio(PlayBlack, PlayWhite, PlayBlack);
            SaveSetting("playAs", "black");
            if (!_playAsBlack) { _playAsBlack = true; await NewGameAsync(); }
        };
        LvlMaster.Click += (_, _) => SetLevel(0, LvlMaster);
        LvlClub.Click += (_, _) => SetLevel(1, LvlClub);
        LvlCasual.Click += (_, _) => SetLevel(2, LvlCasual);
        LvlBeginner.Click += (_, _) => SetLevel(3, LvlBeginner);
        PuzzleDbItem.Click += async (_, _) => await DownloadDbAsync();
        AiSettingsItem.Click += async (_, _) => await AiSettingsAsync();
        RatingItem.Click += async (_, _) => await MyRatingAsync();
        BookHeader.Click += (_, _) => SetCloudDbCollapsed(!_cloudDbCollapsed);
        SetCloudDbCollapsed(LoadSetting("cloudDbCollapsed") == "1");
        ((FrameworkElement)Content).Loaded += OnLoaded;
        Closed += OnClosed;
        BuildPromoPanel();
        Refresh();
    }

    private List<string> _importedSets = new();
    private readonly List<ToggleMenuFlyoutItem> _pieceItems = new();
    private bool _cloudDbCollapsed;

    private void SetCloudDbCollapsed(bool collapsed)
    {
        _cloudDbCollapsed = collapsed;
        BookBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        BookChevron.Text = collapsed ? "▸" : "▾";
        SaveSetting("cloudDbCollapsed", collapsed ? "1" : "0");
    }

    private static void CheckRadio(ToggleMenuFlyoutItem picked, params ToggleMenuFlyoutItem[] group)
    {
        foreach (var i in group) i.IsChecked = i == picked;
    }

    private void SetLevel(int idx, ToggleMenuFlyoutItem item)
    {
        _levelIndex = idx;
        CheckRadio(item, LvlMaster, LvlClub, LvlCasual, LvlBeginner);
        SaveSetting("level", idx.ToString());
    }

    private void BuildPiecesMenu(string? select = null)
    {
        _importedSets = _setMgr.ListSets().Where(s => s != "cburnett").ToList();
        PiecesMenu.Items.Clear();
        _pieceItems.Clear();
        foreach (var name in new[] { "Classic", "3D", "Wireframe" }.Concat(_importedSets))
        {
            var item = new ToggleMenuFlyoutItem { Text = name };
            var n = name;
            item.Click += (_, _) =>
            {
                CheckRadio(item, _pieceItems.ToArray());
                _selectedPiece = n;
                ApplyPieceStyle();
            };
            PiecesMenu.Items.Add(item);
            _pieceItems.Add(item);
        }
        PiecesMenu.Items.Add(new MenuFlyoutSeparator());
        var import = new MenuFlyoutItem { Text = "Import set…" };
        import.Click += async (_, _) => await ImportSetAsync();
        PiecesMenu.Items.Add(import);
        var saved = select ?? LoadSetting("pieceStyle") ?? "Classic";
        _selectedPiece = _pieceItems.Any(i => i.Text == saved) ? saved : "Classic";
        foreach (var i in _pieceItems) i.IsChecked = i.Text == _selectedPiece;
    }

    private void ApplyPieceStyle()
    {
        var sel = _selectedPiece;
        if (sel == "Wireframe") Board.SetImages(null, false);
        else
        {
            var set = sel is "Classic" or "3D" ? "cburnett" : sel;
            try
            {
                var imgs = _setMgr.SetPaths(set).ToDictionary(
                    kv => kv.Key, kv => (ImageSource)new BitmapImage(new Uri(kv.Value)));
                Board.SetImages(imgs, sel == "3D");
            }
            catch { Board.SetImages(null, false); }
        }
        SaveSetting("pieceStyle", sel);
        Refresh();
    }

    private async Task ImportSetAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) { BuildPiecesMenu(); return; }
        try
        {
            var name = _setMgr.ImportSet(folder.Path);
            BuildPiecesMenu(name);
        }
        catch (Exception ex)
        {
            _notes.Insert(0, new CoachNote { Title = "Import failed" });
            _notes[0].Why.Add(ex.Message);
            BuildPiecesMenu();
        }
        ApplyPieceStyle();
    }

    private async Task AiSettingsAsync()
    {
        var ep = new TextBox { Text = _ollamaEndpoint, PlaceholderText = "http://localhost:11434" };
        var model = new TextBox { Text = _ollamaModel, PlaceholderText = "qwen2.5:7b-instruct" };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Ollama endpoint" });
        panel.Children.Add(ep);
        panel.Children.Add(new TextBlock { Text = "Model", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(model);
        var dlg = new ContentDialog
        {
            Title = "AI settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            _ollamaEndpoint = ep.Text.Trim();
            _ollamaModel = model.Text.Trim();
            SaveSetting("ollamaEndpoint", _ollamaEndpoint);
            SaveSetting("ollamaModel", _ollamaModel);
        }
    }

    private async Task MyRatingAsync()
    {
        var slider = new Slider
        {
            Minimum = 300, Maximum = 2500, StepFrequency = 50,
            Value = Math.Clamp(_rating.R, 300, 2500), MinWidth = 320
        };
        var val = new TextBlock { Text = $"Your estimate: {(int)slider.Value}" };
        slider.ValueChanged += (_, e) => val.Text = $"Your estimate: {(int)e.NewValue}";
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(val);
        panel.Children.Add(slider);
        panel.Children.Add(new TextBlock
        {
            Text = "A self-estimate resets the tracker to max uncertainty — finished games pull it toward your real level. For reference: the Beginner opponent is ~900.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 158, 154, 147))
        });
        var dlg = new ContentDialog
        {
            Title = "Your rating",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            _rating.SetEstimate(slider.Value);
            RatingText.Text = $"You: {_rating.Display}";
        }
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoachMate", "coach.log");

    private static void Log(string m)
    {
        try { System.IO.File.AppendAllLines(LogPath, new[] { $"{DateTime.Now:HH:mm:ss.fff} {m}" }); } catch { }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoachMate", "settings.json");

    private static Dictionary<string, string> LoadAll()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(SettingsPath)) ?? new(); }
        catch { return new(); }
    }

    private static string? LoadSetting(string k) => LoadAll().GetValueOrDefault(k);

    private static void SaveSetting(string k, string v)
    {
        try
        {
            var d = LoadAll(); d[k] = v;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            System.IO.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    private async Task SaveGameAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedFileName = "coachmate-game.pgn";
        picker.FileTypeChoices.Add("PGN", new List<string> { ".pgn" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var result = _game.IsCheckmate ? (_game.WhiteToMove ? "0-1" : "1-0")
            : _game.IsStalemate || _game.IsRepetition || _game.IsFiftyMove ? "1/2-1/2" : "*";
        var pgn = $"[Result \"{result}\"]\n[FEN \"{_game.Fen}\"]\n\n{_game.Pgn()} {result}\n";
        await Windows.Storage.FileIO.WriteTextAsync(file, pgn);
        Status.Text = $"Saved to {file.Name}";
    }

    private void BuildPromoPanel()
    {
        foreach (var (pt, ch) in new[] { (PieceTypes.Queen, 'q'), (PieceTypes.Rook, 'r'), (PieceTypes.Bishop, 'b'), (PieceTypes.Knight, 'n') })
        {
            var b = new Button { FontSize = 26, Padding = new Thickness(8) };
            var (pg, pf, _) = PieceVisual(pt, _userIsWhite);
            b.Content = pg.ToString();
            if (pf is not null) b.FontFamily = new FontFamily(pf);
            var p = ch;
            b.Click += (_, _) => { _promoChoice = p; FlyoutBase.GetAttachedFlyout(Board).Hide(); };
            PromoPanel.Children.Add(b);
        }
    }

    private char? _promoChoice;
    private async Task<char?> AskPromotionAsync()
    {
        _promoChoice = null;
        FlyoutBase.GetAttachedFlyout(Board).ShowAt(Board);
        var tcs = new TaskCompletionSource<char?>();
        var flyout = FlyoutBase.GetAttachedFlyout(Board);
        void onClosed(object? s, object e) { flyout.Closed -= onClosed; tcs.TrySetResult(_promoChoice); }
        flyout.Closed += onClosed;
        return await tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Status.Text = "Engine: starting...";
        await _engine.StartAsync();
        // restore persisted mode/side: puzzle mode or play-as-black need setup
        if (_practice) { _puzzleStore.Load(); await NextPuzzleAsync(); return; }
        if (_playAsBlack) { await NewGameAsync(); return; }
        _ = AnalyzeTurnAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs e)
        => await _engine.DisposeAsync();

    private async Task AskCoachChatAsync()
    {
        var q = AskBox.Text.Trim();
        if (q.Length == 0) return;
        _ollama.Endpoint = _ollamaEndpoint.Trim().TrimEnd('/');
        _ollama.Model = _ollamaModel.Trim();
        AskOut.Text = "Thinking...";
        var ctx = $"Game so far (PGN): {_game.Pgn()}\n";
        if (_notes.Count > 0)
        {
            ctx += "Coach notes this game (oldest first):\n";
            foreach (var n in _notes.Reverse().Take(40))
            {
                var detail = n.IsEngine ? "" : $" — {string.Join(" ", n.Why)}";
                var reff = n.Refutation is { } r ? $" {r}" : "";
                ctx += $"  {n.Title} {n.Symbol}{detail}{reff}\n";
            }
        }
        ctx += $"Position (FEN): {_game.Fen}\nSide to move: {(_game.WhiteToMove ? "White" : "Black")}\n";
        if (_cur?.Best is { } b)
        {
            var best = b.Pv.Length > 0 ? _game.UciLineToSan(new[] { b.Pv[0] }).FirstOrDefault() : null;
            ctx += $"Engine eval: cp={b.Cp}{(b.Mate is { } m ? $" mate={m}" : "")} best={best}\n";
        }
        if (_notes.Count > 0 && _notes[0].Why.Count > 0)
            ctx += $"Latest coach note: {string.Join(" | ", _notes[0].Why)}\n";
        ctx += $"Question: {q}";
        var text = await _ollama.ChatAsync(Narrator.System, ctx);
        AskOut.Text = text ?? "Coach unavailable — is Ollama running? (docker / ollama serve)";
    }

    private async Task AnalyzeTurnAsync()
    {
        _busy = true; Status.Text = "Engine: thinking...";
        _cur = await _engine.AnalyzeAsync(_game.Fen, 14);
        _busy = false;
        UpdateEval(_cur);
        _ = RenderBookAsync();
        Refresh();
    }

    private async Task RenderBookAsync()
    {
        var rows = await _cloud.QueryAsync(_game.Fen);
        _book.Clear();
        if (rows is null) { BookStatus.Text = "unavailable (offline?)"; return; }
        if (rows.Count == 0) { BookStatus.Text = "no data — out of book"; return; }
        var c = _game.Clone();
        foreach (var r in rows)
        {
            var san = c.TryUci(r.Uci);
            if (san is not null) c.Undo();
            _book.Add(new BookRow { San = san ?? r.Uci, Winrate = $"{r.Winrate:0.0}% {r.Note}", BarWidth = r.Winrate * 1.6 });
        }
        BookStatus.Text = "win% for side to move";
    }

    private async Task HandleSquareAsync(string sq)
    {
        if (_busy || !_engine.IsReady) return;
        if (_selected is not null)
        {
            var from = _selected;
            if (await AttemptMoveAsync(from, sq)) return;
        }
        var piece = _game.Occupant(sq);
        if (piece is not null && piece.Value.IsWhite == _game.WhiteToMove
            && piece.Value.IsWhite == _userIsWhite && !_game.IsGameOver)
        {
            _selected = sq;
            Refresh();
            Board.Mark(sq, 1);
            Board.ShowTargets(Targets(sq));
        }
        else { _selected = null; Refresh(); }
    }

    private async Task<bool> AttemptMoveAsync(string from, string to)
    {
        if (_selected is null && from != _selected) _selected = from;
        var targets = _game.LegalTargets(from);
        var t = targets.Find(x => x.To == to);
        // king dragged/clicked to its castle destination -> remap to the rook square
        if (t.Uci is null && _game.Occupant(from) is { Type: PieceTypes.King })
        {
            var rookSq = to[0] switch { 'g' => "h" + to[1], 'c' => "a" + to[1], _ => null };
            if (rookSq is not null && from[1] == to[1])
                t = targets.Find(x => x.To == rookSq && x.Castle);
        }
        if (t.Uci is null) { _selected = null; Board.ClearTargets(); Refresh(); return false; }
        if (PracticeMode) return await AttemptPuzzleMoveAsync(from, to);
        char? promo = null;
        if (t.Promo)
        {
            promo = await AskPromotionAsync();
            if (promo is null) { Refresh(); return true; } // cancelled
        }
        var pre = _game.Clone();
        var san = promo is not null ? _game.TryMove(from, to, promo.Value) : _game.TryMove(from, to);
        _selected = null;
        Board.ClearTargets(); Board.ClearArrows();
        if (san is null) { Refresh(); return false; }
        _lastMove = (from, to);
        Refresh();
        await OnUserMovedAsync(t.Uci, san, pre);
        return true;
    }

    private async Task OnUserMovedAsync(string uci, string san, GameState pre)
    {
        _busy = true; Status.Text = "Engine: thinking...";
        var moverWhite = !_game.WhiteToMove;
        var post = await _engine.AnalyzeAsync(_game.Fen, 14);

        var loss = MoveGrader.LossCp(uci, _cur, post);
        var grade = MoveGrader.Classify(loss);
        var note = new CoachNote
        {
            Title = $"{MoveNum()}. {san}",
            Symbol = MoveGrader.Symbol(grade),
            IsBad = grade >= Grade.Inaccuracy,
            Line = _cur?.Best?.Pv is { Length: > 0 } pv
                ? "Line: " + string.Join(" ", _game.UciLineToSan(pv)) : null,
            Refutation = Explainer.Refutation(_game.Clone(), post, moverWhite) is { } r
                ? $"Refutation: {r}" : null
        };
        foreach (var why1 in Explainer.Explain(pre, uci, san, _game.Clone(), _cur, post, loss, moverWhite, _game.MoveCount / 2 + 1))
            note.Why.Add(why1);
        note.AiContext = Narrator.Prompt(pre.Fen, san, grade.ToString(), loss,
            _cur?.Best?.Pv is { Length: > 0 } p ? pre.UciLineToSan(new[] { p[0] }).FirstOrDefault() : null,
            _cur?.Best?.Pv is { Length: > 0 } p2 ? string.Join(" ", pre.UciLineToSan(p2)) : null,
            note.Why);
        _notes.Insert(0, note);

        if (_game.IsGameOver) { _busy = false; ApplyResult(); Refresh(); return; }

        var reply = post.BestMove;
        var lvl = LevelElos[_levelIndex];
        if (lvl > 0)
            reply = (await _engine.AnalyzeAsync(_game.Fen, 12, elo: lvl)).BestMove;

        Log($"reply: fen={_game.Fen} best={reply ?? "null"}");
        if (reply is not null and not "(none)")
        {
            var rs = _game.TryUci(reply);
            if (rs is null)
            {
                Log($"REPLY DROPPED: '{reply}' not legal at {_game.Fen}");
                _notes.Insert(0, new CoachNote
                {
                    Title = $"⚠ engine tried '{reply}'",
                    Why = { "Move rejected — details in coach.log (File ▸ Open log folder)." }
                });
            }
            else
            {
                _lastMove = (reply[..2], reply.Substring(2, 2));
                _notes.Insert(0, new CoachNote { Title = $"» {MoveNum()}… {rs}", IsEngine = true });
            }
        }
        _busy = false;
        Refresh();
        if (_game.IsGameOver) ApplyResult(); else _ = AnalyzeTurnAsync();
    }

    /// <summary>Score the finished game for the user and update the Glicko estimate.</summary>
    private void ApplyResult()
    {
        if (PracticeMode || _game.MoveCount < 4) return;
        double score;
        if (_game.IsCheckmate)
            score = _game.WhiteToMove == _userIsWhite ? 0 : 1; // side to move is mated
        else score = 0.5; // stalemate / fifty-move / repetition
        _rating.Apply(LevelRatings[_levelIndex], 60, score);
        var note = new CoachNote
        {
            Title = $"Game over — rating {_rating.Display}",
            Why = { $"Games scored: {_rating.Games}" }
        };
        if (_game.DrawReason is { } dr)
            note.Why.Add(dr switch
            {
                "stalemate" => "Stalemate — the side to move had no legal moves but wasn't in check.",
                "threefold repetition" => "Threefold repetition — the same position occurred three times.",
                "fifty-move rule" => "Fifty-move rule — 50 moves passed without a capture or pawn move.",
                _ => "Insufficient material — neither side has enough pieces to ever deliver mate."
            });
        _notes.Insert(0, note);
        RatingText.Text = $"You: {_rating.Display}";
        Refresh();
    }

    private int MoveNum() => (_game.MoveCount + 1) / 2;

    private async Task BackAsync()
    {
        if (_game.MoveCount == 0) return;
        _engine.Stop();
        _game.Undo();
        if (_game.MoveCount > 0) _game.Undo();
        _selected = null; _cur = null; Board.ClearTargets(); Board.ClearArrows();
        if (_notes.Count > 0) _notes.RemoveAt(0);
        if (_notes.Count > 0) _notes.RemoveAt(0);
        _lastMove = null;
        Refresh();
        _ = AnalyzeTurnAsync();
    }

    private async Task NextPuzzleAsync()
    {
        _engine.Stop();
        _puzzle = _puzzleStore.Random();
        _puzzlePly = 0;
        _game = new GameState(_puzzle.Fen);
        _selected = null; _cur = null; _lastMove = null;
        Board.ClearTargets(); Board.ClearArrows();
        _notes.Clear();
        _notes.Insert(0, new CoachNote
        {
            Title = $"Puzzle {_puzzle.Id} (~{_puzzle.Rating})",
            Why = { $"Find the best move for {_game.WhiteToMove switch { true => "White", _ => "Black" }}." }
        });
        // engine's setup move (lichess format: first move precedes the solve position)
        if (_puzzle.EngineStarts)
        {
            _game.TryUci(_puzzle.Moves[0]);
            _puzzlePly = 1;
            var m = _puzzle.Moves[0];
            _lastMove = (m[..2], m.Substring(2, 2));
        }
        // orient the board for the solving side
        var userIsWhite = _game.WhiteToMove;
        if (userIsWhite != _userIsWhite) { _userIsWhite = userIsWhite; Board.Flip(); }
        Refresh();
        await Task.CompletedTask;
    }

    private async Task<bool> AttemptPuzzleMoveAsync(string from, string to)
    {
        if (_puzzle is null) return false;
        var expected = _puzzle.Moves[_puzzlePly];
        var targets = _game.LegalTargets(from);
        var t = targets.Find(x => x.To == to);
        if (t.Uci is null) { _selected = null; Board.ClearTargets(); Refresh(); return false; }
        char? promo = null;
        if (t.Promo)
        {
            promo = await AskPromotionAsync();
            if (promo is null) { Refresh(); return true; }
        }
        var uci = from + to + (promo?.ToString() ?? "");
        if (uci != expected)
        {
            _selected = null; Board.ClearTargets();
            _notes.Insert(0, new CoachNote { Title = "✗", Symbol = "?", IsBad = true });
            _notes[0].Why.Add("Not the best move — try again.");
            Refresh();
            return true;
        }
        var san = _game.TryUci(uci);
        _selected = null; Board.ClearTargets(); Board.ClearArrows();
        _lastMove = (from, to);
        _notes.Insert(0, new CoachNote { Title = $"✓ {san}", Symbol = "✓" });
        _puzzlePly++;
        // engine's reply from the solution
        if (_puzzlePly < _puzzle.Moves.Length && !_puzzle.IsUserToMove(_puzzlePly))
        {
            var r = _puzzle.Moves[_puzzlePly];
            var c = _game.Clone(); var rs = c.TryUci(r) ?? r;
            _game.TryUci(r);
            _lastMove = (r[..2], r.Substring(2, 2));
            _notes.Insert(0, new CoachNote { Title = $"» {rs}", IsEngine = true });
            _puzzlePly++;
        }
        if (_puzzlePly >= _puzzle.Moves.Length)
            _notes.Insert(0, new CoachNote { Title = "Solved!", Symbol = "!", Why = { "Puzzle complete — press New for the next one." } });
        Refresh();
        return true;
    }

    private async Task DownloadDbAsync()
    {
        if (_busy) return;
        _busy = true; Status.Text = "Downloading puzzle DB (~700 MB)...";
        try
        {
            var prog = new Progress<(long Got, long Total)>(p =>
                Status.Text = p.Total > 0
                    ? $"Downloading puzzle DB: {p.Got / 1048576} MB of ~4 GB data"
                    : $"Downloading puzzle DB: {p.Got / 1048576} MB");
            await PuzzleDownloader.DownloadAsync(prog);
            _puzzleStore.Load();
            Status.Text = $"Puzzle DB ready ({_puzzleStore.Puzzles.Count:N0} puzzles)";
        }
        catch (Exception ex) { Status.Text = $"Download failed: {ex.Message}"; }
        _busy = false;
    }

    private async Task NewGameAsync()
    {
        if (PracticeMode) { await NextPuzzleAsync(); return; }
        _engine.Stop();
        _game = new GameState();
        _selected = null; _cur = null; _lastMove = null;
        Board.ClearTargets(); Board.ClearArrows();
        _notes.Clear();
        var wantBlack = _playAsBlack;
        if (wantBlack && _userIsWhite) { _userIsWhite = false; Board.Flip(); }
        else if (!wantBlack && !_userIsWhite) { _userIsWhite = true; Board.Flip(); }
        Refresh();
        if (!_userIsWhite)
        {
            _busy = true; Status.Text = "Engine: thinking...";
            var res = await _engine.AnalyzeAsync(_game.Fen, 14);
            var m = res.BestMove;
            if (m is not null)
            {
                var c = _game.Clone(); var rs = c.TryUci(m) ?? m;
                _game.TryUci(m);
                _lastMove = (m[..2], m.Substring(2, 2));
                _notes.Insert(0, new CoachNote { Title = $"» 1. {rs}", IsEngine = true });
            }
            _busy = false; Refresh();
        }
        _ = AnalyzeTurnAsync();
    }

    private void ShowHint()
    {
        if (PracticeMode)
        {
            if (_puzzle is null || _puzzlePly >= _puzzle.Moves.Length) return;
            var m = _puzzle.Moves[_puzzlePly];
            Board.ClearArrows();
            Board.ShowArrow(m[..2], m.Substring(2, 2));
            return;
        }
        if (_cur?.Best?.Pv is not { Length: > 0 } pv || _busy) return;
        Board.ClearArrows();
        Board.ShowArrow(pv[0][..2], pv[0].Substring(2, 2));
        var san = _game.UciLineToSan(new[] { pv[0] });
        _notes.Insert(0, new CoachNote
        {
            Title = "Hint",
            Why = { $"Engine suggests {san.FirstOrDefault() ?? pv[0]}." },
            Line = "Line: " + string.Join(" ", _game.UciLineToSan(pv))
        });
    }

    private void UpdateEval(SearchResult? res)
    {
        var best = res?.Best;
        if (best is null) return;
        var cp = _game.WhiteToMove ? best.Cp : -best.Cp;
        var pct = best.Mate is not null ? (cp > 0 ? 100 : 0)
            : (int)System.Math.Round(100.0 / (1 + System.Math.Exp(-0.004 * cp)));
        EvalFill.Height = pct * 4.8;
        EvalText.Text = best.Mate is not null ? $"#{best.Mate}"
            : $"{(cp >= 0 ? "+" : "")}{cp / 100.0:0.0}";
    }

    private IEnumerable<(string To, bool Capture)> Targets(string sq)
    {
        foreach (var t in _game.LegalTargets(sq))
        {
            yield return (t.To, t.Capture);
            // castle targets point at the rook square; also dot the king's landing square
            if (t.Castle && t.To.Length == 2)
                yield return ((t.To[0] == 'h' ? "g" : "c") + t.To[1], false);
        }
    }

    private void Refresh()
    {
        var pieces = new Dictionary<string, (char, bool, string?, string)>();
        foreach (var (name, type, white) in _game.BoardSquares())
            if (type != PieceTypes.NoPieceType)
            {
                var (g, f, k) = PieceVisual(type, white);
                pieces[name] = (g, white, f, k);
            }
        Board.EnsureBuilt();
        Board.SetPosition(pieces);
        Board.ClearMarks();
        var phase = GamePhase.Classify(_game);
        PhaseText.Text = GamePhase.Title(phase);
        GoalText.Text = GamePhase.Goal(phase);
        if (_lastMove is { } lm) { Board.Mark(lm.From, 0); Board.Mark(lm.To, 0); }
        if (_selected is not null) Board.Mark(_selected, 1);
        if (_game.InCheck)
        {
            var k = _game.KingSquare(_game.WhiteToMove);
            if (k is not null) Board.Mark(k, 2);
        }
        if (!_busy)
            Status.Text = _game.IsCheckmate ? (_game.WhiteToMove ? "Checkmate — Black wins" : "Checkmate — White wins")
                : _game.DrawReason is { } dr ? $"Draw — {dr}"
                : _game.InCheck ? (_game.WhiteToMove ? "White to move — check!" : "Black to move — check!")
                : !_engine.IsReady ? "Engine: starting..."
                : _game.WhiteToMove ? "White to move" : "Black to move";
    }
}
