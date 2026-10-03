using Cad.Geometry;

namespace Cad.Plot;

/// <summary>
/// Dove finisce il disegno sul foglio: la trasformazione da coordinate di disegno a millimetri di carta (origine in alto
/// a sinistra, Y verso il basso) e la scala risultante.
/// </summary>
public sealed class PlotLayout
{
    private PlotLayout(PlotSettings settings, BoundingBox area, Matrix2D worldToPaper, double paperPerUnit)
    {
        Settings = settings;
        Area = area;
        WorldToPaper = worldToPaper;
        PaperPerUnit = paperPerUnit;
    }

    public PlotSettings Settings { get; }

    /// <summary>Area del disegno stampata.</summary>
    public BoundingBox Area { get; }

    public Matrix2D WorldToPaper { get; }

    /// <summary>Millimetri di carta per unità di disegno.</summary>
    public double PaperPerUnit { get; }

    /// <summary>Unità di disegno per millimetro: 50 vuol dire 1:50.</summary>
    public double Scale => 1 / PaperPerUnit;

    /// <summary>Zona stampabile del foglio, dentro i margini (millimetri, dall'angolo in alto a sinistra).</summary>
    public (double X, double Y, double Width, double Height) Printable =>
        (Settings.Margin, Settings.Margin, Settings.PaperWidth - 2 * Settings.Margin, Settings.PaperHeight - 2 * Settings.Margin);

    /// <summary>
    /// Parte del disegno che cade nella zona stampabile: con una scala fissa può essere più grande dell'area scelta,
    /// e ciò che le sta attorno si stampa comunque.
    /// </summary>
    public BoundingBox VisibleWorld
    {
        get
        {
            if (!WorldToPaper.TryInvert(out var inverse))
            {
                return Area;
            }

            var (x, y, width, height) = Printable;
            return BoundingBox.FromPoints(inverse.Transform(new Vector2(x, y)), inverse.Transform(new Vector2(x + width, y + height)));
        }
    }

    /// <summary>Falso se, alla scala scelta, l'area esce dal foglio (la parte fuori viene tagliata).</summary>
    public bool Fits
    {
        get
        {
            var (_, _, width, height) = Printable;
            return Area.Width * PaperPerUnit <= width + 1e-6 && Area.Height * PaperPerUnit <= height + 1e-6;
        }
    }

    /// <summary>Calcola la posizione sul foglio di <paramref name="area"/>.</summary>
    public static PlotLayout Compute(PlotSettings settings, BoundingBox area)
    {
        if (area.IsEmpty)
        {
            area = new BoundingBox(Vector2.Zero, new Vector2(settings.PaperWidth, settings.PaperHeight));
        }

        var printableWidth = Math.Max(1, settings.PaperWidth - 2 * settings.Margin);
        var printableHeight = Math.Max(1, settings.PaperHeight - 2 * settings.Margin);
        double paperPerUnit;
        if (settings.Fit)
        {
            var sx = area.Width > 0 ? printableWidth / area.Width : double.PositiveInfinity;
            var sy = area.Height > 0 ? printableHeight / area.Height : double.PositiveInfinity;
            paperPerUnit = Math.Min(sx, sy);
            if (double.IsInfinity(paperPerUnit))
            {
                paperPerUnit = 1;
            }
        }
        else
        {
            paperPerUnit = 1 / Math.Max(settings.Scale, 1e-12);
        }

        // Punto del foglio (Y verso l'alto, dal basso a sinistra) dove cade l'angolo in basso a sinistra dell'area.
        var x = settings.Margin;
        var y = settings.Margin;
        if (settings.Center)
        {
            x += (printableWidth - area.Width * paperPerUnit) / 2;
            y += (printableHeight - area.Height * paperPerUnit) / 2;
        }
        else
        {
            // Senza centratura l'area parte dall'angolo in alto a sinistra del foglio, come si legge un disegno.
            y += printableHeight - area.Height * paperPerUnit;
        }

        var m = Matrix2D.Translation(-area.Min)
            * Matrix2D.Scaling(paperPerUnit, -paperPerUnit)
            * Matrix2D.Translation(new Vector2(x, settings.PaperHeight - y));
        return new PlotLayout(settings, area, m, paperPerUnit);
    }
}
