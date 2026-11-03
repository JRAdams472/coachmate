using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CoachMate.Chess;
using CoachMate.Coach;
using CoachMate.Engine;
using CoachMate.Models;
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
    private int _pieceStyle;
    private char Glyph(PieceTypes t, bool white) => _pieceStyle switch
    {
        1 => Filled[t], 2 => Outline[t], _ => white ? Outline[t] : Filled[t]
    };

    // level -> (skill, depth) for weakened searches; null = bestmove direct
    private static readonly (int Skill, int Depth)?[] Levels =
        { null, (12, 10), (6, 6), (0, 3) };

    private GameState _game = new();
    private readonly UciEngine _engine = new();
    private readonly ObservableCollection<CoachNote> _notes = new();
    private string? _selected;
    private (string From, string To)? _lastMove;
    private bool _busy;
    private SearchResult? _cur;
    private bool _userIsWhite = true;

    public MainWindow()
    {
        InitializeComponent();
        CoachFeed.ItemsSource = _notes;
        PieceSel.SelectionChanged += (_, _) => { _pieceStyle = PieceSel.SelectedIndex < 0 ? 0 : PieceSel.SelectedIndex; Refresh(); };
        PlaySel.SelectionChanged += async (_, _) => await NewGameAsync();
        Board.SquareClicked += sq => _ = HandleSquareAsync(sq);
        Board.MoveAttempted += (f, t) => _ = AttemptMoveAsync(f, t);
        Board.PositionRendered += Refresh;
        BackBtn.Click += async (_, _) => await BackAsync();
        NewBtn.Click += async (_, _) => await NewGameAsync();
        FlipBtn.Click += (_, _) => Board.Flip();
        HintBtn.Click += (_, _) => ShowHint();
        ((FrameworkElement)Content).Loaded += OnLoaded;
        Closed += OnClosed;
        BuildPromoPanel();
        Refresh();
    }

    private void BuildPromoPanel()
    {
        foreach (var (pt, ch) in new[] { (PieceTypes.Queen, 'q'), (PieceTypes.Rook, 'r'), (PieceTypes.Bishop, 'b'), (PieceTypes.Knight, 'n') })
        {
            var b = new Button { FontSize = 26, Padding = new Thickness(8) };
            b.Content = Glyph(pt, _userIsWhite).ToString();
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
        _ = AnalyzeTurnAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs e)
        => await _engine.DisposeAsync();

    private async Task AnalyzeTurnAsync()
    {
        _busy = true; Status.Text = "Engine: thinking...";
        _cur = await _engine.AnalyzeAsync(_game.Fen, 14);
        _busy = false;
        UpdateEval(_cur);
        Refresh();
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
        if (t.Uci is null) { _selected = null; Board.ClearTargets(); Refresh(); return false; }
        char? promo = null;
        if (t.Promo)
        {
            promo = await AskPromotionAsync();
            if (promo is null) { Refresh(); return true; } // cancelled
        }
        var pre = _game.Clone();
        var san = _game.TryMove(from, to, promo ?? 'q');
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
        _notes.Insert(0, note);

        if (_game.IsGameOver) { _busy = false; Refresh(); return; }

        var reply = post.BestMove;
        var level = Levels[LevelSel.SelectedIndex < 0 ? 2 : LevelSel.SelectedIndex];
        if (level is { } w)
            reply = (await _engine.AnalyzeAsync(_game.Fen, w.Depth, w.Skill)).BestMove;

        if (reply is not null and not "(none)")
        {
            var c = _game.Clone();
            var rs = c.TryUci(reply) ?? reply;
            _game.TryUci(reply);
            _lastMove = (reply[..2], reply.Substring(2, 2));
            _notes.Insert(0, new CoachNote { Title = $"» {MoveNum()}… {rs}", IsEngine = true });
        }
        _busy = false;
        Refresh();
        if (!_game.IsGameOver) _ = AnalyzeTurnAsync();
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

    private async Task NewGameAsync()
    {
        _engine.Stop();
        _game = new GameState();
        _selected = null; _cur = null; _lastMove = null;
        Board.ClearTargets(); Board.ClearArrows();
        _notes.Clear();
        var wantBlack = PlaySel.SelectedIndex == 1;
        if (wantBlack != !_userIsWhite) { } // orientation handled below
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
        foreach (var t in _game.LegalTargets(sq)) yield return (t.To, t.Capture);
    }

    private void Refresh()
    {
        var pieces = new Dictionary<string, (char, bool)>();
        foreach (var (name, type, white) in _game.BoardSquares())
            if (type != PieceTypes.NoPieceType) pieces[name] = (Glyph(type, white), white);
        Board.EnsureBuilt();
        Board.SetPosition(pieces);
        Board.ClearMarks();
        if (_lastMove is { } lm) { Board.Mark(lm.From, 0); Board.Mark(lm.To, 0); }
        if (_selected is not null) Board.Mark(_selected, 1);
        if (_game.InCheck)
        {
            var k = _game.KingSquare(_game.WhiteToMove);
            if (k is not null) Board.Mark(k, 2);
        }
        if (!_busy)
            Status.Text = _game.IsCheckmate ? (_game.WhiteToMove ? "Checkmate — Black wins" : "Checkmate — White wins")
                : _game.IsStalemate ? "Stalemate — draw"
                : _game.IsRepetition || _game.IsFiftyMove ? "Draw"
                : _game.InCheck ? (_game.WhiteToMove ? "White to move — check!" : "Black to move — check!")
                : !_engine.IsReady ? "Engine: starting..."
                : _game.WhiteToMove ? "White to move" : "Black to move";
    }
}
