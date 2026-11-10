using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MSlide;

/// <summary>
/// Writes a minimal, standards-compliant 16:9 .pptx with one full-bleed picture per slide.
/// Each image is resampled (stretched) to an exact 16:9 pixel size before embedding,
/// so it stays stretched even if someone uses "Reset Picture" in PowerPoint.
/// </summary>
public static class PptxWriter
{
    private const long SlideCx = 12192000; // 13.333 in
    private const long SlideCy = 6858000;  // 7.5 in

    private const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";
    private const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Ns = $"xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"";

    private const string EmptyGroup =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/>" +
        "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    public static void Write(string outPath, IReadOnlyList<string> images, IProgress<int>? progress = null) =>
        Write(outPath, images, progress, StretchTo16x9);

    internal static void Write(string outPath, IReadOnlyList<string> images, IProgress<int>? progress,
        Action<string, Stream> writeImage)
    {
        if (images.Count == 0) throw new ArgumentException("No images to save.");

        string tmp = outPath + ".mslide-tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                int n = images.Count;
                Text(zip, "[Content_Types].xml", ContentTypes(n));
                Text(zip, "_rels/.rels", Rels(("rId1", "officeDocument", "ppt/presentation.xml")));
                Text(zip, "ppt/presentation.xml", Presentation(n));
                Text(zip, "ppt/_rels/presentation.xml.rels", PresentationRels(n));
                Text(zip, "ppt/presProps.xml", $"{Xml}<p:presentationPr {Ns}/>");
                Text(zip, "ppt/theme/theme1.xml", Theme);
                Text(zip, "ppt/slideMasters/slideMaster1.xml", SlideMaster);
                Text(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels",
                    Rels(("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml"),
                         ("rId2", "theme", "../theme/theme1.xml")));
                Text(zip, "ppt/slideLayouts/slideLayout1.xml", SlideLayout);
                Text(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels",
                    Rels(("rId1", "slideMaster", "../slideMasters/slideMaster1.xml")));

                for (int i = 0; i < n; i++)
                {
                    int k = i + 1;
                    Text(zip, $"ppt/slides/slide{k}.xml", Slide(k, Path.GetFileName(images[i])));
                    Text(zip, $"ppt/slides/_rels/slide{k}.xml.rels",
                        Rels(("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml"),
                             ("rId2", "image", $"../media/image{k}.png")));

                    // PNGs are already compressed; don't waste time deflating them again.
                    var entry = zip.CreateEntry($"ppt/media/image{k}.png", CompressionLevel.NoCompression);
                    using (var es = entry.Open())
                        writeImage(images[i], es);

                    progress?.Report(k);
                }
            }
            File.Move(tmp, outPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    /// <summary>Resamples the image to an exact 16:9 size (distorting it to fit) and writes PNG.</summary>
    private static void StretchTo16x9(string path, Stream output)
    {
        using var fs = File.OpenRead(path);
        using var src = Image.FromStream(fs);

        // Keep roughly the source's detail: wide enough that neither axis gets downsampled,
        // bounded between 1280x720 and 3840x2160.
        int w = Math.Max(src.Width, (int)Math.Ceiling(src.Height * 16.0 / 9.0));
        w = Math.Clamp(w, 1280, 3840);
        w -= w % 16;
        int h = w * 9 / 16;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        using (var attrs = new ImageAttributes())
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            attrs.SetWrapMode(WrapMode.TileFlipXY); // avoids faint edge halos
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        bmp.Save(output, ImageFormat.Png);
    }

    private static void Text(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Rels(params (string id, string type, string target)[] rels)
    {
        var sb = new StringBuilder(Xml).Append($"<Relationships xmlns=\"{NsRel}\">");
        foreach (var (id, type, target) in rels)
            sb.Append($"<Relationship Id=\"{id}\" Type=\"{RelBase}{type}\" Target=\"{target}\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static string ContentTypes(int n)
    {
        const string pml = "application/vnd.openxmlformats-officedocument.presentationml.";
        var sb = new StringBuilder(Xml)
            .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
            .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
            .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
            .Append("<Default Extension=\"png\" ContentType=\"image/png\"/>")
            .Append($"<Override PartName=\"/ppt/presentation.xml\" ContentType=\"{pml}presentation.main+xml\"/>")
            .Append($"<Override PartName=\"/ppt/presProps.xml\" ContentType=\"{pml}presProps+xml\"/>")
            .Append($"<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"{pml}slideMaster+xml\"/>")
            .Append($"<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"{pml}slideLayout+xml\"/>")
            .Append("<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
        for (int k = 1; k <= n; k++)
            sb.Append($"<Override PartName=\"/ppt/slides/slide{k}.xml\" ContentType=\"{pml}slide+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    // rId1 = master, rId2 = theme, rId3 = presProps, rId(100+k) = slide k
    private static string PresentationRels(int n)
    {
        var list = new List<(string, string, string)>
        {
            ("rId1", "slideMaster", "slideMasters/slideMaster1.xml"),
            ("rId2", "theme", "theme/theme1.xml"),
            ("rId3", "presProps", "presProps.xml"),
        };
        for (int k = 1; k <= n; k++)
            list.Add(($"rId{100 + k}", "slide", $"slides/slide{k}.xml"));
        return Rels(list.ToArray());
    }

    private static string Presentation(int n)
    {
        var sb = new StringBuilder(Xml)
            .Append($"<p:presentation {Ns} saveSubsetFonts=\"1\">")
            .Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>")
            .Append("<p:sldIdLst>");
        for (int k = 1; k <= n; k++)
            sb.Append($"<p:sldId id=\"{255 + k}\" r:id=\"rId{100 + k}\"/>");
        return sb.Append("</p:sldIdLst>")
            .Append($"<p:sldSz cx=\"{SlideCx}\" cy=\"{SlideCy}\"/>")
            .Append("<p:notesSz cx=\"6858000\" cy=\"9144000\"/>")
            .Append("</p:presentation>").ToString();
    }

    private static string Slide(int k, string fileName) =>
        $"{Xml}<p:sld {Ns}><p:cSld><p:spTree>{EmptyGroup}" +
        "<p:pic><p:nvPicPr>" +
        $"<p:cNvPr id=\"2\" name=\"Picture {k}\" descr=\"{SecurityElement.Escape(fileName)}\"/>" +
        "<p:cNvPicPr/><p:nvPr/></p:nvPicPr>" +
        "<p:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
        $"<p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{SlideCx}\" cy=\"{SlideCy}\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
        "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";

    private static readonly string SlideMaster =
        $"{Xml}<p:sldMaster {Ns}><p:cSld>" +
        "<p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" +
        $"<p:spTree>{EmptyGroup}</p:spTree></p:cSld>" +
        "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" " +
        "accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
        "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
        "<p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles>" +
        "</p:sldMaster>";

    private static readonly string SlideLayout =
        $"{Xml}<p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\">" +
        $"<p:spTree>{EmptyGroup}</p:spTree></p:cSld>" +
        "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static readonly string Theme = BuildTheme();

    private static string BuildTheme()
    {
        static string Clr(string name, string hex) => $"<a:{name}><a:srgbClr val=\"{hex}\"/></a:{name}>";
        const string fill = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
        string Three(string s) => s + s + s;

        return $"{Xml}<a:theme xmlns:a=\"{NsA}\" name=\"Office Theme\"><a:themeElements>" +
            "<a:clrScheme name=\"Office\">" +
            "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
            "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
            Clr("dk2", "44546A") + Clr("lt2", "E7E6E6") +
            Clr("accent1", "4472C4") + Clr("accent2", "ED7D31") + Clr("accent3", "A5A5A5") +
            Clr("accent4", "FFC000") + Clr("accent5", "5B9BD5") + Clr("accent6", "70AD47") +
            Clr("hlink", "0563C1") + Clr("folHlink", "954F72") +
            "</a:clrScheme>" +
            "<a:fontScheme name=\"Office\">" +
            "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
            "</a:fontScheme>" +
            "<a:fmtScheme name=\"Office\">" +
            $"<a:fillStyleLst>{Three(fill)}</a:fillStyleLst>" +
            $"<a:lnStyleLst>{Three($"<a:ln w=\"6350\">{fill}</a:ln>")}</a:lnStyleLst>" +
            $"<a:effectStyleLst>{Three("<a:effectStyle><a:effectLst/></a:effectStyle>")}</a:effectStyleLst>" +
            $"<a:bgFillStyleLst>{Three(fill)}</a:bgFillStyleLst>" +
            "</a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }
}
