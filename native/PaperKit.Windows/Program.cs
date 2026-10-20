using Avalonia;
using PaperKitApp = PaperKit.App.App;

namespace PaperKit.Windows;

internal static class Program
{
    /// <summary>The entry point of the Windows (and Linux/macOS) build.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        PaperKit.Core.Fonts.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by the Avalonia previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PaperKitApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
