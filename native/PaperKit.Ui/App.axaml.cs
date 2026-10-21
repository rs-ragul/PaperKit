using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace PaperKit.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
            // the PDF tools need a font resolver before they draw anything
            PaperKit.Core.Fonts.Install();
        }
        catch (Exception ex)
        {
            // A phone that cannot load the interface must say so - a white
            // screen tells nobody anything.
            StartupFailed = ex;
        }
    }

    /// <summary>Set when the interface could not be loaded.</summary>
    public static Exception? StartupFailed { get; private set; }

    /// <summary>
    /// The view the app shows. A desktop window holds it, a phone shows it
    /// directly - a Window cannot be used on Android.
    /// </summary>
    public static Control CreateMainView() => new MainView();

    public override void OnFrameworkInitializationCompleted()
    {
        // Windows and the desktop get a window, Android gets the same view as
        // the single screen of the app - without this the phone shows nothing.
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = CreateMainView();
            }
        }
        catch (Exception ex)
        {
            StartupFailed = ex;
        }

        if (StartupFailed is not null && ApplicationLifetime is ISingleViewApplicationLifetime phone)
        {
            phone.MainView = BuildErrorView(StartupFailed);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A plain screen with the reason on it. Better a message on the phone
    /// than a white screen.
    /// </summary>
    private static Control BuildErrorView(Exception error)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "PaperKit could not start",
            FontSize = 22,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Brushes.Firebrick,
        });
        panel.Children.Add(new TextBlock
        {
            Text = error.GetType().Name + ": " + error.Message,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = error.StackTrace ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.7,
        });

        return new Border
        {
            Background = Brushes.White,
            Child = panel,
        };
    }
}
