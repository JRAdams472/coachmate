using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Notation;
using Rudzoft.ChessLib.Notation.Notations;
using Rudzoft.ChessLib.Types;

namespace CoachMate.Chess;

/// <summary>Wraps Rudzoft.ChessLib: position, legal moves, SAN, undo, attack queries.</summary>
public sealed class GameState
{
    private readonly IGame _game;
    private readonly List<(Move Move, string San)> _history = new();

    public GameState() : this(Rudzoft.ChessLib.Fen.Fen.StartPositionFen) {}
    public GameState(string fen) => _game = GameFactory.Create(fen);

    public string Fen => _game.Pos.FenNotation;
    public bool WhiteToMove => _game.Pos.SideToMove.IsWhite;
    public bool InCheck => _game.Pos.InCheck;
    public bool IsCheckmate => _game.Pos.IsMate;
    public bool IsStalemate => !InCheck && !_game.Pos.GenerateMoves().Any();
    public bool IsRepetition => _game.IsRepetition;
    public bool IsFiftyMove => _game.Pos.Rule50 >= 100;
    public bool IsGameOver => IsCheckmate || IsStalemate || IsRepetition || IsFiftyMove;
    public int MoveCount => _history.Count;
    public IReadOnlyList<string> MoveSans => _history.Select(h => h.San).ToList();

    private static Square Sq(string name) => new(name[1] - '1', name[0] - 'a');
    private static string Name(Square sq) => $"{sq.FileChar}{sq.RankChar}";

    /// <summary>64 squares in a8..h1 board() order (rank 8 first, files a-h).</summary>
    public List<(string Name, PieceTypes Type, bool IsWhite)> BoardSquares()
    {
        var list = new List<(string, PieceTypes, bool)>(64);
        for (var rank = 7; rank >= 0; rank--)
        for (var file = 0; file < 8; file++)
        {
            var sq = new Square(rank, file);
            var p = _game.Pos.GetPiece(sq);
            list.Add((Name(sq), p.Type(), p.IsWhite));
        }
        return list;
    }

    /// <summary>Legal targets for a square: to-square + is-capture + is-promotion.</summary>
    public List<(string To, bool Capture, bool Promo, string Uci)> LegalTargets(string name)
    {
        var sq = Sq(name);
        return _game.Pos.GenerateMoves()
            .Where(em => Equals(em.Move.FromSquare(), sq))
            .Select(em => (Name(em.Move.ToSquare()),
                           em.Move.IsType(MoveTypes.Enpassant) || _game.Pos.IsOccupied(em.Move.ToSquare()),
                           em.Move.IsType(MoveTypes.Promotion),
                           Uci(em.Move)))
            .ToList();
    }

    /// <summary>Attempts a move by square names; returns its SAN or null if illegal.</summary>
    public string? TryMove(string from, string to, char? promo = null)
    {
        var move = FindLegal(from, to, promo);
        if (move is null) return null;
        var san = San(move.Value);
        _game.Pos.MakeMove(move.Value, new State());
        _history.Add((move.Value, san));
        return san;
    }

    /// <summary>Attempts a UCI move string (e.g. "e7e8q"); returns SAN or null.</summary>
    public string? TryUci(string uci)
        => TryMove(uci[..2], uci.Substring(2, 2), uci.Length > 4 ? uci[4] : null);

    public Move? FindLegal(string from, string to, char? promo = null)
    {
        var f = Sq(from); var t = Sq(to);
        foreach (var em in _game.Pos.GenerateMoves())
        {
            var m = em.Move;
            if (!Equals(m.FromSquare(), f) || !Equals(m.ToSquare(), t)) continue;
            if (promo is null) return m;
            var want = char.ToLowerInvariant(promo.Value) switch
            {
                'q' => PieceTypes.Queen, 'r' => PieceTypes.Rook,
                'b' => PieceTypes.Bishop, _ => PieceTypes.Knight
            };
            if (m.PromotedPieceType() == want) return m;
        }
        return null;
    }

    public void Undo()
    {
        if (_history.Count == 0) return;
        var (m, _) = _history[^1];
        _game.Pos.TakeMove(m);
        _history.RemoveAt(_history.Count - 1);
    }

    public bool IsAttackedByWhite(string sq) => _game.Pos.IsAttacked(Sq(sq), Player.White);
    public bool IsAttackedByBlack(string sq) => _game.Pos.IsAttacked(Sq(sq), Player.Black);

    public string? KingSquare(bool white)
    {
        var pt = white ? Piece.WhiteKing : Piece.BlackKing;
        foreach (var (name, type, isW) in BoardSquares())
            if (type == PieceTypes.King && isW == white) return name;
        return null;
    }

    /// <summary>Converts a PV of UCI moves into SAN, making+taking on the live position.</summary>
    public List<string> UciLineToSan(IEnumerable<string> ucis)
    {
        var san = new List<string>();
        var made = new List<Move>();
        foreach (var u in ucis)
        {
            var m = FindLegal(u[..2], u.Substring(2, 2), u.Length > 4 ? u[4] : null);
            if (m is null) break;
            san.Add(San(m.Value));
            _game.Pos.MakeMove(m.Value, new State());
            made.Add(m.Value);
        }
        for (var i = made.Count - 1; i >= 0; i--) _game.Pos.TakeMove(made[i]);
        return san;
    }

    public string San(Move m) =>
        MoveNotation.Create(_game.Pos).ToNotation(MoveNotations.San).Convert(m);

    public static string Uci(Move m)
    {
        var s = Name(m.FromSquare()) + Name(m.ToSquare());
        if (m.IsType(MoveTypes.Promotion))
            s += m.PromotedPieceType() switch
            {
                PieceTypes.Queen => 'q', PieceTypes.Rook => 'r',
                PieceTypes.Bishop => 'b', _ => 'n'
            };
        return s;
    }
}
