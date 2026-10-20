using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PaperKit.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // the PDF tools need a font resolver before they draw anything
        PaperKit.Core.Fonts.Install();
    }

    /// <summary>
    /// The view the app shows. A desktop window holds it, a phone shows it
    /// directly - a Window cannot be used on Android.
    /// </summary>
    public static Control CreateMainView() => new MainView();

    public override void OnFrameworkInitializationCompleted()
    {
        // Windows and the desktop get a window, Android gets the same view as
        // the single screen of the app - without this the phone shows nothing.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
