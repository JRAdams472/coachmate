using System.Collections.Generic;
using CoachMate.Chess;
using Microsoft.UI.Xaml;
using Rudzoft.ChessLib.Types;

namespace CoachMate;

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<(PieceTypes, bool), char> Glyphs =
        new Dictionary<(PieceTypes, bool), char>
        {
            [(PieceTypes.King, true)] = '♔', [(PieceTypes.Queen, true)] = '♕',
            [(PieceTypes.Rook, true)] = '♖', [(PieceTypes.Bishop, true)] = '♗',
            [(PieceTypes.Knight, true)] = '♘', [(PieceTypes.Pawn, true)] = '♙',
            [(PieceTypes.King, false)] = '♚', [(PieceTypes.Queen, false)] = '♛',
            [(PieceTypes.Rook, false)] = '♜', [(PieceTypes.Bishop, false)] = '♝',
            [(PieceTypes.Knight, false)] = '♞', [(PieceTypes.Pawn, false)] = '♟',
        };

    private readonly GameState _game = new();
    private string? _selected;
    private (string From, string To)? _lastMove;

    public MainWindow()
    {
        InitializeComponent();
        Board.SquareClicked += OnSquareClicked;
        Board.PositionRendered += Refresh;
        Refresh();
    }

    private void OnSquareClicked(string sq)
    {
        if (_selected is not null)
        {
            var san = _game.TryMove(_selected, sq, 'q'); // auto-queen until phase 5 flyout
            _selected = null;
            Board.ClearTargets();
            if (san is not null) { _lastMove = null; Refresh(); return; }
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
            if (type != PieceTypes.NoPieceType && Glyphs.TryGetValue((type, white), out var g))
                pieces[name] = (g, white);
        Board.EnsureBuilt();
        Board.SetPosition(pieces);
        Board.ClearMarks();
        var hist = _game.MoveCount;
        if (hist > 0 && _lastMove is null)
        {
            // highlight last move via SAN-less path: recompute from game history later;
        }
        if (_game.InCheck)
        {
            var k = _game.KingSquare(_game.WhiteToMove);
            if (k is not null) Board.Mark(k, 2);
        }
        Status.Text = _game.IsCheckmate ? (_game.WhiteToMove ? "Checkmate — Black wins" : "Checkmate — White wins")
            : _game.IsStalemate ? "Stalemate — draw"
            : _game.IsRepetition || _game.IsFiftyMove ? "Draw"
            : _game.InCheck ? (_game.WhiteToMove ? "White to move — check!" : "Black to move — check!")
            : _game.WhiteToMove ? "White to move" : "Black to move";
    }
}
