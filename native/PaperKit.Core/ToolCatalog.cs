namespace PaperKit.Core;

/// <summary>What kind of control an option uses.</summary>
public enum OptionKind
{
    Choice,
    Text,
    Number,
    Toggle,
    Color,
}

/// <summary>One setting of a tool.</summary>
public sealed record ToolOption(
    string Key,
    string Label,
    OptionKind Kind,
    string Default = "",
    string[]? Choices = null,
    double Minimum = 0,
    double Maximum = 100,
    string? Suffix = null);

/// <summary>One tool of the toolbox.</summary>
public sealed record ToolDescriptor(
    string Id,
    string Name,
    string Blurb,
    string Category,
    string Icon,
    bool Ready,
    IReadOnlyList<ToolOption> Options);

/// <summary>A group of tools in the home screen.</summary>
public sealed record ToolGroup(string Title, string Subtitle, IReadOnlyList<ToolDescriptor> Tools);

/// <summary>
/// The catalogue of the toolbox - the same set as iLovePDF, minus the two AI
/// tools that would need a model on the device.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolGroup> Groups { get; } = new[]
    {
        new ToolGroup("Organise", "Rearrange, trim and label your pages", new[]
        {
            new ToolDescriptor("merge", "Merge PDF", "Combine several PDFs into one document.", "Organise", "merge", true, new[]
            {
                new ToolOption("order", "Order", OptionKind.Choice, "As picked", new[] { "As picked", "Reverse", "Odd pages first", "Even pages first" }),
                new ToolOption("pages", "Pages to take", OptionKind.Choice, "Every page", new[] { "Every page", "Odd pages", "Even pages" }),
            }),
            new ToolDescriptor("split", "Split PDF", "Break one PDF into separate files by page.", "Organise", "split", true, new[]
            {
                new ToolOption("mode", "Split by", OptionKind.Choice, "Every page", new[] { "Every page", "Fixed count", "Ranges" }),
                new ToolOption("ranges", "Ranges", OptionKind.Text, "1-1, 2-2"),
                new ToolOption("pagesPerFile", "Pages per file", OptionKind.Number, "1", Minimum: 1, Maximum: 500),
            }),
            new ToolDescriptor("organise", "Organise pages", "Delete, rotate or duplicate pages, then reorder them.", "Organise", "organize", true, new[]
            {
                new ToolOption("order", "Order", OptionKind.Choice, "As picked", new[] { "As picked", "Reverse", "Odd pages first", "Even pages first" }),
                new ToolOption("rotation", "Rotate every page", OptionKind.Choice, "0", new[] { "0", "90", "180", "270" }),
            }),
            new ToolDescriptor("rotate", "Rotate PDF", "Turn pages 90, 180 or 270 degrees.", "Organise", "rotate", true, new[]
            {
                new ToolOption("angle", "Angle", OptionKind.Choice, "90", new[] { "90", "180", "270" }),
                new ToolOption("pages", "Pages", OptionKind.Choice, "All pages", new[] { "All pages", "Even pages", "Odd pages" }),
            }),
            new ToolDescriptor("crop", "Crop PDF", "Trim the white margins off every page.", "Organise", "crop", true, new[]
            {
                new ToolOption("margin", "Extra margin", OptionKind.Number, "0", Minimum: 0, Maximum: 100, Suffix: "pt"),
            }),
            new ToolDescriptor("pagenumbers", "Page numbers", "Stamp a page number on every page.", "Organise", "pagenumbers", true, new[]
            {
                new ToolOption("prefix", "Prefix", OptionKind.Text, ""),
                new ToolOption("format", "Format", OptionKind.Choice, "1", new[] { "1", "1 of n", "A", "a", "i" }),
                new ToolOption("position", "Position", OptionKind.Choice, "Bottom centre", new[] { "Bottom centre", "Bottom right", "Top right", "Top left" }),
                new ToolOption("start", "Start at", OptionKind.Number, "1", Minimum: 0, Maximum: 10000),
                new ToolOption("skipFirst", "Skip the first page", OptionKind.Toggle, "false"),
            }),
            new ToolDescriptor("watermark", "Watermark", "Add a text watermark across the pages.", "Organise", "watermark", true, new[]
            {
                new ToolOption("text", "Text", OptionKind.Text, "CONFIDENTIAL"),
                new ToolOption("size", "Size", OptionKind.Number, "48", Minimum: 8, Maximum: 200),
                new ToolOption("opacity", "Opacity", OptionKind.Number, "25", Minimum: 5, Maximum: 100, Suffix: "%"),
                new ToolOption("mode", "Layout", OptionKind.Choice, "Diagonal", new[] { "Diagonal", "Horizontal", "Tiled" }),
            }),
            new ToolDescriptor("repair", "Repair PDF", "Rebuild a damaged file so it opens again.", "Organise", "repair", false, Array.Empty<ToolOption>()),
        }),

        new ToolGroup("Convert to PDF", "Turn anything into a PDF", new[]
        {
            new ToolDescriptor("jpg2pdf", "JPG to PDF", "Turn photos into a single PDF.", "Convert to PDF", "jpg2pdf", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("scan", "Scan to PDF", "Photograph a document and save it as PDF.", "Convert to PDF", "scan", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("word2pdf", "Word to PDF", "Convert .docx files to PDF.", "Convert to PDF", "word2pdf", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("ppt2pdf", "PowerPoint to PDF", "Convert .pptx decks to PDF.", "Convert to PDF", "ppt2pdf", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("xls2pdf", "Excel to PDF", "Convert .xlsx sheets to PDF.", "Convert to PDF", "xls2pdf", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("html2pdf", "HTML to PDF", "Save a web page as a PDF.", "Convert to PDF", "html2pdf", false, Array.Empty<ToolOption>()),
        }),

        new ToolGroup("Convert from PDF", "Turn a PDF into something else", new[]
        {
            new ToolDescriptor("pdf2jpg", "PDF to JPG", "Every page as an image.", "Convert from PDF", "pdf2jpg", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("pdf2word", "PDF to Word", "An editable .docx file.", "Convert from PDF", "pdf2word", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("pdf2ppt", "PDF to PowerPoint", "One slide per page.", "Convert from PDF", "pdf2ppt", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("pdf2xls", "PDF to Excel", "The tables as a sheet.", "Convert from PDF", "pdf2xls", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("pdf2md", "PDF to Markdown", "Plain text for your notes.", "Convert from PDF", "pdf2md", false, Array.Empty<ToolOption>()),
        }),

        new ToolGroup("Optimise", "Make the file smaller or cleaner", new[]
        {
            new ToolDescriptor("compress", "Compress PDF", "Shrink the file, keep the text sharp.", "Optimise", "compress", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("ocr", "OCR PDF", "Make a scanned document searchable.", "Optimise", "ocr", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("pdfa", "PDF to PDF/A", "The long term archive format.", "Optimise", "pdfa", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("edit", "Edit PDF", "Change the text, the images and the order.", "Optimise", "edit", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("compare", "Compare PDF", "Show what changed between two files.", "Optimise", "compare", false, Array.Empty<ToolOption>()),
        }),

        new ToolGroup("Secure", "Protect the document", new[]
        {
            new ToolDescriptor("protect", "Protect PDF", "Ask for a password to open it.", "Secure", "protect", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("unlock", "Unlock PDF", "Remove a password you know.", "Secure", "unlock", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("redact", "Redact PDF", "Black out text for good.", "Secure", "redact", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("sign", "Sign PDF", "Draw or type your signature.", "Secure", "sign", false, Array.Empty<ToolOption>()),
            new ToolDescriptor("forms", "Fill forms", "Fill a form and flatten it.", "Secure", "forms", false, Array.Empty<ToolOption>()),
        }),

        new ToolGroup("Verify", "Trust the signature", new[]
        {
            new ToolDescriptor("verify", "Verify signature", "Check a digital signature and the certificate chain - with the 7 official CCA India roots built in.", "Verify", "verify", false, Array.Empty<ToolOption>()),
        }),
    };

    /// <summary>Every tool, in the order of the groups.</summary>
    public static IEnumerable<ToolDescriptor> All => Groups.SelectMany(g => g.Tools);

    /// <summary>Finds a tool by its id.</summary>
    public static ToolDescriptor? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
