using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// IMMAGINE inserisce un'immagine da ricalcare (collegata al file, non copiata nel disegno); CALIBRA la porta in scala
/// indicando due punti di cui si conosce la distanza reale.
/// </summary>
public static class ImageCommands
{
    /// <summary>Layer delle immagini: si blocca per non spostarle mentre si ricalca, o si spegne per nasconderle.</summary>
    public const string LayerName = "IMMAGINI";

    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("IMMAGINE", Attach, "IAT", "IMAGEATTACH");
        editor.RegisterCommand("CALIBRA", Calibrate, "CAL", "CALIBRATE");
    }

    private static async Task Attach(Editor ed)
    {
        var file = await ed.GetStringAsync("File dell'immagine (PNG, JPG, BMP, GIF):");
        if (!file.IsOk || string.IsNullOrWhiteSpace(file.Text))
        {
            return;
        }

        var path = file.Text.Trim().Trim('"');
        if (ImageEntity.ReadPixelSize(path) is not { } size)
        {
            ed.Write($"Immagine non trovata o formato non riconosciuto: {path}");
            return;
        }

        var opacity = 0.5;
        Vector2 corner;
        while (true)
        {
            var point = await ed.GetPointAsync(
                $"Angolo in basso a sinistra o [Opacità] <opacità {Math.Round(opacity * 100)}%>:",
                null,
                p => [Create(ed, path, size, p, size.Width, opacity)],
                "Opacità");
            if (point.Status == PromptStatus.Keyword)
            {
                var value = await ed.GetNumberAsync("Opacità in percentuale (10-100):");
                if (value.IsOk && value.Value is >= 10 and <= 100)
                {
                    opacity = value.Value / 100;
                }

                continue;
            }

            if (!point.IsOk)
            {
                return;
            }

            corner = point.Point;
            break;
        }

        var width = await ed.GetDistanceAsync(
            $"Larghezza <{size.Width}>:",
            corner,
            p => Math.Abs(p.X - corner.X) > Tolerance.Default ? [Create(ed, path, size, corner, Math.Abs(p.X - corner.X), opacity)] : []);
        if (width.Status == PromptStatus.Cancel || (width.IsOk && width.Value <= 0))
        {
            return;
        }

        var image = Insert(ed, path, corner, width.IsOk ? width.Value : size.Width, opacity);
        ed.Write($"Immagine di {size.Width}×{size.Height} pixel sul layer {image?.Layer.Name}. Con CALIBRA la porti in scala.");
    }

    /// <summary>Inserisce l'immagine sul layer <see cref="LayerName"/>, con la larghezza data e l'altezza in proporzione.</summary>
    public static ImageEntity? Insert(Editor ed, string path, Vector2 corner, double width, double opacity)
    {
        if (ImageEntity.ReadPixelSize(path) is not { } size || width <= 0)
        {
            return null;
        }

        var image = Create(ed, path, size, corner, width, opacity);
        if (ed.Document.FindLayer(LayerName) is not { } layer)
        {
            layer = ed.Document.GetOrAddLayer(LayerName);
            layer.Color = new CadColor(128, 128, 128);
        }

        image.Layer = layer;
        ed.Document.Edit("IMMAGINE", e => e.Add(image));
        return image;
    }

    private static ImageEntity Create(Editor ed, string path, (int Width, int Height) size, Vector2 corner, double width, double opacity)
    {
        var height = width * size.Height / size.Width;
        var image = ed.Styled(new ImageEntity(ed.CurrentLayer, Path.GetFullPath(path), corner, new Vector2(width, 0), new Vector2(0, height), size.Width, size.Height));
        image.Opacity = opacity;
        return image;
    }

    private static async Task Calibrate(Editor ed)
    {
        // Con una sola immagine scelta, o una sola nel disegno, non serve selezionarla.
        var picked = ed.Selection.Items.OfType<ImageEntity>().ToList();
        var all = ed.Document.ModelSpace.OfType<ImageEntity>().Take(2).ToList();
        var image = picked.Count == 1 ? picked[0] : all.Count == 1 ? all[0] : null;
        while (image is null)
        {
            var result = await ed.GetEntityAsync("Seleziona l'immagine:");
            if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.None)
            {
                return;
            }

            image = result.Entity as ImageEntity;
            if (image is null)
            {
                ed.Write("Non è un'immagine.");
            }
        }

        var first = await ed.GetPointAsync("Primo punto di riferimento sull'immagine:");
        if (!first.IsOk)
        {
            return;
        }

        var second = await ed.GetPointAsync("Secondo punto di riferimento:", first.Point);
        if (!second.IsOk)
        {
            return;
        }

        var measured = (second.Point - first.Point).Length;
        if (measured < Tolerance.Default)
        {
            ed.Write("I due punti coincidono.");
            return;
        }

        var real = await ed.GetNumberAsync($"Distanza reale tra i due punti <{PropertySheet.Format(measured)}>:");
        if (!real.IsOk || real.Value <= 0)
        {
            return;
        }

        Calibrate(ed, image, first.Point, measured, real.Value);
        ed.Selection.Clear();
    }

    /// <summary>Scala l'immagine attorno a <paramref name="basePoint"/> perché <paramref name="measured"/> diventi <paramref name="real"/>.</summary>
    public static void Calibrate(Editor ed, ImageEntity image, Vector2 basePoint, double measured, double real)
    {
        var factor = real / measured;
        ed.Document.Edit("CALIBRA", e => e.Replace(image, image.Transformed(Matrix2D.Scaling(factor, basePoint))));
        ed.Write($"Immagine scalata di {PropertySheet.Format(factor)}: ora è larga {PropertySheet.Format(image.U.Length * factor)}.");
    }
}
