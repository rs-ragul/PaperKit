using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PaperKit.App.ViewModels;
using PaperKitApp = PaperKit.App.App;
using PaperKitView = PaperKit.App.MainView;
using PaperKitWindow = PaperKit.App.MainWindow;

namespace PaperKit.Shot;

/// <summary>
/// Renders the real window to a PNG. The same layout that ships to Windows
/// and Android, so the screenshots are never a mock-up.
///
///   dotnet run --project native/PaperKit.Shot -- out.png 1180 780
///   dotnet run --project native/PaperKit.Shot -- phone.png 360 740
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "paperkit-ui.png";
        var width = args.Length > 1 ? double.Parse(args[1]) : 1180;
        var height = args.Length > 2 ? double.Parse(args[2]) : 780;

        AppBuilder.Configure<PaperKitApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var window = new PaperKitWindow { Width = width, Height = height };
        window.Show();

        Settle();
        Shot(window, output);

        // the tool screen as well
        var view = window.GetVisualDescendants().OfType<PaperKitView>().First();
        var model = (MainWindowViewModel)view.DataContext!;
        var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
        model.Open(card);
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "valid.pdf");
        model.AddFile(new FileItem
        {
            Name = "annual-report-2026.pdf",
            Path = "annual-report-2026.pdf",
            Size = new FileInfo(fixture).Length,
            Bytes = File.ReadAllBytes(fixture),
        });
        Settle();
        Shot(window, output.Replace(".png", "-tool.png"));

        // and the same tool screen with a result in it
        var run = model.RunAsync();
        for (var i = 0; i < 300 && !run.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Settle();
        Shot(window, output.Replace(".png", "-result.png"));
    }

    private static void Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void Shot(Window window, string path)
    {
        // draw what was just measured, then take the frame
        window.InvalidateVisual();
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        var frame = window.CaptureRenderedFrame()!;
        frame.Save(path);
        Console.WriteLine($"saved {path}");
    }
}
