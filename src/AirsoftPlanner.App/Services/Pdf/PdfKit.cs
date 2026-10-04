using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace AirsoftPlanner.App.Services.Pdf;

/// <summary>Couleurs des documents (#RRGGBB).</summary>
public static class PdfColors
{
    public const string Black = "#000000";
    public const string White = "#FFFFFF";
    public const string Grey50 = "#FAFAFA";
    public const string GreyLighten4 = "#F5F5F5";
    public const string GreyLighten3 = "#EEEEEE";
    public const string GreyLighten2 = "#E0E0E0";
    public const string GreyLighten1 = "#BDBDBD";
    public const string GreyDarken1 = "#757575";
    public const string GreyDarken2 = "#616161";
    public const string GreyDarken3 = "#424242";
    public const string Red = "#D32F2F";
    public const string Green = "#388E3C";
    public const string Orange = "#F57C00";
    public const string Blue = "#1E88E5";
}

/// <summary>Style d'un morceau de texte.</summary>
public record struct TextStyle(double? Size = null, bool Bold = false, bool SemiBold = false, bool Italic = false,
    string? Color = null, bool Mono = false)
{
    public static TextStyle Plain => default;
}

/// <summary>
/// Document PDF (PDFsharp/MigraDoc, licence MIT) : pages A4 avec en-tête et pied de page, texte, encadrés, tableaux,
/// images. Les symboles (★, ✔, ⛺, 🌐...) sont écrits avec une police de symboles.
/// </summary>
public sealed class PdfDoc
{
    private readonly Document _document = new();

    static PdfDoc()
    {
        GlobalFontSettings.FontResolver ??= new SystemFontResolver();
    }

    public PdfDoc(double fontSize = 10, double lineSpacing = 1.0)
    {
        var normal = _document.Styles[StyleNames.Normal]!;
        normal.Font.Name = SystemFontResolver.Sans;
        normal.Font.Size = fontSize;
        if (lineSpacing > 1.0)
        {
            normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
            normal.ParagraphFormat.LineSpacing = lineSpacing;
        }
    }

    /// <summary>Nouvelle section de pages (portrait ou paysage) ; le contenu s'ajoute au flux renvoyé.</summary>
    /// <param name="header">En-tête : titre à gauche (couleur), nom de l'OP à droite, filet en dessous.</param>
    /// <param name="footer">Pied de page centré, suivi de « page / total » si <paramref name="pageNumbers"/>.</param>
    public PdfFlow Section(bool landscape = false, double marginCm = 1.5, (string Left, string Right, string Color, double Line)? header = null,
        string? footer = null, bool pageNumbers = true, IEnumerable<string>? footerLines = null, bool headerPageNumbers = false,
        double headerSize = 11)
    {
        var section = _document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        // MigraDoc attend des dimensions explicites pour l'orientation paysage.
        setup.PageWidth = Unit.FromCentimeter(landscape ? 29.7 : 21);
        setup.PageHeight = Unit.FromCentimeter(landscape ? 21 : 29.7);
        var margin = Unit.FromCentimeter(marginCm);
        setup.LeftMargin = setup.RightMargin = margin;
        setup.TopMargin = Unit.FromCentimeter(marginCm + (header is null ? 0 : 0.9));
        setup.BottomMargin = Unit.FromCentimeter(marginCm + (footer is null && footerLines is null ? 0 : 0.8));
        setup.HeaderDistance = margin;
        setup.FooterDistance = Unit.FromCentimeter(Math.Max(0.4, marginCm - 0.3));
        var width = (landscape ? 29.7 : 21) - 2 * marginCm;

        if (header is { } h)
        {
            var p = section.Headers.Primary.AddParagraph();
            p.Format.Borders.Bottom.Width = h.Line;
            p.Format.Borders.Bottom.Color = Hex(h.Color);
            p.Format.Borders.DistanceFromBottom = 3;
            p.Format.TabStops.AddTabStop(Unit.FromCentimeter(width), TabAlignment.Right);
            Add(p, h.Left, new TextStyle(headerSize, Bold: !headerPageNumbers, SemiBold: headerPageNumbers, Color: h.Color));
            p.AddTab();
            if (headerPageNumbers)
            {
                p.Format.Font.Size = headerSize;
                p.Format.Font.Color = Hex(PdfColors.GreyDarken1);
                p.AddPageField();
                p.AddText(" / ");
                p.AddNumPagesField();
            }
            else
                Add(p, h.Right, new TextStyle(headerSize, SemiBold: true));
        }

        if (footer is not null || footerLines is not null)
        {
            foreach (var line in footerLines ?? [])
            {
                var extra = section.Footers.Primary.AddParagraph();
                Add(extra, line, new TextStyle(8, Color: PdfColors.GreyDarken1));
            }
            if (footer is not null)
            {
                var p = section.Footers.Primary.AddParagraph();
                p.Format.Alignment = ParagraphAlignment.Center;
                p.Format.Font.Size = 8;
                p.Format.Font.Color = Hex(PdfColors.GreyDarken1);
                Add(p, footer, new TextStyle(8, Color: PdfColors.GreyDarken1));
                if (pageNumbers)
                {
                    p.AddPageField();
                    p.AddText(" / ");
                    p.AddNumPagesField();
                }
            }
        }

        var contentHeight = (landscape ? 21 : 29.7) - setup.TopMargin.Centimeter - setup.BottomMargin.Centimeter;
        return new PdfFlow(section.Elements, width, contentHeight);
    }

    /// <summary>Page entière sur fond de couleur (couverture), avec une marge intérieure.</summary>
    public PdfFlow FullPage(string background, double paddingPt)
    {
        var section = _document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.PageWidth = Unit.FromCentimeter(21);
        setup.PageHeight = Unit.FromCentimeter(29.7);
        setup.LeftMargin = setup.RightMargin = setup.TopMargin = setup.BottomMargin = 0;
        var table = section.AddTable();
        table.AddColumn(Unit.FromCentimeter(21));
        var row = table.AddRow();
        row.Height = Unit.FromCentimeter(29.6);
        row.HeightRule = RowHeightRule.Exactly;
        table.Shading.Color = Hex(background);
        table.LeftPadding = table.RightPadding = table.TopPadding = table.BottomPadding = Unit.FromPoint(paddingPt);
        // Par défaut, MigraDoc place le bord du tableau à gauche de la marge (texte aligné sur la marge) : bord sur la marge.
        table.Rows.LeftIndent = 0;
        return new PdfFlow(row.Cells[0].Elements, 21 - 2 * paddingPt / 28.35, 29.7 - 2 * paddingPt / 28.35, 0);
    }

    public void Save(string path)
    {
        var renderer = new PdfDocumentRenderer { Document = _document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(path);
    }

    public byte[] ToBytes()
    {
        var renderer = new PdfDocumentRenderer { Document = _document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    // ----- Texte -----

    internal static Color Hex(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return Colors.Black;
        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>Ajoute du texte ; les symboles passent dans la police de symboles, le reste dans la police du texte.</summary>
    internal static void Add(Paragraph paragraph, string text, TextStyle style)
    {
        foreach (var (segment, symbol) in Segments(text))
        {
            var run = paragraph.AddFormattedText(segment);
            var font = run.Font;
            font.Name = symbol ? SystemFontResolver.Symbol
                : style.Mono ? SystemFontResolver.Mono
                : style.SemiBold && !style.Bold ? SystemFontResolver.SansSemiBold
                : SystemFontResolver.Sans;
            if (style.Size is { } size)
                font.Size = size;
            font.Bold = style.Bold && !symbol;
            font.Italic = style.Italic && !symbol;
            if (style.Color is { } color)
                font.Color = Hex(color);
        }
    }

    /// <summary>Découpe le texte en morceaux « texte » et « symboles » (flèches, pictogrammes, émojis : U+2190 et au-delà).</summary>
    internal static IEnumerable<(string Text, bool Symbol)> Segments(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        var current = new StringBuilder();
        bool? currentSymbol = null;
        for (var i = 0; i < text.Length; i++)
        {
            var isPair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            var codePoint = isPair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            // Sélecteur de variante (émoji) : sans effet dans un PDF, ignoré.
            if (codePoint is 0xFE0E or 0xFE0F)
                continue;
            var symbol = codePoint >= 0x2190 && codePoint is not (0x2212);
            if (currentSymbol is { } previous && previous != symbol && current.Length > 0)
            {
                yield return (current.ToString(), previous);
                current.Clear();
            }
            currentSymbol = symbol;
            current.Append(text[i]);
            if (isPair)
                current.Append(text[++i]);
        }
        if (current.Length > 0)
            yield return (current.ToString(), currentSymbol == true);
    }
}

/// <summary>Flux de contenu (page ou cellule) : chaque élément s'ajoute sous le précédent, séparés par un espacement.</summary>
public sealed class PdfFlow
{
    private readonly DocumentElements _elements;

    internal PdfFlow(DocumentElements elements, double widthCm, double heightCm, double spacing = 8)
    {
        _elements = elements;
        WidthCm = widthCm;
        HeightCm = heightCm;
        Spacing = spacing;
    }

    /// <summary>Largeur disponible (cm).</summary>
    public double WidthCm { get; }

    /// <summary>Hauteur de page disponible (cm).</summary>
    public double HeightCm { get; }

    /// <summary>Espace sous chaque élément (points).</summary>
    public double Spacing { get; set; }

    /// <summary>Paragraphe d'un seul style.</summary>
    public Paragraph Text(string text, TextStyle style = default, double spaceBefore = 0, double indentCm = 0,
        ParagraphAlignment alignment = ParagraphAlignment.Left)
    {
        var p = Paragraph(spaceBefore, indentCm, alignment);
        PdfDoc.Add(p, text, style);
        return p;
    }

    /// <summary>Paragraphe composé de plusieurs morceaux de styles différents.</summary>
    public Paragraph Line(params (string Text, TextStyle Style)[] spans) => Line(0, 0, spans);

    public Paragraph Line(double spaceBefore, double indentCm, params (string Text, TextStyle Style)[] spans)
    {
        var p = Paragraph(spaceBefore, indentCm, ParagraphAlignment.Left);
        foreach (var (text, style) in spans)
            PdfDoc.Add(p, text, style);
        return p;
    }

    /// <summary>Puce « • texte » en retrait.</summary>
    public Paragraph Bullet(string text, double indentCm = 0.4, TextStyle style = default, string? bulletColor = null)
    {
        var p = Paragraph(0, indentCm + 0.4, ParagraphAlignment.Left);
        p.Format.FirstLineIndent = Unit.FromCentimeter(-0.4);
        p.Format.TabStops.AddTabStop(Unit.FromCentimeter(indentCm + 0.4));
        PdfDoc.Add(p, "•", bulletColor is null ? style : style with { Color = bulletColor });
        p.AddTab();
        PdfDoc.Add(p, text, style);
        return p;
    }

    /// <summary>Encadré : fond (et bordure) avec un titre en petites capitales facultatif.</summary>
    public void Box(string? title, Action<PdfFlow> content, string background = PdfColors.GreyLighten4, string? border = null,
        double padding = 8, double spacing = 2)
    {
        var table = NewTable([WidthCm]);
        var cell = table.AddRow().Cells[0];
        Decorate(table, background, border, padding);
        var inner = new PdfFlow(cell.Elements, WidthCm - 2 * padding / 28.35, HeightCm, spacing);
        if (title is not null)
            inner.Text(title.ToUpperInvariant(), new TextStyle(8, Bold: true, Color: PdfColors.GreyDarken2));
        content(inner);
        inner.TrimLastSpacing();
        Gap();
    }

    /// <summary>Colonnes côte à côte (largeurs relatives), chacune avec son contenu.</summary>
    public void Columns(double[] weights, double gapPt, params Action<PdfFlow>[] contents)
    {
        var gapCm = gapPt / 28.35;
        var widths = Widths(weights, WidthCm - gapCm * (weights.Length - 1));
        var columns = new List<double>();
        for (var i = 0; i < widths.Length; i++)
        {
            columns.Add(widths[i]);
            if (i < widths.Length - 1)
                columns.Add(gapCm);
        }
        var table = NewTable(columns);
        table.LeftPadding = table.RightPadding = table.TopPadding = table.BottomPadding = 0;
        var row = table.AddRow();
        for (var i = 0; i < contents.Length; i++)
        {
            var inner = new PdfFlow(row.Cells[i * 2].Elements, widths[i], HeightCm, Spacing);
            contents[i](inner);
            inner.TrimLastSpacing();
        }
        Gap();
    }

    /// <summary>Tableau : en-tête en gras souligné (répété sur chaque page), une ligne par élément.</summary>
    public Table Table(double[] weights, IReadOnlyList<string>? headers, IEnumerable<IReadOnlyList<(string Text, TextStyle Style)>> rows,
        double rowPadding = 2, string? headerLine = PdfColors.Black, string? headerBackground = null, string? zebra = null)
    {
        var widths = Widths(weights, WidthCm);
        var table = NewTable(widths);
        table.TopPadding = table.BottomPadding = rowPadding;
        table.LeftPadding = headerBackground is null ? 0 : rowPadding;
        table.Rows.LeftIndent = 0;
        table.RightPadding = 4;
        if (headers is not null)
        {
            var head = table.AddRow();
            head.HeadingFormat = true;
            if (headerLine is not null)
            {
                head.Borders.Bottom.Width = 1;
                head.Borders.Bottom.Color = PdfDoc.Hex(headerLine);
            }
            if (headerBackground is not null)
                head.Shading.Color = PdfDoc.Hex(headerBackground);
            for (var i = 0; i < headers.Count; i++)
                PdfDoc.Add(head.Cells[i].AddParagraph(), headers[i],
                    new TextStyle(SemiBold: true, Color: headerBackground is null ? null : PdfColors.White));
        }
        var index = 0;
        foreach (var cells in rows)
        {
            var row = table.AddRow();
            if (zebra is not null && index++ % 2 == 0)
                row.Shading.Color = PdfDoc.Hex(zebra);
            for (var i = 0; i < cells.Count && i < widths.Length; i++)
                PdfDoc.Add(row.Cells[i].AddParagraph(), cells[i].Text, cells[i].Style);
        }
        Gap();
        return table;
    }

    /// <summary>Image ajustée à la largeur disponible (ou à <paramref name="widthCm"/>), sans dépasser <paramref name="maxHeightCm"/>.</summary>
    public Paragraph Image(byte[] data, double? widthCm = null, double? maxHeightCm = null, ParagraphAlignment alignment = ParagraphAlignment.Left,
        string? border = null)
    {
        var (pixelWidth, pixelHeight) = ImageSize(data);
        var width = Math.Min(widthCm ?? WidthCm, WidthCm);
        var height = pixelWidth > 0 ? width * pixelHeight / pixelWidth : width;
        var limit = maxHeightCm ?? HeightCm - 0.5;
        if (height > limit && pixelHeight > 0)
        {
            height = limit;
            width = height * pixelWidth / pixelHeight;
        }

        var p = Paragraph(0, 0, alignment);
        var image = p.AddImage("base64:" + Convert.ToBase64String(Normalize(data)));
        image.Width = Unit.FromCentimeter(width);
        image.Height = Unit.FromCentimeter(height);
        image.LockAspectRatio = true;
        if (border is not null)
        {
            image.LineFormat.Width = 0.5;
            image.LineFormat.Color = PdfDoc.Hex(border);
        }
        return p;
    }

    /// <summary>Espace vertical supplémentaire (points).</summary>
    public void Space(double points)
    {
        var p = _elements.AddParagraph();
        p.Format.Font.Size = 1;
        p.Format.SpaceAfter = Unit.FromPoint(Math.Max(0, points - 1));
    }

    public void PageBreak() => _elements.AddPageBreak();

    // ----- Interne -----

    private Paragraph Paragraph(double spaceBefore, double indentCm, ParagraphAlignment alignment)
    {
        var p = _elements.AddParagraph();
        p.Format.SpaceAfter = Unit.FromPoint(Spacing);
        if (spaceBefore > 0)
            p.Format.SpaceBefore = Unit.FromPoint(spaceBefore);
        if (indentCm > 0)
            p.Format.LeftIndent = Unit.FromCentimeter(indentCm);
        p.Format.Alignment = alignment;
        return p;
    }

    private Table NewTable(IEnumerable<double> widthsCm)
    {
        var table = _elements.AddTable();
        foreach (var width in widthsCm)
            table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }

    private static void Decorate(Table table, string background, string? border, double padding)
    {
        table.Shading.Color = PdfDoc.Hex(background);
        table.LeftPadding = table.RightPadding = table.TopPadding = table.BottomPadding = Unit.FromPoint(padding);
        // Par défaut, MigraDoc place le bord du tableau à gauche de la marge (texte aligné sur la marge) : le fond reste dans la colonne.
        table.Rows.LeftIndent = 0;
        if (border is not null)
        {
            table.Borders.Width = 1;
            table.Borders.Color = PdfDoc.Hex(border);
        }
    }

    // Espace sous un tableau (MigraDoc n'en met pas).
    private void Gap()
    {
        if (Spacing > 0)
            Space(Spacing);
    }

    // Pas d'espace sous le dernier élément d'un encadré ou d'une colonne.
    internal void TrimLastSpacing()
    {
        if (_elements.Count > 0 && _elements[_elements.Count - 1] is Paragraph last)
            last.Format.SpaceAfter = 0;
    }

    private static double[] Widths(double[] weights, double total)
    {
        var sum = weights.Sum();
        return weights.Select(w => total * w / sum).ToArray();
    }

    /// <summary>
    /// Image dans un format que PDFsharp sait lire : les JPEG restent tels quels, les autres images (PNG en niveaux de
    /// gris ou à palette, comme celles des QR codes...) sont réencodées en PNG couleur.
    /// </summary>
    internal static byte[] Normalize(byte[] data)
    {
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(data));
            if (codec is null || codec.EncodedFormat == SkiaSharp.SKEncodedImageFormat.Jpeg)
                return data;
            using var bitmap = SkiaSharp.SKBitmap.Decode(data).Copy(SkiaSharp.SKColorType.Rgba8888);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
        }
        catch (ArgumentException)
        {
            return data;
        }
    }

    /// <summary>Dimensions en pixels d'une image PNG ou JPEG.</summary>
    internal static (int Width, int Height) ImageSize(byte[] data)
    {
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(data));
            return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
        }
        catch (ArgumentException)
        {
            return (0, 0);
        }
    }
}
