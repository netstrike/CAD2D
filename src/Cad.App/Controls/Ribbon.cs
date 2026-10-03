using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cad.App.Controls;

/// <summary>Pulsante della barra multifunzione: grande (icona e nome sotto) o piccolo (icona e nome a fianco).</summary>
public sealed record RibbonButton(string Command, string Label, string Tip, bool Large = false, string? Icon = null);

/// <summary>Gruppo di pulsanti con il titolo sotto; <see cref="Custom"/> ospita controlli propri (caselle dei layer).</summary>
public sealed record RibbonPanel(string Title, IReadOnlyList<RibbonButton> Buttons, Func<Control>? Custom = null);

public sealed record RibbonTab(string Title, IReadOnlyList<RibbonPanel> Panels);

/// <summary>
/// Barra multifunzione come in DraftSight: schede per argomento, gruppi con titolo, pulsanti grandi per i comandi più
/// usati e piccoli in colonne da tre per gli altri. Ogni pulsante chiede un comando con <see cref="CommandRequested"/>.
/// </summary>
public sealed class Ribbon : TabControl
{

    public event EventHandler<string>? CommandRequested;

    protected override Type StyleKeyOverride => typeof(TabControl);

    public void Build(IReadOnlyList<RibbonTab> tabs)
    {
        Items.Clear();
        foreach (var tab in tabs)
        {
            var panels = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var panel in tab.Panels)
            {
                panels.Children.Add(BuildPanel(panel));
            }

            Items.Add(new TabItem
            {
                Header = tab.Title,
                FontSize = 12.5,
                MinHeight = 26,
                Padding = new Thickness(12, 3),
                Content = new Border
                {
                    Height = 98,
                    Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = panels },
                }.Themed(Border.BackgroundProperty, "Cad.Panel"),
            });
        }

        SelectedIndex = 0;
    }

    private Control BuildPanel(RibbonPanel panel)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 4, 4, 0) };
        var small = new List<RibbonButton>();

        void FlushSmall()
        {
            for (var i = 0; i < small.Count; i += 3)
            {
                var column = new StackPanel { Spacing = 1 };
                foreach (var button in small.Skip(i).Take(3))
                {
                    column.Children.Add(SmallButton(button));
                }

                content.Children.Add(column);
            }

            small.Clear();
        }

        foreach (var button in panel.Buttons)
        {
            if (button.Large)
            {
                FlushSmall();
                content.Children.Add(LargeButton(button));
            }
            else
            {
                small.Add(button);
            }
        }

        FlushSmall();
        if (panel.Custom is not null)
        {
            content.Children.Add(panel.Custom());
        }

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(content);
        var title = new TextBlock
        {
            Text = panel.Title,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        };
        title.Themed(TextBlock.ForegroundProperty, "Cad.Subtle");
        Grid.SetRow(title, 1);
        grid.Children.Add(title);

        return new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(2, 0),
            Child = grid,
        }.Themed(Border.BorderBrushProperty, "Cad.Border");
    }

    private Button LargeButton(RibbonButton button)
    {
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Icons.Create(button.Icon ?? button.Command, 30));
        stack.Children.Add(new TextBlock { Text = button.Label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        return MakeButton(button, stack, new Thickness(6, 4), 58);
    }

    private Button SmallButton(RibbonButton button)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        stack.Children.Add(Icons.Create(button.Icon ?? button.Command, 16));
        stack.Children.Add(new TextBlock { Text = button.Label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
        return MakeButton(button, stack, new Thickness(4, 2), 0);
    }

    private Button MakeButton(RibbonButton button, Control content, Thickness padding, double minWidth)
    {
        var result = new Button
        {
            Content = content,
            Padding = padding,
            MinWidth = minWidth,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Focusable = false,
        };
        ToolTip.SetTip(result, $"{button.Tip}\n\nComando: {button.Command}");
        result.Click += (_, _) => CommandRequested?.Invoke(this, button.Command);
        return result;
    }

    /// <summary>Le schede di CAD2D. <paramref name="properties"/> costruisce le caselle layer, colore e tipo di linea.</summary>
    public static IReadOnlyList<RibbonTab> Default(Func<Control> properties) =>
    [
        new("Home",
        [
            new("Disegna",
            [
                new("LINEA", "Linea", "Linee consecutive", Large: true),
                new("POLILINEA", "Polilinea", "Polilinea con più tratti", Large: true),
                new("CERCHIO", "Cerchio", "Cerchio da centro e raggio", Large: true),
                new("ARCO", "Arco", "Arco per tre punti"),
                new("RETTANGOLO", "Rettangolo", "Rettangolo da due angoli"),
                new("DLGTRATTEGGIO", "Tratteggio", "Tratteggio di un'area chiusa: motivo, scala, angolo", Icon: "TRATTEGGIO"),
            ]),
            new("Modifica",
            [
                new("SPOSTA", "Sposta", "Sposta gli oggetti", Large: true),
                new("COPIA", "Copia", "Copia gli oggetti"),
                new("RUOTA", "Ruota", "Ruota attorno a un punto"),
                new("SPECCHIA", "Specchia", "Simmetria rispetto a un asse"),
                new("SCALA", "Scala", "Scala rispetto a un punto"),
                new("OFFSET", "Offset", "Copia parallela a distanza"),
                new("SERIE", "Serie", "Copie in serie rettangolare o polare"),
                new("TAGLIA", "Taglia", "Taglia sui bordi"),
                new("ESTENDI", "Estendi", "Estende fino ai bordi"),
                new("RACCORDA", "Raccorda", "Raccordo con raggio"),
                new("CIMA", "Cima", "Smusso tra due linee"),
                new("ESPLODI", "Esplodi", "Scompone blocchi, polilinee, quote"),
                new("CANCELLA", "Cancella", "Cancella gli oggetti"),
            ]),
            new("Gruppi",
            [
                new("GRUPPO", "Gruppo", "Riunisce gli oggetti in un gruppo: si selezionano insieme", Large: true),
                new("SEPARA", "Separa", "Scioglie i gruppi degli oggetti scelti"),
            ]),
            new("Appunti",
            [
                new("INCOLLACLIP", "Incolla", "Incolla gli oggetti degli appunti (Ctrl+V)", Large: true),
                new("COPIACLIP", "Copia", "Copia negli appunti (Ctrl+C)"),
                new("TAGLIACLIP", "Taglia", "Taglia negli appunti (Ctrl+X)"),
                new("COPIABASE", "Con base", "Copia con punto base (Ctrl+Maiusc+C)"),
            ]),
            new("Layer e proprietà",
            [
                new("GESTORELAYER", "Layer", "Gestore layer: stato, colore, tipo e spessore di linea", Large: true, Icon: "LAYER"),
                new("PROPRIETA", "Proprietà", "Mostra la palette Proprietà", Large: true),
            ], properties),
        ]),
        new("Inserisci",
        [
            new("Blocchi",
            [
                new("BLOCCO", "Crea blocco", "Raggruppa oggetti in un blocco", Large: true),
                new("INSERISCI", "Inserisci", "Inserisce un blocco", Large: true),
                new("ESPLODI", "Esplodi", "Scompone un blocco nei suoi oggetti", Large: true),
            ]),
            new("Immagini",
            [
                new("DLGIMMAGINE", "Immagine", "Inserisce un'immagine (PNG, JPG, BMP) da ricalcare, semitrasparente e dietro al disegno", Large: true, Icon: "IMMAGINE"),
                new("CALIBRA", "Calibra", "Porta in scala un'immagine: due punti e la loro distanza reale", Large: true),
            ]),
            new("Scala",
            [
                new("DLGSCALA", "Scala disegno", "Scala del disegno: assegna (quote invariate) o ridimensiona dalla scala corrente", Large: true, Icon: "SCALADISEGNO"),
            ]),
        ]),
        new("Annota",
        [
            new("Testo",
            [
                new("TESTO", "Testo", "Testo su una riga", Large: true),
                new("TESTOM", "Più righe", "Testo su più righe", Large: true),
                new("MODIFICATESTO", "Modifica", "Modifica un testo o una quota"),
                new("DLGSTILETESTO", "Stile", "Stili di testo: carattere, altezza, larghezza, inclinazione", Icon: "STILETESTO"),
            ]),
            new("Direttrici",
            [
                new("DIRETTRICE", "Direttrice", "Freccia con testo: punta, punti intermedi, testo", Large: true),
            ]),
            new("Quote",
            [
                new("QLINEARE", "Lineare", "Quota orizzontale o verticale", Large: true),
                new("QALLINEATA", "Allineata", "Quota parallela ai due punti"),
                new("QANGOLARE", "Angolare", "Angolo tra due linee o di un arco"),
                new("QRAGGIO", "Raggio", "Raggio di un arco o cerchio"),
                new("QDIAMETRO", "Diametro", "Diametro di un cerchio"),
                new("QARCO", "Lungh. arco", "Lunghezza di un arco"),
                new("QCOORDINATA", "Coordinata", "Coordinata X o Y di un punto rispetto all'origine"),
                new("QCONTINUA", "In serie", "Quote in serie dalla fine dell'ultima quota"),
                new("QBASE", "Da base", "Quote dalla stessa linea di base, una sopra l'altra"),
                new("DLGSTILEQUOTA", "Stile", "Stile di quota: testo, frecce, decimali", Icon: "STILEQUOTA"),
            ]),
            new("Simboli",
            [
                new("SEGNOCENTRO", "Segno centro", "Assi di un cerchio o di un arco"),
                new("ASSE", "Asse", "Asse tra due linee"),
                new("TOLLERANZA", "Tolleranza", "Riquadro di tolleranza geometrica"),
                new("NUVOLA", "Nuvola", "Nuvola di revisione: rettangolo, poligono o oggetto", Large: true),
            ]),
            new("Tabelle",
            [
                new("TABELLA", "Tabella", "Tabella con righe e colonne, testi nelle celle", Large: true),
                new("CELLA", "Cella", "Scrive o cambia il testo di una cella", Large: true),
            ]),
            new("Tratteggio",
            [
                new("DLGTRATTEGGIO", "Tratteggio", "Tratteggio di un'area chiusa: motivo, scala, angolo", Large: true, Icon: "TRATTEGGIO"),
            ]),
        ]),
        new("Vista",
        [
            new("Zoom",
            [
                new("ZOOMESTENSIONI", "Estensioni", "Mostra tutto il disegno", Large: true),
                new("ZOOM", "Finestra", "Ingrandisce una finestra", Large: true),
            ]),
            new("Aiuti",
            [
                new("GRIGLIA", "Griglia", "Passo e accensione della griglia (F7, F9)", Large: true),
                new("POLARE", "Polare", "Incremento del tracciamento polare (F10)", Large: true, Icon: "POLARE"),
            ]),
            new("Palette",
            [
                new("PROPRIETA", "Proprietà", "Mostra la palette Proprietà", Large: true),
                new("PALETTELAYER", "Layer", "Mostra la palette Layer", Large: true, Icon: "LAYER"),
            ]),
            new("Aspetto",
            [
                new("TEMA", "Tema", "Passa dal tema scuro a quello chiaro e viceversa", Large: true),
            ]),
        ]),
        new("Output",
        [
            new("Stampa",
            [
                new("STAMPA", "Stampa", "Stampa in scala su stampante, PDF, SVG o PNG, con anteprima (Ctrl+P)", Large: true),
                new("ESPORTAPDF", "PDF", "Esporta il foglio in PDF vettoriale", Large: true),
            ]),
            new("File",
            [
                new("SALVACOME", "Salva come", "Salva in DXF o DWG (Ctrl+Maiusc+S)", Large: true),
                new("CHIUDI", "Chiudi", "Chiude il disegno corrente (Ctrl+F4)", Large: true),
            ]),
        ]),
        new("Gestisci",
        [
            new("Misura",
            [
                new("DISTANZA", "Distanza", "Distanza e angolo tra due punti", Large: true),
                new("AREA", "Area", "Area e perimetro per punti o di un oggetto", Large: true),
                new("ID", "Coordinate", "Coordinate di un punto", Large: true, Icon: "ID"),
            ]),
            new("Layer",
            [
                new("GESTORELAYER", "Gestore", "Gestore layer: stato, colore, tipo e spessore di linea", Large: true, Icon: "LAYER"),
                new("LAYER", "Da comando", "Gestione dei layer dalla riga di comando"),
                new("COLORE", "Colore", "Colore della selezione o dei nuovi oggetti"),
                new("TIPOLINEA", "Tipo linea", "Tipo di linea della selezione o dei nuovi oggetti"),
                new("SCALATL", "Scala TL", "Scala globale dei tipi di linea"),
            ]),
            new("Programma",
            [
                new("OPZIONI", "Opzioni", "Griglia, tracciamento, snap, tema", Large: true),
            ]),
        ]),
    ];
}
