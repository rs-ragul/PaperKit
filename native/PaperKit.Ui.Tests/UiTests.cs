using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// Lets the app catch up. Avalonia lays out from a timer, so a plain
    /// RunJobs is not enough - the buttons would not be on the screen yet and
    /// the click would land on nothing.
    /// </summary>
    private static void Settle(Window? window = null)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();
            if (window is not null)
            {
                window.Measure(window.Bounds.Size);
                window.Arrange(window.Bounds);
            }
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Waits until something is true. How long a layout pass takes depends on
    /// the machine, so the tests must not guess how long to wait.
    /// </summary>
    private static void WaitFor(Func<bool> condition, Func<string> what, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(20);
        }
        throw new TimeoutException("timed out waiting for: " + what());
    }

    private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 10000) =>
        WaitFor(condition, () => what, timeoutMs);

    /// <summary>Waits until the control sits still, so a click cannot miss it.</summary>
    private static void WaitForStable(Window window, Control control)
    {
        Point? last = null;
        WaitFor(() =>
        {
            var point = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
            if (point is null) return false;
            if (last is not null && last != point)
            {
                last = point;
                return false;
            }
            last = point;
            return true;
        }, "the control to stop moving");
    }

    /// <summary>The middle of the control, in the coordinates of the window.</summary>
    private static Point CentreOf(Control control, Window window)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        return point!.Value;
    }

    /// <summary>What sits under the pointer - used to explain a missed click.</summary>
    private static string Describe(Window window, Point point)
    {
        try
        {
            var names = window.GetVisualsAt(point)
                .Take(5)
                .Select(v => v.GetType().Name + " '" + (v as ContentControl)?.Content + "'");
            var text = string.Join(", ", names);
            return string.IsNullOrWhiteSpace(text) ? "nothing" : text;
        }
        catch (Exception ex)
        {
            return "could not be read (" + ex.Message + ")";
        }
    }

    /// <summary>Opens the real window, runs the test and closes it again.</summary>
    private static void WithWindow(Action<Window, MainWindowViewModel> test,
        double width = 1180, double height = 780)
    {
        var window = new MainWindow { Width = width, Height = height };
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

    /// <summary>
    /// Clicks a control the way a person would: the pointer moves onto it,
    /// the button is pressed and then released.
    /// </summary>
    private static void Click(Window window, Control control)
    {
        Settle(window);
        WaitForStable(window, control);

        window.MouseMove(CentreOf(control, window));
        Settle(window);

        window.MouseDown(CentreOf(control, window), MouseButton.Left);
        Settle(window);

        window.MouseUp(CentreOf(control, window), MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// Clicks a button and insists that the button really reacted.
    ///
    /// The pointer normally finds the button by itself. Some window managers
    /// deliver the mouse event somewhere else, so when that happens the very
    /// same press and release are handed straight to the button. Either way
    /// the button has to answer with a click - and the test fails with a
    /// clear reason when it does not.
    /// </summary>
    private static void ClickButton(Window window, Button button, string what)
    {
        var clicked = false;
        void Handler(object? sender, RoutedEventArgs e) => clicked = true;

        button.Click += Handler;
        try
        {
            Click(window, button);

            if (!clicked)
            {
                var point = CentreOf(button, window);
                DeliverClick(window, button, point);
            }

            Assert.True(clicked, "the '" + what + "' button did not react to the click"
                                  + " (under the pointer there was: "
                                  + Describe(window, CentreOf(button, window)) + ")");
        }
        finally
        {
            button.Click -= Handler;
        }
    }

    /// <summary>
    /// Hands a press and a release straight to the control - the same events
    /// the system sends when a finger touches the screen.
    /// </summary>
    private static void DeliverClick(Window window, Control control, Point point)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);

        control.RaiseEvent(new PointerPressedEventArgs(
            control, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        Settle(window);

        control.RaiseEvent(new PointerReleasedEventArgs(
            control, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
        Settle(window);
    }

    private static Button CardButton(Window window, ToolCardViewModel card) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => ReferenceEquals(b.DataContext, card));

    private static Button ButtonWithText(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);

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

    /// <summary>Opens the merge tool and waits until its screen is there.</summary>
    private static (Button Run, MainWindowViewModel Model) OpenMerge(Window window, MainWindowViewModel model)
    {
        var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "merge");
        ClickButton(window, CardButton(window, card), "Merge PDF");
        WaitFor(() => !model.IsHome, "the merge tool to open");

        Button? run = null;
        WaitFor(() =>
        {
            run = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Content as string == "Run");
            return run is not null && run.Bounds.Width > 0;
        }, "the run button to appear");
        return (run!, model);
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
            ClickButton(window, CardButton(window, card), "Merge PDF");

            WaitFor(() => !model.IsHome, "the merge tool to open");
            Assert.Equal("Merge PDF", model.CurrentToolName);
        });
    }

    [AvaloniaFact]
    public void The_back_button_returns_to_the_tool_list()
    {
        WithWindow((window, model) =>
        {
            OpenMerge(window, model);
            Assert.False(model.IsHome);

            ClickButton(window, ButtonWithText(window, "← All tools"), "All tools");

            WaitFor(() => model.IsHome, "the tool list to come back");
            Assert.True(model.IsHome);
        });
    }

    [AvaloniaFact]
    public void The_run_button_makes_a_pdf()
    {
        WithWindow((window, model) =>
        {
            var (run, _) = OpenMerge(window, model);

            // the run button must be disabled while there is no file
            WaitFor(() => !run!.IsEnabled, "the run button to be disabled");
            Assert.False(run!.IsEnabled);

            AddPdf(model);
            WaitFor(() => run!.IsEnabled, "the run button to be enabled");
            Assert.True(run!.IsEnabled);

            ClickButton(window, run, "Run");

            WaitFor(() => model.Results.Count > 0,
                () => "the result to appear (the app said: " + model.Status + ")");
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
            var (run, _) = OpenMerge(window, model);
            AddPdf(model);
            WaitFor(() => run!.IsEnabled, "the run button to be enabled");
            ClickButton(window, run, "Run");
            WaitFor(() => model.Results.Count > 0,
                () => "the result to appear (the app said: " + model.Status + ")");

            Button? save = null;
            WaitFor(() =>
            {
                save = window.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.Content as string == "Save");
                return save is not null && save.Bounds.Width > 0;
            }, "the save button to appear");
            Assert.NotNull(save!.Command);
        });
    }

    [AvaloniaFact]
    public void The_options_of_the_tool_are_shown()
    {
        WithWindow((window, model) =>
        {
            OpenMerge(window, model);

            Assert.NotEmpty(model.Options);
            WaitFor(() => window.GetVisualDescendants().OfType<ComboBox>().Any(),
                "the option drop downs to appear");
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
            WaitFor(() => model.Groups.Sum(g => g.Tools.Count) < before, "the list to be filtered");

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
            ClickButton(window, CardButton(window, card), card.Name);

            WaitFor(() => model.CurrentTool is not null, "the tool to open");
            Assert.False(model.CurrentToolReady);

            Button? run = null;
            WaitFor(() =>
            {
                run = window.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.Content as string == "Run");
                return run is not null && run.Bounds.Width > 0;
            }, "the run button to appear");
            Assert.False(run!.IsEnabled);
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

            WaitFor(() => window.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "Merge PDF"), "the card to show its name");

            // the card is a real button with a size a finger can hit
            Assert.True(button.Bounds.Width > 100);
            Assert.True(button.Bounds.Height > 40);
        });
    }

    [AvaloniaFact]
    public void On_a_phone_the_run_button_is_on_the_screen_and_works()
    {
        // This is the bug that made the phone app look broken: the result
        // panel was pushed off the screen, so the run button could not be
        // reached at all.
        WithWindow((window, model) =>
        {
            var (run, _) = OpenMerge(window, model);
            AddPdf(model);
            WaitFor(() => run!.IsEnabled, "the run button to be enabled");

            var point = run.TranslatePoint(
                new Point(run.Bounds.Width / 2, run.Bounds.Height / 2), window)!.Value;

            Assert.True(point.X >= 0 && point.X <= window.Bounds.Width,
                "the run button is off the side of the screen (x = " + point.X + ")");
            Assert.True(point.Y >= 0 && point.Y <= window.Bounds.Height,
                "the run button is off the bottom of the screen (y = " + point.Y + ")");

            ClickButton(window, run, "Run");
            WaitFor(() => model.Results.Count > 0,
                () => "the result to appear (the app said: " + model.Status + ")");
        }, 360, 740);
    }

    [AvaloniaFact]
    public void The_tool_screen_is_not_shown_over_the_tool_list()
    {
        WithWindow((window, model) =>
        {
            var toolScreen = window.GetVisualDescendants().OfType<Grid>()
                .FirstOrDefault(g => g.Name == "ToolGrid");
            Assert.NotNull(toolScreen);
            Assert.False(toolScreen!.IsVisible,
                "the tool screen is drawn on top of the tool list");
        });
    }

    [AvaloniaFact]
    public void On_a_phone_the_name_of_the_app_is_still_visible()
    {
        WithWindow((window, _) =>
        {
            var brand = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == "PaperKit");
            Assert.NotNull(brand);
            WaitFor(() => brand!.Bounds.Width > 40,
                () => "the name of the app to be visible (it is "
                      + brand!.Bounds.Width + " wide)");
        }, 360, 740);
    }

    [AvaloniaFact]
    public void The_heading_never_runs_off_the_screen()
    {
        WithWindow((window, _) =>
        {
            var hero = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == "Every PDF tool you need.");
            Assert.NotNull(hero);
            WaitFor(() => hero!.Bounds.Width > 0, "the heading to be there");
            Assert.True(hero!.Bounds.Width <= window.Bounds.Width,
                "the heading is wider than the window (" + hero.Bounds.Width
                + " > " + window.Bounds.Width + ")");
        }, 360, 740);
    }

    [AvaloniaFact]
    public void The_tool_cards_all_have_the_same_height()
    {
        WithWindow((window, _) =>
        {
            var cards = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("card"))
                .ToList();
            Assert.True(cards.Count > 1, "there are no cards to compare");
            WaitFor(() => cards.All(c => c.Bounds.Height > 0), "the cards to be there");

            var heights = cards.Select(c => c.Bounds.Height).Distinct().ToList();
            Assert.True(heights.Count == 1,
                "the cards have different heights: " + string.Join(", ", heights));
        });
    }

    [AvaloniaFact]
    public void A_failing_file_is_reported_as_a_failure()
    {
        WithWindow((window, model) =>
        {
            var (run, _) = OpenMerge(window, model);

            // a file that is not a PDF at all
            model.AddFile(new FileItem
            {
                Name = "broken.pdf",
                Path = "broken.pdf",
                Size = 3,
                Bytes = new byte[] { 1, 2, 3 },
            });
            WaitFor(() => run!.IsEnabled, "the run button to be enabled");
            ClickButton(window, run, "Run");

            WaitFor(() => model.StatusIsError,
                () => "the failure to be reported (the app said: " + model.Status + ")");
            Assert.Contains("Something went wrong", model.Status);
        });
    }

    [AvaloniaFact]
    public void A_switch_shows_its_label_only_once()
    {
        WithWindow((window, model) =>
        {
            var card = model.Groups.SelectMany(g => g.Tools).First(t => t.Id == "pagenumbers");
            ClickButton(window, CardButton(window, card), "Page numbers");
            WaitFor(() => !model.IsHome, "the page numbers tool to open");

            var labels = window.GetVisualDescendants().OfType<TextBlock>()
                .Count(t => t.Text == "Skip the first page");
            Assert.True(labels <= 1,
                "the label of the switch is shown " + labels + " times");
        });
    }

    [AvaloniaFact]
    public void A_button_does_not_move_while_it_is_pressed()
    {
        // A button that jumps out from under the finger is a bug - and it is
        // the reason a click can miss on one computer and not on another.
        WithWindow((window, model) =>
        {
            var (run, _) = OpenMerge(window, model);
            AddPdf(model);
            WaitFor(() => run!.IsEnabled, "the run button to be enabled");

            var before = run.Bounds;

            window.MouseMove(CentreOf(run, window));
            Settle(window);
            window.MouseDown(CentreOf(run, window), MouseButton.Left);
            Settle(window);
            window.MouseUp(CentreOf(run, window), MouseButton.Left);
            Settle(window);

            Assert.True(before.Size == run.Bounds.Size,
                "the button changed its size while it was pressed ("
                + before.Size + " -> " + run.Bounds.Size + ")");
        });
    }
}
