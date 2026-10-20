using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PaperKit.App.ViewModels;
using PaperKit.Core;

namespace PaperKit.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
        _model.Build();

        _model.PickFilesRequested += async (_, _) => await PickFilesAsync();
        _model.RunRequested += (_, _) => Run();
        _model.SaveRequested += async (_, item) => await SaveAsync(item as ResultItem);

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ------------------------------------------------------------ drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        await AddPathsAsync(files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!));
    }

    // --------------------------------------------------------------- picking

    private async Task PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Choose your PDF files",
            FileTypeFilter = new[] { FilePickerFileTypes.Pdf, FilePickerFileTypes.All },
        });

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

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = item.Name,
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { FilePickerFileTypes.Pdf },
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(item.Bytes);
        await stream.FlushAsync();
    }
}
