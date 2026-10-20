using System.Collections.ObjectModel;
using PaperKit.Core;

namespace PaperKit.App.ViewModels;

/// <summary>A file the user picked.</summary>
public sealed class FileItem
{
    /// <summary>Filled in by the window model - removes this file from the list.</summary>
    public RelayCommand? RemoveCommand { get; set; }

    public required string Name { get; init; }
    public required string Path { get; init; }
    public required long Size { get; init; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string SizeText => Format(Size);

    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };
}

/// <summary>A file a tool produced.</summary>
public sealed class ResultItem
{
    /// <summary>Filled in by the window model - saves this result to disk.</summary>
    public RelayCommand? SaveCommand { get; set; }

    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
    public string SizeText => FileItem.Format(Bytes.Length);
}

/// <summary>Base class of the option rows the tool view builds.</summary>
public abstract class OptionItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    protected string _value = string.Empty;
    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) RaiseValueChanged(); }
    }
    protected virtual void RaiseValueChanged() { }
}

/// <summary>A drop-down.</summary>
public sealed class ChoiceOption : OptionItem
{
    public required string[] Choices { get; init; }
    protected override void RaiseValueChanged() => Raise(nameof(SelectedIndex));
    public int SelectedIndex
    {
        get
        {
            var index = Array.IndexOf(Choices, Value);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (value >= 0 && value < Choices.Length) Value = Choices[value];
        }
    }
}

/// <summary>A single line of text.</summary>
public sealed class TextOption : OptionItem
{
}

/// <summary>A number.</summary>
public sealed class NumberOption : OptionItem
{
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public string? Suffix { get; init; }

    public double Number
    {
        get => double.TryParse(Value, out var number) ? number : Minimum;
        set => Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>A switch.</summary>
public sealed class ToggleOption : OptionItem
{
    public bool IsOn
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }
}

/// <summary>A colour.</summary>
public sealed class ColorOption : OptionItem
{
}

/// <summary>One tool in the home grid.</summary>
public sealed class ToolCardViewModel : ObservableObject
{
    /// <summary>Filled in by the window model - opens this tool.</summary>
    public RelayCommand? OpenCommand { get; set; }

    public required ToolDescriptor Descriptor { get; init; }
    public string Id => Descriptor.Id;
    public string Name => Descriptor.Name;
    public string Blurb => Descriptor.Blurb;
    public string Icon => Icons.Path(Descriptor.Icon);
    public bool Ready => Descriptor.Ready;
    public bool NotReady => !Descriptor.Ready;
}

/// <summary>A group of tools.</summary>
public sealed class ToolGroupViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required IReadOnlyList<ToolCardViewModel> AllTools { get; init; }
    public ObservableCollection<ToolCardViewModel> Tools { get; init; } = new();
    public bool IsEmpty => Tools.Count == 0;
}

/// <summary>
/// The state of the window: the home grid, the tool screen and the result.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private string _search = string.Empty;
    private ToolCardViewModel? _currentTool;
    private string _status = string.Empty;
    private bool _busy;

    public MainWindowViewModel()
    {
        PickFilesCommand = new RelayCommand(_ => PickFilesRequested?.Invoke(this, EventArgs.Empty));
        RunCommand = new RelayCommand(_ => RunRequested?.Invoke(this, EventArgs.Empty));
        SaveCommand = new RelayCommand(p =>
        {
            if (p is ResultItem item) SaveRequested?.Invoke(this, item);
        });
        BackCommand = new RelayCommand(_ => Back());
        RemoveFileCommand = new RelayCommand(p => RemoveFile(p as FileItem));
        OpenCommand = new RelayCommand(p =>
        {
            if (p is ToolCardViewModel card) Open(card);
        });
    }

    // ----------------------------------------------------------------- events

    public event EventHandler? PickFilesRequested;
    public event EventHandler? RunRequested;
    public event EventHandler<object>? SaveRequested;

    // --------------------------------------------------------------- commands

    public RelayCommand PickFilesCommand { get; }
    public RelayCommand RunCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RemoveFileCommand { get; }
    public RelayCommand OpenCommand { get; }

    // ----------------------------------------------------------------- state

    public ObservableCollection<ToolGroupViewModel> Groups { get; } = new();
    public ObservableCollection<FileItem> Files { get; } = new();
    public ObservableCollection<ResultItem> Results { get; } = new();
    public ObservableCollection<OptionItem> Options { get; } = new();

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            ApplyFilter();
            Raise(nameof(IsHome));
        }
    }

    public ToolCardViewModel? CurrentTool
    {
        get => _currentTool;
        private set
        {
            if (!Set(ref _currentTool, value)) return;
            Raise(nameof(IsHome));
            Raise(nameof(CurrentToolName));
            Raise(nameof(CurrentToolBlurb));
            Raise(nameof(CurrentToolReady));
        }
    }

    public bool IsHome => CurrentTool is null;
    public bool CurrentToolReady => CurrentTool?.Ready ?? false;

    public string CurrentToolName => CurrentTool?.Name ?? string.Empty;
    public string CurrentToolBlurb => CurrentTool?.Blurb ?? string.Empty;

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public bool Busy
    {
        get => _busy;
        set { if (Set(ref _busy, value)) Raise(nameof(CanRun)); }
    }

    public bool CanRun => !Busy && Files.Count > 0 && CurrentTool is { Ready: true };

    public bool HasResults => Results.Count > 0;

    /// <summary>Builds the home grid from the catalogue.</summary>
    public void Build()
    {
        Groups.Clear();
        foreach (var group in ToolCatalog.Groups)
        {
            var cards = group.Tools
                .Select(t => new ToolCardViewModel { Descriptor = t, OpenCommand = OpenCommand })
                .ToList();
            Groups.Add(new ToolGroupViewModel
            {
                Title = group.Title,
                Subtitle = group.Subtitle,
                AllTools = cards,
                Tools = new ObservableCollection<ToolCardViewModel>(cards),
            });
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = Search.Trim();
        foreach (var group in Groups)
        {
            var all = group.AllTools;
            var visible = query.Length == 0
                ? all
                : all.Where(t =>
                    t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Blurb.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            group.Tools.Clear();
            foreach (var tool in visible) group.Tools.Add(tool);
        }
        Raise(nameof(HasResults));
    }

    /// <summary>Opens a tool and builds its options.</summary>
    public void Open(ToolCardViewModel tool)
    {
        CurrentTool = tool;
        Options.Clear();
        foreach (var option in tool.Descriptor.Options)
        {
            OptionItem item = option.Kind switch
            {
                OptionKind.Choice => new ChoiceOption
                {
                    Key = option.Key, Label = option.Label, Choices = option.Choices ?? Array.Empty<string>(),
                    Value = option.Default,
                },
                OptionKind.Number => new NumberOption
                {
                    Key = option.Key, Label = option.Label, Minimum = option.Minimum, Maximum = option.Maximum,
                    Suffix = option.Suffix, Value = option.Default,
                },
                OptionKind.Toggle => new ToggleOption
                {
                    Key = option.Key, Label = option.Label, Value = option.Default,
                },
                OptionKind.Color => new ColorOption
                {
                    Key = option.Key, Label = option.Label, Value = option.Default,
                },
                _ => new TextOption { Key = option.Key, Label = option.Label, Value = option.Default },
            };
            Options.Add(item);
        }
        Status = string.Empty;
        Results.Clear();
    }

    /// <summary>Back to the home grid.</summary>
    public void Back()
    {
        CurrentTool = null;
        Status = string.Empty;
    }

    public void AddFile(FileItem file)
    {
        file.RemoveCommand = RemoveFileCommand;
        Files.Add(file);
        Raise(nameof(CanRun));
    }

    public void RemoveFile(FileItem? file)
    {
        if (file is null) return;
        Files.Remove(file);
        Raise(nameof(CanRun));
    }

    public string OptionValue(string key) =>
        Options.FirstOrDefault(o => o.Key == key)?.Value ?? string.Empty;

    private double OptionNumber(string key, double fallback)
    {
        var option = Options.OfType<NumberOption>().FirstOrDefault(o => o.Key == key);
        if (option is null) return fallback;
        return double.TryParse(option.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    /// <summary>Runs the tool on the files the user picked.</summary>
    public void Run()
    {
        var tool = CurrentTool;
        if (tool is null || !tool.Ready || Files.Count == 0) return;

        try
        {
            Busy = true;
            Results.Clear();
            switch (tool.Id)
            {
                case "merge": RunMerge(); break;
                case "split": RunSplit(); break;
                case "organise": RunOrganise(); break;
                case "rotate": RunRotate(); break;
                case "crop": RunCrop(); break;
                case "pagenumbers": RunPageNumbers(); break;
                case "watermark": RunWatermark(); break;
                default:
                    Status = "This tool is not ready yet.";
                    break;
            }
        }
        catch (Exception ex)
        {
            Status = "Something went wrong: " + ex.Message;
        }
        finally
        {
            Busy = false;
            Raise(nameof(HasResults));
            Raise(nameof(CanRun));
        }
    }

    private void AddResult(string name, byte[] bytes)
    {
        Results.Add(new ResultItem { Name = name, Bytes = bytes, SaveCommand = SaveCommand });
    }

    private void RunMerge()
    {
        var order = OptionValue("order") switch
        {
            "Reverse" => PageOrder.Reverse,
            "Odd pages first" => PageOrder.OddFirst,
            "Even pages first" => PageOrder.EvenFirst,
            _ => PageOrder.AsPicked,
        };
        var bytes = PdfTools.Merge(Files.Select(f => f.Bytes), order);
        AddResult("merged.pdf", bytes);
        Status = "Merged into one document.";
    }

    private void RunSplit()
    {
        var mode = OptionValue("mode") switch
        {
            "Fixed count" => SplitMode.FixedCount,
            "Ranges" => SplitMode.Ranges,
            _ => SplitMode.EveryPage,
        };
        var pieces = PdfTools.Split(Files[0].Bytes, mode, OptionValue("ranges"),
            (int)OptionNumber("pagesPerFile", 1));
        var index = 1;
        foreach (var piece in pieces)
            AddResult($"part-{index++}.pdf", piece);
        Status = $"{Results.Count} file(s) created.";
    }

    private void RunOrganise()
    {
        var order = OptionValue("order") switch
        {
            "Reverse" => PageOrder.Reverse,
            "Odd pages first" => PageOrder.OddFirst,
            "Even pages first" => PageOrder.EvenFirst,
            _ => PageOrder.AsPicked,
        };
        var angle = int.TryParse(OptionValue("rotation"), out var parsed) ? parsed : 0;
        var rotations = new Dictionary<int, int>();
        if (angle != 0)
            for (var i = 0; i < 64; i++) rotations[i] = angle;
        var bytes = PdfTools.Organise(Files[0].Bytes, null, rotations, order);
        AddResult("organised.pdf", bytes);
        Status = "Pages rearranged.";
    }

    private void RunRotate()
    {
        var mode = OptionValue("angle") switch
        {
            "180" => RotateMode.Clockwise180,
            "270" => RotateMode.Clockwise270,
            _ => RotateMode.Clockwise90,
        };
        var bytes = PdfTools.Rotate(Files[0].Bytes, mode);
        AddResult("rotated.pdf", bytes);
        Status = "Pages turned.";
    }

    private void RunCrop()
    {
        var margin = OptionNumber("margin", 0);
        var bytes = PdfTools.Crop(Files[0].Bytes, margin);
        AddResult("cropped.pdf", bytes);
        Status = "White margins removed.";
    }

    private void RunPageNumbers()
    {
        var position = OptionValue("position") switch
        {
            "Bottom right" => NumberPosition.BottomRight,
            "Top right" => NumberPosition.TopRight,
            "Top left" => NumberPosition.TopLeft,
            _ => NumberPosition.BottomCentre,
        };
        var skipFirst = string.Equals(OptionValue("skipFirst"), "true", StringComparison.OrdinalIgnoreCase);
        var bytes = PdfTools.AddPageNumbers(Files[0].Bytes, OptionValue("prefix"), OptionValue("format"),
            position, (int)OptionNumber("start", 1), skipFirst);
        AddResult("numbered.pdf", bytes);
        Status = "Page numbers added.";
    }

    private void RunWatermark()
    {
        var mode = OptionValue("mode") switch
        {
            "Horizontal" => WatermarkMode.Horizontal,
            "Tiled" => WatermarkMode.Tiled,
            _ => WatermarkMode.Diagonal,
        };
        var bytes = PdfTools.AddWatermark(Files[0].Bytes, OptionValue("text"),
            OptionNumber("size", 48), OptionNumber("opacity", 25) / 100.0, mode);
        AddResult("watermarked.pdf", bytes);
        Status = "Watermark added.";
    }
}
