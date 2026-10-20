namespace PaperKit.App.ViewModels;

/// <summary>
/// The line icons of the toolbox. Every path is drawn on a 24 x 24 grid with
/// a stroke of about 1.7, the same look as the rest of the app.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["merge"] = "M4 7h5l4 5h7M16 8l4 4-4 4M4 17h5l2-2.5",
        ["split"] = "M20 7h-5l-4 5H4M8 8l-4 4 4 4M20 17h-5l-2-2.5",
        ["organize"] = "M4 6h16M4 12h16M4 18h16M9 4v4M15 10v4M7 16v4",
        ["rotate"] = "M20 12a8 8 0 1 1-2.6-5.9M20 4v4h-4",
        ["crop"] = "M6 2v13a2 2 0 0 0 2 2h13M2 6h13a2 2 0 0 1 2 2v13",
        ["pagenumbers"] = "M4 7h16M4 12h9M4 17h12M17 15v4M15 17h4",
        ["watermark"] = "M4 19h16M12 4l7 7-7 7M5 12l4-4",
        ["repair"] = "M14.7 6.3a4 4 0 0 1 5 5L12 19H7v-5l7.7-7.7z",
        ["jpg2pdf"] = "M4 6h9l3 3v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1zM13 6v4h4M7 14l2-2 2 2-2 2z",
        ["scan"] = "M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3M4 12h16",
        ["word2pdf"] = "M6 3h8l4 4v14H6zM14 3v4h4M9 13h6M9 17h4",
        ["ppt2pdf"] = "M4 4h16v11H4zM12 15v5M8 20h8",
        ["xls2pdf"] = "M4 4h16v16H4zM4 10h16M4 15h16M10 4v16",
        ["html2pdf"] = "M8 8l-4 4 4 4M16 8l4 4-4 4M13 5l-2 14",
        ["pdf2jpg"] = "M4 5h9l3 3v11H4zM13 5v4h4M15 15l2-2 2 2-2 2z",
        ["pdf2word"] = "M6 3h8l4 4v14H6zM14 3v4h4M9 12h6M9 16h6",
        ["pdf2ppt"] = "M4 4h16v11H4zM12 15v5M8 20h8M9 9h6",
        ["pdf2xls"] = "M4 4h16v16H4zM4 10h16M4 15h16M10 4v16",
        ["pdf2md"] = "M6 3h8l4 4v14H6zM14 3v4h4M9 12l3 3 3-3",
        ["compress"] = "M4 9h16M4 15h16M9 4v5M15 4v5M9 15v5M15 15v5",
        ["ocr"] = "M4 6h16M4 10h16M4 14h10M4 18h7M16 13l2 2 4-4",
        ["pdfa"] = "M6 3h8l4 4v14H6zM14 3v4h4M9 14h6M9 18h6",
        ["edit"] = "M4 20h4L20 8l-4-4L4 16zM14 6l4 4",
        ["compare"] = "M9 4H4v16h5M15 4h5v16h-5M9 12h6",
        ["protect"] = "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM9 12l2 2 4-4",
        ["unlock"] = "M7 11V8a5 5 0 0 1 9.5-2M5 11h14v10H5z",
        ["redact"] = "M4 6h16v12H4zM8 10h8v4H8z",
        ["sign"] = "M4 18c4-8 6 4 9-3s3 1 7-4M4 21h16",
        ["forms"] = "M5 3h14v18H5zM9 8h6M9 12h6M9 16h3",
        ["verify"] = "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM9 12l2 2 4-4",
    };

    /// <summary>The path data of an icon, or an empty string.</summary>
    public static string Path(string name) => Paths.TryGetValue(name, out var path) ? path : Paths["merge"];
}
