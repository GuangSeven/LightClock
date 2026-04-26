using Microsoft.UI.Xaml;

namespace WidgetClock;

/// <summary>
/// Entry point for the WidgetClock application.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        this.InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
