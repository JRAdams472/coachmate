using System.Collections.Generic;
using System.Threading.Tasks;
using CoachMate.Chess;
using CoachMate.Engine;
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
    // 0 = Classic (white outline / black filled), 1 = 3D (all filled), 2 = Wireframe (all outline)
    private int _pieceStyle;
    private char Glyph(PieceTypes t, bool white) => _pieceStyle switch
    {
        1 => Filled[t],
        2 => Outline[t],
        _ => white ? Outline[t] : Filled[t]
    };

    // level -> (skill, depth); null = use the full-strength bestmove directly
    private static readonly (int Skill, int Depth)?[] Levels =
        { null, (12, 10), (6, 6), (0, 3) };

    private readonly GameState _game = new();
    private readonly UciEngine _engine = new();
    private string? _selected;
    private (string From, string To)? _lastMove;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        PieceSel.SelectionChanged += (_, _) => { _pieceStyle = PieceSel.SelectedIndex < 0 ? 0 : PieceSel.SelectedIndex; Refresh(); };
        Board.SquareClicked += OnSquareClicked;
        Board.PositionRendered += Refresh;
        ((Microsoft.UI.Xaml.FrameworkElement)Content).Loaded += OnLoaded;
        Closed += OnClosed;
        Refresh();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Status.Text = "Engine: starting...";
        await _engine.StartAsync();
        Status.Text = "Engine: ready — White to move";
    }

    private async void OnClosed(object sender, WindowEventArgs e)
        => await _engine.DisposeAsync();

    private async void OnSquareClicked(string sq)
    {
        if (_busy || !_engine.IsReady) return;
        if (_selected is not null)
        {
            var san = _game.TryMove(_selected, sq, 'q'); // auto-queen until phase 5 flyout
            _selected = null;
            Board.ClearTargets();
            if (san is not null)
            {
                _lastMove = (_selected ?? sq, sq);
                Refresh();
                await EngineReplyAsync();
                return;
            }
        }
        var piece = PieceAt(sq);
        if (piece is not null && piece.Value.IsWhite == _game.WhiteToMove && !_game.IsGameOver)
        {
            _selected = sq;
            Refresh();
            Board.Mark(sq, 1);
            Board.ShowTargets(Targets(sq));
        }
        else { _selected = null; Refresh(); }
    }

    private async Task EngineReplyAsync()
    {
        if (_game.IsGameOver) { Refresh(); return; }
        _busy = true; Status.Text = "Engine: thinking...";

        // full-strength analysis of the position after the user's move
        var post = await _engine.AnalyzeAsync(_game.Fen, 14);
        UpdateEval(post);

        // opponent reply: Master plays the bestmove; weaker levels re-search
        // at reduced Skill Level + depth (grading/eval stay full strength)
        var reply = post.BestMove;
        var level = Levels[LevelSel.SelectedIndex < 0 ? 2 : LevelSel.SelectedIndex];
        if (level is { } w)
            reply = (await _engine.AnalyzeAsync(_game.Fen, w.Depth, w.Skill)).BestMove;

        if (reply is not null and not "(none)")
        {
            _game.TryUci(reply);
            _lastMove = (reply[..2], reply.Substring(2, 2));
        }
        _busy = false;
        Refresh();
    }

    private void UpdateEval(SearchResult res)
    {
        var best = res.Best;
        if (best is null) return;
        var cp = _game.WhiteToMove ? best.Cp : -best.Cp; // score is STM-relative
        var pct = best.Mate is not null
            ? (cp > 0 ? 100 : 0)
            : (int)System.Math.Round(100.0 / (1 + System.Math.Exp(-0.004 * cp)));
        EvalFill.Height = pct * 4.8; // 480px bar
        EvalText.Text = best.Mate is not null
            ? $"#{best.Mate}"
            : $"{(cp >= 0 ? "+" : "")}{cp / 100.0:0.0}";
    }

    private (PieceTypes Type, bool IsWhite)? PieceAt(string sq)
    {
        foreach (var (name, type, white) in _game.BoardSquares())
            if (name == sq) return (type, white);
        return null;
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
