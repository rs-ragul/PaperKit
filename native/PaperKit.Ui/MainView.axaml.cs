using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
    private readonly MainWindowViewModel _model = new();

    public MainView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Build();

        _model.PickFilesRequested += async (_, _) => await PickFilesAsync();
        _model.RunRequested += (_, _) => Run();
        _model.SaveRequested += async (_, item) => await SaveAsync(item as ResultItem);

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>The file and folder picker of the platform we run on.</summary>
    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

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

    // ----------------------------------------------------------------- run

    private void Run()
    {
        try
        {
            _model.Run();
        }
        catch (Exception ex)
        {
            _model.Status = "Something went wrong: " + ex.Message;
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
