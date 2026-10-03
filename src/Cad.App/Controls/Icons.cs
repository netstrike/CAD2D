using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Cad.App.Controls;

/// <summary>
/// Icone vettoriali a linea sottile in una griglia 24x24: la parte grigia è il disegno esistente, quella azzurra il
/// risultato del comando, come nelle icone dei CAD. Restano nitide a ogni dimensione e su ogni schermo.
/// </summary>
public static class Icons
{
    public static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));

    private const string Circle = "M20,12 A8,8 0 1 1 4,12 A8,8 0 1 1 20,12";
    private const string Magnifier = "M4,10 A6,6 0 1 0 16,10 A6,6 0 1 0 4,10 M14.5,14.5 L21,21";

    private static readonly Dictionary<string, (string Base, string Accent)> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LINEA"] = ("M2.5,18.5h3v3h-3z M18.5,2.5h3v3h-3z", "M5,19 L19,5"),
        ["POLILINEA"] = ("M1.5,17.5h3v3h-3z M6.5,5.5h3v3h-3z M13.5,12.5h3v3h-3z M19.5,3.5h3v3h-3z", "M3,19 L8,7 L15,14 L21,5"),
        ["CERCHIO"] = ("M10,12h4 M12,10v4", Circle),
        ["ARCO"] = ("M2.5,16.5h3v3h-3z M10.5,7.5h3v3h-3z M18.5,16.5h3v3h-3z", "M4,18 A9,9 0 0 1 20,18"),
        ["RETTANGOLO"] = ("M2.5,4.5h3v3h-3z M18.5,16.5h3v3h-3z", "M4,6h16v12h-16z"),
        ["TRATTEGGIO"] = ("M4,4h16v16h-16z", "M4,12 L12,4 M4,20 L20,4 M12,20 L20,12"),
        ["SPOSTA"] = ("M12,3v18 M3,12h18", "M12,3l-3,3 M12,3l3,3 M12,21l-3,-3 M12,21l3,-3 M3,12l3,-3 M3,12l3,3 M21,12l-3,-3 M21,12l-3,3"),
        ["COPIA"] = ("M3,9h11v12h-11z", "M9,3h12v12h-12z"),
        ["RUOTA"] = ("M8,10h8v4h-8z", "M5,12 A7,7 0 1 1 12,19 M12,19l3,-2.5 M12,19l3,2"),
        ["SPECCHIA"] = ("M10,5 L3,19 L10,19z M12,2v3 M12,7v3 M12,12v3 M12,17v3", "M14,5 L21,19 L14,19z"),
        ["SCALA"] = ("M3,15h6v6h-6z", "M3,3h18v18h-18z M10,14 L17,7 M17,7h-4 M17,7v4"),
        ["OFFSET"] = ("M3,21 C3,11 10,4 21,4", "M8,21 C8,15 13,9 21,9"),
        ["TAGLIA"] = ("M8,3v18 M16,3v18 M10,12h1 M12.5,12h1 M14,12h0.5", "M3,12h5 M16,12h5"),
        ["ESTENDI"] = ("M20,3v18 M3,12h9", "M13,12h2.5 M17,12h3 M20,12l-3,-2 M20,12l-3,2"),
        ["RACCORDA"] = ("M4,21v-9 M12,4h9", "M4,12 A8,8 0 0 1 12,4"),
        ["CIMA"] = ("M4,21v-9 M12,4h9", "M4,12 L12,4"),
        ["SERIE"] = ("M3,3h6v6h-6z", "M15,3h6v6h-6z M3,15h6v6h-6z M15,15h6v6h-6z"),
        ["ESPLODI"] = ("M9,9h6v6h-6z", "M12,2v4 M12,18v4 M2,12h4 M18,12h4 M5,5l3,3 M19,5l-3,3 M5,19l3,-3 M19,19l-3,-3"),
        ["CANCELLA"] = ("M4,7h16 M9,7v-3h6v3 M6,7l1.5,14h9l1.5,-14", "M10,11v6 M14,11v6"),
        ["TESTO"] = ("M8,20h8", "M5,5h14 M5,5v2 M19,5v2 M12,5v15"),
        ["TESTOM"] = ("M3,17h18 M3,21h13 M15,9h6 M15,13h6", "M3,4h10 M8,4v10"),
        ["MODIFICATESTO"] = ("M3,5h12 M9,5v13", "M13,21 L20,14 L22.5,16.5 L15.5,23.5 L13,23.5z"),
        ["QLINEARE"] = ("M4,21v-11 M20,21v-11 M10,7h4", "M4,14h16 M4,14l3,-2 M4,14l3,2 M20,14l-3,-2 M20,14l-3,2"),
        ["QALLINEATA"] = ("M7,19 L19,7 M7,19 L3,15 M19,7 L15,3", "M3,15 L15,3 M3,15l1,-3.5 M3,15l3.5,-1 M15,3l-3.5,1 M15,3l-1,3.5"),
        ["QRAGGIO"] = (Circle + " M11,12h2", "M12,12 L17.7,6.3 M17.7,6.3l-3.6,0.8 M17.7,6.3l-0.8,3.6"),
        ["QDIAMETRO"] = (Circle, "M6.3,17.7 L17.7,6.3 M17.7,6.3l-3.6,0.8 M17.7,6.3l-0.8,3.6 M6.3,17.7l3.6,-0.8 M6.3,17.7l0.8,-3.6"),
        ["QANGOLARE"] = ("M3,20h18 M3,20 L15,6", "M15,20 A12,12 0 0 0 10.8,10.9 M15,20l-2,-3 M15,20l2,-3"),
        ["STILEQUOTA"] = ("M3,9h18 M3,5v8 M21,5v8", "M6,15h12v6h-12z M9,18h6"),
        ["BLOCCO"] = ("M5,13h6v6h-6z M16,8 A3,3 0 1 0 16,8.01", "M2,2h20v20h-20z"),
        ["INSERISCI"] = ("M9,9h12v12h-12z", "M2,2 L13,13 M13,13h-5 M13,13v-5"),
        ["LAYER"] = ("M3,13 L12,18 L21,13 M3,17 L12,22 L21,17", "M12,3 L21,8.5 L12,14 L3,8.5z"),
        ["COLORE"] = ("M12,3 A9,9 0 1 0 12,21 C14,21 14,18 12.5,17 C11,16 12,14 14,14 H17 A4,4 0 0 0 21,10 C21,6 17,3 12,3", "M7,11 A1.2,1.2 0 1 0 7,11.01 M10,7 A1.2,1.2 0 1 0 10,7.01 M15,7 A1.2,1.2 0 1 0 15,7.01"),
        ["TIPOLINEA"] = ("M3,6h18 M3,18h7 M12,18h1 M15,18h6", "M3,12h5 M11,12h5 M19,12h2"),
        ["SCALATL"] = ("M3,8h4 M10,8h4 M17,8h4", "M3,16h8 M14,16h7 M3,20l3,-2 M21,20l-3,-2"),
        ["ZOOMESTENSIONI"] = (Magnifier, "M7,7h6v6h-6z"),
        ["ZOOM"] = (Magnifier, "M2,2h3 M7,2h3 M15,2h3 M2,2v3 M2,7v3"),
        ["NUOVO"] = ("M6,3h8l4,4v14h-12z M14,3v4h4", "M12,10v8 M8,14h8"),
        ["APRI"] = ("M3,6h6l2,2h9v3", "M3,20 L6,11 H22 L19,20z"),
        ["SALVA"] = ("M4,4h13l3,3v13h-16z", "M8,4v5h7v-5 M7,20v-6h10v6"),
        ["SALVACOME"] = ("M4,4h11l3,3v6 M4,4v16h8", "M8,4v4h6v-4 M14,22 L21,15 L23,17 L16,24z"),
        ["ANNULLA"] = (string.Empty, "M4,9h10a6,6 0 0 1 0,12h-5 M4,9l4,-4 M4,9l4,4"),
        ["RIPETI"] = (string.Empty, "M20,9h-10a6,6 0 0 0 0,12h5 M20,9l-4,-4 M20,9l-4,4"),
        ["PROPRIETA"] = ("M5,3h14v18h-14z", "M8,8h8 M8,12h8 M8,16h5"),
        ["DISTANZA"] = ("M2.5,16.5h3v3h-3z M18.5,4.5h3v3h-3z", "M5,17 L19,7 M3,21h18"),
        ["AREA"] = ("M4,18 L8,5 L20,8 L17,19z", "M9,10 L15,16 M8,14 L12,18 M12,8 L18,14"),
        ["OPZIONI"] = ("M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8", "M12,2v3 M12,19v3 M2,12h3 M19,12h3 M4.9,4.9l2.1,2.1 M17,17l2.1,2.1 M4.9,19.1l2.1,-2.1 M17,7l2.1,-2.1"),
        ["GRIGLIA"] = (string.Empty, "M4,4h0.1 M10,4h0.1 M16,4h0.1 M4,10h0.1 M10,10h0.1 M16,10h0.1 M4,16h0.1 M10,16h0.1 M16,16h0.1"),
        ["INCOLLACLIP"] = ("M7,5h-3v17h16v-17h-3 M8,3h8v4h-8z", "M9,12h8 M9,16h6"),
        ["COPIACLIP"] = ("M3,9h11v12h-11z", "M9,3h12v12h-12z"),
        ["TAGLIACLIP"] = ("M6,18 A3,3 0 1 0 6,18.01 M18,18 A3,3 0 1 0 18,18.01", "M8,16 L18,3 M16,16 L6,3"),
        ["COPIABASE"] = ("M3,9h11v12h-11z M9,3h12v12h-12z", "M3,21 m-2,0 h4 M3,19v4"),
        ["POLARE"] = ("M3,20h18 M3,20 A16,16 0 0 1 19,4", "M3,20 L15,8 M3,20 L19,14"),
        ["ID"] = ("M4,20 L20,20 M4,20 L4,4", "M14,10 m-3,0 h6 M14,7 v6"),
        ["GRUPPO"] = ("M3,3h2v2h-2z M19,3h2v2h-2z M3,19h2v2h-2z M19,19h2v2h-2z M6,4h12 M6,20h12 M4,6v12 M20,6v12", "M7,8h5v5h-5z M12,12 A3,3 0 1 0 12,12.01 M14,14h3v3h-3z"),
        ["SEPARA"] = ("M3,3h2v2h-2z M19,19h2v2h-2z M6,4h3 M15,4h3 M4,6v3 M4,15v3 M20,6v3 M20,15v3 M6,20h3 M15,20h3", "M7,7h5v5h-5z M13,13h4v4h-4z"),
        ["DIRETTRICE"] = ("M13,8h9 M14,12h6", "M3,21 L12,10 h1 M3,21l1,-4 M3,21l4,-1"),
        ["QCONTINUA"] = ("M3,20v-11 M12,20v-11 M21,20v-11", "M3,13h18 M3,13l2.5,-1.5 M3,13l2.5,1.5 M12,13l-2.5,-1.5 M12,13l-2.5,1.5 M12,13l2.5,-1.5 M12,13l2.5,1.5 M21,13l-2.5,-1.5 M21,13l-2.5,1.5"),
        ["QBASE"] = ("M3,21v-17 M12,21v-11 M21,21v-17", "M3,14h9 M3,14l2.5,-1.5 M3,14l2.5,1.5 M12,14l-2.5,-1.5 M12,14l-2.5,1.5 M3,7h18 M3,7l2.5,-1.5 M3,7l2.5,1.5 M21,7l-2.5,-1.5 M21,7l-2.5,1.5"),
        ["QCOORDINATA"] = ("M3,21h18 M3,21v-18", "M9,21v-8 l3,-3 v-5 M16,21v-6 v-8 M11,3h2 M15,3h2"),
        ["QARCO"] = ("M4,20 A9,9 0 0 1 20,20", "M2,14 A11,11 0 0 1 22,14 M10,4 A3,2 0 0 1 14,4"),
        ["SEGNOCENTRO"] = ("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4", "M1,12h7 M10,12h4 M16,12h7 M12,1v7 M12,10v4 M12,16v7"),
        ["ASSE"] = ("M3,5h18 M3,19h18", "M1,12h8 M11,12h2 M15,12h8"),
        ["TABELLA"] = ("M3,10h18 M3,15h18 M9,5v15 M15,5v15", "M3,5h18v15h-18z"),
        ["CELLA"] = ("M3,4h18v16h-18z M3,10h18 M3,15h18 M9,4v16 M15,4v16", "M9.5,10.5h5v4h-5z"),
        ["TOLLERANZA"] = ("M2,7h20v10h-20z M8,7v10 M16,7v10", "M3.5,15h3 M5,15v-6 M11,12 A1.8,1.8 0 1 0 11,12.01 M17.5,15l1.5,-5 l1.5,5"),
        ["NUVOLA"] = (string.Empty, "M5,8 A2.5,2.5 0 0 1 10,7 A2.5,2.5 0 0 1 15,7 A2.5,2.5 0 0 1 19.5,9 A2.5,2.5 0 0 1 19.5,15 A2.5,2.5 0 0 1 15,17.5 A2.5,2.5 0 0 1 10,17.5 A2.5,2.5 0 0 1 5,16 A2.5,2.5 0 0 1 5,8"),
        ["STILETESTO"] = ("M3,20h8 M7,4v16 M2,4h10", "M14,20 L18,9 L22,20 M15.5,16h5"),
        ["IMMAGINE"] = ("M3,4h18v16h-18z", "M3,17 L9,11 L13,15 L16,12 L21,17 M15,8 A1.5,1.5 0 1 0 15,8.01"),
        ["CALIBRA"] = ("M3,4h18v16h-18z", "M6,15h12 M6,13v4 M18,13v4 M6,15l2,-1.5 M6,15l2,1.5 M18,15l-2,-1.5 M18,15l-2,1.5"),
        ["SCALADISEGNO"] = ("M3,3h18v18h-18z", "M6,17h12 M6,15v4 M10,16v2 M14,16v2 M18,15v4 M7,10h3 M11.5,8.5v0.5 M11.5,11v0.5 M13,7v6 M16,7v6"),
        ["STAMPA"] = ("M6,9v-6h12v6 M6,17h-3v-8h18v8h-3", "M6,14h12v7h-12z M9,17h6"),
        ["ESPORTAPDF"] = ("M6,3h8l4,4v14h-12z M14,3v4h4", "M8.5,17v-5h2a1.5,1.5 0 0 1 0,3h-2 M13,12v5 M13,12h3 M13,14.5h2"),
        ["CHIUDI"] = ("M4,4h16v16h-16z", "M8,8 L16,16 M16,8 L8,16"),
        ["TEMA"] = ("M12,3 A9,9 0 1 0 12,21z", "M12,3 A9,9 0 0 1 12,21z"),
    };

    public static bool Has(string name) => Paths.ContainsKey(name);

    /// <summary>Icona di <paramref name="size"/> pixel; un comando senza icona ottiene un riquadro vuoto.</summary>
    public static Control Create(string name, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Paths.TryGetValue(name, out var paths))
        {
            // Ai piccoli formati il tratto si ingrossa un poco, per restare leggibile.
            var thickness = size <= 18 ? 1.8 : 1.4;
            if (paths.Base.Length > 0)
            {
                canvas.Children.Add(Stroke(paths.Base, null, thickness).Themed(Shape.StrokeProperty, "Cad.IconBase"));
            }

            canvas.Children.Add(Stroke(paths.Accent, AccentBrush, thickness));
        }

        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static Avalonia.Controls.Shapes.Path Stroke(string data, IBrush? brush, double thickness) => new()
    {
        Data = Avalonia.Media.Geometry.Parse(data),
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    };
}
