using System;
using System.Collections.Generic;
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
    private readonly Image[,] _imgs = new Image[8, 8];
    private readonly Border[,] _marks = new Border[8, 8];
    private readonly Ellipse[,] _shadows = new Ellipse[8, 8];
    private Dictionary<string, ImageSource>? _images;
    private readonly string?[,] _keys = new string?[8, 8];
    private bool _showShadow;
    private readonly List<(UIElement El, Grid Host)> _targets = new();

    // drag state
    private string? _dragFrom;
    private (int R, int C)? _pressCell;
    private UIElement? _ghost;
    private bool _dragging;

    public event Action<string>? SquareClicked;      // click-select flow
    public event Action<string, string>? MoveAttempted; // drag from->to
    public event Action? PositionRendered;

    public ChessBoardView()
    {
        InitializeComponent();
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        Root.PointerExited += OnPointerExited;
    }

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
            var shadow = new Ellipse
            {
                Width = 34, Height = 9, Opacity = 0.28,
                Fill = new SolidColorBrush(Colors.Black),
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
            var img = new Image
            {
                Width = 52, Height = 52, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
            inner.Children.Add(mark);
            inner.Children.Add(shadow);
            inner.Children.Add(img);
            inner.Children.Add(glyph);
            cell.Child = inner;
            cell.Tag = SqName(r, c);
            cell.PointerPressed += OnCellPressed;
            Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
            BoardGrid.Children.Add(cell);
            _cells[r, c] = cell; _glyphs[r, c] = glyph; _marks[r, c] = mark;
            _imgs[r, c] = img; _shadows[r, c] = shadow;
        }
        BuildLabels();
    }

    /// <summary>Rank labels down the left, file labels along the bottom — outside the board.</summary>
    private void BuildLabels()
    {
        var fg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 158, 154, 147));
        RankLabels.Children.Clear(); RankLabels.RowDefinitions.Clear();
        for (var r = 0; r < 8; r++)
        {
            RankLabels.RowDefinitions.Add(new RowDefinition());
            var t = new TextBlock
            {
                Text = _flipped ? (r + 1).ToString() : (8 - r).ToString(),
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(t, r); RankLabels.Children.Add(t);
        }
        FileLabels.Children.Clear(); FileLabels.ColumnDefinitions.Clear();
        for (var c = 0; c < 8; c++)
        {
            FileLabels.ColumnDefinitions.Add(new ColumnDefinition());
            var t = new TextBlock
            {
                Text = _flipped ? ((char)('h' - c)).ToString() : ((char)('a' + c)).ToString(),
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, c); FileLabels.Children.Add(t);
        }
    }

    public string SqName(int r, int c) =>
        _flipped ? $"{(char)('h' - c)}{r + 1}" : $"{(char)('a' + c)}{8 - r}";

    public void Flip() { _flipped = !_flipped; Rebuild(); }

    private void Rebuild()
    {
        BoardGrid.Children.Clear(); Overlay.Children.Clear(); _targets.Clear();
        _built = false; EnsureBuilt();
        PositionRendered?.Invoke();
    }

    /// <summary>Raster set: key->image ("wp".."bk"), or null for glyph rendering.</summary>
    public void SetImages(Dictionary<string, ImageSource>? images, bool shadow)
    {
        _images = images; _showShadow = shadow;
    }

    public void SetPosition(IReadOnlyDictionary<string, (char Glyph, bool IsWhite, string? Font, string ImgKey)> pieces)
    {
        EnsureBuilt();
        for (var r = 0; r < 8; r++)
        for (var c = 0; c < 8; c++)
        {
            var name = SqName(r, c);
            var g = _glyphs[r, c]; var img = _imgs[r, c]; var sh = _shadows[r, c];
            _keys[r, c] = pieces.TryGetValue(name, out var pk) ? pk.ImgKey : null;
            if (pieces.TryGetValue(name, out var p) &&
                _images is not null && _images.TryGetValue(p.ImgKey, out var src))
            {
                g.Text = "";
                img.Source = src; img.Visibility = Visibility.Visible;
                sh.Visibility = _showShadow ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }
            img.Visibility = Visibility.Collapsed;
            sh.Visibility = Visibility.Collapsed;
            if (pieces.TryGetValue(name, out p))
            {
                g.Text = p.Glyph.ToString();
                if (p.Font is not null) g.FontFamily = new FontFamily(p.Font);
                g.Foreground = p.IsWhite
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Colors.Black);
            }
            else g.Text = "";
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
        Overlay.Children.Add(new Line
        {
            X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
            Stroke = new SolidColorBrush(col), StrokeThickness = 7, Opacity = .8,
            StrokeEndLineCap = PenLineCap.Triangle
        });
        var ux = dx / len; var uy = dy / len;
        var (px, py) = (-uy * 9, ux * 9);
        Overlay.Children.Add(new Polygon
        {
            Fill = new SolidColorBrush(col), Opacity = .8,
            Points = new PointCollection
            {
                new Point(ex + ux * 16, ey + uy * 16),
                new Point(ex + px, ey + py),
                new Point(ex - px, ey - py)
            }
        });
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

    private string? CellAt(Point p)
    {
        var c = (int)(p.X / 60); var r = (int)(p.Y / 60);
        return r is >= 0 and <= 7 && c is >= 0 and <= 7 ? SqName(r, c) : null;
    }

    private void OnCellPressed(object sender, PointerRoutedEventArgs e)
    {
        var sq = (string)((Border)sender).Tag;
        var cell = Rc(sq);
        _pressCell = cell;
        _dragFrom = sq;
        _dragging = false;
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is null || _pressCell is null) return;
        var pt = e.GetCurrentPoint(BoardArea).Position;
        if (!_dragging)
        {
            var (r, c) = _pressCell.Value;
            var cx = c * 60 + 30; var cy = r * 60 + 30;
            if (Math.Abs(pt.X - cx) + Math.Abs(pt.Y - cy) < 14) return;
            // begin drag if the source has a piece
            if (_keys[r, c] is null) { _dragFrom = null; return; }
            _dragging = true;
            if (_images is not null && _keys[r, c] is { } k && _images.TryGetValue(k, out var dragSrc))
            {
                _ghost = new Image
                {
                    Source = dragSrc, Width = 52, Height = 52, Stretch = Stretch.Uniform,
                    IsHitTestVisible = false, Opacity = .9
                };
            }
            _ghost ??= new TextBlock
            {
                Text = _glyphs[r, c].Text, FontSize = 44,
                FontFamily = _glyphs[r, c].FontFamily,
                Foreground = _glyphs[r, c].Foreground, IsHitTestVisible = false, Opacity = .9
            };
            Overlay.Children.Add(_ghost);
            _glyphs[r, c].Opacity = 0.35;
            _imgs[r, c].Opacity = 0.35;
            SquareClicked?.Invoke(_dragFrom); // selects the piece (shows dots)
        }
        if (_ghost is not null)
        {
            Canvas.SetLeft(_ghost, pt.X - 28);
            Canvas.SetTop(_ghost, pt.Y - 28);
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // cancel the drag if the pointer leaves the board while pressed
        if (_dragging)
        {
            if (_pressCell is { } pc) { _glyphs[pc.R, pc.C].Opacity = 1; _imgs[pc.R, pc.C].Opacity = 1; }
            _dragFrom = null; _pressCell = null; _dragging = false;
            if (_ghost is not null) { Overlay.Children.Remove(_ghost); _ghost = null; }
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var from = _dragFrom;
        var wasDragging = _dragging;
        var cell = _pressCell;
        _dragFrom = null; _pressCell = null; _dragging = false;
        if (_ghost is not null) { Overlay.Children.Remove(_ghost); _ghost = null; }
        if (cell is { } pc) { _glyphs[pc.R, pc.C].Opacity = 1; _imgs[pc.R, pc.C].Opacity = 1; }
        if (from is null) return;
        var to = CellAt(e.GetCurrentPoint(BoardArea).Position);
        if (wasDragging && to is not null && to != from)
            MoveAttempted?.Invoke(from, to);
        else if (!wasDragging)
            SquareClicked?.Invoke(from);
    }
}
