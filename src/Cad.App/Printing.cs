using System.Drawing.Printing;
using System.Runtime.Versioning;
using Cad.Plot;
using PaperSize = System.Drawing.Printing.PaperSize;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.App;

/// <summary>
/// Stampa su una stampante di Windows. Il foglio si disegna con lo stesso motore del PDF, in strisce di pixel alla
/// risoluzione della stampante (al massimo 600 dpi), così anche un A0 non occupa troppa memoria.
/// </summary>
public static class Printing
{
    private const double MaxDpi = 600;
    private const int BandRows = 512;

    /// <summary>Stampanti installate; vuoto fuori da Windows.</summary>
    public static IReadOnlyList<string> Printers()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            return [];
        }

        try
        {
            return [.. PrinterSettings.InstalledPrinters.Cast<string>()];
        }
        catch (Exception)
        {
            // Servizio di stampa assente o non raggiungibile.
            return [];
        }
    }

    [SupportedOSPlatform("windows6.1")]
    public static void Print(string printer, Scene scene, PlotLayout layout, string documentName)
    {
        var settings = layout.Settings;
        using var document = new PrintDocument { DocumentName = documentName, PrintController = new StandardPrintController() };
        document.PrinterSettings.PrinterName = printer;
        if (!document.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException($"La stampante {printer} non è disponibile.");
        }

        document.DefaultPageSettings.PaperSize = ChoosePaper(document.PrinterSettings, settings.Paper);
        document.DefaultPageSettings.Landscape = settings.Landscape;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics!;
            graphics.PageUnit = System.Drawing.GraphicsUnit.Millimeter;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            var dpi = Math.Min(MaxDpi, Math.Max(e.PageSettings.PrinterResolution.X, 150));
            var unitsPerMm = dpi / 25.4;
            // L'origine della grafica è sul margine fisico della stampante: il foglio si sposta indietro di tanto.
            var offsetX = e.PageSettings.HardMarginX * 0.254;
            var offsetY = e.PageSettings.HardMarginY * 0.254;
            var height = (int)Math.Round(settings.PaperHeight * unitsPerMm);
            for (var top = 0; top < height; top += BandRows)
            {
                var rows = Math.Min(BandRows, height - top);
                using var band = PlotExporter.Band(scene, layout, dpi, top, rows);
                using var bitmap = ToGdi(band);
                graphics.DrawImage(bitmap, new System.Drawing.RectangleF(
                    (float)-offsetX,
                    (float)(top / unitsPerMm - offsetY),
                    (float)(band.Width / unitsPerMm),
                    (float)(rows / unitsPerMm)));
            }

            e.HasMorePages = false;
        };
        document.Print();
    }

    /// <summary>Il formato della stampante con le stesse misure (entro 2 mm), altrimenti un formato personalizzato.</summary>
    [SupportedOSPlatform("windows6.1")]
    private static PaperSize ChoosePaper(PrinterSettings printer, Cad.Plot.PaperSize paper)
    {
        static double Mm(int hundredthsOfInch) => hundredthsOfInch * 0.254;
        var width = Math.Min(paper.Width, paper.Height);
        var height = Math.Max(paper.Width, paper.Height);
        foreach (PaperSize candidate in printer.PaperSizes)
        {
            var w = Math.Min(Mm(candidate.Width), Mm(candidate.Height));
            var h = Math.Max(Mm(candidate.Width), Mm(candidate.Height));
            if (Math.Abs(w - width) < 2 && Math.Abs(h - height) < 2)
            {
                return candidate;
            }
        }

        return new PaperSize(paper.Name, (int)Math.Round(width / 0.254), (int)Math.Round(height / 0.254));
    }

    /// <summary>Copia una striscia di Skia (BGRA premoltiplicato) in una bitmap GDI+ dello stesso formato.</summary>
    [SupportedOSPlatform("windows6.1")]
    private static System.Drawing.Bitmap ToGdi(SKBitmap band)
    {
        var bitmap = new System.Drawing.Bitmap(band.Width, band.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, band.Width, band.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var source = band.Bytes;
            for (var y = 0; y < band.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(source, y * band.RowBytes, data.Scan0 + (y * data.Stride), band.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
