using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CoachMate.Chess;
using CoachMate.Coach;
using CoachMate.Engine;
using CoachMate.Models;
using Microsoft.UI.Xaml;
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

    private static readonly (int Skill, int Depth)?[] Levels =
        { null, (12, 10), (6, 6), (0, 3) };

    private readonly GameState _game = new();
    private readonly UciEngine _engine = new();
    private readonly ObservableCollection<CoachNote> _notes = new();
    private string? _selected;
    private (string From, string To)? _lastMove;
    private bool _busy;
    private SearchResult? _cur;      // full-strength analysis of the user-to-move position

    public MainWindow()
    {
        InitializeComponent();
        PieceSel.SelectionChanged += (_, _) => { _pieceStyle = PieceSel.SelectedIndex < 0 ? 0 : PieceSel.SelectedIndex; Refresh(); };
        Board.SquareClicked += OnSquareClicked;
        Board.PositionRendered += Refresh;
        ((FrameworkElement)Content).Loaded += OnLoaded;
        Closed += OnClosed;
        CoachFeed.ItemsSource = _notes;
        Refresh();
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

    private async void OnSquareClicked(string sq)
    {
        if (_busy || !_engine.IsReady) return;
        if (_selected is not null)
        {
            var from = _selected;
            var promo = _game.LegalTargets(from).Find(t => t.To == sq).Promo ? 'q' : (char?)null;
            var pre = _game.Clone();
            var san = promo is not null ? _game.TryMove(from, sq, promo.Value) : _game.TryMove(from, sq);
            _selected = null;
            Board.ClearTargets();
            if (san is not null)
            {
                _lastMove = (from, sq);
                Refresh();
                await OnUserMovedAsync(from + sq, san, pre);
                return;
            }
        }
        var piece = _game.Occupant(sq);
        if (piece is not null && piece.Value.IsWhite == _game.WhiteToMove && !_game.IsGameOver)
        {
            _selected = sq;
            Refresh();
            Board.Mark(sq, 1);
            Board.ShowTargets(Targets(sq));
        }
        else { _selected = null; Refresh(); }
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
            Title = $"{Math.Ceiling(_game.MoveCount / 2.0)}. {san}",
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

        // opponent reply: Master plays the bestmove; weaker levels re-search at
        // reduced skill+depth — grading/eval/hints always stay full strength
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
            _notes.Insert(0, new CoachNote { Title = $"» {Math.Ceiling(_game.MoveCount / 2.0)}… {rs}", IsEngine = true });
        }
        _busy = false;
        Refresh();
        if (!_game.IsGameOver) _ = AnalyzeTurnAsync();
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
