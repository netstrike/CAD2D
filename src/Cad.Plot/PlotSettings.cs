using Cad.Geometry;

namespace Cad.Plot;

/// <summary>Formato del foglio in millimetri, in verticale.</summary>
public sealed record PaperSize(string Name, double Width, double Height)
{
    public static readonly PaperSize A4 = new("A4", 210, 297);
    public static readonly PaperSize A3 = new("A3", 297, 420);
    public static readonly PaperSize A2 = new("A2", 420, 594);
    public static readonly PaperSize A1 = new("A1", 594, 841);
    public static readonly PaperSize A0 = new("A0", 841, 1189);
    public static readonly PaperSize Letter = new("Letter", 215.9, 279.4);

    public static IReadOnlyList<PaperSize> Standard { get; } = [A4, A3, A2, A1, A0, Letter];

    public override string ToString() => $"{Name} ({Width:0.#} × {Height:0.#} mm)";
}

/// <summary>Parte del disegno da stampare.</summary>
public enum PlotArea
{
    /// <summary>Tutto il disegno.</summary>
    Extents,

    /// <summary>Quello che si vede ora nella finestra.</summary>
    Display,

    /// <summary>Un rettangolo indicato con due punti.</summary>
    Window,
}

/// <summary>Impostazioni di stampa: foglio, area, scala e aspetto delle linee.</summary>
public sealed class PlotSettings
{
    public PaperSize Paper { get; set; } = PaperSize.A4;
    public bool Landscape { get; set; } = true;
    public PlotArea Area { get; set; } = PlotArea.Extents;

    /// <summary>Rettangolo per <see cref="PlotArea.Window"/> o vista corrente per <see cref="PlotArea.Display"/>.</summary>
    public BoundingBox Window { get; set; } = BoundingBox.Empty;

    /// <summary>Adatta l'area al foglio invece di usare <see cref="Scale"/>.</summary>
    public bool Fit { get; set; } = true;

    /// <summary>Unità di disegno per millimetro di carta: 50 è 1:50 per un disegno in millimetri, 0,5 è 2:1.</summary>
    public double Scale { get; set; } = 1;

    public bool Center { get; set; } = true;

    /// <summary>Margine bianco su ogni lato, in millimetri.</summary>
    public double Margin { get; set; } = 5;

    /// <summary>Tutto nero, come con lo stile di stampa monochrome.ctb.</summary>
    public bool Monochrome { get; set; }

    /// <summary>Spessori di linea dei layer e degli oggetti; senza, tutte le linee sono sottili.</summary>
    public bool Lineweights { get; set; } = true;

    /// <summary>Larghezza del foglio così come è orientato.</summary>
    public double PaperWidth => Landscape ? Math.Max(Paper.Width, Paper.Height) : Math.Min(Paper.Width, Paper.Height);

    public double PaperHeight => Landscape ? Math.Min(Paper.Width, Paper.Height) : Math.Max(Paper.Width, Paper.Height);

    public PlotSettings Clone() => (PlotSettings)MemberwiseClone();
}
