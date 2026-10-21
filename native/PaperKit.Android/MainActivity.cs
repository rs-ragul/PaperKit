using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using PaperKitApp = PaperKit.App.App;

namespace PaperKit.Android;

/// <summary>
/// The native Android entry point. The window, the tools and the PDF engine
/// are the same code the Windows app runs - only the shell is different.
///
/// If anything goes wrong while the app starts, the reason is written on the
/// screen. A white screen tells nobody anything.
/// </summary>
[Activity(
    Label = "PaperKit",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<PaperKitApp>
{
    private const string Tag = "PaperKit";

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        builder.AfterSetup(_ => PaperKit.Core.Fonts.Install());

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // a crash anywhere in the app must end up on the screen, not in a
        // white window
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Log.Error(Tag, "unhandled exception: " + e.Exception);
            e.Handled = true;
            ShowError(e.Exception);
        };

        try
        {
            Log.Info(Tag, "starting the app");
            base.OnCreate(savedInstanceState);
            Log.Info(Tag, "the interface is up");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, "the app could not start: " + ex);
            ShowError(ex);
            return;
        }

        EnsureThereIsAView();
        WatchForMissingInterface();
    }

    /// <summary>
    /// The phone has to show the interface, no matter what. The app sets the
    /// view itself when it starts; this is the safety net for the case where
    /// that did not happen - a phone without a view shows nothing but white.
    /// </summary>
    private void EnsureThereIsAView()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is ISingleViewApplicationLifetime single && single.MainView is null)
            {
                Log.Warn(Tag, "the app did not set a view - doing it here");
                single.MainView = PaperKitApp.CreateMainView();
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, "the interface could not be shown: " + ex);
            ShowError(ex);
        }
    }

    /// <summary>
    /// If the interface is still not on the screen a moment after the app
    /// started, the reason is written on the screen. A white window tells
    /// nobody anything.
    /// </summary>
    private void WatchForMissingInterface()
    {
        _ = Task.Delay(12000).ContinueWith(_ =>
        {
            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is ISingleViewApplicationLifetime single)
                {
                    var view = single.MainView;
                    if (view is not null && !view.IsAttachedToVisualTree())
                    {
                        Log.Error(Tag, "the interface was built but never shown");
                        ShowError(new InvalidOperationException(
                            "The interface was built but never shown on the screen."));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(Tag, "the watchdog failed: " + ex);
            }
        });
    }

    /// <summary>Shows the reason on the screen instead of an empty window.</summary>
    private void ShowError(Exception error)
    {
        var text = "PaperKit could not start\n\n"
                   + error.GetType().Name + ": " + error.Message + "\n\n"
                   + (error.StackTrace ?? string.Empty);

        try
        {
            RunOnUiThread(() =>
            {
                var view = new TextView(this) { Text = text, TextSize = 12f };
                view.SetBackgroundColor(Color.White);
                view.SetTextColor(Color.Firebrick);
                SetContentView(view);
            });
        }
        catch (Exception)
        {
            // nothing else we can do
        }
    }
}
