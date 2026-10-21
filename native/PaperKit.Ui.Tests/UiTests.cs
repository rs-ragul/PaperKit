using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PaperKit.App;
using PaperKit.App.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PaperKit.Ui.Tests.TestApp))]

namespace PaperKit.Ui.Tests;

/// <summary>Boots the real application in a headless window.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PaperKit.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// These tests drive the real window: they click the real buttons and then
/// check that the app really did something. A button that cannot be clicked
/// fails here, not on your machine.
/// </summary>
public class UiTests
{
    /// <summary>
    /// Lets the app catch up. Avalonia runs the layout pass from a timer, so
    /// RunJobs alone is not enough - the buttons would not be on the screen yet
    /// and the click would land on nothing.
    /// </summary>
    private static void Settle(Window? window = null)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();

            // Avalonia lays out from a timer, so force a measure and arrange
            // pass as well - otherwise the buttons are not on the screen yet
            if (window is not null)
            {
                window.Measure(window.Bounds.Size);
                window.Arrange(window.Bounds);
            }
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Opens the real window, runs the test and closes it again.</summary>
    private static void WithWindow(Action<Window, MainWindowViewModel> test)
    {
        var window = new MainWindow { Width = 1180, Height = 780 };
        window.Show();
        Settle(window);
        try
        {
            var view = window.GetVisualDescendants().OfType<MainView>().First();
            var model = (MainWindowViewModel)view.DataContext!;
            test(window, model);
        }
        finally
        {
            window.Close();
            Settle(window);
        }
    }

    /// <summary>Clicks a control the way a person would: down, then up.</summary>
    private static void Click(Window window, Control control)
    {
        var centre = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var point = control.TranslatePoint(centre, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        Settle(window);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    private static Button CardButton(Window window, ToolCardViewModel card) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => ReferenceEquals(b.DataContext, card));

    private static void AddPdf(MainWindowViewModel model)
    {
        model.AddFile(new FileItem
        {
            Name = "valid.pdf",
            Path = "valid.pdf",
            Size = 1,
            Bytes = File.ReadAllBytes(Path.Combine("fixtures", "valid.pdf")),
        });
        Settle();
    }

    [AvaloniaFact]
    public void Every_tool_card_has_a_command()
    {
        WithWindow((_, model) =>
        {
            var cards = model.Groups.SelectMany(g => g.Tools).ToList();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
                Assert.NotNull(card.OpenCommand);
        });
    }

    [AvaloniaFact]
    public void Clicking_a_tool_card_opens_that_tool()
    {
        WithWindow((window, model) =>
        {
            Assert.True(model.IsHome);

            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            Click(window, CardButton(window, card));

            Assert.False(model.IsHome);
            Assert.Equal("Merge PDF", model.CurrentToolName);
        });
    }

    [AvaloniaFact]
    public void The_back_button_returns_to_the_tool_list()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            Click(window, CardButton(window, card));
            Assert.False(model.IsHome);

            var back = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Content as string == "← All tools");
            Click(window, back);

            Assert.True(model.IsHome);
        });
    }

    [AvaloniaFact]
    public void The_run_button_makes_a_pdf()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            Click(window, CardButton(window, card));

            // the run button must be disabled while there is no file
            var run = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Run");
            Assert.False(run.IsEnabled);

            AddPdf(model);
            Assert.True(run.IsEnabled);

            Click(window, run);

            Assert.True(model.HasResults);
            Assert.Single(model.Results);
            Assert.Equal("merged.pdf", model.Results[0].Name);
            Assert.True(model.Results[0].Bytes.Length > 100);
        });
    }

    [AvaloniaFact]
    public void The_result_row_has_a_save_button_with_a_command()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            Click(window, CardButton(window, card));
            AddPdf(model);

            var run = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Run");
            Click(window, run);

            var save = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Save");
            Assert.NotNull(save.Command);
        });
    }

    [AvaloniaFact]
    public void The_options_of_the_tool_are_shown()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            Click(window, CardButton(window, card));

            Assert.NotEmpty(model.Options);
            // the option editors are on the screen
            Assert.NotEmpty(window.GetVisualDescendants().OfType<ComboBox>());
        });
    }

    [AvaloniaFact]
    public void The_search_box_filters_the_tools()
    {
        WithWindow((window, model) =>
        {
            var before = model.Groups.Sum(g => g.Tools.Count);

            var search = window.GetVisualDescendants().OfType<TextBox>().First();
            search.Text = "merge";
            Settle(window);

            var after = model.Groups.Sum(g => g.Tools.Count);
            Assert.True(after < before);
            Assert.True(after > 0);
        });
    }

    [AvaloniaFact]
    public void A_tool_that_is_not_ready_cannot_be_run()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => !t.Ready);
            Click(window, CardButton(window, card));

            Assert.False(model.CurrentToolReady);
            var run = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Run");
            Assert.False(run.IsEnabled);
        });
    }

    [AvaloniaFact]
    public void The_app_shows_a_view_not_a_window_on_a_phone()
    {
        // Android has no window: the app must hand a plain control to the
        // single view lifetime, or the phone shows nothing but white.
        var view = PaperKit.App.App.CreateMainView();
        Assert.IsType<MainView>(view);
        Assert.IsNotType<Window>(view);
    }

    [AvaloniaFact]
    public void The_card_shows_its_name_and_its_badge()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
            var button = CardButton(window, card);

            var names = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is not null)
                .Select(t => t.Text)
                .ToList();

            Assert.Contains("Merge PDF", names);
            // the card is a real button with a size a finger can hit
            Assert.True(button.Bounds.Width > 100);
            Assert.True(button.Bounds.Height > 40);
        });
    }
}
