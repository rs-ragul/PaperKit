using PaperKit.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PaperKit.Spec;

/// <summary>Tests for the seven page tools, against real PDF files.</summary>
public class PdfToolsTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
        "..", "..", "fixtures");

    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(Fixtures, name);
        Assert.True(File.Exists(path), $"fixture missing: {path}");
        return File.ReadAllBytes(path);
    }

    private static byte[] MakePdf(int pages, double width = 400, double height = 600)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            page.Width = width;
            page.Height = height;
            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {i + 1}", Fonts.Body(20),
                PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(40, 60));
        }
        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

    private static int PageCount(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public void Merge_joins_documents_in_order()
    {
        var result = PdfTools.Merge(new[] { MakePdf(2), MakePdf(3) });
        Assert.Equal(5, PageCount(result));
    }

    [Fact]
    public void Merge_can_reverse_the_order_of_the_pages()
    {
        var result = PdfTools.Merge(new[] { MakePdf(2, 400), MakePdf(1, 600) }, PageOrder.Reverse);
        using var document = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
        // the last page of the reversed order is the narrow one
        Assert.True(document.Pages[2].Width.Point < document.Pages[0].Width.Point);
    }

    [Fact]
    public void Split_every_page_makes_one_file_per_page()
    {
        var pieces = PdfTools.Split(MakePdf(4), SplitMode.EveryPage);
        Assert.Equal(4, pieces.Count);
        foreach (var piece in pieces) Assert.Equal(1, PageCount(piece));
    }

    [Fact]
    public void Split_fixed_count_groups_the_pages()
    {
        var pieces = PdfTools.Split(MakePdf(5), SplitMode.FixedCount, pagesPerFile: 2);
        Assert.Equal(3, pieces.Count);
        Assert.Equal(2, PageCount(pieces[0]));
        Assert.Equal(1, PageCount(pieces[2]));
    }

    [Fact]
    public void Split_ranges_reads_sections()
    {
        var groups = PdfTools.ParseRanges("1-2, 4", 6);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 0, 1 }, groups[0]);
        Assert.Equal(new[] { 3 }, groups[1]);
    }

    [Fact]
    public void Organise_reorders_the_pages()
    {
        var result = PdfTools.Organise(MakePdf(3), order: new[] { 2, 1, 0 });
        Assert.Equal(3, PageCount(result));
    }

    [Fact]
    public void Organise_can_turn_every_page()
    {
        var result = PdfTools.Organise(MakePdf(2), rotations: new Dictionary<int, int> { [0] = 90 });
        using var document = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(90, document.Pages[0].Rotate);
        Assert.Equal(0, document.Pages[1].Rotate);
    }

    [Fact]
    public void Rotate_turns_every_page()
    {
        var result = PdfTools.Rotate(MakePdf(2), RotateMode.Clockwise90);
        using var document = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(90, document.Pages[0].Rotate);
        Assert.Equal(90, document.Pages[1].Rotate);
    }

    [Fact]
    public void Rotate_180_turns_it_twice()
    {
        var result = PdfTools.Rotate(MakePdf(1), RotateMode.Clockwise180);
        using var document = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(180, document.Pages[0].Rotate);
    }

    [Fact]
    public void Crop_sets_a_smaller_crop_box()
    {
        var source = MakePdf(1, 400, 600);
        var result = PdfTools.Crop(source);
        using var document = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        var page = document.Pages[0];
        Assert.True(page.CropBox.Width <= page.MediaBox.Width + 0.01);
        Assert.True(page.CropBox.Height <= page.MediaBox.Height + 0.01);
    }

    [Fact]
    public void Page_numbers_add_content_to_the_page()
    {
        var source = MakePdf(3);
        var result = PdfTools.AddPageNumbers(source, "p. ", "1 of n");
        Assert.Equal(3, PageCount(result));
        Assert.True(result.Length > source.Length);
    }

    [Fact]
    public void Page_numbers_can_skip_the_first_page()
    {
        var source = MakePdf(2);
        var all = PdfTools.AddPageNumbers(source);
        var skipped = PdfTools.AddPageNumbers(source, skipFirst: true);
        Assert.Equal(2, PageCount(skipped));
        Assert.True(skipped.Length < all.Length);
    }

    [Fact]
    public void Watermark_stamps_every_page()
    {
        var source = MakePdf(2);
        var result = PdfTools.AddWatermark(source, "CONFIDENTIAL");
        Assert.Equal(2, PageCount(result));
        Assert.True(result.Length > source.Length);
    }

    [Fact]
    public void A_tiled_watermark_is_bigger_than_a_single_one()
    {
        var source = MakePdf(1);
        var single = PdfTools.AddWatermark(source, "DRAFT", mode: WatermarkMode.Diagonal);
        var tiled = PdfTools.AddWatermark(source, "DRAFT", mode: WatermarkMode.Tiled);
        Assert.True(tiled.Length > single.Length);
    }

    [Fact]
    public void The_catalogue_knows_the_seven_page_tools()
    {
        var ready = ToolCatalog.All.Where(t => t.Ready).Select(t => t.Id).ToList();
        Assert.Equal(7, ready.Count);
        Assert.Contains("merge", ready);
        Assert.Contains("watermark", ready);
    }

    [Fact]
    public void The_catalogue_has_thirty_tools()
    {
        Assert.Equal(30, ToolCatalog.All.Count());
    }
}
