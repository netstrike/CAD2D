using Avalonia.Platform.Storage;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using Cad.Plot;

namespace Cad.App;

/// <summary>STAMPA: finestra di stampa, scelta dell'area nel disegno e uscita su PDF, SVG, PNG o stampante.</summary>
public partial class MainWindow
{
    private static readonly FilePickerFileType PdfFiles = new("Documento PDF") { Patterns = ["*.pdf"] };
    private static readonly FilePickerFileType SvgFiles = new("Disegno SVG") { Patterns = ["*.svg"] };
    private static readonly FilePickerFileType PngFiles = new("Immagine PNG") { Patterns = ["*.png"] };

    private async Task PlotAsync(Editor ed, string? destination)
    {
        if (_activeTab is not { } tab)
        {
            return;
        }

        // Si parte adattando al foglio; togliendo la spunta la scala proposta è quella del disegno.
        var settings = tab.Plot ?? new PlotSettings { Scale = ed.Document.DrawingScale };
        destination ??= tab.PlotDestination;
        var printers = Printing.Printers();
        PlotDialog dialog;
        while (true)
        {
            dialog = new PlotDialog(Canvas.Scene, settings, Canvas.View.VisibleWorldBounds, destination, printers);
            await dialog.ShowDialog(this);
            settings = dialog.Settings;
            destination = dialog.Destination;
            if (!dialog.PickWindow)
            {
                break;
            }

            var first = await ed.GetPointAsync("Primo angolo dell'area da stampare:");
            if (first.IsOk)
            {
                var a = first.Point;
                var second = await ed.GetPointAsync("Angolo opposto:", a, p => [Rectangle(ed.CurrentLayer, a, p)]);
                if (second.IsOk && Math.Abs(second.Point.X - a.X) > Tolerance.Default && Math.Abs(second.Point.Y - a.Y) > Tolerance.Default)
                {
                    settings.Area = PlotArea.Window;
                    settings.Window = BoundingBox.FromPoints(a, second.Point);
                }
            }
        }

        tab.Plot = settings;
        tab.PlotDestination = destination;
        if (!dialog.Accepted)
        {
            return;
        }

        var scene = Canvas.Scene;
        var layout = PlotLayout.Compute(settings, PlotExporter.AreaOf(settings, scene));
        var summary = $"{settings.Paper.Name} {(settings.Landscape ? "orizzontale" : "verticale")}, scala {CadDocument.FormatScale(layout.Scale)}";
        var name = ed.Document.FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : "Disegno1";
        try
        {
            if (dialog.Printer is { } printer)
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                {
                    ed.Write("La stampa su stampante funziona solo su Windows: usa il PDF.");
                    return;
                }

                ed.Write($"Invio a {printer}...");
                await Task.Run(() =>
                {
                    if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                    {
                        Printing.Print(printer, scene, layout, name);
                    }
                });
                ed.Write($"Stampato su {printer}: {summary}.");
                return;
            }

            var (type, extension) = destination switch
            {
                PlotDialog.Svg => (SvgFiles, "svg"),
                PlotDialog.Png => (PngFiles, "png"),
                _ => (PdfFiles, "pdf"),
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Stampa su file",
                SuggestedFileName = $"{name}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices = [type],
                ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is not { } target)
            {
                return;
            }

            await Task.Run(() =>
            {
                using var stream = File.Create(target);
                switch (extension)
                {
                    case "svg":
                        PlotExporter.Svg(stream, scene, layout);
                        break;
                    case "png":
                        PlotExporter.Png(stream, scene, layout);
                        break;
                    default:
                        PlotExporter.Pdf(stream, scene, layout, name);
                        break;
                }
            });
            ed.Write($"Stampato {Path.GetFileName(target)}: {summary}.");
            if (!layout.Fits)
            {
                ed.Write("Attenzione: alla scala scelta parte del disegno esce dal foglio.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ed.Write($"Stampa non riuscita: {ex.Message}");
        }
    }

    private static PolylineEntity Rectangle(Layer layer, Vector2 a, Vector2 b) => new(
        layer,
        [new PolylineVertex(a), new PolylineVertex(new Vector2(b.X, a.Y)), new PolylineVertex(b), new PolylineVertex(new Vector2(a.X, b.Y))],
        isClosed: true);
}
