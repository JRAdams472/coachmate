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

    /// <summary>PGN move text, e.g. "1. e4 e5 2. Nf3".</summary>
    public string Pgn()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < _history.Count; i += 2)
        {
            sb.Append($"{i / 2 + 1}. {_history[i].San}");
            if (i + 1 < _history.Count) sb.Append($" {_history[i + 1].San}");
            sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

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
    public List<(string To, bool Capture, bool Promo, string Uci, bool Castle)> LegalTargets(string name)
    {
        var sq = Sq(name);
        return _game.Pos.GenerateMoves()
            .Where(em => Equals(em.Move.FromSquare(), sq))
            .Select(em => (Name(em.Move.ToSquare()),
                           em.Move.IsType(MoveTypes.Enpassant) || _game.Pos.IsOccupied(em.Move.ToSquare()),
                           em.Move.IsType(MoveTypes.Promotion),
                           Uci(em.Move),
                           em.Move.IsType(MoveTypes.Castling)))
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
        // ChessLib encodes castling as king->rook-square; translate the standard
        // destination form. Only the four real castle patterns remap — any other
        // same-rank king step (e.g. d6c6) must stay as-is.
        var castleTo = (from, to) switch
        {
            ("e1", "g1") => "h1", ("e1", "c1") => "a1",
            ("e8", "g8") => "h8", ("e8", "c8") => "a8",
            _ => null
        };
        if (castleTo is not null && Occupant(from) is { Type: PieceTypes.King })
            t = Sq(castleTo);
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

    public GameState Clone() => new(Fen);

    /// <summary>Occupant of a square, or null.</summary>
    public (PieceTypes Type, bool IsWhite)? Occupant(string name)
    {
        var p = _game.Pos.GetPiece(Sq(name));
        return p.Type() != PieceTypes.NoPieceType ? (p.Type(), p.IsWhite) : null;
    }

    /// <summary>Own pieces attacked by the enemy; undefended = own side does not cover it.</summary>
    public List<(string Sq, PieceTypes Type)> Hanging(bool white)
    {
        var res = new List<(string, PieceTypes)>();
        foreach (var (name, type, isW) in BoardSquares())
        {
            if (type == PieceTypes.NoPieceType || isW != white || type == PieceTypes.King) continue;
            var attacked = white ? _game.Pos.IsAttacked(Sq(name), Player.Black) : _game.Pos.IsAttacked(Sq(name), Player.White);
            if (!attacked) continue;
            var defended = white ? _game.Pos.IsAttacked(Sq(name), Player.White) : _game.Pos.IsAttacked(Sq(name), Player.Black);
            if (!defended) res.Add((name, type));
        }
        return res;
    }

    /// <summary>Enemy pieces of <paramref name="whiteAttacker"/> attacked by that side.</summary>
    public List<(string Sq, PieceTypes Type)> AttackedEnemyPieces(bool whiteAttacker)
    {
        var res = new List<(string, PieceTypes)>();
        foreach (var (name, type, isW) in BoardSquares())
        {
            if (type == PieceTypes.NoPieceType || isW == whiteAttacker) continue;
            var attacked = whiteAttacker ? _game.Pos.IsAttacked(Sq(name), Player.White) : _game.Pos.IsAttacked(Sq(name), Player.Black);
            if (attacked) res.Add((name, type));
        }
        return res;
    }

    /// <summary>Count of enemy-attacked squares inside the 3x3 zone around the king.</summary>
    public int KingZonePressure(bool whiteKing)
    {
        var k = KingSquare(whiteKing);
        if (k is null) return 0;
        var f0 = k[0] - 97; var r0 = k[1] - 49;
        var n = 0;
        for (var df = -1; df <= 1; df++)
        for (var dr = -1; dr <= 1; dr++)
        {
            var f = f0 + df; var r = r0 + dr;
            if (f < 0 || f > 7 || r < 0 || r > 7) continue;
            var sq = $"{(char)(97 + f)}{(char)(49 + r)}";
            var attacked = whiteKing ? _game.Pos.IsAttacked(Sq(sq), Player.Black) : _game.Pos.IsAttacked(Sq(sq), Player.White);
            if (attacked) n++;
        }
        return n;
    }

    public int Material(bool white) =>
        BoardSquares().Where(b => b.IsWhite == white).Sum(b => PieceValues.Of(b.Type));

    /// <summary>Squares the side to move could reach that are NOT enemy-attacked (safe-ish).</summary>
    public bool HasSafeEscape(string name, bool white)
    {
        foreach (var t in LegalTargets(name))
        {
            var c = Clone();
            c.TryUci(t.Uci);
            var stillAttacked = white ? c.IsAttackedByBlack(t.To) : c.IsAttackedByWhite(t.To);
            if (!stillAttacked) return true;
        }
        return false;
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

public static class PieceValues
{
    public static int Of(PieceTypes t) => t switch
    {
        PieceTypes.Pawn => 1, PieceTypes.Knight => 3, PieceTypes.Bishop => 3,
        PieceTypes.Rook => 5, PieceTypes.Queen => 9, _ => 0
    };
    public static string Name(PieceTypes t) => t switch
    {
        PieceTypes.Pawn => "pawn", PieceTypes.Knight => "knight", PieceTypes.Bishop => "bishop",
        PieceTypes.Rook => "rook", PieceTypes.Queen => "queen", PieceTypes.King => "king", _ => "piece"
    };
}


