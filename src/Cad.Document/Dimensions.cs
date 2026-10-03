using System.Globalization;
using Cad.Geometry;

namespace Cad.Document;

public enum DimensionKind
{
    /// <summary>Distanza misurata lungo una direzione fissa (orizzontale, verticale o ruotata).</summary>
    Linear,

    /// <summary>Distanza vera tra i due punti, con la linea di quota parallela.</summary>
    Aligned,

    Radius,
    Diameter,
    Angular,

    /// <summary>Coordinata X o Y di un punto rispetto all'origine, con la direttrice e il valore in fondo.</summary>
    Ordinate,

    /// <summary>Lunghezza di un arco, con l'arco di quota concentrico.</summary>
    ArcLength,
}

/// <summary>Stile di quota: dimensioni di testo, frecce e linee, formato dei numeri. Valori iniziali come ISO-25.</summary>
public sealed class DimensionStyle(string name)
{
    public const string DefaultName = "ISO-25";

    public string Name { get; } = name;
    public double TextHeight { get; set; } = 2.5;
    public double ArrowSize { get; set; } = 2.5;

    /// <summary>Distacco delle linee di estensione dall'oggetto (DIMEXO).</summary>
    public double ExtensionOffset { get; set; } = 0.625;

    /// <summary>Sporgenza delle linee di estensione oltre la linea di quota (DIMEXE).</summary>
    public double ExtensionExtend { get; set; } = 1.25;

    /// <summary>Distanza del testo dalla linea di quota (DIMGAP).</summary>
    public double TextGap { get; set; } = 0.625;

    public int Decimals { get; set; } = 2;
    public char DecimalSeparator { get; set; } = ',';

    /// <summary>Distanza tra le linee di quota delle quote da linea di base (DIMDLI).</summary>
    public double BaselineSpacing { get; set; } = 3.75;

    /// <summary>
    /// Fattore che moltiplica le misure lineari mostrate (DIMLFAC): un disegno rimpicciolito a 1:2 lo ha a 2, così le
    /// quote mostrano le misure reali. Gli angoli non cambiano.
    /// </summary>
    public double LinearFactor { get; set; } = 1;

    /// <summary>Fattore globale che moltiplica tutte le grandezze (DIMSCALE): utile per disegni in scala.</summary>
    public double Scale { get; set; } = 1;

    public string FormatLength(double value) => Format(value, Decimals);

    public string FormatAngle(double degrees) => Format(degrees, Math.Max(0, Decimals - 2)) + "°";

    /// <summary>Numero arrotondato, senza zeri finali (DIMZIN = 8) e con il separatore dello stile.</summary>
    private string Format(double value, int decimals)
    {
        var text = Math.Round(value, decimals).ToString("0." + new string('#', Math.Max(decimals, 0)), CultureInfo.InvariantCulture);
        if (text == "-0")
        {
            text = "0";
        }

        return text.Replace('.', DecimalSeparator);
    }
}

/// <summary>
/// Quota. La grafica si genera dai punti di definizione (<see cref="Explode"/>); per le quote lette da file si usa invece
/// il blocco già pronto (<see cref="Graphics"/>) finché non vengono modificate con i grip.
/// </summary>
public sealed class DimensionEntity(Layer layer, DimensionKind kind, DimensionStyle style) : Entity(layer)
{
    /// <summary>Simbolo della lunghezza d'arco davanti alla misura.</summary>
    public const string ArcSymbol = "⌒";

    public DimensionKind Kind { get; } = kind;
    public DimensionStyle Style { get; set; } = style;

    /// <summary>
    /// Lineare e allineata: origine della prima linea di estensione. Raggio e diametro: centro. Angolare: punto sul primo lato.
    /// Coordinata: punto misurato. Lunghezza d'arco: inizio dell'arco.
    /// </summary>
    public Vector2 First { get; set; }

    /// <summary>
    /// Lineare e allineata: origine della seconda linea di estensione. Raggio e diametro: punto sulla circonferenza.
    /// Angolare: punto sul secondo lato. Coordinata: fine della direttrice. Lunghezza d'arco: fine dell'arco (in senso antiorario).
    /// </summary>
    public Vector2 Second { get; set; }

    /// <summary>Angolare: vertice dell'angolo. Lunghezza d'arco: centro dell'arco. Coordinata: origine delle coordinate.</summary>
    public Vector2 Vertex { get; set; }

    /// <summary>Dove passa la linea di quota (o l'arco, per l'angolare); per raggio e diametro, dove sta il testo.</summary>
    public Vector2 Location { get; set; }

    /// <summary>Solo lineare: direzione della misura (0 = orizzontale, π/2 = verticale).</summary>
    public double Rotation { get; set; }

    /// <summary>Solo coordinata: true misura la X (direttrice verticale), false la Y.</summary>
    public bool OrdinateX { get; set; }

    /// <summary>Testo scritto al posto della misura; "&lt;&gt;" viene sostituito dalla misura. Null = solo la misura.</summary>
    public string? TextOverride { get; set; }

    /// <summary>Grafica letta dal file, in coordinate del disegno dopo <see cref="GraphicsTransform"/>.</summary>
    public BlockDefinition? Graphics { get; set; }
    public Matrix2D GraphicsTransform { get; set; } = Matrix2D.Identity;

    /// <summary>Misura: lunghezza in unità di disegno oppure angolo in gradi.</summary>
    public double Measurement => Kind switch
    {
        DimensionKind.Linear => Math.Abs(Vector2.Dot(Second - First, Vector2.FromPolar(1, Rotation))),
        DimensionKind.Aligned => Vector2.Distance(First, Second),
        DimensionKind.Radius => Vector2.Distance(First, Second),
        DimensionKind.Diameter => 2 * Vector2.Distance(First, Second),
        DimensionKind.Ordinate => Math.Abs(OrdinateX ? First.X - Vertex.X : First.Y - Vertex.Y),
        DimensionKind.ArcLength => MeasuredArc().Radius * MeasuredArc().Sweep,
        _ => AngularArc().Sweep * 180 / Math.PI,
    };

    public string Text
    {
        get
        {
            var length = Measurement * Style.LinearFactor;
            var value = Kind switch
            {
                DimensionKind.Angular => Style.FormatAngle(Measurement),
                DimensionKind.Radius => "R" + Style.FormatLength(length),
                DimensionKind.Diameter => "Ø" + Style.FormatLength(length),
                DimensionKind.ArcLength => ArcSymbol + Style.FormatLength(length),
                _ => Style.FormatLength(length),
            };
            return TextOverride is null ? value : TextOverride.Replace("<>", value, StringComparison.Ordinal);
        }
    }

    public override IReadOnlyList<Vector2> Grips => Kind switch
    {
        DimensionKind.Angular or DimensionKind.ArcLength => [Vertex, First, Second, Location],
        DimensionKind.Ordinate => [First, Second],
        _ => [First, Second, Location],
    };

    public override BoundingBox Bounds => Graphics is { } graphics
        ? TransformedBox(graphics.Bounds, GraphicsTransform)
        : Explode().Aggregate(BoundingBox.Empty, (box, e) => box.Union(e.Bounds));

    protected override Entity TransformCore(Matrix2D m)
    {
        var result = CopyGeometry(m);
        // Una simmetria renderebbe illeggibile il testo della grafica letta: meglio rigenerarla.
        if (Graphics is not null && m.Determinant > 0)
        {
            result.Graphics = Graphics;
            result.GraphicsTransform = GraphicsTransform * m;
        }

        return result;
    }

    protected override Entity MoveGripCore(int index, Vector2 position)
    {
        var result = CopyGeometry(Matrix2D.Identity);
        var slot = index + (Kind is DimensionKind.Angular or DimensionKind.ArcLength ? 0 : 1);
        switch (slot)
        {
            case 0:
                result.Vertex = position;
                break;
            case 1:
                result.First = position;
                break;
            case 2:
                result.Second = position;
                break;
            default:
                result.Location = position;
                break;
        }

        // Coordinata: la fine della direttrice è anche la posizione del testo.
        if (Kind == DimensionKind.Ordinate && slot == 2)
        {
            result.Location = position;
        }

        // Raggio e diametro: il punto sulla circonferenza segue la posizione del testo.
        if (Kind is DimensionKind.Radius or DimensionKind.Diameter && slot == 3)
        {
            var radius = Vector2.Distance(First, Second);
            var direction = (position - First).Normalized();
            if (direction != Vector2.Zero)
            {
                result.Second = First + direction * radius;
            }
        }

        return result;
    }

    private DimensionEntity CopyGeometry(Matrix2D m) => new(Layer, Kind, Style)
    {
        First = m.Transform(First),
        Second = m.Transform(Second),
        Vertex = m.Transform(Vertex),
        Location = m.Transform(Location),
        Rotation = TransformAngle(m, Rotation),
        OrdinateX = OrdinateX,
        TextOverride = TextOverride,
    };

    // ---------- Grafica ----------

    /// <summary>
    /// Linee, frecce piene e testo della quota, sul layer della quota e con colore DaBlocco
    /// (prendono il colore della quota quando vengono disegnate).
    /// </summary>
    public IReadOnlyList<Entity> Explode()
    {
        var parts = new List<Entity>();
        switch (Kind)
        {
            case DimensionKind.Linear:
            case DimensionKind.Aligned:
                ExplodeLinear(parts);
                break;
            case DimensionKind.Radius:
                ExplodeRadius(parts);
                break;
            case DimensionKind.Diameter:
                ExplodeDiameter(parts);
                break;
            case DimensionKind.Ordinate:
                ExplodeOrdinate(parts);
                break;
            case DimensionKind.ArcLength:
                ExplodeArc(parts, DimensionArc(MeasuredArc()), radial: true);
                break;
            default:
                ExplodeAngular(parts);
                break;
        }

        foreach (var part in parts)
        {
            part.Color = EntityColor.ByBlock;
        }

        return parts;
    }

    private double Size(double value) => value * Style.Scale;

    private void ExplodeLinear(List<Entity> parts)
    {
        var direction = Kind == DimensionKind.Linear ? Vector2.FromPolar(1, Rotation) : (Second - First).Normalized();
        if (direction == Vector2.Zero)
        {
            direction = Vector2.UnitX;
        }

        var normal = direction.Perpendicular();
        var p1 = First + normal * Vector2.Dot(Location - First, normal);
        var p2 = Second + normal * Vector2.Dot(Location - Second, normal);

        AddExtensionLine(parts, First, p1);
        AddExtensionLine(parts, Second, p2);

        var length = Vector2.Distance(p1, p2);
        var arrow = Size(Style.ArrowSize);
        var along = length > Tolerance.Default ? (p2 - p1) / length : direction;
        if (length >= 2.5 * arrow)
        {
            parts.Add(new LineEntity(Layer, p1, p2));
            parts.Add(Arrow(p1, -along));
            parts.Add(Arrow(p2, along));
        }
        else
        {
            // Quota stretta: frecce all'esterno, rivolte verso le linee di estensione.
            parts.Add(new LineEntity(Layer, p1 - along * 2 * arrow, p2 + along * 2 * arrow));
            parts.Add(Arrow(p1, along));
            parts.Add(Arrow(p2, -along));
        }

        parts.Add(AlignedText((p1 + p2) / 2, direction));
    }

    private void AddExtensionLine(List<Entity> parts, Vector2 origin, Vector2 foot)
    {
        var length = Vector2.Distance(origin, foot);
        if (length <= Size(Style.ExtensionOffset))
        {
            return;
        }

        var outward = (foot - origin) / length;
        parts.Add(new LineEntity(Layer, origin + outward * Size(Style.ExtensionOffset), foot + outward * Size(Style.ExtensionExtend)));
    }

    private void ExplodeRadius(List<Entity> parts)
    {
        var center = First;
        var onCurve = Second;
        var radius = Vector2.Distance(center, onCurve);
        var direction = (onCurve - center).Normalized();
        if (direction == Vector2.Zero)
        {
            return;
        }

        if (Vector2.Distance(center, Location) > radius + Tolerance.Default)
        {
            // Testo fuori: linea dal punto sulla curva fino al testo, freccia rivolta verso la curva.
            var end = center + direction * Vector2.Distance(center, Location);
            parts.Add(new LineEntity(Layer, onCurve, end));
            parts.Add(Arrow(onCurve, -direction));
            parts.Add(AlignedText((onCurve + end) / 2 + direction * Size(Style.ArrowSize) / 2, direction));
        }
        else
        {
            parts.Add(new LineEntity(Layer, center, onCurve));
            parts.Add(Arrow(onCurve, direction));
            parts.Add(AlignedText((center + onCurve) / 2, direction));
        }
    }

    private void ExplodeDiameter(List<Entity> parts)
    {
        var center = First;
        var a = Second;
        var b = 2 * center - a;
        var direction = (a - b).Normalized();
        if (direction == Vector2.Zero)
        {
            return;
        }

        parts.Add(new LineEntity(Layer, b, a));
        parts.Add(Arrow(a, direction));
        parts.Add(Arrow(b, -direction));
        parts.Add(AlignedText(center, direction));
    }

    private void ExplodeAngular(List<Entity> parts) => ExplodeArc(parts, AngularArc(), radial: false);

    /// <summary>Arco di quota con frecce e testo; le linee di estensione vanno dai punti indicati fino all'arco.</summary>
    private void ExplodeArc(List<Entity> parts, Arc2D arc, bool radial)
    {
        var radius = arc.Radius;
        if (radius <= Tolerance.Default)
        {
            return;
        }

        // Linee di estensione lungo i lati, solo se l'arco sta oltre i punti indicati.
        foreach (var (point, angle) in new[] { (First, arc.StartAngle), (Second, arc.EndAngle) })
        {
            var reach = Vector2.Distance(Vertex, point);
            var ray = Vector2.FromPolar(1, angle);
            if (radial)
            {
                AddExtensionLine(parts, point, Vertex + ray * radius);
            }
            else if (radius > reach + Size(Style.ExtensionOffset))
            {
                parts.Add(new LineEntity(Layer, Vertex + ray * (reach + Size(Style.ExtensionOffset)), Vertex + ray * (radius + Size(Style.ExtensionExtend))));
            }
        }

        parts.Add(new ArcEntity(Layer, Vertex, radius, arc.StartAngle, arc.EndAngle));
        var startTangent = Vector2.FromPolar(1, arc.StartAngle).Perpendicular();
        var endTangent = Vector2.FromPolar(1, arc.EndAngle).Perpendicular();
        parts.Add(Arrow(arc.StartPoint, -startTangent));
        parts.Add(Arrow(arc.EndPoint, endTangent));

        var middle = arc.StartAngle + arc.Sweep / 2;
        var text = AlignedText(arc.Midpoint, Vector2.FromPolar(1, middle).Perpendicular());
        parts.Add(text);
        if (Kind == DimensionKind.ArcLength)
        {
            // Il simbolo ⌒ manca in quasi tutti i caratteri: si disegna come un piccolo arco sopra il testo.
            text.Value = text.Value.Replace(ArcSymbol, string.Empty, StringComparison.Ordinal);
            var h = text.Height;
            var up = Vector2.FromPolar(1, text.Rotation).Perpendicular();
            var center = text.Position + up * (0.9 * h);
            parts.Add(new ArcEntity(Layer, center, 0.7 * h, text.Rotation + Math.PI / 6, text.Rotation + 5 * Math.PI / 6));
        }
    }

    /// <summary>Arco misurato dalla quota di lunghezza d'arco: dal primo al secondo punto in senso antiorario.</summary>
    private Arc2D MeasuredArc() =>
        new(Vertex, Vector2.Distance(Vertex, First), (First - Vertex).Angle, (Second - Vertex).Angle);

    /// <summary>Lo stesso arco portato alla distanza di <see cref="Location"/> dal centro.</summary>
    private Arc2D DimensionArc(Arc2D measured) =>
        new(Vertex, Vector2.Distance(Vertex, Location), measured.StartAngle, measured.EndAngle);

    /// <summary>
    /// Quota a coordinata: direttrice dal punto (con un gradino se la fine non è allineata) e valore in fondo,
    /// nella direzione della direttrice.
    /// </summary>
    private void ExplodeOrdinate(List<Entity> parts)
    {
        var axis = OrdinateX ? Vector2.UnitY : Vector2.UnitX;
        var delta = Second - First;
        var along = Vector2.Dot(delta, axis);
        var direction = along < 0 ? -axis : axis;
        var length = Math.Abs(along);
        var cross = delta - axis * along;
        var start = First + direction * Math.Min(Size(Style.ExtensionOffset), length);
        if (cross.Length <= Tolerance.Default)
        {
            parts.Add(new LineEntity(Layer, start, Second));
        }
        else
        {
            var a = First + direction * (length / 3);
            var b = First + cross + direction * (2 * length / 3);
            parts.Add(new LineEntity(Layer, start, a));
            parts.Add(new LineEntity(Layer, a, b));
            parts.Add(new LineEntity(Layer, b, Second));
        }

        var angle = direction.Angle;
        var flipped = angle > Math.PI / 2 + 1e-9 || angle <= -Math.PI / 2 + 1e-9;
        parts.Add(new TextEntity(Layer, Second + direction * Size(Style.TextGap), Size(Style.TextHeight), Text)
        {
            Rotation = Arc2D.NormalizeAngle(flipped ? angle + Math.PI : angle),
            HorizontalAlignment = flipped ? TextHorizontalAlignment.Right : TextHorizontalAlignment.Left,
            VerticalAlignment = TextVerticalAlignment.Middle,
        });
    }

    /// <summary>Arco della quota angolare: tra i due lati, dalla parte in cui sta <see cref="Location"/>.</summary>
    private Arc2D AngularArc()
    {
        var a1 = (First - Vertex).Angle;
        var a2 = (Second - Vertex).Angle;
        var radius = Vector2.Distance(Vertex, Location);
        var location = (Location - Vertex).Angle;
        var inside = Arc2D.NormalizeAngle(location - a1) <= Arc2D.NormalizeAngle(a2 - a1);
        return inside ? new Arc2D(Vertex, radius, a1, a2) : new Arc2D(Vertex, radius, a2, a1);
    }

    /// <summary>Freccia piena con la punta in <paramref name="tip"/>, rivolta nella direzione <paramref name="pointing"/>.</summary>
    private SolidEntity Arrow(Vector2 tip, Vector2 pointing)
    {
        var size = Size(Style.ArrowSize);
        var back = tip - pointing * size;
        var side = pointing.Perpendicular() * (size / 6);
        return new SolidEntity(Layer, [tip, back + side, back - side]);
    }

    /// <summary>Testo centrato sopra la linea, ruotato come la linea ma sempre leggibile (mai capovolto).</summary>
    private TextEntity AlignedText(Vector2 anchor, Vector2 direction)
    {
        var angle = direction.Angle;
        if (angle > Math.PI / 2 + 1e-9 || angle <= -Math.PI / 2 + 1e-9)
        {
            angle += Math.PI;
        }

        var up = Vector2.FromPolar(1, angle).Perpendicular();
        return new TextEntity(Layer, anchor + up * Size(Style.TextGap), Size(Style.TextHeight), Text)
        {
            Rotation = Arc2D.NormalizeAngle(angle),
            HorizontalAlignment = TextHorizontalAlignment.Center,
            VerticalAlignment = TextVerticalAlignment.Bottom,
        };
    }

    private static BoundingBox TransformedBox(BoundingBox box, Matrix2D m) => box.IsEmpty
        ? box
        : BoundingBox.FromPoints(
        [
            m.Transform(box.Min), m.Transform(box.Max),
            m.Transform(new Vector2(box.Min.X, box.Max.Y)), m.Transform(new Vector2(box.Max.X, box.Min.Y)),
        ]);
}
