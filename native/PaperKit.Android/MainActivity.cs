using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using PaperKitApp = PaperKit.App.App;

namespace PaperKit.Android;

/// <summary>
/// The native Android entry point. The window, the tools and the PDF engine
/// are the same code the Windows app runs - only the shell is different.
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
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        builder.AfterSetup(_ => PaperKit.Core.Fonts.Install());
}
