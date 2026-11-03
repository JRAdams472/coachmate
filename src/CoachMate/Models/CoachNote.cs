using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CoachMate.Models;

public sealed class CoachNote
{
    public string Title { get; set; } = "";
    public string Symbol { get; set; } = "";
    public bool IsBad { get; set; }
    public bool IsEngine { get; set; }
    public List<string> Why { get; } = new();
    public string? Line { get; set; }
    public string? Refutation { get; set; }
    public string? AiContext { get; set; }
    public SolidColorBrush SymbolBrush => new(IsBad ? Color.FromArgb(255, 202, 52, 49) : Color.FromArgb(255, 129, 182, 76));
}
