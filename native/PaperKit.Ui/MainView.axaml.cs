using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaperKit.App.ViewModels;
using PaperKit.Core;

namespace PaperKit.App;

/// <summary>
/// The whole application surface. It is a control, not a window, so the very
/// same UI can live in a desktop window on Windows and be the single view of
/// the app on Android.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>Below this width the app switches to the phone layout.</summary>
    private const double CompactWidth = 860;

    private readonly MainWindowViewModel _model = new();
    private bool _compact;
    private bool _layoutApplied;

    public MainView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Build();

        _model.PickFilesRequested += async (_, _) => await PickFilesAsync();
        _model.RunRequested += async (_, _) => await _model.RunAsync();
        _model.SaveRequested += async (_, item) => await SaveAsync(item as ResultItem);

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(KeyDownEvent, OnKeyDown);

        // The layout is changed on the next frame: the grid cannot be
        // rebuilt while Avalonia is measuring it.
        SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            Dispatcher.UIThread.Post(() => ApplyLayout(width), DispatcherPriority.Loaded);
        };
    }

    /// <summary>The file and folder picker of the platform we run on.</summary>
    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    // ------------------------------------------------------------ the layout

    /// <summary>
    /// The app has to look right on a big screen and on a phone.
    ///
    /// On a narrow screen the search moves under the brand, the tool screen
    /// becomes a single column and the run button is pinned to the bottom -
    /// so it can never be off the screen or out of reach.
    /// </summary>
    private void ApplyLayout(double width)
    {
        var compact = width < CompactWidth;
        if (_layoutApplied && compact == _compact) return;
        _compact = compact;
        _layoutApplied = true;

        Tagline.IsVisible = !compact;
        BadgeText.Text = compact ? "OFFLINE" : "100% OFFLINE";
        DropHintDesktop.IsVisible = !compact;
        DropHintPhone.IsVisible = compact;

        if (compact)
        {
            // the search gets a row of its own
            Grid.SetColumn(SearchBox, 0);
            Grid.SetRow(SearchBox, 1);
            Grid.SetColumnSpan(SearchBox, 4);
            SearchBox.Width = double.NaN;
            SearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            SearchBox.Margin = new Thickness(0, 10, 0, 0);

            // one column, and the result stays in reach at the bottom
            ToolGrid.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            ToolGrid.RowDefinitions = new RowDefinitions("*,Auto");
            Grid.SetColumn(ToolLeft, 0);
            Grid.SetRow(ToolLeft, 0);
            Grid.SetColumn(ToolRight, 0);
            Grid.SetRow(ToolRight, 1);
            ToolLeft.Margin = new Thickness(0);
            ToolRight.Margin = new Thickness(0, 12, 0, 0);
            ToolRight.MaxHeight = 300;
            ResultEmpty.IsVisible = false;
            ToolGrid.Margin = new Thickness(16, 16, 16, 16);
        }
        else
        {
            Grid.SetColumn(SearchBox, 2);
            Grid.SetRow(SearchBox, 0);
            Grid.SetColumnSpan(SearchBox, 1);
            SearchBox.Width = 280;
            SearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            SearchBox.Margin = new Thickness(0, 0, 14, 0);

            // two columns: the files and the options, and the result
            ToolGrid.ColumnDefinitions = new ColumnDefinitions("520,*");
            ToolGrid.RowDefinitions = new RowDefinitions("*,Auto");
            Grid.SetColumn(ToolLeft, 0);
            Grid.SetRow(ToolLeft, 0);
            Grid.SetColumn(ToolRight, 1);
            Grid.SetRow(ToolRight, 0);
            ToolLeft.Margin = new Thickness(0, 0, 24, 0);
            ToolRight.Margin = new Thickness(0);
            ToolRight.MaxHeight = double.PositiveInfinity;
            ResultEmpty.IsVisible = true;
            ToolGrid.Margin = new Thickness(30, 22, 30, 30);
        }

    }

    // ------------------------------------------------------------ drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data?.Contains(DataFormats.Files) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data is not { } data) return;
        if (!data.Contains(DataFormats.Files)) return;

        var paths = new List<string>();
        // the drag data can be empty, and a dropped item can be a web link
        foreach (var item in data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
        {
            if (item is null) continue;
            var local = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local)) paths.Add(local);
        }
        await AddPathsAsync(paths);
    }

    // ------------------------------------------------------------- back key

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // the Android back button goes back to the tool list, not out of the app
        if (e.Key != Key.Back) return;
        if (_model.IsHome) return;
        _model.Back();
        e.Handled = true;
    }

    // --------------------------------------------------------------- picking

    private async Task PickFilesAsync()
    {
        var storage = Storage;
        if (storage is null) return;

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Choose your PDF files",
                FileTypeFilter = new[] { FilePickerFileTypes.Pdf, FilePickerFileTypes.All },
            });
        }
        catch (Exception ex)
        {
            _model.Status = "Could not open the file picker: " + ex.Message;
            return;
        }

        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                _model.AddFile(new FileItem
                {
                    Name = file.Name,
                    Path = file.TryGetLocalPath() ?? file.Name,
                    Size = buffer.Length,
                    Bytes = buffer.ToArray(),
                });
            }
            catch (Exception)
            {
                // a file that cannot be read is simply skipped
            }
        }
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                _model.AddFile(new FileItem
                {
                    Name = info.Name,
                    Path = info.FullName,
                    Size = info.Length,
                    Bytes = await File.ReadAllBytesAsync(info.FullName),
                });
            }
            catch (Exception)
            {
                // skip what cannot be read
            }
        }
    }

    // ---------------------------------------------------------------- save

    private async Task SaveAsync(ResultItem? item)
    {
        if (item is null) return;
        var storage = Storage;
        if (storage is null) return;

        IStorageFile? file;
        try
        {
            file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = item.Name,
                DefaultExtension = "pdf",
                FileTypeChoices = new[] { FilePickerFileTypes.Pdf },
            });
        }
        catch (Exception ex)
        {
            _model.Status = "Could not open the save dialog: " + ex.Message;
            return;
        }
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(item.Bytes);
        await stream.FlushAsync();
    }
}
