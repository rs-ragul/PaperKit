using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PaperKit.Core;

/// <summary>How the pages of the input are put into the output.</summary>
public enum PageOrder
{
    AsPicked,
    Reverse,
    OddFirst,
    EvenFirst,
}

/// <summary>How a document is cut into pieces.</summary>
public enum SplitMode
{
    EveryPage,
    Ranges,
    FixedCount,
}

/// <summary>How a single page is turned.</summary>
public enum RotateMode
{
    Clockwise90,
    Clockwise180,
    Clockwise270,
}

/// <summary>Where a page number is printed.</summary>
public enum NumberPosition
{
    BottomCentre,
    BottomRight,
    TopRight,
    TopLeft,
}

/// <summary>How a watermark is laid out over the page.</summary>
public enum WatermarkMode
{
    Diagonal,
    Horizontal,
    Tiled,
}

/// <summary>
/// The PDF tools of PaperKit. Everything runs on the device, nothing is
/// uploaded anywhere.
/// </summary>
public static class PdfTools
{
    // ------------------------------------------------------------------ merge

    /// <summary>Puts several PDFs into one document, in the given order.</summary>
    public static byte[] Merge(IEnumerable<byte[]> files, PageOrder order = PageOrder.AsPicked)
    {
        var list = files.Where(f => f is { Length: > 0 }).ToList();
        if (list.Count == 0) throw new ArgumentException("No file to merge.", nameof(files));

        using var target = new PdfDocument();
        foreach (var file in list)
        {
            using var source = PdfReader.Open(new MemoryStream(file), PdfDocumentOpenMode.Import);
            for (var i = 0; i < source.Pages.Count; i++) target.Pages.Add(source.Pages[i]);
        }

        return Reorder(target, order);
    }

    // ------------------------------------------------------------------ split

    /// <summary>Cuts a document into smaller ones.</summary>
    public static List<byte[]> Split(byte[] source, SplitMode mode, string ranges = "", int pagesPerFile = 1)
    {
        using var input = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Import);
        var count = input.PageCount;
        var groups = new List<List<int>>();

        switch (mode)
        {
            case SplitMode.EveryPage:
                for (var i = 0; i < count; i++) groups.Add(new List<int> { i });
                break;

            case SplitMode.FixedCount:
                pagesPerFile = Math.Max(1, pagesPerFile);
                for (var i = 0; i < count; i += pagesPerFile)
                {
                    var group = new List<int>();
                    for (var j = i; j < Math.Min(i + pagesPerFile, count); j++) group.Add(j);
                    groups.Add(group);
                }
                break;

            case SplitMode.Ranges:
                groups = ParseRanges(ranges, count);
                break;
        }

        if (groups.Count == 0) throw new ArgumentException("The range is empty.", nameof(ranges));

        var pieces = new List<byte[]>();
        foreach (var group in groups)
        {
            using var piece = new PdfDocument();
            foreach (var index in group)
                if (index >= 0 && index < input.PageCount) piece.Pages.Add(input.Pages[index]);
            pieces.Add(Write(piece));
        }
        return pieces;
    }

    /// <summary>Parses "1-3, 5, 8-" into page groups (1 based).</summary>
    public static List<List<int>> ParseRanges(string ranges, int pageCount)
    {
        var groups = new List<List<int>>();
        foreach (var part in ranges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var group = new List<int>();
            if (part.Contains('-'))
            {
                var sides = part.Split('-', 2);
                if (!int.TryParse(sides[0].Trim(), out var from)) continue;
                var to = int.TryParse(sides[1].Trim(), out var t) ? t : pageCount;
                for (var p = from; p <= to && p <= pageCount; p++)
                    if (p >= 1) group.Add(p - 1);
            }
            else if (int.TryParse(part, out var single) && single >= 1 && single <= pageCount)
            {
                group.Add(single - 1);
            }
            if (group.Count > 0) groups.Add(group);
        }
        return groups;
    }

    // --------------------------------------------------------------- organise

    /// <summary>
    /// Rebuilds a document with the pages in the given order and the given
    /// rotation per page.
    /// </summary>
    public static byte[] Organise(byte[] source, IReadOnlyList<int>? order = null,
        IReadOnlyDictionary<int, int>? rotations = null, PageOrder fallback = PageOrder.AsPicked)
    {
        using var input = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        var indexes = order is { Count: > 0 }
            ? order.Where(i => i >= 0 && i < input.PageCount).ToList()
            : SortedIndexes(input.PageCount, fallback);

        foreach (var index in indexes)
        {
            var page = input.Pages[index];
            output.Pages.Add(page);
            var added = output.Pages[output.Pages.Count - 1];
            if (rotations is not null && rotations.TryGetValue(index, out var angle) && angle % 360 != 0)
                added.Rotate = Normalise(added.Rotate + angle);
        }
        return Write(output);
    }

    // ----------------------------------------------------------------- rotate

    /// <summary>Turns the given pages (or all of them).</summary>
    public static byte[] Rotate(byte[] source, RotateMode mode, IReadOnlyList<int>? pages = null)
    {
        var angle = mode switch
        {
            RotateMode.Clockwise90 => 90,
            RotateMode.Clockwise180 => 180,
            RotateMode.Clockwise270 => 270,
            _ => 0,
        };

        using var input = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Modify);
        var list = pages is { Count: > 0 } ? pages : Enumerable.Range(0, input.PageCount).ToList();

        foreach (var index in list)
            if (index >= 0 && index < input.PageCount)
                input.Pages[index].Rotate = Normalise(input.Pages[index].Rotate + angle);

        return Write(input);
    }

    // ------------------------------------------------------------------- crop

    /// <summary>Trims the white margin around the content of every page.</summary>
    public static byte[] Crop(byte[] source, double marginPoints = 0)
    {
        using var input = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Modify);

        foreach (var page in input.Pages)
        {
            var box = ContentBox(page);
            if (box is null) continue;

            var media = page.MediaBox;
            var left = Math.Max(media.X1, box.Value.Left - marginPoints);
            var bottom = Math.Max(media.Y1, box.Value.Bottom - marginPoints);
            var right = Math.Min(media.X2, box.Value.Right + marginPoints);
            var top = Math.Min(media.Y2, box.Value.Top + marginPoints);

            if (right - left < 10 || top - bottom < 10) continue;   // nothing sensible to crop
            page.CropBox = new PdfRectangle(new XRect(left, bottom, right - left, top - bottom));
        }
        return Write(input);
    }

    // ----------------------------------------------------------- page numbers

    /// <summary>Prints a page number on every page.</summary>
    public static byte[] AddPageNumbers(byte[] source, string prefix = "", string format = "1",
        NumberPosition position = NumberPosition.BottomCentre, int startAt = 1, bool skipFirst = false)
    {
        using var document = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Modify);

        for (var i = 0; i < document.PageCount; i++)
        {
            if (skipFirst && i == 0) continue;
            var page = document.Pages[i];
            var number = startAt + i;
            var text = prefix + FormatNumber(number, format, document.PageCount);

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var font = Fonts.Body(10);
            var size = gfx.MeasureString(text, font);
            var (x, y) = Position(page, size, position, 24);
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y));
        }
        return Write(document);
    }

    // -------------------------------------------------------------- watermark

    /// <summary>Stamps a watermark over every page.</summary>
    public static byte[] AddWatermark(byte[] source, string text, double fontSize = 48,
        double opacity = 0.25, WatermarkMode mode = WatermarkMode.Diagonal, double rotation = -45)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("The watermark text is empty.", nameof(text));

        using var document = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Modify);
        var alpha = (byte)Math.Clamp(opacity * 255, 8, 255);
        var brush = new XSolidBrush(XColor.FromArgb(alpha, 90, 90, 110));

        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var font = Fonts.Bold(fontSize);
            var size = gfx.MeasureString(text, font);
            var pageWidth = page.Width.Point;
            var pageHeight = page.Height.Point;

            if (mode == WatermarkMode.Tiled)
            {
                var stepX = size.Width + 60;
                var stepY = size.Height + 50;
                for (var y = -pageHeight; y < pageHeight * 2; y += stepY)
                    for (var x = -pageWidth; x < pageWidth * 2; x += stepX)
                    {
                        gfx.Save();
                        gfx.RotateAtTransform(rotation, new XPoint(x, y));
                        gfx.DrawString(text, font, brush, new XPoint(x, y));
                        gfx.Restore();
                    }
            }
            else
            {
                var x = (pageWidth - size.Width) / 2;
                var y = (pageHeight - size.Height) / 2 + size.Height / 3;
                if (mode == WatermarkMode.Horizontal) rotation = 0;
                gfx.Save();
                gfx.RotateAtTransform(rotation, new XPoint(pageWidth / 2, pageHeight / 2));
                gfx.DrawString(text, font, brush, new XPoint(x, y));
                gfx.Restore();
            }
        }
        return Write(document);
    }

    // ---------------------------------------------------------------- helpers

    private static (double X, double Y) Position(PdfPage page, XSize size,
        NumberPosition position, double margin)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;
        return position switch
        {
            NumberPosition.BottomRight => (width - size.Width - margin, height - margin),
            NumberPosition.TopRight => (width - size.Width - margin, margin + size.Height),
            NumberPosition.TopLeft => (margin, margin + size.Height),
            _ => ((width - size.Width) / 2, height - margin),
        };
    }

    private static string FormatNumber(int number, string format, int total) => format switch
    {
        "1 of n" => $"{number} of {total}",
        "A" => ToLetters(number),
        "a" => ToLetters(number).ToLowerInvariant(),
        "i" => ToRoman(number),
        _ => number.ToString(),
    };

    private static string ToLetters(int number)
    {
        var result = string.Empty;
        var value = number;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string ToRoman(int number)
    {
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] letters = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var result = string.Empty;
        var value = number;
        for (var i = 0; i < values.Length && value > 0; i++)
            while (value >= values[i])
            {
                result += letters[i];
                value -= values[i];
            }
        return result;
    }

    private static int Normalise(int angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    private static List<int> SortedIndexes(int count, PageOrder order)
    {
        var all = Enumerable.Range(0, count).ToList();
        switch (order)
        {
            case PageOrder.Reverse: all.Reverse(); break;
            case PageOrder.OddFirst:
                all = all.Where(i => i % 2 == 0).Concat(all.Where(i => i % 2 == 1)).ToList();
                break;
            case PageOrder.EvenFirst:
                all = all.Where(i => i % 2 == 1).Concat(all.Where(i => i % 2 == 0)).ToList();
                break;
        }
        return all;
    }

    private static byte[] Reorder(PdfDocument document, PageOrder order)
    {
        if (order == PageOrder.AsPicked) return Write(document);

        var pages = new List<PdfPage>();
        for (var i = 0; i < document.Pages.Count; i++) pages.Add(document.Pages[i]);
        var wanted = SortedIndexes(pages.Count, order);
        for (var target = 0; target < wanted.Count; target++)
        {
            var page = pages[wanted[target]];
            var current = IndexOfPage(document.Pages, page);
            if (current >= 0 && current != target) document.Pages.MovePage(current, target);
        }
        return Write(document);
    }

    private static int IndexOfPage(PdfPages pages, PdfPage page)
    {
        for (var i = 0; i < pages.Count; i++)
            if (ReferenceEquals(pages[i], page)) return i;
        return -1;
    }

    private static byte[] Write(PdfDocument document)
    {
        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Finds the box that holds the visible content of a page by reading the
    /// content stream. It is a good approximation of what the eye sees.
    /// </summary>
    private static (double Left, double Bottom, double Right, double Top)? ContentBox(PdfPage page)
    {
        var bytes = ContentBytes(page);
        if (bytes is null || bytes.Length == 0) return null;

        var text = System.Text.Encoding.Latin1.GetString(bytes);
        var ctm = new double[6];
        var currentNumbers = new List<double>();
        var stack = new Stack<double[]>();
        var path = new List<(double X, double Y)>();
        var left = double.MaxValue;
        var bottom = double.MaxValue;
        var right = double.MinValue;
        var top = double.MinValue;
        var hasContent = false;

        foreach (var token in Tokenize(text))
        {
            if (token.Kind == TokenKind.Number)
            {
                currentNumbers.Add(token.Value);
                continue;
            }

            switch (token.Text)
            {
                case "cm" when currentNumbers.Count >= 6:
                    var m = currentNumbers.Skip(currentNumbers.Count - 6).ToArray();
                    stack.Push((double[])ctm.Clone());
                    ctm = Multiply(m, ctm);
                    currentNumbers.Clear();
                    break;
                case "q":
                    stack.Push((double[])ctm.Clone());
                    currentNumbers.Clear();
                    break;
                case "Q":
                    if (stack.Count > 0) ctm = stack.Pop();
                    currentNumbers.Clear();
                    break;
                case "m" when currentNumbers.Count >= 2:
                    path.Add(Transform(ctm, currentNumbers[currentNumbers.Count - 2], currentNumbers[currentNumbers.Count - 1]));
                    currentNumbers.Clear();
                    break;
                case "l" when currentNumbers.Count >= 2:
                    path.Add(Transform(ctm, currentNumbers[currentNumbers.Count - 2], currentNumbers[currentNumbers.Count - 1]));
                    currentNumbers.Clear();
                    break;
                case "re" when currentNumbers.Count >= 4:
                    var n = currentNumbers.Count;
                    var x = currentNumbers[n - 4];
                    var y = currentNumbers[n - 3];
                    var w = currentNumbers[n - 2];
                    var h = currentNumbers[n - 1];
                    foreach (var corner in new[] { (x, y), (x + w, y), (x, y + h), (x + w, y + h) })
                        path.Add(Transform(ctm, corner.Item1, corner.Item2));
                    currentNumbers.Clear();
                    break;
                case "c" when currentNumbers.Count >= 6:
                    for (var i = 0; i < 6; i += 2)
                        path.Add(Transform(ctm, currentNumbers[currentNumbers.Count - 6 + i],
                            currentNumbers[currentNumbers.Count - 5 + i]));
                    currentNumbers.Clear();
                    break;
                case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                    foreach (var point in path)
                    {
                        hasContent = true;
                        left = Math.Min(left, point.X);
                        bottom = Math.Min(bottom, point.Y);
                        right = Math.Max(right, point.X);
                        top = Math.Max(top, point.Y);
                    }
                    path.Clear();
                    currentNumbers.Clear();
                    break;
                default:
                    currentNumbers.Clear();
                    break;
            }
        }

        if (!hasContent || left > right) return null;

        var media = page.MediaBox;
        left = Math.Max(left, media.X1);
        bottom = Math.Max(bottom, media.Y1);
        right = Math.Min(right, media.X2);
        top = Math.Min(top, media.Y2);
        if (right - left < 10 || top - bottom < 10) return null;
        return (left, bottom, right, top);
    }

    /// <summary>The decoded content stream of a page.</summary>
    private static byte[]? ContentBytes(PdfPage page)
    {
        try
        {
            using var buffer = new MemoryStream();
            foreach (var content in page.Contents)
            {
                var stream = content.Stream;
                if (stream is null) continue;
                var bytes = stream.UnfilteredValue ?? stream.Value;
                if (bytes is { Length: > 0 })
                {
                    buffer.Write(bytes, 0, bytes.Length);
                    buffer.WriteByte((byte)'\n');
                }
            }
            return buffer.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (double X, double Y) Transform(double[] m, double x, double y) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

    private static double[] Multiply(double[] m, double[] c)
    {
        var result = new double[6];
        result[0] = m[0] * c[0] + m[1] * c[2];
        result[1] = m[0] * c[1] + m[1] * c[3];
        result[2] = m[2] * c[0] + m[3] * c[2];
        result[3] = m[2] * c[1] + m[3] * c[3];
        result[4] = m[4] * c[0] + m[5] * c[2] + c[4];
        result[5] = m[4] * c[1] + m[5] * c[3] + c[5];
        return result;
    }

    private enum TokenKind { Number, Operator }

    private readonly record struct Token(TokenKind Kind, double Value, string Text);

    /// <summary>A very small content stream reader - enough to follow the paths.</summary>
    private static IEnumerable<Token> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '%')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '(')
            {
                var depth = 1; i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '\\') i++;
                    else if (text[i] == '(') depth++;
                    else if (text[i] == ')') depth--;
                    i++;
                }
                continue;
            }
            if (c == '<' && i + 1 < text.Length && text[i + 1] == '<')
            {
                var end = text.IndexOf(">>", i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }
            if (c == '<' || c == '>') { i++; continue; }
            if (c == '[' || c == ']' || c == '{' || c == '}') { i++; continue; }
            if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' ||
                       ((text[i] == '-' || text[i] == '+') && i > start && !char.IsLetter(text[i - 1])))) i++;
                var slice = text[start..i];
                if (double.TryParse(slice, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                    yield return new Token(TokenKind.Number, value, slice);
                continue;
            }
            var opStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && !char.IsDigit(text[i]) &&
                   text[i] != '(' && text[i] != '<' && text[i] != '>' && text[i] != '[' && text[i] != ']')
                i++;
            if (i == opStart) i++;
            var op = text[opStart..i];
            if (op.Length > 0 && char.IsLetter(op[0]))
                yield return new Token(TokenKind.Operator, 0, op);
        }
    }
}
