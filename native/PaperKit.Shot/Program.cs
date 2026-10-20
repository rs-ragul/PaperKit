using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using PaperKit.App.ViewModels;
using PaperKitApp = PaperKit.App.App;
using PaperKitWindow = PaperKit.App.MainWindow;

namespace PaperKit.Shot;

/// <summary>
/// Renders the real window to a PNG. The same layout that ships to Windows
/// and Android, so the screenshots are never a mock-up.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "paperkit-ui.png";

        AppBuilder.Configure<PaperKitApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var window = new PaperKitWindow { Width = 1180, Height = 780 };
        window.Show();

        // let the layout settle before the frame is taken
        for (var i = 0; i < 40; i++) Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        frame.Save(output);
        Console.WriteLine($"saved {output}");

        // the tool screen as well
        var model = (MainWindowViewModel)window.DataContext!;
        var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
        model.Open(card);
        for (var i = 0; i < 40; i++) Dispatcher.UIThread.RunJobs();
        var toolFrame = window.CaptureRenderedFrame();
        toolFrame.Save(output.Replace(".png", "-tool.png"));
        Console.WriteLine($"saved {output.Replace(".png", "-tool.png")}");
    }
}
