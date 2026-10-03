using Cad.Geometry;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.Plot;

/// <summary>Stampa su PDF vettoriale, SVG o immagine: un foglio per file, alla scala e nel formato delle impostazioni.</summary>
public static class PlotExporter
{
    /// <summary>Punti tipografici (unità del PDF) per millimetro.</summary>
    public const double PointsPerMm = 72 / 25.4;

    /// <summary>Pixel CSS (unità dell'SVG, 96 per pollice) per millimetro.</summary>
    public const double SvgUnitsPerMm = 96 / 25.4;

    public static void Pdf(Stream stream, Scene scene, PlotLayout layout, string title = "CAD2D")
    {
        var settings = layout.Settings;
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "CAD2D",
            Producer = "CAD2D",
            RasterDpi = 300,
        });
        var canvas = document.BeginPage((float)(settings.PaperWidth * PointsPerMm), (float)(settings.PaperHeight * PointsPerMm));
        PlotRenderer.Render(canvas, scene, layout, PointsPerMm);
        document.EndPage();
        document.Close();
    }

    public static void Svg(Stream stream, Scene scene, PlotLayout layout)
    {
        var settings = layout.Settings;
        // Lo stream va chiuso dalla tela SVG prima di essere usato: si scrive in un buffer e poi si copia.
        using var buffer = new SKDynamicMemoryWStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, (float)(settings.PaperWidth * SvgUnitsPerMm), (float)(settings.PaperHeight * SvgUnitsPerMm)), buffer))
        {
            PlotRenderer.Render(canvas, scene, layout, SvgUnitsPerMm);
        }

        // Skia scrive la misura in pixel: si passa ai millimetri, così l'SVG si apre e si stampa alla misura del foglio.
        using var data = buffer.DetachAsData();
        var svg = System.Text.Encoding.UTF8.GetString(data.ToArray());
        var size = System.Text.RegularExpressions.Regex.Match(svg, "<svg ([^>]*?)width=\"([\\d.]+)\" height=\"([\\d.]+)\"");
        if (size.Success)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var replacement = $"<svg {size.Groups[1].Value}width=\"{settings.PaperWidth.ToString("0.###", inv)}mm\" height=\"{settings.PaperHeight.ToString("0.###", inv)}mm\" viewBox=\"0 0 {size.Groups[2].Value} {size.Groups[3].Value}\"";
            svg = string.Concat(svg.AsSpan(0, size.Index), replacement, svg.AsSpan(size.Index + size.Length));
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        stream.Write(bytes);
    }

    /// <summary>Il foglio come immagine a <paramref name="dpi"/> punti per pollice (anteprima, PNG).</summary>
    public static SKBitmap Bitmap(Scene scene, PlotLayout layout, double dpi)
    {
        var settings = layout.Settings;
        var unitsPerMm = dpi / 25.4;
        var bitmap = new SKBitmap(Math.Max(1, (int)Math.Round(settings.PaperWidth * unitsPerMm)), Math.Max(1, (int)Math.Round(settings.PaperHeight * unitsPerMm)));
        using var canvas = new SKCanvas(bitmap);
        PlotRenderer.Render(canvas, scene, layout, unitsPerMm);
        return bitmap;
    }

    public static void Png(Stream stream, Scene scene, PlotLayout layout, double dpi = 300)
    {
        using var bitmap = Bitmap(scene, layout, dpi);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Una striscia orizzontale del foglio a <paramref name="dpi"/>, dalla riga <paramref name="top"/> per
    /// <paramref name="rows"/> righe: la stampante riceve il foglio a pezzi senza tenerlo tutto in memoria.
    /// </summary>
    public static SKBitmap Band(Scene scene, PlotLayout layout, double dpi, int top, int rows)
    {
        var settings = layout.Settings;
        var unitsPerMm = dpi / 25.4;
        var width = Math.Max(1, (int)Math.Round(settings.PaperWidth * unitsPerMm));
        var bitmap = new SKBitmap(new SKImageInfo(width, rows, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(0, -top);
        PlotRenderer.Render(canvas, scene, layout, unitsPerMm);
        return bitmap;
    }

    /// <summary>Area del disegno da stampare secondo le impostazioni.</summary>
    public static BoundingBox AreaOf(PlotSettings settings, Scene scene) => settings.Area switch
    {
        PlotArea.Extents => scene.Bounds,
        _ when !settings.Window.IsEmpty => settings.Window,
        _ => scene.Bounds,
    };
}
