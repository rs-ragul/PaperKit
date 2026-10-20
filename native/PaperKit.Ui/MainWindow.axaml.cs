using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PaperKit.App;

/// <summary>
/// The desktop shell. Windows gets a real window with the shared view inside;
/// Android uses the view itself as the single screen of the app.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
