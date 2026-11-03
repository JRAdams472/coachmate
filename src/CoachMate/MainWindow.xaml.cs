using Microsoft.UI.Xaml;

namespace CoachMate;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new(1080, 660));
    }
}
