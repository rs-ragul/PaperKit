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

    /// <summary>What the number means, shown next to the field.</summary>
    public string Unit => Suffix is null ? string.Empty : " (" + Suffix + ")";

    /// <summary>The range the number is allowed to be in.</summary>
    public string Range => Maximum > Minimum
        ? $"between {Minimum:0.##} and {Maximum:0.##}"
        : $"at least {Minimum:0.##}";

    public double Number
    {
        get => double.TryParse(Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= Minimum && number <= Maximum
            ? number
            : Minimum;
        set => Value = Clamp(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Keeps a number inside the range the tool allows.</summary>
    public double Clamp(double value) => Math.Min(Maximum, Math.Max(Minimum, value));
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

/// <summary>One colour the user can pick.</summary>
public sealed record ColorChoice(string Name, string Hex);

/// <summary>A colour.</summary>
public sealed class ColorOption : OptionItem
{
    public ColorOption() =>
        PickCommand = new RelayCommand(p =>
        {
            if (p is string hex) Value = hex;
        });

    /// <summary>Picks one of the colours.</summary>
    public RelayCommand PickCommand { get; }

    /// <summary>The colours on offer.</summary>
    public IReadOnlyList<ColorChoice> Swatches { get; } = new List<ColorChoice>
    {
        new("Indigo", "#4F46E5"),
        new("Sky", "#0EA5E9"),
        new("Emerald", "#10B981"),
        new("Amber", "#F59E0B"),
        new("Red", "#EF4444"),
        new("Ink", "#111827"),
        new("White", "#FFFFFF"),
    };
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
        set
        {
            if (Set(ref _status, value))
            {
                StatusIsError = value.StartsWith("Something went wrong", StringComparison.Ordinal)
                                || value.StartsWith("Could not", StringComparison.Ordinal);
                Raise(nameof(HasStatus));
                Raise(nameof(StatusIsError));
            }
        }
    }

    /// <summary>True when the message is a failure and not an information.</summary>
    public bool StatusIsError { get; private set; }

    /// <summary>True when there is something to say under the run button.</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    /// <summary>What the run button says while the tool works.</summary>
    public string RunLabel => Busy ? "Working on it..." : "Run";

    /// <summary>True when the search found nothing at all.</summary>
    public bool HasSearchResults => Groups.Any(g => g.Tools.Count > 0);

    public bool Busy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(CanRun));
                Raise(nameof(RunLabel));
            }
        }
    }

    public bool CanRun => !Busy && Files.Count > 0 && CurrentTool is { Ready: true };

    public bool HasResults => Results.Count > 0;

    /// <summary>True when the user has already added a file.</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>True when this tool has options to show.</summary>
    public bool HasOptions => Options.Count > 0;

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
        Raise(nameof(HasSearchResults));
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
        Raise(nameof(HasOptions));
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
        Raise(nameof(HasFiles));
    }

    public void RemoveFile(FileItem? file)
    {
        if (file is null) return;
        Files.Remove(file);
        Raise(nameof(CanRun));
        Raise(nameof(HasFiles));
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

    /// <summary>What a tool produced, and what to tell the user about it.</summary>
    private sealed record ToolOutput(List<(string Name, byte[] Bytes)> Files, string Message);

    /// <summary>
    /// A copy of the options. The tool works on a background thread, and the
    /// user may change an option while it is running - so the values are
    /// taken once and the work is done with those.
    /// </summary>
    private sealed class OptionSnapshot
    {
        private readonly Dictionary<string, OptionItem> _options;

        public OptionSnapshot(IEnumerable<OptionItem> options) =>
            _options = options.ToDictionary(o => o.Key);

        public string Text(string key) =>
            _options.TryGetValue(key, out var option) ? option.Value : string.Empty;

        public double Number(string key, double fallback)
        {
            if (!_options.TryGetValue(key, out var option) || option is not NumberOption number)
                return fallback;
            if (!double.TryParse(number.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                return number.Minimum;
            return number.Clamp(value);
        }
    }

    /// <summary>
    /// Runs the tool on the files the user picked. The PDF work happens off
    /// the UI thread, so a big document never freezes the window.
    /// </summary>
    public async Task RunAsync()
    {
        var tool = CurrentTool;
        if (tool is null || !tool.Ready || Files.Count == 0 || Busy) return;

        try
        {
            Busy = true;
            Status = string.Empty;
            Results.Clear();

            // take a copy of the input, so the work can happen in the background
            var inputs = Files.Select(f => f.Bytes).ToArray();
            var options = new OptionSnapshot(Options);

            var output = await Task.Run(() => RunTool(tool, inputs, options));

            foreach (var (name, bytes) in output.Files)
                Results.Add(new ResultItem { Name = name, Bytes = bytes, SaveCommand = SaveCommand });

            Status = output.Message;
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

    private ToolOutput RunTool(ToolCardViewModel tool, byte[][] inputs, OptionSnapshot options) => tool.Id switch
    {
        "merge" => RunMerge(inputs, options),
        "split" => RunSplit(inputs, options),
        "organise" => RunOrganise(inputs, options),
        "rotate" => RunRotate(inputs, options),
        "crop" => RunCrop(inputs, options),
        "pagenumbers" => RunPageNumbers(inputs, options),
        "watermark" => RunWatermark(inputs, options),
        _ => new ToolOutput(new List<(string, byte[])>(), "This tool is not ready yet."),
    };

    private ToolOutput RunMerge(byte[][] inputs, OptionSnapshot options)
    {
        var order = options.Text("order") switch
        {
            "Reverse" => PageOrder.Reverse,
            "Odd pages first" => PageOrder.OddFirst,
            "Even pages first" => PageOrder.EvenFirst,
            _ => PageOrder.AsPicked,
        };
        var bytes = PdfTools.Merge(inputs, order);
        return new ToolOutput(new() { ("merged.pdf", bytes) }, "Merged into one document.");
    }

    private ToolOutput RunSplit(byte[][] inputs, OptionSnapshot options)
    {
        var mode = options.Text("mode") switch
        {
            "Fixed count" => SplitMode.FixedCount,
            "Ranges" => SplitMode.Ranges,
            _ => SplitMode.EveryPage,
        };
        var pieces = PdfTools.Split(inputs[0], mode, options.Text("ranges"),
            (int)options.Number("pagesPerFile", 1));
        var files = new List<(string, byte[])>();
        var index = 1;
        foreach (var piece in pieces)
            files.Add(($"part-{index++}.pdf", piece));
        return new ToolOutput(files, $"{files.Count} file(s) created.");
    }

    private ToolOutput RunOrganise(byte[][] inputs, OptionSnapshot options)
    {
        var order = options.Text("order") switch
        {
            "Reverse" => PageOrder.Reverse,
            "Odd pages first" => PageOrder.OddFirst,
            "Even pages first" => PageOrder.EvenFirst,
            _ => PageOrder.AsPicked,
        };
        var angle = int.TryParse(options.Text("rotation"), out var parsed) ? parsed : 0;
        var rotations = new Dictionary<int, int>();
        if (angle != 0)
            for (var i = 0; i < 64; i++) rotations[i] = angle;
        var bytes = PdfTools.Organise(inputs[0], null, rotations, order);
        return new ToolOutput(new() { ("organised.pdf", bytes) }, "Pages rearranged.");
    }

    private ToolOutput RunRotate(byte[][] inputs, OptionSnapshot options)
    {
        var mode = options.Text("angle") switch
        {
            "180" => RotateMode.Clockwise180,
            "270" => RotateMode.Clockwise270,
            _ => RotateMode.Clockwise90,
        };
        var bytes = PdfTools.Rotate(inputs[0], mode);
        return new ToolOutput(new() { ("rotated.pdf", bytes) }, "Pages turned.");
    }

    private ToolOutput RunCrop(byte[][] inputs, OptionSnapshot options)
    {
        var bytes = PdfTools.Crop(inputs[0], options.Number("margin", 0));
        return new ToolOutput(new() { ("cropped.pdf", bytes) }, "White margins removed.");
    }

    private ToolOutput RunPageNumbers(byte[][] inputs, OptionSnapshot options)
    {
        var position = options.Text("position") switch
        {
            "Bottom right" => NumberPosition.BottomRight,
            "Top right" => NumberPosition.TopRight,
            "Top left" => NumberPosition.TopLeft,
            _ => NumberPosition.BottomCentre,
        };
        var skipFirst = string.Equals(options.Text("skipFirst"), "true", StringComparison.OrdinalIgnoreCase);
        var bytes = PdfTools.AddPageNumbers(inputs[0], options.Text("prefix"), options.Text("format"),
            position, (int)options.Number("start", 1), skipFirst);
        return new ToolOutput(new() { ("numbered.pdf", bytes) }, "Page numbers added.");
    }

    private ToolOutput RunWatermark(byte[][] inputs, OptionSnapshot options)
    {
        var mode = options.Text("mode") switch
        {
            "Horizontal" => WatermarkMode.Horizontal,
            "Tiled" => WatermarkMode.Tiled,
            _ => WatermarkMode.Diagonal,
        };
        var bytes = PdfTools.AddWatermark(inputs[0], options.Text("text"),
            options.Number("size", 48), options.Number("opacity", 25) / 100.0, mode);
        return new ToolOutput(new() { ("watermarked.pdf", bytes) }, "Watermark added.");
    }
}
