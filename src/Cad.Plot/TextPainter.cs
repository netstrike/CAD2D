using Cad.Document;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.Plot;

/// <summary>
/// Disegna i testi della scena con SkiaSharp, sullo schermo come sulla carta: altezza delle maiuscole, larghezza,
/// inclinazione, rotazione e allineamento come nei CAD.
/// </summary>
public static class TextPainter
{
    public static readonly SKTypeface DefaultTypeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;

    private static readonly Dictionary<string, SKTypeface> Typefaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Altezza delle maiuscole rispetto alla dimensione del carattere: l'altezza di un testo CAD è quella delle maiuscole.</summary>
    public static readonly float CapHeightRatio = MeasureCapHeightRatio();

    /// <summary>Carattere di una famiglia, cercato una volta sola; Arial se la famiglia non è installata.</summary>
    public static SKTypeface Typeface(string family)
    {
        lock (Typefaces)
        {
            if (!Typefaces.TryGetValue(family, out var typeface))
            {
                typeface = SKFontManager.Default.MatchFamily(family) ?? DefaultTypeface;
                Typefaces[family] = typeface;
            }

            return typeface;
        }
    }

    /// <summary>
    /// Disegna <paramref name="text"/> con la linea di base in (<paramref name="x"/>, <paramref name="y"/>) di una tela con la Y
    /// verso il basso, alto <paramref name="height"/> unità della tela. Il colore va già impostato su <paramref name="paint"/>.
    /// </summary>
    public static void Draw(SKCanvas canvas, RenderText text, double x, double y, double height, SKPaint paint)
    {
        paint.Typeface = Typeface(text.FontFamily);
        paint.TextSize = (float)(height / CapHeightRatio);
        paint.TextAlign = text.Alignment switch
        {
            TextHorizontalAlignment.Center => SKTextAlign.Center,
            TextHorizontalAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left,
        };

        canvas.Save();
        canvas.Translate((float)x, (float)y);
        // La Y va verso il basso: la rotazione antioraria del disegno diventa negativa.
        canvas.RotateRadians((float)-text.Rotation);
        canvas.Scale((float)text.WidthFactor, 1);
        if (text.Oblique != 0)
        {
            // Per inclinare a destra la parte alta si sposta verso X positive.
            canvas.Skew((float)-Math.Tan(text.Oblique), 0);
        }

        canvas.DrawText(text.Text, 0, 0, paint);
        canvas.Restore();
    }

    private static float MeasureCapHeightRatio()
    {
        using var paint = new SKPaint { Typeface = DefaultTypeface, TextSize = 100 };
        var capHeight = paint.FontMetrics.CapHeight;
        return capHeight > 0 ? capHeight / 100 : 0.7f;
    }
}
