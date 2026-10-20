using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.WPFonts;

namespace PaperKit.Core;

/// <summary>
/// Resolves the fonts the PDF tools draw with.
///
/// Windows and Linux have system fonts, Android has Roboto - but the app must
/// look and behave the same everywhere, and it must never fail because a font
/// is missing. So: use a system font when there is one, otherwise fall back to
/// the metric-compatible fonts that ship with PDFsharp.
/// </summary>
internal sealed class PaperKitFontResolver : IFontResolver
{
    private const string FallbackRegular = "PaperKitFont-Regular";
    private const string FallbackBold = "PaperKitFont-Bold";

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (!string.IsNullOrWhiteSpace(familyName))
        {
            try
            {
                var info = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
                if (info != null) return info;
            }
            catch (Exception)
            {
                // no system font of that name - use the bundled one
            }
        }
        return new FontResolverInfo(isBold ? FallbackBold : FallbackRegular);
    }

    public byte[] GetFont(string fontName) => fontName switch
    {
        FallbackBold => FontDataHelper.SegoeWPBold,
        FallbackRegular => FontDataHelper.SegoeWP,
        _ => FontDataHelper.SegoeWP,
    };
}

/// <summary>The fonts the tools draw with.</summary>
public static class Fonts
{
    private static bool _installed;

    private static readonly string[] Families =
    {
        "Segoe UI", "Roboto", "Noto Sans", "DejaVu Sans", "Liberation Sans",
        "Helvetica", "Arial",
    };

    /// <summary>Must be called once before any font is used.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            GlobalFontSettings.FontResolver = new PaperKitFontResolver();
        }
        catch (Exception)
        {
            // a platform resolver is already in place - that is fine
        }
    }

    public static XFont Body(double size) => Resolve(size, XFontStyleEx.Regular);
    public static XFont Bold(double size) => Resolve(size, XFontStyleEx.Bold);

    private static XFont Resolve(double size, XFontStyleEx style)
    {
        Install();
        foreach (var family in Families)
        {
            try
            {
                return new XFont(family, size, style);
            }
            catch (Exception)
            {
                // try the next one
            }
        }
        return new XFont("PaperKitFont-Regular", size, style);
    }
}
