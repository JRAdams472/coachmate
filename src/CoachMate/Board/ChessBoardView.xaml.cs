using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace CoachMate.Board;

public sealed partial class ChessBoardView : UserControl
{
    private static readonly SolidColorBrush LightSq = new(Windows.UI.Color.FromArgb(255, 240, 217, 181));
    private static readonly SolidColorBrush DarkSq = new(Windows.UI.Color.FromArgb(255, 181, 136, 99));
    private static readonly SolidColorBrush LightCoord = new(Windows.UI.Color.FromArgb(255, 181, 136, 99));
    private static readonly SolidColorBrush DarkCoord = new(Windows.UI.Color.FromArgb(255, 240, 217, 181));
    private static readonly SolidColorBrush LastMove = new(Windows.UI.Color.FromArgb(90, 205, 210, 60));
    private static readonly SolidColorBrush SelBrush = new(Windows.UI.Color.FromArgb(115, 100, 190, 90));
    private static readonly SolidColorBrush CheckBrush = new(Windows.UI.Color.FromArgb(140, 220, 60, 50));
    private static readonly SolidColorBrush DotBrush = new(Windows.UI.Color.FromArgb(115, 20, 60, 20));
    private static readonly SolidColorBrush RingBrush = new(Windows.UI.Color.FromArgb(140, 20, 60, 20));

    private bool _flipped;
    private readonly Border[,] _cells = new Border[8, 8];
    private readonly TextBlock[,] _glyphs = new TextBlock[8, 8];
    private readonly Border[,] _marks = new Border[8, 8];   // last-move/selection/check tint
    private readonly List<(UIElement El, Grid Host)> _targets = new();

    public event Action<string>? SquareClicked;

    public ChessBoardView() => InitializeComponent();

    protected override void OnApplyTemplate() { }

    private bool _built;

    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        for (var i = 0; i < 8; i++)
        {
            BoardGrid.RowDefinitions.Add(new RowDefinition());
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++)
        {
            var cell = new Border { Background = (r + c) % 2 == 0 ? LightSq : DarkSq };
            var mark = new Border();
            var glyph = new TextBlock
            {
                FontSize = 44, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var inner = new Grid { Width = 60, Height = 60 };
            inner.Children.Add(mark);
            inner.Children.Add(glyph);
            // coords: file letter bottom-right of last row, rank number top-left of first col
            if (r == 7)
                inner.Children.Add(new TextBlock
                {
                    Text = _flipped ? ((char)('h' - c)).ToString() : ((char)('a' + c)).ToString(),
                    FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = (r + c) % 2 == 0 ? DarkCoord : LightCoord,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 2)
                });
            if (c == 0)
                inner.Children.Add(new TextBlock
                {
                    Text = _flipped ? (r + 1).ToString() : (8 - r).ToString(),
                    FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = (r + c) % 2 == 0 ? DarkCoord : LightCoord,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 2, 0, 0)
                });
            cell.Child = inner;
            var name = SqName(r, c);
            cell.Tag = name;
            cell.PointerPressed += OnCellPressed;
            Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
            BoardGrid.Children.Add(cell);
            _cells[r, c] = cell; _glyphs[r, c] = glyph; _marks[r, c] = mark;
        }
    }

    public string SqName(int r, int c) =>
        _flipped
            ? $"{(char)('h' - c)}{r + 1}"
            : $"{(char)('a' + c)}{8 - r}";

    public void Flip() { _flipped = !_flipped; Rebuild(); }

    private void Rebuild()
    {
        BoardGrid.Children.Clear(); Overlay.Children.Clear(); _targets.Clear();
        _built = false; EnsureBuilt();
        PositionRendered?.Invoke();
    }

    /// <summary>Fired after a flip so the host can repaint pieces/highlights.</summary>
    public event Action? PositionRendered;

    public void SetPosition(IReadOnlyDictionary<string, (char Glyph, bool IsWhite)> pieces)
    {
        EnsureBuilt();
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++)
        {
            var name = SqName(r, c);
            if (pieces.TryGetValue(name, out var p))
            {
                _glyphs[r, c].Text = p.Glyph.ToString();
                _glyphs[r, c].Foreground = p.IsWhite
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Colors.Black);
            }
            else _glyphs[r, c].Text = "";
        }
    }

    public void ClearMarks()
    {
        for (var r = 0; r < 8; r++) for (var c = 0; c < 8; c++) _marks[r, c].Background = null;
    }

    public void Mark(string sq, int kind) // 0=last,1=selected,2=check
    {
        var (r, c) = Rc(sq);
        _marks[r, c].Background = kind switch { 0 => LastMove, 1 => SelBrush, _ => CheckBrush };
    }

    public void ShowTargets(IEnumerable<(string To, bool Capture)> targets)
    {
        ClearTargets();
        foreach (var (to, capture) in targets)
        {
            var (r, c) = Rc(to);
            UIElement el = capture
                ? new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(27),
                               BorderBrush = RingBrush, BorderThickness = new Thickness(3),
                               HorizontalAlignment = HorizontalAlignment.Center,
                               VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }
                : new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                               Background = DotBrush, HorizontalAlignment = HorizontalAlignment.Center,
                               VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            if (_cells[r, c].Child is Grid host) { host.Children.Add(el); _targets.Add((el, host)); }
        }
    }

    public void ClearTargets()
    {
        foreach (var (el, host) in _targets) host.Children.Remove(el);
        _targets.Clear();
    }

    public void ShowArrow(string from, string to, Windows.UI.Color? color = null)
    {
        var (x1, y1) = SqXy(from); var (x2, y2) = SqXy(to);
        var dx = x2 - x1; var dy = y2 - y1; var len = Math.Sqrt(dx * dx + dy * dy);
        var sx = x1 + dx * 6 / len; var sy = y1 + dy * 6 / len;
        var ex = x2 - dx * 20 / len; var ey = y2 - dy * 20 / len;
        var col = color ?? Windows.UI.Color.FromArgb(255, 61, 145, 66);
        var line = new Line
        {
            X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
            Stroke = new SolidColorBrush(col), StrokeThickness = 7, Opacity = .8,
            StrokeEndLineCap = PenLineCap.Triangle
        };
        Overlay.Children.Add(line);
        var ux = dx / len; var uy = dy / len;
        var (px, py) = (-uy * 9, ux * 9);
        var head = new Polygon
        {
            Fill = new SolidColorBrush(col), Opacity = .8,
       
            Points = new PointCollection
            {
                new Point(ex + ux * 16, ey + uy * 16),
                new Point(ex + px, ey + py),
                new Point(ex - px, ey - py)
            }
        };
        Overlay.Children.Add(head);
    }

    public void ClearArrows() => Overlay.Children.Clear();

    private (double X, double Y) SqXy(string sq)
    {
        var (r, c) = Rc(sq);
        return (c * 60 + 30, r * 60 + 30);
    }

    private (int R, int C) Rc(string sq)
    {
        var f = sq[0] - 'a'; var rank = 8 - (sq[1] - '0');
        return _flipped ? (7 - rank, 7 - f) : (rank, f);
    }

    private void OnCellPressed(object sender, PointerRoutedEventArgs e)
        => SquareClicked?.Invoke((string)((Border)sender).Tag);
}
