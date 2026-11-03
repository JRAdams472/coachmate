using Microsoft.UI.Xaml;

namespace CoachMate;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coachmate-crash.txt"),
                ex.ToString());
            throw;
        }
    }
}
